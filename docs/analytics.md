# Deterministik analytics sözleşmesi

Bu belge `BankingAI.Application/Analytics` altındaki servislerin girdi, çıktı ve
hesap kurallarını tanımlar. LLM bu servisleri seçer ve sonucu açıklar; hesap,
filtreleme, yetki ve dönem çözümü backend'de yapılır (RULE.md §2, VAL-05).

## Ortak kurallar

- **Dönem:** `PeriodResolver` (ANA-01). Sembolik dönem (ThisMonth, LastMonth,
  Last3Months, Last6Months, Custom) müşterinin `Customers.TimeZone` değerine göre
  `[StartUtc, EndUtc)` aralığına çevrilir. Sonuçta yerel `StartDate`/`EndDate` döner.
- **Saat:** `TimeProvider` ile verilir; testler ve demo sabit saat kullanabilir.
- **Müşteri kimliği** güvenilir çağıran bağlamından gelir (API oturumu), model
  argümanından değil (SEC-01).
- **Harcama** = `Kind = Purchase`. Transfer, Sale, Salary ve OpeningFunding sayılmaz.
- **Para birimleri** ayrı satırlarda döner; dönüştürülmez ve toplanmaz.
- **Hata sözleşmesi:** olmayan müşteri veya başka müşterinin hesabı
  `KeyNotFoundException` (API'de 404; varlık sızdırılmaz). Geçersiz girdi
  `ArgumentException` (API'de 400).

## Ortak kapsam (`CustomerScope` ve `PurchaseScope`)

`CustomerScope` (internal, ANA-05) müşteri ve saat dilimi, hesap sahipliği (başka müşterinin
hesabı = bulunamadı) ve dönem çözümünün tek yeridir; yetki kuralı bir serviste düzeltilip
diğerinde unutulamaz. `personalOnly` ile Business hesapları dışarıda bırakabilir.

`PurchaseScope` (internal) harcama servislerinin kapsamıdır: `CustomerScope(personalOnly: true)`
üstüne `Kind = Purchase`. Girdi `SpendingQuery(CustomerId, Period, From?, To?, AccountId?)`.
Sorgular çalıştırılmadan döner, her servis kendi tek toplamasını ekler.

## ANA-02 — Harcama özeti (`SpendingSummaryService`)

Girdi: `SpendingQuery`.
Çıktı: `SpendingSummary(Period, Currencies)`; her para birimi için
`TotalAmount`, `TransactionCount`, `UncategorizedAmount`, `UncategorizedCount`.

- Varsayılan kapsam müşterinin **Personal** hesaplarıdır. `AccountId` verilirse
  müşteriye ait şahsi hesap olmalıdır; Business hesap `ArgumentException` verir.
- Kategorisiz Purchase toplamdan düşmez; ayrıca gösterilir.
- Dönemde harcama yoksa `Currencies` boş listedir; "0" satırı uydurulmaz.
- SQL: müşterinin saat dilimini okuyan bir sorgu ve hesap filtresini alt sorgu
  olarak içeren tek bir `GROUP BY "CurrencyCode"` sorgusu. Satırlar belleğe çekilmez;
  değerler parametre olarak gönderilir.

Kabul örnekleri (10 Ekim 2026 15:00 Istanbul, demo verisi):

| İstek | Sonuç |
| --- | --- |
| Ada, bu ay | TRY 735,00, 5 işlem, kategorisiz 145,00 (1). Bütün Debit toplamı 2.235 olurdu. |
| Deniz, bu ay | TRY 11.350, 2 işlem; dükkân satışı (355) ve transferler hariç |
| Ege, bu ay | TRY 840, 2 işlem |
| Deniz, geçen ay | TRY 31.000, 1 işlem; Eylül transferleri (2.750) hariç |
| Ada, son 3 ay | TRY 2.389,94, 15 işlem |
| Ada, Ocak 2026 | Boş liste |
| Ada + geçici 10 USD harcama | Ayrı satırlar: TRY 735, USD 10 |
| Ada, Deniz'in hesabı | `KeyNotFoundException` |
| Deniz, dükkân hesabı | `ArgumentException` |

Para biçimlendirme ("735,00 TL") servis sorumluluğu değildir; API/AI katmanında yapılır.

## ANA-03 — Kategori dağılımı (`CategoryBreakdownService`)

Girdi: `SpendingQuery`. Çıktı: para birimi başına `TotalAmount` ve `Categories`;
her satırda `CategoryCode`, `DisplayName`, `Amount`, `TransactionCount`, `Percentage`.

- Kapsam ANA-02 ile aynıdır (`PurchaseScope`); kategori tutarları toplamı ve işlem
  sayısı her zaman harcama özetine eşittir (tutarlılık testi).
- Kategorisiz Purchase ayrı grupta döner: `CategoryCode = null`, kullanıcı kararıyla
  `DisplayName = "Sınıflandırılmamış"` (`AnalyticsLabels.Unclassified`). `OTHER` ("Diğer") ayrı bir kategoridir; birleştirilmez.
- Sıra: tutar büyükten küçüğe; eşitlikte kod, Sınıflandırılmamış en sonda. "En çok
  harcanan kategori" listenin ilk elemanıdır. Sıfır tutarlı kategori listelenmez.
- Yüzde backend'de `tutar / para birimi toplamı × 100` olarak hesaplanır ve 2 ondalığa
  yuvarlanır (AwayFromZero). Tutarlar kesindir; yuvarlanmış yüzdelerin toplamı 100'den
  birkaç yüzde birlik sapabilir ve zorla 100'e tamamlanmaz.
- SQL: saat dilimi sorgusu ve kategorilere `LEFT JOIN` yapan tek bir
  `GROUP BY CurrencyCode, Code, DisplayName` sorgusu. Yüzde ve sıralama birkaç satırlık
  sonuçta C# içinde yapılır.

| İstek | Sonuç |
| --- | --- |
| Ada, bu ay | FOOD 590 (4, %80,27); Sınıflandırılmamış 145 (1, %19,73) |
| Ada, son 3 ay | FOOD 1.255 (8, %52,51); SUBSCRIPTION 989,94 (6, %41,42); Sınıflandırılmamış 145 (1, %6,07) |
| Deniz, bu ay | GROCERY 9.500 (%83,70); BILLS 1.850 (%16,30); dükkân geliri yok |
| Ege, son 6 ay | TRANSPORTATION 5.600 (13, %79,21); TRAVEL 1.200 (%16,97); FOOD 270 (2, %3,82) |
| Ada, Haziran 2025 | OTHER 780 (%65,00) ve Sınıflandırılmamış 420 (%35,00) ayrı satırlar |

## ANA-04 — Merchant harcamaları (`MerchantSpendingService`)

İki işlem, aynı `PurchaseScope`:

**A) `GetBreakdownAsync(SpendingQuery)`:** Para birimi başına `NormalizedMerchant`
dağılımı. Kategori dağılımıyla aynı sunum kuralları (kesin tutar, gösterim yüzdesi,
büyükten küçüğe). Merchant'ı olmayan Purchase'lar `Merchant = null`,
`DisplayName = "Sınıflandırılmamış"` grubunda; toplamlar harcama özetine eşittir.

**B) `GetSpendingAsync(SpendingQuery, merchant)`:** Tek merchant harcaması. `merchant`
güvenilmeyen metindir (model argümanı). `MerchantMatcher` ile müşterinin **kendi tüm
zamanlar** Purchase merchant'larına karşı çözülür:

| Durum | Sonuç |
| --- | --- |
| Sadeleştirilmiş (Türkçe harf/aksan → düz, sabit tabloyla büyük harf, boşluk birleştirme) tam eşleşme | `Matched`, `IsPartialMatch = false` |
| Tam eşleşme yok, metin (en az 3 harf) tam bir merchant adının içinde | `Matched`, `IsPartialMatch = true`; açıklama varsayımı söyler |
| Birden fazla aday | `Ambiguous`, `Candidates` dolu, tutar yok |
| Yok, yazım hatası veya 3 harften kısa kısmi metin | `NotFound`, tutar yok |
| Boş/whitespace veya 150 karakterden uzun | `ArgumentException` (DB'ye gitmeden) |

- Sadeleştirme makinenin kültüründen bağımsızdır (tr-TR'de `"I".ToLower()` = `"ı"` tuzağı).
- Yazım hatası otomatik düzeltilmez; yanlış merchant'ın tutarını söylemek "bulunamadı"dan kötüdür.
- Bilinen merchant'ın dönemde harcaması yoksa `Matched` + boş `Currencies` döner.
- Başka müşterinin merchant'ı aday listesinde görünmez.
- SQL: saat dilimi, müşterinin `DISTINCT NormalizedMerchant` listesi ve kanonik adla
  filtrelenmiş tek `GROUP BY` (3 komut). Modelin ham metni SQL'e girmez; yalnız eşleşen
  kanonik ad parametre olarak gider (yakalanan SQL'de `@canonical=Starbucks`).

| İstek | Sonuç |
| --- | --- |
| Ada, son 3 ay, "starbucks" | Matched; TRY 930, 5 işlem (Kahve Durağı hariç) |
| Ada, son 3 ay, "starbuck" | Matched, kısmi; 930 |
| Ada, bu ay, "hobi dükkanı" | Matched; boş tutar listesi |
| Ege, son 6 ay, "şehir" | Ambiguous: Şehir Ulaşım, Şehirlerarası Otobüs |
| Ada, "Aile Marketi" (Deniz'in) | NotFound |
| Ada, bu ay, dağılım | Starbucks 375 (%51,02); Kahve Durağı 215 (%29,25); Sınıflandırılmamış 145 (%19,73) |
| Ege, son 6 ay, dağılım | Şehir Ulaşım 5.600; Şehirlerarası Otobüs 1.200; Kahve Durağı 270 |

## ANA-05 — Transaction araması (`TransactionSearchService`)

Ekstre gibi davranır: **bütün türler** (maaş, transfer, satış, alışveriş) ve varsayılan olarak
müşterinin **bütün hesapları** (Business dükkân hesabı dahil; `CustomerScope(personalOnly: false)`).
Kullanıcı kararları: dönem zorunlu (tarihsiz arama yok), sayfa varsayılan 20 / en fazla 50,
maskeleme "Deniz M***", serbest metin araması yok.

Girdi `TransactionSearchQuery`: müşteri, dönem, isteğe bağlı hesap, `Kind`, `Direction`,
`CategoryCode`, `OnlyUnclassified`, `Merchant`, `Limit` (1–50), `Offset` (0–10.000).
Bütün girdi kontrolleri DB'den önce yapılır.

| Kural | Davranış |
| --- | --- |
| Bilinmeyen kategori kodu (`"YEMEK"`) | `ArgumentException`; "böyle kategori yok" ≠ "bu kategoride işlem yok" |
| `CategoryCode` + `OnlyUnclassified` veya kategori filtresi + Purchase dışı `Kind` | Çelişki, `ArgumentException` |
| Merchant | ANA-04 `MerchantMatcher`; Ambiguous/NotFound'da satır dönmez, `MerchantMatch` döner |
| Sıra | `OccurredAt` azalan, eşitlikte `Id` azalan; sayfalar kararlı |
| Sonuç | `TotalCount` (COUNT sorgusu) ve `HasMore` |

Çıktı entity değil `TransactionItem` DTO'sudur: hesap adı, UTC zaman ve müşterinin saat dilimine
göre `LocalDate`, tür, yön, tutar, para birimi, merchant, kategori (Purchase'ta kategorisiz ise
"Sınıflandırılmamış"; Purchase değilse null), ham açıklama ve karşı taraf.

Karşı taraf PairId ile diğer bacaktan bulunur: başka müşteri → maskeli ad ("Deniz M***");
Business hesap → dükkân hesap adı; kendi hesabı → hesap adı ve `CounterpartyIsOwnAccount = true`;
çiftsiz kayıt → null. Tam ad servis dışına çıkmaz (test ile korunur).

`RawDescription` banka POS metnidir ve LLM'e gidecektir; içindeki talimat benzeri metin veridir,
komut değildir. Koruma MCP/prompt katmanında yapılacak (SEC-03).

SQL: saat dilimi, COUNT ve tek sayfa sorgusu (3 komut; merchant filtresinde +1 merchant listesi).
Hesap, kategori, karşı bacak, karşı hesap ve karşı müşteri aynı sorguda join'lenir; N+1 yoktur.
**EF tuzağı:** korelasyonlu alt sorgu LEFT JOIN'e çevrilirken C# null semantiği korunur
(`null == null` true) ve dış satırdaki `t.PairId != null` kontrolü düşürülür. Bu yüzden kontrol
join'lenen satıra (`o.PairId != null`) konur; aksi halde çiftsiz her satır diğer çiftsiz
satırlarla çoğalır (ilk uygulamada testler bunu yakaladı).

| İstek | Sonuç |
| --- | --- |
| Ada, bu ay | 8 kayıt: 145 Sınıflandırılmamış; 300 gelen (Ege Y***); Starbucks 165; Kahve Durağı 95 (karşı taraf dükkân hesabı); Starbucks 210; maaş 45.000; 1.500 giden (Deniz M***); Kahve Durağı 120 |
| Ada, bu ay, limit 3 / offset 6 | 3 kayıt + HasMore; 2 kayıt, HasMore yok |
| Ada, Transfer + Debit | 1.500, Deniz M*** |
| Ada, yalnız sınıflandırılmamış | `POS 6604 ONLINE ODEME 4471` |
| Ada, FOOD | 165, 95, 210, 120 |
| Deniz, dükkân hesabı, bu ay | Sale: Ada K***, Ege Y***, Ada K*** |
| Deniz, geçen ay, Transfer + Debit | 2.000 kendi dükkânına (own), 750 Ege Y*** |
