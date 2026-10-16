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
