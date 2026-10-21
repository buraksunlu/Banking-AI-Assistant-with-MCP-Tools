# Deterministik analytics sözleşmesi

Bu belge `BankingAI.Application/Analytics` altındaki servislerin girdi, çıktı ve
hesap kurallarını tanımlar. LLM bu servisleri seçer ve sonucu açıklar; hesap,
filtreleme, yetki ve dönem çözümü backend'de yapılır (RULE.md §2, VAL-05).

## Ortak kurallar

- **Dönem:** `PeriodResolver` (ANA-01, ANA-20). Sembolik dönem müşterinin `Customers.TimeZone` değerine göre
  `[StartUtc, EndUtc)` aralığına çevrilir. Sonuçta yerel `StartDate`/`EndDate` döner. Model yalnız adı seçer; tarihi
  her zaman backend hesaplar (AI-14 denemesinde gün dönemi yokken "Dün ne harcadım?" sessizce "geçen ay" olmuştu).

  | Dönem | Anlamı (dahil günler) | Demo (Cumartesi 10 Ekim 2026) |
  | --- | --- | --- |
  | `ThisMonth` | ay başı – bugün | 1–10 Eki |
  | `LastMonth` | önceki tam ay | 1–30 Eyl |
  | `Last3Months` / `Last6Months` | 3/6 ay önceki **aynı gün** – bugün (iki uç dahil) | 10 Tem – 10 Eki / 10 Nis – 10 Eki |
  | `Today` | bugün | 10 Eki |
  | `Yesterday` | dün | 9 Eki |
  | `ThisWeek` | bu haftanın **pazartesisi** – bugün (K2-A) | 5–10 Eki |
  | `LastWeek` | geçen haftanın pazartesisi – pazarı | 28 Eyl – 4 Eki |
  | `Last7Days` / `Last30Days` | **bugün dahil** 7/30 gün (K3-A) | 4–10 Eki / 11 Eyl – 10 Eki |
  | `ThisYear` | 1 Ocak – bugün | 1 Oca – 10 Eki |
  | `Custom` | `from`–`to` (en fazla 24 ay, gelecek yok) | — |

  Dikkat: "son N gün" bugünü N günden biri sayar (tam N gün); "son N ay" N ay önceki aynı günden başlar (N ay + 1 gün).
  Eski kural davranış değiştirmemek için korundu. Kabul örnekleri (Ada, harcama): dün 145, bu hafta 615, geçen hafta 120,
  son 7 gün 615, son 30 gün 1.359,98, bu yıl 3.934,88 TRY; bugün harcama yok (boş liste).
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
`CategoryCode`, `OnlyUnclassified`, `Merchant`, `Limit` (1–50), `Offset` (0–10.000) ve ANA-11'den beri
`MinAmount`/`MaxAmount`/`CurrencyCode` (aşağıda).
Bütün girdi kontrolleri DB'den önce yapılır.

| Kural | Davranış |
| --- | --- |
| Bilinmeyen kategori kodu (`"YEMEK"`) | `ArgumentException`; "böyle kategori yok" ≠ "bu kategoride işlem yok" |
| `CategoryCode` + `OnlyUnclassified` veya kategori filtresi + Purchase dışı `Kind` | Çelişki, `ArgumentException` |
| Merchant | ANA-04 `MerchantMatcher`; Ambiguous/NotFound'da satır dönmez, `MerchantMatch` döner |
| Sıra | `OccurredAt` azalan, eşitlikte `Id` azalan; sayfalar kararlı |
| Sonuç | `TotalCount` (COUNT sorgusu) ve `HasMore` |

Çıktı entity değil `TransactionItem` DTO'sudur: hesap kimliği (`AccountId`, API-08) ve adı, UTC zaman ve müşterinin saat dilimine
göre `LocalDate`, tür, yön, tutar, para birimi, merchant, kategori (Purchase'ta kategorisiz ise
"Sınıflandırılmamış"; Purchase değilse null), ham açıklama ve karşı taraf.

Karşı taraf PairId ile diğer bacaktan bulunur: başka müşteri → maskeli ad ("Deniz M***");
Business hesap → dükkân hesap adı; kendi hesabı → hesap adı ve `CounterpartyIsOwnAccount = true`;
çiftsiz kayıt → null. Tam ad servis dışına çıkmaz (test ile korunur).

`RawDescription` banka POS metnidir ve LLM'e gidecektir; içindeki talimat benzeri metin veridir,
komut değildir. Koruma MCP/prompt katmanında yapılacak (SEC-03).

SQL: saat dilimi, COUNT ve tek sayfa sorgusu (3 komut; merchant filtresinde +1 merchant listesi).
Hesap, kategori, karşı bacak, karşı hesap ve karşı müşteri aynı sorguda join'lenir; N+1 yoktur.
Bu okuma ve maskeleme ANA-06'dan itibaren ortak `TransactionItemReader` (internal) içindedir;
sıralama (`NewestFirst`/`LargestFirst`) join'lerden sonra son sorguda uygulanır.
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

**Tek işlem (`GetAsync(customerId, transactionId)`, API-10):** aynı ekstre kapsamı (bütün türler, bütün hesaplar,
`CustomerScope(personalOnly: false)`) ama **dönemsiz**. Kaynak sorgu `Id = @id AND AccountId IN (müşterinin
hesapları)`; satır `TransactionItemReader` ile okunur, yani tek işlem liste kalemiyle birebir aynıdır (testle
korunur). Satır yoksa — olmayan veya başka müşterinin işlemi — aynı `KeyNotFoundException("İşlem bulunamadı.")`.
SQL: saat dilimi + tek join'li okuma (2 komut, yakalanarak doğrulandı).

| İstek | Sonuç |
| --- | --- |
| Ada, 602 | Starbucks 210 TRY, FOOD "Yeme & İçme", 6 Eki |
| Ada, 709 / Deniz, 710 | Ada: Purchase 120, karşı taraf "Kahve Durağı – Kadıköy"; Deniz (dükkân): Sale 120, "Ada K***" |
| Deniz, 707 | 2.000 kendi dükkânına, own = true, 30 Eyl |
| Ada, 001 | 2025 USD açılış fonlaması 500 (dönem sınırı yok) |
| Ada, 710 veya 712 | `KeyNotFoundException` (işlem Ada'yla ilgili ama Deniz'in hesabında) |

## ANA-06 — En yüksek işlemler (`HighestTransactionsService`)

Girdi `HighestTransactionsQuery(CustomerId, Period, From?, To?, AccountId?, Limit = 5,
IncludeAllTransactions = false, MinAmount?, MaxAmount?, CurrencyCode?)` (tutar alanları ANA-11). Çıktı para birimi başına sıralı `TransactionItem` listesi.

Kullanıcı kararları:

- **Varsayılan: harcama.** `PurchaseScope`, yani şahsi hesaplar ve `Kind = Purchase`. Ada'nın
  1.500 TL transferi ve 45.000 TL maaşı "en yüksek harcamalar" listesine girmez.
- **"Hepsi" istenirse** (`IncludeAllTransactions = true`): bütün türler ve müşterinin bütün
  hesapları (dükkân dahil), arama servisiyle aynı ekstre kapsamı (`CustomerScope(personalOnly: false)`).
- **Limit** varsayılan 5; istenen sayı döner, en fazla 100. 0 veya 101+ `ArgumentException`.
- **Eşit tutarlarda en yeni önce**, sonra `Id`.
- **Para birimi başına ayrı liste**; 10 USD ile 210 TRY karşılaştırılmaz.

SQL: saat dilimi, `DISTINCT CurrencyCode` ve para birimi başına bir sıralı sorgu
(`ORDER BY Amount DESC, OccurredAt DESC, Id DESC LIMIT n`). Pencere fonksiyonu yerine bu tercih
edildi; sorgu sayısı satıra değil para birimi whitelist'ine (en fazla 4) bağlıdır.

| İstek | Sonuç |
| --- | --- |
| Ada, bu ay | 210, 165, 145 (Sınıflandırılmamış), 120, 95; transfer ve maaş yok |
| Ada, son 3 ay | Netflix 229,99 × 3 (12 Eyl, 12 Ağu, 12 Tem), Starbucks 210, 185 |
| Deniz, son 6 ay, 3 | 31.000, 24.000, 18.000 |
| Ege, son 6 ay, 3 | 1.200 (14 Ağu), 700 (3 Eki), 700 (25 Eyl) |
| Ada, bu ay, hepsi | Maaş 45.000; giden 1.500 (Deniz M***); gelen 300; 210; 165 |
| Deniz, bu ay, hepsi, 10 | 55.000; 9.500; 1.850; 1.500; dükkân satışları 140, 120, 95 |
| Ada + geçici 10 USD, 2 | TRY: 210, 165; USD: 10 |

## ANA-11 — Tutar filtresi (arama ve en yüksek işlemler, 16 Ekim 2026)

`TransactionSearchQuery` ve `HighestTransactionsQuery`'nin sonuna isteğe bağlı `MinAmount`, `MaxAmount` ve
`CurrencyCode` eklendi. Doğrulama ve `WHERE` koşulu iki serviste ortak `AmountFilter` (internal) içindedir; mevcut
sorguya koşul olarak eklenir (yeni SQL komutu yok). Kullanıcı kararları:

- **K1-A — sınırlar dahil:** `MinAmount ≤ Amount ≤ MaxAmount`; `min = max` tam tutar araması olur.
- **K2-A — tutar verilirse para birimi zorunlu:** eşik başka para birimindeki işlemle karşılaştırılmaz (500 TRY ≠ 500
  USD). `CurrencyCode` tek başına da filtredir ("dolar işlemlerim").
- **K3-A — para birimi kodu birebir:** `TRY/USD/EUR/GBP`; `try`, `JPY` reddedilir (`InputRules.Currency`, entity
  doğrulamasıyla aynı kural ve mesaj).

| Kural | Davranış (`ArgumentException`, alan) |
| --- | --- |
| Sınır 0,01'den küçük, üst sınırdan büyük ya da 2'den fazla ondalık | `InputRules.Money` (`MinAmount`/`MaxAmount`) |
| Sınır var, para birimi yok | `CurrencyCode`: "Tutar filtresi için para birimi (currencyCode) verilmeli." |
| `MinAmount > MaxAmount` | `MinAmount`: "En az tutar en çok tutardan büyük olamaz." |

Arama ekstre kapsamında kaldığı için tutar filtresi bütün türlere uygulanır (maaş, transfer dahil); en yüksek
işlemlerde varsayılan yine harcamadır ve `Limit` filtreden **sonra** uygulanır.

| İstek | Sonuç |
| --- | --- |
| Arama: Ada, bu ay, TRY, 145–165 | 145 (9 Eki), 165 (8 Eki): iki sınır dahil |
| Arama: Ada, bu ay, TRY, en az 150 | 300, 165, 210, 45.000, 1.500 (5 kayıt; transferler ve maaş dahil) |
| Arama: Ada, bu ay, TRY, en çok 150 | 145, 95, 120 |
| Arama: Deniz, son 6 ay, GROCERY, TRY, en az 20.000 | 31.000 (Eyl), 24.000 (Ağu) |
| Arama: Ada + geçici 300 USD, en az 200 | TRY: 300, 210, 45.000, 1.500 (USD yok); USD: yalnız 300 |
| En yüksek: Ada, bu ay, TRY, en çok 150 | 145, 120, 95 |
| En yüksek: Ada, bu ay, hepsi, TRY, en az 1.000 | maaş 45.000, transfer 1.500 |
| En yüksek: Deniz, son 6 ay, 3, TRY, en çok 20.000 | 18.000, 16.000, 14.000 |

## ANA-07 — Dönem karşılaştırması ve aylık seri (`SpendingComparisonService`)

Kapsam harcama kapsamıdır (`PurchaseScope`: şahsi hesaplar, `Kind = Purchase`). Dönem çiftleri ve
ay pencereleri `PeriodResolver.ResolveComparison` / `ResolveMonthlyWindows` ile saf C#'ta üretilir.

### A) `CompareAsync(SpendingComparisonQuery)`

Kullanıcı kararıyla "geçen aya göre" varsayılanı **son tam ay vs bir önceki ay**dır.

| `ComparisonKind` | 10 Ekim 2026'da | Not |
| --- | --- | --- |
| `PreviousFullMonth` (varsayılan) | Eylül vs Ağustos | İki tamamlanmış ay |
| `MonthToDate` | 1–10 Ekim vs 1–10 Eylül | Önceki ay kısaysa son güne sabitlenir (31 Mar → 1–28 Şub) |
| `Custom` | Verilen iki aralık | Her biri ≤ 24 ay, gelecek yok, **çakışma yok** |

"Bu ay şimdiye kadar vs geçen tam ay" (10 gün vs 30 gün) yanıltıcı olduğu için desteklenmez.

Çıktı para birimi başına: iki toplam, işlem sayıları, `Change` (işaretli) ve `PercentageChange`
(önceki 0 ise **null**; sıfırdan büyüme oranı uydurulmaz, 2 ondalık `AwayFromZero`). `Categories`
her kategorinin iki dönem tutarını ve farkını taşır; **mutlak farka göre** büyükten küçüğe sıralıdır,
böylece artışta da azalışta da en büyük etken ilk satırdadır. Sıfır farklı kategori de listelenir
(iki dönemde de sıfır olan listelenmez). Sınıflandırılmamış grup en sonda.

SQL: saat dilimi ve tek bir toplama sorgusu. Satırlar iki aralığın birleşimiyle filtrelenir, her
dönem kendi `CASE WHEN` / `FILTER` koşuluyla toplanır. Dönemlerin çakışmaması bu yüzden zorunludur.

| İstek | Sonuç |
| --- | --- |
| Deniz, varsayılan | 31.000 vs 24.000 → +7.000, +%29,17; GROCERY +7.000 |
| Ada, varsayılan | 624,98 vs 514,98 → +110, +%21,36; FOOD +110, SUBSCRIPTION 0 |
| Ege, varsayılan | 1.530 vs 2.100 → −570, −%27,14; TRAVEL −1.200 ilk sırada, TRANSPORTATION +500, FOOD +130 |
| Deniz, MonthToDate | 11.350 vs 0 → yüzde null |
| Ada, MonthToDate | 735 (1.500 transfer dahil değil) |

### B) `GetMonthlyTrendAsync(SpendingTrendQuery)`

Kullanıcı kararları: **takvim ayları** (müşterinin saat dilimi), son N tam ay + **devam eden ay**
(`IsPartial = true`), **harcamasız ay 0** olarak listelenir, N varsayılan 6, en fazla 24. İsteğe
bağlı kategori kodu **veya** merchant (`MerchantMatcher`; Ambiguous/NotFound'da seri boş döner).

SQL: saat dilimi, (varsa) kategori ID veya merchant listesi ve **yalnız üç kolonun** (para birimi,
zaman, tutar) filtrelenmiş okuması. Satırlar pencerelerin kesin UTC sınırlarıyla C#'ta aylara
dağıtılır. Sebep: Npgsql 10, `DateTimeOffset` saat dilimi dönüşümünü
(`TimeZoneInfo.ConvertTimeBySystemTimeZoneId`) SQL'e çeviremiyor (deneme ile doğrulandı); yerel ay
LINQ ile SQL'de gruplanamıyor. Okunan satırlar tek müşteri, şahsi hesap, Purchase, filtre ve en
fazla 25 ayla sınırlıdır. PERF-02'de ölçülecek; gerekirse `AT TIME ZONE` ile ham SQL'e geçilecek.

| İstek | Sonuç |
| --- | --- |
| Ege, TRANSPORTATION | Nis 600, May 650, Haz 700, Tem 650, Ağu 900, Eyl 1.400, Eki 700 (devam eden) |
| Ege, TRAVEL | Yalnız Ağustos 1.200; diğer aylar 0 |
| Ada, "starbucks" | Nis–Eyl 185'er; Eki 375 (devam eden) |
| Deniz, filtresiz | 12.000 … 31.000; Eki 11.350; tam aylar harcama özetiyle aynı |

## REC-01–03 — Düzenli ödemeler (`RecurringPaymentService`, `RecurringPaymentDetector`)

Veritabanında abonelik alanı veya RecurringPayments tablosu yoktur; düzenli ödeme geçmiş
işlemlerden **deterministik kuralla** bulunur, LLM yalnız sonucu anlatır (RULE.md §8).

Kapsam: son 6 ay (`Last6Months`), şahsi hesaplar, `Kind = Purchase`, merchant'ı olan işlemler.
Gruplar **hesap + para birimi + merchant**. Kullanıcı kararıyla bir grup ancak hepsi sağlanırsa
düzenlidir (`RecurringPaymentDetector`, saf C#):

| # | Şart | Elediği örnek |
| --- | --- | --- |
| 1 | En az 3 ödeme | Tek seferlik / iki ödeme |
| 2 + 4 | Ardışık her iki ödeme arası 26–35 gün (yerel takvim günü) | Ayda iki kez ulaşım; ekstra kahve; **atlanan ay** (~61 gün) |
| 3 | Her tutar grubun ortanca tutarının ±%10'u içinde | Büyüyen market harcaması (12.000 → 31.000); 100 → 150 |
| 5 | Son ödeme bugünden en fazla 35 gün önce | İptal edilmiş abonelik |

Küçük zam (229,99 → 249,99) şart 3'ü geçer; sonuçta en düşük ve en yüksek tutar görünür.
Bilinen sınır: hiç ekstra ziyareti olmayan, her ay aynı gün aynı tutarda bir kahve alışverişi
abonelik gibi görünür (testte belgelendi); demo verisinde Ekim'deki ekstra Starbucks
alışverişleri bu yüzden düzeni bozar.

Çıktı: merchant, kategori (son ödemenin), hesap adı, para birimi ve `RecurringPattern`
(ödeme sayısı, son/en düşük/en yüksek tutar, ilk/son tarih, en kısa/en uzun aralık,
**tahmini** sonraki ödeme = son ödeme + 1 ay, ayın gözlenen en büyük gününe göre; ay sonu kısalırsa
sabitlenir). Para birimi başına aylık toplam = son tutarların toplamı. Sıra: son tutar büyükten küçüğe.

SQL: saat dilimi ve tek okuma sorgusu (hesap adı ve kategori join'li). Kural ödemeler arası gün
farklarına baktığı için tek tek satırlar gerekir; okunan satırlar tek müşterinin 6 aylık,
merchant'ı olan alışverişleridir.

Demo sonucu (10 Ekim 2026):

| Müşteri | Grup | Aralıklar (gün) | Sonuç |
| --- | --- | --- | --- |
| Ada | Netflix 229,99 × 6 | 30, 31, 30, 31, 31 | Düzenli; sonraki tahmin 12 Eki |
| Ada | Spotify 99,99 × 6 | 30, 31, 30, 31, 31 | Düzenli; sonraki tahmin 18 Eki |
| Ada | Starbucks 185 × 6 + 210 + 165 | 30, 31, 30, 31, 31, **16, 2** | Değil |
| Ada | Kahve Durağı 110, 120, 95 | 17, 5 | Değil |
| Deniz | Aile Marketi (12.000 → 31.000, + 4 Eki) | — | Değil |
| Ege | Şehir Ulaşım (ayda 2) | ~14 | Değil |

Ada'nın aylık toplamı: TRY 329,98.

## ANA-12 — Yaklaşan ödemeler (`UpcomingPaymentService`, 16 Ekim 2026)

Düzenli ödeme kuralının (REC-01) tahmini sonraki tarihlerinden önümüzdeki `Days` günü listeler. Kendi sorgusu
yoktur: `RecurringPaymentService`'i çağırır, hangi ödemenin düzenli olduğuna yine yalnız `RecurringPaymentDetector`
karar verir. "Bugün", düzenli ödeme sonucunun dönem sonudur (`Period.EndDate`, müşterinin saat dilimi); saat dilimi
mantığı tekrarlanmaz. Kapsam düzenli ödemelerle aynı (şahsi hesaplar, harcama, isteğe bağlı `AccountId`).

Kullanıcı kararları:

- **K1-A — gecikmişler listede:** tahmini tarihi geçmiş ama ödemesi henüz görünmeyen kalem `IsOverdue = true` ve
  negatif `DaysUntil` ile kalır (kayıt gecikebilir). Sonsuza kadar kalmaz: son ödeme 35 günden eskiyse kural onu
  iptal sayar ve listeden düşer.
- **K2-A — ufuk 1–31 gün:** aylık bir ödeme pencerede en fazla bir kez görünür; "son ödeme + 1 ay" tahmini daha
  ileri taşınmaz.
- **K3-A — `Days` isteğe bağlı, varsayılan 30:** cevap penceresini (`From` = bugün, `To` = bugün + gün, dahil) söyler.

Tahmin olduğu alan adlarında açıktır: `EstimatedDate` (kuralın `NextExpectedDate`'i) ve `EstimatedAmount` (son
ödemenin tutarı; fiyat artışı varsa yeni fiyat). Her kalem kanıtını (`Pattern`) taşır. Sıra tahmini tarih, eşitse
merchant; toplam para birimi başına `EstimatedAmount` toplamı; ödeme yoksa iki liste de boş.

| İstek | Sonuç |
| --- | --- |
| Ada, varsayılan (10 Eki) | 10 Eki – 9 Kas: Netflix 12 Eki 229,99 (+2), Spotify 18 Eki 99,99 (+8); toplam 329,98 TRY |
| Ada, 5 / 2 / 1 gün | Netflix / Netflix (12 Eki = bugün + 2, sınır dahil) / boş |
| Deniz, Ege | boş |
| Ada, bugün 14 Eki | Netflix 12 Eki gecikmiş (−2), Spotify 18 Eki (+4); toplam 329,98 |
| Ada, bugün 20 Eki | Netflix yok (son ödeme 38 gün önce), Spotify 18 Eki gecikmiş (−2); toplam 99,99 |
| Ada + geçici abonelik 100, 100, 105 (25 Tem–25 Eyl) | 25 Eki tahmini 105 (son tutar; en küçük 100 değil); toplam 434,98 |
| `Days` 0 / 32; Deniz'in dükkân hesabı; Ada + Deniz'in hesabı | `ArgumentException` (`Days`); `ArgumentException`; `KeyNotFoundException` |

Sınırlar: yalnız aylık kalıp tanınır (yıllık abonelik, düzensiz fatura, 3 ödemesi olmayan yeni abonelik görünmez);
tutar tahmini yaklaşan bir fiyat değişikliğini bilemez.

## ANA-13 — Bütçe kontrolü (`BudgetCheckService`, 16 Ekim 2026)

Bütçe kullanıcının sorusundan argüman olarak gelir ve **saklanmaz** (tablo yok). Harcanan tutar mevcut analizlerden
alınır: toplam için `SpendingSummaryService`, kategori için `CategoryBreakdownService`, merchant için
`MerchantSpendingService` (eşleştirme dahil). Böylece bütçedeki "harcanan" bu analizlerin aynı soruya verdiği rakamla
her zaman aynıdır (testle korunur); servis yalnız bütçe aritmetiğini ekler. Kategori kodu kontrolü ortak
`CategoryLookup`'tadır (aylık seri ve arama da kullanır; bu adımda iki kopyadan birleştirildi).

Kullanıcı kararları:

- **K1-A — hedef:** toplam (kategori ve merchant verilmezse), kategori **ya da** merchant; ikisi birlikte `ArgumentException`.
- **K2-A — yalnız bu ay** (müşterinin saat dilimine göre devam eden ay).
- **K3-A — ortalama bugün dahil:** `DailyAverage` = harcanan / geçen gün (`ElapsedDays` = bugünün günü; ayın 1'inde 1).
  `ProjectedMonthEnd` = harcanan × ay gün sayısı / geçen gün, yuvarlanmış ortalamadan değil tam orandan
  (`ProjectionMethod = "LinearDailyAverage"`).
- **K4-A — para birimi zorunlu:** harcanan yalnız o para biriminde; bütçe para birimleri arasında karşılaştırılmaz.

`RemainingAmount` = max(0, bütçe − harcanan), `OverAmount` = max(0, harcanan − bütçe), `ProjectedOverAmount` =
max(0, tahmin − bütçe); tutarlar 2 ondalık, `AwayFromZero`. Harcama yoksa **gerçek 0** döner (soru bir bütçe hakkında;
uydurma satır değil). Merchant eşleşmezse `Figures = null`, `MerchantMatch` nedenini söyler.

| İstek (10 Ekim; 10/31 gün) | Harcanan | Kalan / aşım | Ort. | Ay sonu | Tahmini aşım |
| --- | --- | --- | --- | --- | --- |
| Ada, FOOD, 1.000 TRY | 590 (4) | 410 / 0 | 59 | 1.829 | 829 |
| Ada, toplam, 1.000 TRY | 735 (5) | 265 / 0 | 73,50 | 2.278,50 | 1.278,50 |
| Ada, "starbucks", 300 TRY | 375 (2) | 0 / 75 | 37,50 | 1.162,50 | 862,50 |
| Ada, TRANSPORTATION, 500 TRY · Ada, toplam, 100 USD | 0 | bütçenin tamamı / 0 | 0 | 0 | 0 |
| Deniz, GROCERY, 10.000 TRY | 9.500 (1) | 500 / 0 | 950 | 29.450 | 19.450 |
| Ada, FOOD, 1.000 TRY, bugün 31 Ekim | 590 | 410 / 0 | 19,03 | 590 (tam orandan; 19,03 × 31 = 589,93 değil) | 0 |
| Ege, "şehir" | — | — | — | — | `Ambiguous`, rakam yok |

Sınırlar: tahmin doğrusaldır, ay içi düzensizliği (maaş günü, ay sonu faturası) bilmez ve ayın başında çok oynaktır;
"kalan günlerde günde en fazla X" alanı yok (istenirse eklenir); bütçe kalıcı değil (ayrı karar).

## ANA-14 — Tasarruf simülasyonu (`SavingsSimulationService`, 16 Ekim 2026)

"Starbucks'ı yarıya indirsem ne kazanırım?": son N tam ayın aylık ortalaması × azaltma oranı → aylık ve yıllık tahmini
tasarruf. **Yatırım tavsiyesi ya da getiri hesabı değildir**: faiz, enflasyon, yatırım yok (o, fırsat maliyeti kolu);
tek varsayım geçmiş ortalamanın sürmesidir ve cevapta adıyla yazılıdır (`Method = "FullMonthAverageTimesReduction"`).
Aylık tutarlar `SpendingComparisonService.GetMonthlyTrendAsync`'ten gelir (aylık seriyle her zaman aynı); ay sınırı,
kategori kodu, kategori+merchant yasağı, merchant eşleştirme ve hesap kapsamı onun kurallarıdır. Servisin tek yeni kuralı
azaltma oranıdır (tam sayı, 1–100).

Kullanıcı kararları:

- **K1-A — hedef:** toplam (kategori ve merchant verilmezse), kategori ya da merchant (ANA-13 ile aynı).
- **K2-A — boş aylar ortalamada:** ortalama = toplam / N; tek seferlik bir harcama her aya yayılmış sayılmaz.
- **K3-A — ay sayısı isteğe bağlı, varsayılan 6** (aylık serinin sınırı 1–24). Devam eden ay hiç ortalamaya girmez;
  `From`–`To` kullanılan tam ayları, `MonthlyAmounts` ay ay tutarları gösterir.
- **K4-A — para birimi başına satır, parametre yok:** kullanıcı tutar değil oran verdiği için karşılaştırılacak bir eşik
  yok; analizlerin genel kuralı geçerli. Seçilen aylarda harcaması olmayan para birimi listelenmez (uydurma 0 yok).

Tasarruflar yuvarlanmış ortalamadan değil tam toplamdan: aylık = toplam × oran / (100 × N), yıllık = toplam × oran × 12 /
(100 × N); 2 ondalık, `AwayFromZero`.

| İstek (10 Ekim; Nisan–Eylül) | Toplam | Aylık ort. | Aylık tasarruf | Yıllık |
| --- | --- | --- | --- | --- |
| Ada, Starbucks, %50 | 1.110 | 185 | 92,50 | 1.110 |
| Ada, toplam, %10 | 3.199,88 | 533,31 | 53,33 | 639,98 |
| Ege, TRAVEL, %50 (yalnız Ağu 1.200) | 1.200 | 200 | 100 | 1.200 |
| Ege, TRANSPORTATION, %10 | 4.900 | 816,67 | 81,67 | 980 (81,67 × 12 = 980,04 değil) |
| Deniz, GROCERY, %20, 3 ay (Tem–Eyl) | 73.000 | 24.333,33 | 4.866,67 | 58.400 |
| Ada, Starbucks, %100, 1 ay (Eyl) | 185 | 185 | 185 | 2.220 |
| Ada, CLOTHING · Ege, "şehir" | — | — | — | boş liste (`Ambiguous` için `MerchantMatch`) |

Sınırlar: fiyat artışı, mevsimsellik ve davranış değişimi hesaba katılmaz. MCP-18 (21 Ekim 2026) sonuca kategorinin Türkçe adını
(`CategoryDisplayName`, bütçe kontrolündeki gibi kategori tablosundan) ekledi; AI servisi adların ikinci bir kopyasını tutmaz.

## ANA-10 — Kavramsal merchant harcaması (kapalı küme, 16 Ekim 2026)

"Kahveye ne kadar harcadım?": "kahve" bir merchant ya da kategori değil, bir kavram. Çözüm kapalı küme: backend
müşterinin kendi merchant listesini verir (`MerchantSpendingService.ListAsync`), model bu listeden kavrama uyanları seçer
ve yeni ad uyduramaz; backend her adı listeyle doğrular, toplamı `PurchaseScope` kapsamında hesaplar ve dahil ettiği
merchant'ları döndürür (`GetGroupSpendingAsync`). Embedding/RAG gerekmez.

Kullanıcı kararları:

- **K1-A — doğrulama:** `MerchantMatcher.MatchExact` (eşleştiricinin yalnız tam, sadeleştirilmiş adımı): büyük/küçük harf,
  boşluk ve Türkçe işaretler tolere edilir ("kahve duragi" → "Kahve Durağı"); **kısmi eşleşme yok** ("Starbuck" hata).
  `Match` artık ilk adımında `MatchExact`'i kullanır (refactor; mevcut testler değişmedi).
- **K2-A — liste içeriği:** kanonik ad, son alışverişin kategorisi (kod + ad; yoksa "Sınıflandırılmamış"), son alışveriş
  tarihi (müşterinin yerel günü), toplam işlem sayısı; ordinal sırada.
- **K3-A — liste tüm zamanlar:** doğrulama kümesiyle aynı (eşleştirme adayları da tüm zamanlardır); dönem harcamada uygulanır.
- **K4-A — 1–20 ad:** aynı merchant'a çıkan tekrarlar tek sayılır (hata değil).

Liste dışı ad: `ArgumentException` (`Merchants`) "'X' müşterinin merchant listesinde yok."; iki kanonik ad aynı
sadeleşmiş yazıma çıkarsa "birden çok merchant'a karşılık geliyor". Sonuç `MerchantGroupSpending(Period, Merchants,
Currencies)`: `Merchants` dahil edilen kanonik adlar (istek sırası, tekrarsız); `Currencies` merchant dökümüyle aynı biçim
(`CurrencyMerchantBreakdown`, ortak `BreakdownAsync`); dönemde harcaması olmayan merchant listede var ama satırı yok.
SQL'e modelin metni değil kanonik adlar gider.

| İstek | Sonuç |
| --- | --- |
| Liste, Ada | Hobi Dükkânı (OTHER, 22.06.2025, 1), Kahve Durağı (FOOD, 07.10.2026, 3), Netflix (SUBSCRIPTION, 12.09.2026, 9), Spotify (SUBSCRIPTION, 18.09.2026, 9), Starbucks (FOOD, 08.10.2026, 9) |
| Liste, Deniz | Aile Marketi, Akaryakıt İstasyonu, Elektrik Faturası, Çocuk Giyim, Şehir Ulaşım (dükkân satışları yok) |
| Ada, bu ay, {Starbucks, Kahve Durağı} | 590 TRY: Starbucks 375 (%63,56), Kahve Durağı 215 (%36,44) |
| Ada, son 3 ay, aynı | 1.255: Starbucks 930 (5), Kahve Durağı 325 (3) |
| Ada, bu ay, {Netflix, Starbucks} | `Merchants` ikisi; döküm yalnız Starbucks 375 (%100) |
| Ada, {"starbucks", "kahve duragi"} · {Starbucks, "starbucks"} | kanonik iki ad, 590 · tek ad, 375 |
| Ege, son 6 ay, {Şehir Ulaşım, Şehirlerarası Otobüs} | 6.800: 5.600 (13) %82,35, 1.200 (1) %17,65 |
| "Starbuck", Deniz'in "Aile Marketi", boş liste, 21 ad | `ArgumentException` (`Merchants`) |

Sınırlar: kavrama hangi merchant'ların girdiğini model seçer (backend adların gerçekliğini ve hesabı garanti eder,
seçimin doğruluğunu değil; cevap dahil edilenleri söyler); merchant'ı olmayan alışveriş hiçbir gruba giremez; liste
sayfalanmaz (büyük müşteride PERF/MCP adımında bakılır).

## ANA-08 — Kenar durumları (`AnalyticsEdgeCaseTests`)

Geçici kayıtlar testin kendi DB transaction'ında eklenir ve rollback edilir; demo verisi 116
işlemde kalır.

- **Hesapsız müşteri** (Demo Customer): yedi servis hata vermeden boş sonuç döner; aylık seride
  aylar vardır ama para birimi serisi yoktur.
- **İstanbul gece yarısı:** 31 Ağu 23:59:59 / 1 Eyl 00:00 (karşılaştırılan iki ayın birleştiği an) ve
  30 Eyl 23:59:59 / 1 Eki 00:00. Özet, karşılaştırma (`CASE WHEN`), aylık seri (C# kovalama) ve
  arama (`LocalDate`) aynı ayı seçer. Kayıtlar kategorisizdir; "Sınıflandırılmamış" grubu da doğrulanır.
- **İkinci para birimi:** USD abonelik + tek seferlik alışveriş kategori, merchant, karşılaştırma,
  seri ve düzenli ödemede ayrı satır/seri/toplam olarak çıkar; TRY değerleri değişmez.

Testler mevcut davranışı kapsadığı için ilk çalıştırmada yeşildi. Koruma gücü kasıtlı hata
yerleştirmeyle ölçüldü (sınırda `<` → `<=`, `>=` → `>`): ilk sürüm karşılaştırmadaki dönem
sınırını yakalamıyordu, çünkü geçici kayıtlar yalnız Eylül/Ekim sınırındaydı; 31 Ağustos sınırı
eklendikten sonra dört kasıtlı hatanın hepsi testleri kırmızıya çevirdi.
