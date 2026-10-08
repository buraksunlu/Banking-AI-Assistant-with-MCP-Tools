# Database Foundation — Güncel Tasarım

Son güncelleme: 8 Ekim 2026.

Bu dosya hedef veri modelini ve gerçek mevcut durumu ayrı anlatır. Hedefte altı
domain tablosu vardır: `Customers`, `Accounts`, `TransactionCategories`,
`Transactions`, `MarketAssets`, `DailyMarketPrices`. EF'nin yönettiği
`__EFMigrationsHistory` domain tablosu değildir.

## Gerçek mevcut durum

Aktif veritabanı `bankingai_foundation`dır. Şu altı domain tablosu uygulanmıştır:

- `Customers`
- `Accounts`
- `TransactionCategories`
- `Transactions`
- `MarketAssets`
- `DailyMarketPrices`

İki market tablosu da boştur. Kategori/asset seed'i, synthetic banking seed'i
ve historical market import'u henüz yok.

Aktif veritabanında yedi migration uygulanmıştır:

1. `CreateCustomers`
2. `AddCustomerPreferences`
3. `AddAccountsAndTransactionCategories`
4. `AddTransactions`
5. `SplitCustomerName`
6. `AddMarketAssets`
7. `AddDailyMarketPrices`

Migration yaklaşımı incremental'dır: her schema değişikliği yeni bir migration
olarak eklenir. Uygulanmış migration'lar silinmez veya geriye dönük değiştirilmez;
veritabanı tek başlangıç migration'ına dönmek için resetlenmez.

## İlişkiler

```text
Customer 1 ─── N Account 1 ─── N Transaction N ─── 0..1 TransactionCategory

MarketAsset 1 ─── N DailyMarketPrice
```

Merchant, RecurringPayment, InvestmentSimulation ve AI/MCP özel tabloları ilk
kapsamda yoktur. Recurring ve yatırım karşılaştırmaları mevcut veriden runtime
hesaplanacaktır.

## Customers

Synthetic müşteriyi temsil eder. Gerçek TCKN, adres, telefon veya banka müşteri
verisi tutulmaz.

| Alan | C# | PostgreSQL | Kural |
| --- | --- | --- | --- |
| Id | `Guid` | `uuid` | PK, required, uygulama üretir |
| FirstName | `string` | `varchar(100)` | Required, boş olamaz |
| LastName | `string` | `varchar(100)` | Required, boş olamaz |
| CustomerNumber | `string` | `varchar(32)` | Required, unique, sequence ile CUST-1 biçimi |
| PreferredCurrency | `string` | `varchar(3)` | Required, TRY/USD/EUR; görüntüleme tercihi |
| TimeZone | `string` | `varchar(100)` | Required, PostgreSQL'in tanıdığı saat dilimi |
| CreatedAt | `DateTimeOffset` | `timestamptz` | Required, UTC |

`PreferredCurrency` hesap para birimini veya kur dönüşümünü değiştirmez.
`TimeZone` UTC kayıtların gösterim/takvim tercihidir.

## Accounts

Bir müşterinin belirli para birimindeki hesabını temsil eder.

| Alan | C# | PostgreSQL | Kural |
| --- | --- | --- | --- |
| Id | `Guid` | `uuid` | PK, required |
| CustomerId | `Guid` | `uuid` | Required FK → `Customers.Id` |
| AccountName | `string` | `varchar(100)` | Required, boş olamaz |
| AccountNumber | `string` | `varchar(50)` | Required, unique dummy identifier |
| CurrencyCode | `string` | `char(3)` | Required, TRY/USD/EUR/GBP |
| Balance | `decimal` | `numeric(18,2)` | Required, yeni hesapta 0 |
| CreatedAt | `DateTimeOffset` | `timestamptz` | Required, UTC |

Gerçek IBAN üretilmez. İşlem bulunan hesabın currency'si değiştirilemez.
Negatif bakiye ve yetersiz bakiye politikası henüz kesinleşmemiştir.

## TransactionCategories

Transaction içindeki serbest kategori metni yerine kullanılan lookup tablosudur.

| Alan | C# | PostgreSQL | Kural |
| --- | --- | --- | --- |
| Id | `short` | `smallint` | PK, required, sabit seed değeri, `> 0` |
| Code | `string` | `varchar(50)` | Required, unique, kararlı uygulama kodu |
| DisplayName | `string` | `varchar(100)` | Required, kullanıcıya görünen ad |

İlk seed kodları: `FOOD`, `GROCERY`, `CLOTHING`, `TRANSPORTATION`,
`ENTERTAINMENT`, `SUBSCRIPTION`, `TRAVEL`, `FUEL`, `BILLS`, `HEALTH`, `OTHER`.

`CategoryId = null`, işlemin henüz kategorize edilmediği anlamına gelir.
`OTHER`, değerlendirilmiş fakat diğer kategorilere uymayan işlemdir; otomatik
null yerine atanmaz. LLM yeni kategori oluşturamaz.

## Transactions

Bir hesabın para girişini veya çıkışını ve kategorilendirme bilgisini tutar.

| Alan | C# | PostgreSQL | Required / nullable ve kural |
| --- | --- | --- | --- |
| Id | `Guid` | `uuid` | PK, required |
| AccountId | `Guid` | `uuid` | Required FK → `Accounts`; currency ile birlikte eşleşir |
| CategoryId | `short?` | `smallint` | Nullable FK → `TransactionCategories.Id` |
| Amount | `decimal` | `numeric(18,2)` | Required, `> 0` |
| Direction | `TransactionDirection` | `smallint` | Required; Debit=1, Credit=2 |
| CurrencyCode | `string` | `char(3)` | Required; hesabın currency'siyle aynı |
| RawDescription | `string` | `varchar(500)` | Required, ham açıklama korunur |
| NormalizedMerchant | `string?` | `varchar(150)` | Nullable |
| CategorizationSource | `CategorizationSource?` | `smallint` | Nullable; Rule=1, Llm=2, Manual=3 |
| CategoryConfidence | `decimal?` | `numeric(5,4)` | Nullable; varsa 0–1 |
| CategorizedAt | `DateTimeOffset?` | `timestamptz` | Nullable, varsa UTC |
| OccurredAt | `DateTimeOffset` | `timestamptz` | Required, işlem zamanı UTC |
| CreatedAt | `DateTimeOffset` | `timestamptz` | Required, kayıt zamanı UTC |

`Amount` negatif yapılarak yön belirtilmez. Debit tek başına alışveriş kanıtı
değildir; transfer ve iade kapsamı ayrıca kararlaştırılacaktır.

Transaction foreign key'i `(AccountId, CurrencyCode)` üzerinden
`Accounts(Id, CurrencyCode)` alternatif anahtarına bağlanır. Böylece USD işlem
TRY hesabına yazılamaz. Kategori bağlantısı nullable ve `RESTRICT` davranışlıdır.

Kategorilendirme alanları ilk importta null olabilir. İleride CategoryId,
CategorizationSource, CategoryConfidence ve CategorizedAt aynı application
işleminde tutarlı şekilde güncellenecektir.

## MarketAssets

Tarihsel fiyatı izlenen varlığı tanımlar.

| Alan | C# | PostgreSQL | Kural |
| --- | --- | --- | --- |
| Id | `short` | `smallint` | PK, sabit seed değeri |
| Symbol | `string` | `varchar(30)` | Required, unique |
| Name | `string` | `varchar(100)` | Required |
| AssetType | `string` | `varchar(30)` | Required; Currency veya Commodity |
| BaseUnit | `string` | `varchar(20)` | Required; USD, EUR veya GRAM |
| QuoteCurrency | `string` | `char(3)` | Required; ilk kapsamda TRY |
| IsActive | `bool` | `boolean` | Required |

İlk asset'ler: `USDTRY` (USD), `EURTRY` (EUR), `GRAM_GOLD_TRY` (GRAM).
OHLC, volume veya asset başına birden fazla fiyat serisi ilk kapsamda yoktur.

### MA-03 — Tür, para birimi ve seed eşleşmesi

`MarketAssetConfiguration.cs` içindeki `CK_MarketAssets_AssetType` yalnızca
`Currency` ve `Commodity` değerlerini; `CK_MarketAssets_QuoteCurrency` yalnızca
`TRY` değerini kabul eder. Bu kurallar `AddMarketAssets` migration'ıyla aktif
veritabanına uygulanmıştır. `IsRequired()` eşlemeleri NULL değerleri de engeller.
Bu DB kuralları C# nesnesi oluşturulurken çalışan doğrulamalar değildir.

Seed aşamasında her kaydın aşağıdaki dört alanı birlikte kontrol edilecek:

| Symbol | AssetType | BaseUnit | QuoteCurrency |
| --- | --- | --- | --- |
| USDTRY | Currency | USD | TRY |
| EURTRY | Currency | EUR | TRY |
| GRAM_GOLD_TRY | Commodity | GRAM | TRY |

`BaseUnit` fiyatlanan birimdir; `QuoteCurrency` fiyatın ifade edildiği para
birimidir. Örneğin USDTRY fiyatı, 1 USD karşılığındaki TRY tutarını ifade eder.
Varlık türü veya para birimi için ayrı lookup tablo oluşturulmaz.

Mevcut DB kısıtları bu dört alanın birbirine uyumunu denetlemez. Örneğin
`USDTRY / Currency / EUR / TRY` kaydı DB tarafından kabul edilir; seed kodu
bu eşleşmeyi reddetmelidir. SEED-04/SEED-05 kapsamında beklenen eşleşmelerle
hem yazılacak kayıtlar hem mevcut kayıtlar karşılaştırılacak; uyuşmazlıkta
sessiz düzeltme veya atlama yerine açıklayıcı hata verilecektir.

8 Ekim 2026 doğrulaması: PostgreSQL'de üç geçerli eşleşme kabul edildi;
Stock, Bond, küçük harfli currency türleri ile USD ve küçük harfli try quote
değerleri beklenen CHECK kısıtlarınca reddedildi. USDTRY/EUR uyumsuzluğunun
mevcut DB tarafından kabul edildiği de doğrulandı. Bütün deneme kayıtları
aynı transaction içinde ROLLBACK edildi; tablo boş kaldı. Seed henüz
uygulanmadığından gerçek seed eşleşme doğrulaması tamamlanmış değildir.

## DailyMarketPrices

Bir asset'ın belirli piyasa günündeki günlük referans fiyatını tutar.

| Alan | C# | PostgreSQL | Kural |
| --- | --- | --- | --- |
| Id | `long` | `bigint` | PK, identity |
| MarketAssetId | `short` | `smallint` | Required FK → `MarketAssets.Id` |
| PriceDate | `DateOnly` | `date` | Required, piyasa takvim günü |
| Price | `decimal` | `numeric(20,8)` | Required, pozitif ve sonlu |
| Source | `string?` | `varchar(100)` | Nullable, gerçek importta doldurulur |

`(MarketAssetId, PriceDate)` birleşik unique index olarak uygulandı. Aynı
alanlara ek normal index yoktur. MarketAssets ilişkisi `ON DELETE RESTRICT`
kullanır: fiyat kaydı bulunan varlık silinemez. Eksik hafta sonu/tatil günleri
rastgele fiyatla doldurulmaz.

8 Ekim 2026 MP-03/04/05 doğrulaması: AddDailyMarketPrices migration'ı ve
üretilen SQL incelenerek aktif DB'ye uygulandı. Geçici .NET/EF programında
aynı varlık/gün, olmayan varlık, sıfır/negatif fiyat ve NaN reddedildi.
Infinity/-Infinity değerleri numeric(20,8) tipi tarafından reddedildi.
Fiyat kaydı bulunan varlığı silme denemesi RESTRICT tarafından engellendi.
Farklı gün ve farklı varlık kayıtları kabul edildi. EF üzerinden DateOnly
UTC ve Europe/Istanbul oturumlarında 2026-10-08 olarak okundu; sekiz ondalıklı
decimal değer, nullable Source ve otomatik identity doğrulandı. Test
kayıtları transaction sonunda geri alındı; iki market tablosu boş kaldı.
Identity sequence'in tükettiği numaralar rollback ile geri dönmez; ID
boşlukları normaldir. Bu kontroller kalıcı otomatik test projesi değildir.

## Ortak kurallar

- Foreign key'ler `RESTRICT` davranışlıdır; finansal kayıtlar zincirleme silinmez.
- Account/Transaction currency kodları TRY, USD, EUR, GBP'dir; `TL` kullanılmaz.
- Finansal değerler `decimal`/PostgreSQL `numeric` kullanır; float/double kullanılmaz.
- Zamanlar UTC `DateTimeOffset`/`timestamptz`; market günü `DateOnly`/`date` olur.
- Transaction ve Account balance güncellemesi ileride aynı DB transaction'ında yapılır.
- Index'ler gerçek query pattern'lerine göre eklenir; gereksiz index oluşturulmaz.

## Historical price fallback

Exact fiyat yoksa aynı asset için işlem tarihinden önceki en yakın fiyat kullanılır:

```sql
WHERE "MarketAssetId" = @assetId
  AND "PriceDate" <= @transactionDate
ORDER BY "PriceDate" DESC
LIMIT 1
```

İlk fiyat işlemden sonraysa geriye uygulanmaz. Önceki fiyat yoksa sonuç eksik
veri olarak raporlanır; rastgele fiyat üretilmez.

## Opportunity-cost kullanımı

Uygun sorgularda backend ilgili TRY Debit işlemlerini ve kategoriyi bulur, her
harcama tarihinde seçilen asset fiyatını seçer, `Amount / Price` ile birimleri
hesaplar ve değerleme tarihindeki fiyatla çarpar. Harcama toplamı, birimler,
değer, fark, yüzde, kaynak ve kullanılan fiyat tarihleri döner.

Bu hesap runtime yapılır; InvestmentSimulations tablosu yoktur. Karşılaştırma
her sorguda çalıştırılmaz; hangi sorgularda önerileceği ayrıca netleştirilecektir.
LLM yalnızca backend sonucunu açıklar.

## Uygulama sırası

1. MarketAsset entity → configuration → migration.
2. DailyMarketPrice entity → configuration → migration.
3. Kategori ve MarketAsset seed'i.
4. Kontrollü synthetic banking seed'i.
5. Üç asset için en az iki yıllık gerçek historical import.
6. Database foundation testleri ve kabul review'ü.
7. Bundan sonra controller-based API.

Her adım kullanıcı review'ünden sonra başlar; hedef listesi implementation izni
anlamına gelmez.
