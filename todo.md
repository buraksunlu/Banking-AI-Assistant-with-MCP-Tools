# Proje TODO — AI-Powered Transaction Intelligence Platform

Son güncelleme: 12 Ekim 2026.

Bu dosya projenin tek planıdır ve küçük, uygulanabilir işlerin listesidir.
RULE.md çalışma kurallarını tutar. En son açık kullanıcı kararı, eski taslaklardan
önceliklidir. Bu listedeki gelecek işler henüz uygulanmış değildir.

## Nasıl ilerleyeceğiz?

- Her seferinde tek numaralı madde veya kullanıcının açıkça seçtiği küçük kapsam yapılır.
- Önce neyin, neden, hangi dosyada değişeceği açıklanır; sonra kod yazılır ve kontrol edilir.
- Kullanıcı kodu inceler. Bir sonraki adıma yeni mesajı gelmeden geçilmez.
- `[x]` yapıldı, `[ ]` yapılacak demektir. Bir karar maddesi kod yazma yetkisi değildir.
- Madde ancak belirtilen çıktı gerçekten elde edilince işaretlenir. Test sonucu ve
  bilinen sınır gerekiyorsa maddeye eklenir; yalnızca build geçmesi DB doğrulaması değildir.
- Banking foundation 10 Ekim 2026'da kabul edildi (RULE.md §26). Market data
  (SRC/IMP/MP-06–08) ayrı, engellemeyen koldur; yalnızca OC maddeleri onu bekler.
- Deterministik analytics doğrulanmadan onu açıklayan AI/MCP tool'u yazılmaz.
- Gelecekteki dosya adları öneridir; ihtiyaç oluşmadan boş klasör ve katman açılmaz.

## 0. Şimdiye kadar yapılanlar

Bu bölüm mevcut kaynak dosyaları, bu sohbetin doğrulama sonuçları ve 8 Ekim'deki
canlı PostgreSQL kontrolüne dayanır. Silinen eski uygulamanın testleri buna dahil değildir.

- [x] DONE-01 — Önceki uygulama temizlendi; database-first başlangıca geçildi.
- [x] DONE-02 — Önce veritabanı, sonra uygulama katmanları sırası todo.md içine yazıldı.
- [x] DONE-03 — Küçük adımlar ve kullanıcı review kuralı belirlendi; aktarılmayı bekleyen metin docs/rule-update.md içinde tutuldu. RULE.md ile birleştirme 8 Ekim 2026 tarihinde tamamlandı (DOC-04).
- [x] DONE-04 — .NET 10 veri/console projesi oluşturuldu: src/BankingAI.Data.
- [x] DONE-05 — EF Core, Npgsql ve yerel dotnet-ef aracı eklendi.
- [x] DONE-06 — Yerel PostgreSQL üzerinde bankingai_foundation veritabanı ve ayrı rol oluşturuldu. Eski bankingai veritabanı 8 Ekim 2026'da doğrulanıp silindi.
- [x] DONE-07 — Bağlantı bilgisi .env üzerinden ortam değişkenine aktarılıyor. .env.example ve .gitignore hazır; uygulama .env dosyasını kendiliğinden okumuyor.
- [x] DONE-08 — BankingDbContext oluşturuldu. Program.cs içinde migrate ve show komutları hazır.
- [x] DONE-09 — Customer sınıfı ve Customers tablosu oluşturuldu; bir synthetic demo müşteri var.
- [x] DONE-10 — CustomerNumber, PreferredCurrency ve TimeZone eklendi. Numara sequence ile üretiliyor; unique/currency/timezone kısıtları var.
- [x] DONE-11 — Customer kısıtları ve 12 eşzamanlı numara üretimi denendi; test kayıtları geri alındı. Sequence numaralarında boşluk olması normal.
- [x] DONE-12 — Account.cs oluşturuldu. CustomerId için boş GUID kontrolü ve sıfır başlangıç bakiyesi var.
- [x] DONE-13 — TransactionCategory.cs oluşturuldu: Id, Code, DisplayName. Kategori seed'i henüz yok.
- [x] DONE-14 — AccountConfiguration ve TransactionCategoryConfiguration yazıldı; DbContext'e bağlandı.
- [x] DONE-15 — Accounts ve TransactionCategories tabloları migration ile oluşturuldu. FK, RESTRICT, unique, currency, null, uzunluk ve temel check kontrolleri denendi.
- [x] DONE-16 — TransactionDirection ve CategorizationSource enum'ları oluşturuldu.
- [x] DONE-17 — Transaction.cs oluşturuldu. Boş AccountId ve sıfır/negatif Amount reddi küçük geçici kontrol programında denendi.
- [x] DONE-18 — TransactionConfiguration ve DbSet<Transaction> eklendi; Transactions tablosu oluşturuldu.
- [x] DONE-19 — Transaction → Account ilişkisi AccountId + CurrencyCode üzerinden kuruldu. Accounts üzerinde Id + CurrencyCode alternatif anahtarı var.
- [x] DONE-20 — Transaction → Category nullable ilişkisi, numeric/timestamp eşlemeleri, enum/confidence/tutar kısıtları ve index'ler eklendi.
- [x] DONE-21 — Kategorisiz ve kategorili işlem yazımı ile 13 geçersiz veri kontrolü gerçek PostgreSQL'de denendi; test kayıtları geri alındı.
- [x] DONE-22 — Yapılan entity/migration değişiklikleri derlendi; Transaction entity adımında 0 hata, 0 uyarı görüldü.
- [x] DONE-23 — pgAdmin'de aktif veritabanı, Refresh, Columns ve View/Edit Data kullanımı açıklandı.
- [x] DONE-24 — Mevcut klasörlerin, entity/configuration/DbContext/migration ayrımının açıklaması yapıldı.

### Gerçek mevcut durum

| Öğe | Durum |
| --- | --- |
| Aktif DB | bankingai_foundation |
| Domain tabloları | Customers, Accounts, TransactionCategories, Transactions, MarketAssets, DailyMarketPrices |
| EF yardımcı tablosu | __EFMigrationsHistory |
| Uygulanmış migration | 11 |
| Veri | 4 Customer; 7 Account (1 Business); 11 Category; 116 Transaction (44 Credit, 72 Debit; 9 çift) |
| Hesap bakiyeleri | Ada TRY 577490.18; Ada USD 500; Deniz TRY 522900; Kahve Durağı 2595; Ege EUR 1000; Ege TRY 273380; boş TRY 0 |
| Market tabloları | MarketAssets mevcut ve üç seed kaydı var; DailyMarketPrices henüz boş |
| Bakiye güncelleme akışı | `TransactionSeedWriter` insert ve atomik bakiye delta'sını aynı DB transaction'ında yazar; BAL-03/04 PostgreSQL kabul testleri geçti |
| Kalıcı otomatik test projesi | tests/BankingAI.Data.Tests; C# ve rollback edilen PostgreSQL kontrolleri |
| API / SwiftUI / AI / MCP | Henüz yok |

Migration geçmişi:
1. 20261007142113_CreateCustomers
2. 20261007143550_AddCustomerPreferences
3. 20261008083922_AddAccountsAndTransactionCategories
4. 20261008090052_AddTransactions
5. 20261008111025_SplitCustomerName
6. 20261008124728_AddMarketAssets
7. 20261008132653_AddDailyMarketPrices
8. 20261009104108_EnforceInputValidation
9. 20261010183748_AddTransactionKind
10. 20261010190233_AddAccountType
11. 20261012071956_AddCustomerCredentials

## 1. Mevcut kararları ve belgeleri tutarlı hale getir

- [x] DOC-01 — Customer kapsamı netleştirildi: `FirstName` ve `LastName` required alanları kullanılacak; mevcut CustomerNumber, PreferredCurrency, TimeZone ve CreatedAt korunacak.
- [x] DOC-02 — Customer entity, DbContext eşlemesi, console çıktısı ve veri dönüşümlü `SplitCustomerName` migration'ı güncellendi. Mevcut `Demo Customer` kaydı `Demo` + `Customer` olarak taşınacak; eski migration dosyaları değiştirilmedi.
- [x] DOC-03 — README gerçek mevcut duruma getirildi. Silinmiş API, script, seed ve test sonuçları kaldırıldı; mevcut console ve migration komutları yazıldı.
- [x] DOC-04 — RULE.md ile docs/rule-update.md kuralları 8 Ekim 2026 tarihinde birleştirildi. Küçük adım/review, database-first, altı tablo ve hesaplama kararları güncellendi; eski tamamlanma kayıtları düzeltildi.
- [x] DOC-05 — docs/database-design.md altı tablo kararına göre güncellendi. Nullable CategoryId, FirstName/LastName, güncel tipler ve gerçek mevcut durum ayrı gösteriliyor.
- [x] DOC-06 — Eski ayrı yol haritası dosyası kaldırıldı; todo.md projenin tek planı oldu. Kategori ve opportunity-cost kararları son kullanıcı kararıyla uyumlu tutuldu.
- [x] DOC-07 — Incremental migration yaklaşımı seçildi. Mevcut migration geçmişi korunacak; her schema değişikliği yeni migration olarak eklenecek. Veritabanı resetlenmeyecek ve uygulanmış migration dosyaları geriye dönük değiştirilmeyecek.
- [x] DOC-08 — Yerel Git takibi oluşturuldu. .env ve build çıktıları dışarıda doğrulandı; remote/push ayrı karar olarak bırakıldı.
- [x] DOC-09 — 10 Ekim 2026 kullanıcı kararı: banking foundation kabul edildi; market data (bölüm 8–9) ayrı, engellemeyen kola alındı. Yalnızca opportunity-cost (bölüm 13, MCP-11, EVAL-04, FIN-08) market verisini bekler. RULE.md §26 güncellendi; kod/DB değişmedi.

## 2. MarketAsset — tek sınıf, sonra eşleme, sonra tablo

- [x] MA-01 — MarketAsset.cs oluşturuldu ve açıklandı. Id: short; Symbol: string; Name: string; AssetType: string; BaseUnit: string; QuoteCurrency: string; IsActive: bool. Proje 0 hata, 0 uyarıyla derlendi. Yalnızca entity hazır; EF eşlemesi, tablo ve veri doğrulama kuralları henüz eklenmedi.
- [x] MA-02 — MarketAssetConfiguration oluşturuldu. Id smallint ve otomatik üretilmez; Symbol varchar(30) unique; Name varchar(100); AssetType varchar(30); BaseUnit varchar(20); QuoteCurrency char(3); IsActive boolean. Zorunluluk, pozitif ID, boş metin, Currency/Commodity ve TRY kısıtları tanımlandı. Derleme: 0 hata, 0 uyarı. DbContext kaydı ve migration henüz yok; DB kısıtları uygulanmadı/doğrulanmadı.
- [x] MA-03 — Currency/Commodity ve quote TRY kısıtları aktif PostgreSQL'de doğrulandı. USDTRY, EURTRY ve GRAM_GOLD_TRY seed kayıtlarının Symbol/AssetType/BaseUnit/QuoteCurrency eşleşmeleri doğrulandı; yanlış BaseUnit ve AssetType mevcut kayıtları sessizce düzeltilmeden hata verdi. DB bu alanlar arasındaki tüm kombinasyon ilişkisini değil, temel alan kısıtlarını korur; seed doğrulaması bunu tamamlar. Ek lookup tablo yok.
- [x] MA-04 — DbSet ve configuration kaydını ekle; yalnızca MarketAssets tablosunu ekleyen migration üretip SQL içeriğini incele.
- [x] MA-05 — Migration'ı aktif DB'ye uygula; pgAdmin'de kolonları göster. Unique, required ve uzunluk kısıtlarını rollback edilen kayıtlarla doğrula.

## 3. DailyMarketPrice — günlük fiyat tablosu

- [x] MP-01 — DailyMarketPrice.cs oluşturuldu: Id long (private set), MarketAssetId short, PriceDate DateOnly, Price decimal, Source string?. DateOnly'nin takvim gününü, UTC timestamp'in belirli bir anı temsil ettiği açıklandı. Derleme: 0 hata, 0 uyarı. Yalnızca entity hazır; identity, FK, fiyat ve unique kuralları henüz eşlenmedi, tablo oluşturulmadı.
- [x] MP-02 — DailyMarketPriceConfiguration oluşturuldu. Id bigint identity; MarketAssetId smallint; PriceDate date; Price numeric(20,8); Source varchar(100) nullable. Price için pozitiflik ve NaN/Infinity/-Infinity reddi CHECK kısıtı tanımlandı. Derleme: 0 hata, 0 uyarı. FK/unique ilişki, DbContext kaydı ve migration MP-03/04 adımlarında tamamlandı; DB kontrolleri MP-05'te yapıldı.
- [x] MP-03 — MarketAsset 1:N DailyMarketPrice ilişkisi RESTRICT ile kuruldu. MarketAssetId + PriceDate unique index eklendi; DB'de yalnızca PK index'i ve bu birleşik unique index bulunduğu doğrulandı.
- [x] MP-04 — DbSet/configuration kaydı ve AddDailyMarketPrices migration'ı oluşturuldu. C# migration, model farkı ve üretilen SQL incelendi; yalnızca DailyMarketPrices tablosu, kısıtları/index'i ve migration geçmişi kaydı eklendi. bankingai_foundation üzerinde uygulandı; kolon tipleri ve identity doğrulandı.
- [x] MP-05 — Geçici .NET/EF doğrulama programı ile gerçek PostgreSQL'de aynı asset/gün, olmayan asset, sıfır/negatif/NaN/sonsuz fiyat ve RESTRICT silme reddi denendi. Farklı gün ve farklı asset kabul edildi; DateOnly UTC ve Europe/Istanbul oturumlarında aynı gün olarak okundu. Identity, nullable Source ve 8 ondalıklı decimal okuması doğrulandı. Denemeler ROLLBACK edildi; iki market tablosu da boş. Kalıcı test projesi henüz yok.

## 4. Banking modelinin kalan doğrulama kararları

- [x] VAL-01 — [Girdi kuralları](docs/input-validation.md) tablosu oluşturuldu. Altı entity için GUID/ID, null/whitespace, metin uzunlukları, para birimleri, enum ve confidence sınırları yazıldı; mevcut korumalar ile eksikler ayrıldı. Uygulama ve DB karşılıkları VAL-07 ile tamamlandı.
- [x] VAL-02 — docs/input-validation.md içinde Amount/Balance numeric(18,2) politikası belirlendi ve `InputRules`/SaveChanges/DB CHECK ile uygulandı: iki ondalıkla kayıpsız temsil, fazla anlamlı ondalıkta ret ve sessiz yuvarlama yok. PostgreSQL integration testinde geçersiz precision örnekleri reddedildi.
- [x] VAL-03 — docs/input-validation.md içinde UTC, offset, default/sonsuz, gelecek ve geçmiş import politikası belirlendi; C# SaveChanges ve DB timestamp kontrolleriyle uygulandı. DateOnly takvim günü testi dahil geçersiz tarih örnekleri reddedildi.
- [x] VAL-04 — docs/input-validation.md içinde dört geçerli kategori durumu belirlendi ve entity metotları, SaveChanges doğrulaması ve DB CHECK ile uygulandı. Atomik atama/kaldırma ve confidence precision örnekleri test edildi; LLM eşiği hâlâ CAT-05 kapsamındadır.
- [x] VAL-05 — docs/input-validation.md içinde ilk synthetic dataset kapsamı belirlendi: tamamlanmış mal/hizmet, abonelik ve fatura Debit; maaş/açılış fonlaması Credit. Transfer, nakit, yatırım, refund/iptal ve bekleyen hareketler kapsam dışında. Harcama tek para biriminde brüt Debit toplamı; Credit düşülmez. Net harcama/refund ayrı model kararı gerektirir; TransactionKind eklenmedi. Bu adım belge kararıdır.
- [x] VAL-06 — Kredili hesap simülasyonu yapılmamasına karar verildi. Negatif bakiye `InputRules`, `EntityValidation` ve `CK_Accounts_Balance_NonNegative` ile reddediliyor. Debit'in mevcut bakiyeyi aşmasını atomik olarak reddeden yazma yolu BAL-01/02'de yapılacak.
- [x] VAL-07 — `InputRules` (saf C#), `EntityValidation` (entity kuralları), `BankingDbContext.SaveChanges/SaveChangesAsync` (yazma sınırı) ve `EnforceInputValidation` migration'ı (DB son koruması) uygulandı. Precision, metin, GUID, UTC tarih, kategori kombinasyonu, negatif bakiye ve DB CHECK örnekleri test edildi; 12/12 test geçti.

## 5. Sabit lookup seed'leri

- [x] SEED-01 — Manuel komut sözleşmesi docs/database-design.md ve README'ye yazıldı. `migrate` yalnızca şemayı, açık kapsamlı `seed` lookup/synthetic veriyi, açık parametreli `import` historical market verisini işler; uygulama açılışında otomatik veri eklenmez. Şu an çalışan seed kapsamları `categories` ve `market-assets`tir; synthetic seed/import sonraki adımlardadır.
- [x] SEED-02 — `seed categories` komutu ve `TransactionCategorySeeder` oluşturuldu. Sabit 1–11 ID, kararlı Code değerleri ve Türkçe DisplayName değerleri ekleniyor. Aynı ID/kod aynı içerikle mevcutsa işlem tekrarsız; çelişki varsa sessizce ezmek yerine hata veriliyor. Seed işlemi transaction içinde çalışıyor; market/synthetic seed henüz yok.
- [x] SEED-03 — Kategori seed'i iki kez çalıştırıldı: ilkinde 11 eklendi, ikincisinde 0 eklendi ve 11 değişmeden kaldı. Boş tabloyla iki eşzamanlı komutta biri 11 ekledi, diğeri advisory lock sonrasında 0 ekledi; sonuç 11 kaldı. Id 1 geçici olarak çelişkili Code ile denendi, seed `InvalidOperationException` verdi ve sessiz overwrite yapmadı; değer geri yüklendi.
- [x] SEED-04 — `seed market-assets` komutu ve `MarketAssetSeeder` oluşturuldu. Sabit Id 1–3 ile USDTRY/USD/Currency, EURTRY/EUR/Currency ve GRAM_GOLD_TRY/GRAM/Commodity kayıtları; quote TRY ve IsActive true olarak tanımlandı.
- [x] SEED-05 — Market seed'i tekrar çalıştırıldı: her tekrar ve eşzamanlı çalıştırmada toplam 3 kayıt kaldı. USDTRY için yanlış Symbol/BaseUnit, EURTRY için yanlış AssetType geçici olarak denendi; seed açıklayıcı hata verdi, sessiz düzeltme yapmadı ve doğru değerler geri yüklendi. Yanlış QuoteCurrency doğrudan DB CHECK'i tarafından reddedildi. Son DB eşleşmeleri MA-03 sözleşmesiyle uyumlu.

## 6. Kontrollü synthetic müşteri ve hesaplar

- [x] SYN-01 — Bogus 35.6.5 yalnızca synthetic seed ihtiyacı için eklendi. `SyntheticSeedSettings` içinde random seed `20261009`, UTC referans tarihi `2025-10-01T00:00:00Z` ve dataset sürümü `synthetic-v1` sabitlendi. Her çağrıda bağımsız `Faker.UseSeed(...)` oluşturuluyor; DB'ye synthetic kayıt yazılmadı. Tekrarlanabilirlik testleri eklendi (`docs/synthetic-seed.md`).
- [x] SYN-02 — `SyntheticCustomerProfileCatalog` içinde üç kontrollü profil tanımlandı: `COFFEE_SUBSCRIPTIONS`, `FAMILY_MARKET`, `TRANSPORT_TRAVEL`. Ana/ikincil kategori ve recurring/tek seferlik merchant kalıpları sabit immutable listelerde tutuldu; gerçek müşteri bilgisi kullanılmadı. VAL-05 kapsamı korundu: yalnızca tamamlanmış mal/hizmet, abonelik ve fatura Debit kalıpları; transfer, refund/iptal, nakit, yatırım ve bekleyen işlem yok. Henüz DB kaydı üretilmedi (`docs/synthetic-seed.md`).
- [x] SYN-03 — `seed customers` komutu ve `SyntheticCustomerSeeder` eklendi. Üç sabit GUID kullanılıyor; mevcut demo kayıt silinmiyor/güncellenmiyor. Aynı GUID aynı içerikle varsa atlanıyor, farklı içerikle varsa açıklayıcı hata veriliyor; `CustomerNumber` DB sequence'e bırakılıyor. Gerçek telefon/TCKN/adres üretilmiyor (`docs/synthetic-seed.md`).
- [x] SYN-04 — `SyntheticAccountSeeder` ve `seed accounts` eklendi. Sabit GUID/DUMMY numaralarıyla Ada TRY/USD/boş TRY, Deniz TRY, Ege EUR olmak üzere beş hesap oluşturuldu. Eksik müşteri veya çelişen hesap bilgisi hata verir; tekrar çalıştırma mevcut bakiyeyi/tarihi değiştirmez.
- [x] SYN-05 — PostgreSQL'de beş hesabın mevcut müşterilere bağlı, numaralarının unique, para birimlerinin whitelist içinde ve ilk bakiyelerinin 0 olduğu doğrulandı. İlk seed 5, tekrar 0 ekledi. Kalıcı testte sahiplik/numara çelişkisi, duplicate numara, olmayan müşteri FK'si, geçersiz currency ve bakiye/tarih koruması denendi; test değişiklikleri rollback edildi. 18/18 test geçti.

## 7. Synthetic hareket ve bakiye tutarlılığı

- [x] BAL-01 — Seed için `TransactionSeedWriter` tasarlandı ve eklendi. Tek bir DB transaction'ında önce transaction insert edilir, sonra Account.Balance delta ile güncellenir; commit ikisi de başarılı olunca yapılır. API/application service katmanı eklenmedi.
- [x] BAL-02 — Bakiye `ExecuteUpdate` ile DB içinde `Balance + delta` olarak değiştirilir; eski değer uygulamada okunup yazılmaz. WHERE koşulu negatif ve numeric(18,2) üst sınırını engeller; Debit yetersiz bakiyede reddedilir. Insert/güncelleme hatasında rollback yapılır; aynı Transaction ID tekrar bakiye delta'sı uygulamaz.
- [x] BAL-03 — PostgreSQL entegrasyon testinde bakiye 0'dan 100 Credit'e çıkarıldı; 100 TRY üstünden iki eşzamanlı işlem (25 Credit, 40 Debit) çalıştırıldı. Son bakiye tüm transaction kayıtlarından bağımsız formülle `başlangıç + Credit − Debit = 85 TRY` çıktı; 3 kayıt vardı.
- [x] BAL-04 — İki hata yönü PostgreSQL'de doğrulandı: var olmayan kategori FK'siyle transaction insert'i reddedildi ve bakiye aynı kaldı; 100.01 TRY Debit insert sonrasında yetersiz bakiye nedeniyle reddedildi, transaction rollback oldu ve bakiye 100 kaldı. Test hesap/hareketleri temizlendi. Tüm suite 20/20 geçti.
- [x] SYN-06 — `seed transactions credits` ile 14 sabit ID'li Credit eklendi: Ada ve Deniz için Nisan–Eylül 2025'in her ayının 5'inde sırasıyla 45.000 TRY ve 55.000 TRY maaş; 1 Nisan'da Ada USD hesabına 500 USD ve Ege EUR hesabına 1.000 EUR açılış fonlaması. Bütün Credit kategori alanları null; fonlamalara karşı başka hesapta Debit yok.
- [x] SYN-07 — `seed transactions subscriptions` ile Netflix 229.99 TRY (ayın 12'si) ve Spotify 99.99 TRY (ayın 18'i) Temmuz–Eylül 2025 boyunca Ada'nın TRY hesabına eklendi. Her merchant üç ay tekrarlanıyor, `SUBSCRIPTION` kategorisi Rule kaynağıyla atanıyor.
- [x] SYN-08 — `seed transactions category-examples` ile Starbucks/FOOD, Aile Marketi/GROCERY, giyim/CLOTHING, ulaşım/TRANSPORTATION, yakıt/FUEL ve elektrik faturası/BILLS için altı sabit Debit eklendi. Her harekette POS/banka açıklaması `RawDescription`, sade ad `NormalizedMerchant`; kategoriler mevcut ID+Code ile doğrulanıyor. İlk çalıştırma 6, tekrar 0 kayıt ekledi.
- [x] SYN-09 — `seed transactions grocery-growth` ile Deniz'in TRY hesabında GROCERY/Aile Marketi harcaması Ağustos 2025'te 24.000 TRY, Eylül 2025'te 31.000 TRY olacak şekilde üçer sabit hareket eklendi. Beklenen toplamlar tarihten bağımsız sabit; 1 Ekim 2025 referansına göre iki ay tamamlanmış. İlk çalıştırma 6, tekrar 0 kayıt ekledi; DB toplamları doğrulandı.
- [x] SYN-10 — `seed transactions categorization-boundaries` iki TRY Debit ekler: 420 TRY işlemde `CategoryId`, source, confidence, CategorizedAt ve NormalizedMerchant null; 780 TRY işlemde kategori ID 11 (`OTHER`), Rule kaynağı ve `Hobi Dükkânı` merchant'ı var. Ham açıklamalar iki kayıtta da korunur. PostgreSQL satırları ve null/OTHER ayrımı doğrulandı.
- [x] SYN-11 — Bütün `SyntheticTransactionSeeder` transaction grupları sabit ID, var olan içerik çelişki kontrolü ve `TransactionSeedWriter` atomik insert+bakiye delta'sı kullanır. Seed existence check ile yazma arasını PostgreSQL session advisory lock ile korur. Aynı komut iki ayrı süreçte eşzamanlı çalıştırıldı: biri 2 ekledi, diğeri 0; DB'de iki satır ve Ada TRY bakiyesinde tek 1.200 TRY azalış görüldü. Transaction tablosu/migration değişmedi.
- [x] SYN-12 — `SyntheticDatasetIntegrityTests` her seed hesabının transaction toplamlarını bağımsız, sabit Credit/Debit fixture'larıyla ve `Balance = Credit − Debit` eşitliğiyle doğrular. 2026 seed sonrası Ada TRY: 540.000 − 5.464,82 = 534.535,18; Deniz TRY: 660.000 − 179.500 = 480.500; USD/EUR/boş hesaplar da ayrı doğrulanır. Beklenen rakamlar seeder'dan üretilmez; test suite'i 22/22 geçti.
- [x] SYN-13 — Kullanıcı kararıyla bu madde harcama tarih kapsamına daraltıldı; market fiyatları ve banking/market örtüşmesi SRC/IMP adımlarına ertelendi. `seed transactions recent-spending` Nisan–Eylül 2026 için 12 maaş Credit'i ve Starbucks/Netflix/Spotify/market için 24 Debit ekledi. 10 Nisan–10 Ekim 2026 demo penceresinde altı ayın her birinde harcama var; bağımsız beklenen toplam 118.089,88 TRY. Eski 2025 verisi 34 satırla korundu. İlk seed 36, tekrar 0 ekledi; sabit tarihler gelecekte kendiliğinden kaymaz.
- [x] SYN-14 — Kullanıcı kararıyla tek demo müşterisi yerine üç farklı karakter: Ada (kahve/abonelik, bilinmeyen işlem), Deniz (market artışı, fatura), Ege (ulaşım/seyahat). `seed accounts` Ege'ye `DUMMY-EGE-TRY-001` hesabını ekledi (1 eklendi, tekrar 0). `seed transactions demo-coverage` 28 işlem ekledi (ID 601–628; tekrar 0): üç Ekim 2026 maaşı, Ada/Deniz Ekim harcamaları, Ege Nisan–Ekim maaşları, ayın 11/25'inde ulaşım (aylık 600/650/700/650/900/1.400, Ekim 700) ve 14 Ağustos 1.200 TRY TRAVEL. Belirsiz `POS 6604 ONLINE ODEME 4471` işleminde merchant/kategori null (bilinmeyen). Bağımsız fixture: bu ay Ada 520, Deniz 11.350, Ege 700 TRY; 10 Nisan–10 Ekim penceresi 43 Debit, 136.759,88 TRY. Transfer yok (KIND adımları). Data suite 23/23, Application 17/17.

## 7b. İşlem türü, müşteriler arası transfer ve dummy dükkân — kullanıcı kararı 10 Ekim 2026

Amaç: harcama, satış geliri, transfer ve maaşı ayırmak; demoda bir müşterinin dummy
dükkândan alışverişi diğer müşterinin işletme hesabına anında Credit olarak girsin.
LLM/MCP para gönderemez; yazma yalnızca uygulama/API ve seed üzerindendir.

- [x] KIND-01 — Kullanıcı onayıyla karar verildi: `Kind` = Purchase/Sale/Transfer/Salary/OpeningFunding (1–5); iki bacak ortak nullable `PairId` kolonuyla bağlanır (ayrı tablo yok); kendi hesapları arası transfer serbest (aynı para birimi); karşı taraf adı maskeli. Mevcut 98 kaydın doldurma kuralı ve DB/C# koruma ayrımı docs/input-validation.md içinde. Kod/DB değişmedi.
- [x] KIND-02 — `TransactionKind` enum, `Transaction.Kind`/`PairId`, `InputRules.Kind` + `EntityValidation`, `SetCategorization` yalnız Purchase, tek bacaklı yazıcıda PairId reddi. `AddTransactionKind` migration'ı nullable ekleme → doldurma → belirsiz kayıtta RAISE → NOT NULL sırasıyla elle düzenlendi; 5 CHECK + UNIQUE (PairId, Direction) eklendi, `CK_Transactions_Credit_Uncategorized` kaldırıldı. Kullanıcı onayıyla uygulandı: 63 Purchase, 33 Salary, 2 OpeningFunding, PairId hepsi null. Bütün seed scope'ları tekrar 0 ekledi. Data 27/27, Application 17/17.
- [x] KIND-03 — `PairedTransactionSeedWriter` eklendi (Seeding/; API adımında ortak yere taşınıp adından Seed kalkacak). DB'den önce çift tutarlılığı: Debit+Credit, aynı PairId/tutar/para birimi/OccurredAt, farklı hesap ve Id, yalnız Transfer→Transfer veya Purchase→Sale. Tek DB transaction'ında bakiyeler hesap ID sırasıyla (deadlock önleme), sonra iki kayıt. Bakiye mantığı `TransactionSeedWriter.ApplyBalanceDeltaAsync` olarak paylaşıldı. Aynı PairId tekrar gelirse hata (kullanıcı kararı; API-12'de düzeltilecek). 7 PostgreSQL testi; sıralama geçici kaldırılınca deadlock testi 40P01 ile kırmızı oldu, geri konunca yeşil. Data 34/34, Application 17/17. Demo DB'ye kalıcı veri yazılmadı.
- [x] KIND-04a — Kullanıcı kararıyla `Account.AccountType` (Personal=1, Business=2; C# varsayılanı Personal, `init`) eklendi; `EntityValidation` + `CK_Accounts_AccountType`. `AddAccountType` migration'ı nullable ekleme → mevcut 6 hesap Personal → NOT NULL sırasıyla elle düzenlendi ve onayla uygulandı (DB varsayılanı yok; ham SQL insert türü açıkça vermeli). `PairedTransactionSeedWriter`: Sale yalnız Business hesaba yazılır. Test fixture'ındaki ham Account INSERT'ine AccountType eklendi. Data 37/37, Application 17/17; seed tekrarları 0 ekledi.
- [x] KIND-04b — Uygulandı: `seed accounts` 1 ekledi (7 hesap), `seed transactions pairs` 18 kayıt ekledi (ID 701–718, PairId 40000000-…-1…9); tekrarlar 0. Seed akışı çiftleri iki bacaklı yazıcıya yönlendirir, DB'de yarım çift bulursa hata verir (bu dal otomatik testle denenmedi; demo verisini bozmadan yarım çift üretmek için ayrı test DB gerekir, DB-01). Hesap seed'i AccountType çelişkisini de reddeder. Bağımsız fixture: Ada 577.490,18; Deniz 522.900; Ege 273.380; dükkân 2.595; TRY toplamı 1.376.365,18 korunur; harcama = Kind Purchase (Ekim: tüm Purchase 12.925, tüm Debit 14.725). Data 38/38. Plan: Deniz'e ait "Kahve Durağı – Kadıköy" Business hesabı (`DUMMY-DENIZ-SHOP-001`) ve `seed transactions pairs`: 5 Purchase→Sale (Eyl 15/22, Eki 2/4/7) ve 4 Transfer (Deniz→Ege 750, Deniz→dükkân 2.000, Ada→Deniz 1.500, Ege→Ada 300). Açıklamalarda isim yok; TRY toplamı korunur. Gerçek marka sahipliği uydurulmaz; anında ve komisyonsuz takas basitleştirmesi belgelenir.
- [x] KIND-05 — docs/input-validation.md VAL-05 bölümü yeniden yazıldı: harcama = Kind Purchase (bütün Debit değil; Ada Ekim 735 vs 2.235 örneği), transfer/Sale/maaş/açılış ayrımı, kişisel analiz varsayılanı Personal hesap, karşı taraf PairId + maskeleme, koruma tablosu ve yeni kabul örnekleri. RULE.md §28 ve database-design.md aynı tanıma çekildi. Yalnız belge; kod/DB değişmedi.

## 8. Gerçek historical market kaynağını seç

> Paralel kol (DOC-09): bu bölüm ve bölüm 9 API/analytics/AI işlerini engellemez.

- [x] SRC-00 — 10 Ekim 2026 kararı belgelendi: toplam TRY harcama dönem başlangıcında tek seferde yatırılmış kabul edilir. Başlangıçta sağlayıcının satış, değerlemede alış fiyatı kullanılır. Yalnızca belgeler güncellendi; model/import/hesaplama henüz uygulanmadı.

- [ ] SRC-01 — USDTRY ve EURTRY ücretsiz kaynak adaylarını incele. Resmî kaynak, erişim koşulları, API anahtarı ihtiyacı ve iki yıllık günlük kapsamı örnek yanıtla göster.
- [ ] SRC-02 — Gram altın kaynağını ayrı doğrula. Müşterinin geçmiş alımı için sağlayıcı satış geçmişini, bugün satışı için güncel sağlayıcı alış fiyatını doğrula. Gram/ons, saflık, TRY yönü, fiyat tanımı ve iki yıllık satış geçmişi kanıtlanmadan kaynağı kesinleştirme.
- [ ] SRC-03 — Fiyat sözleşmesini yaz. Varlık başına tutarlı kaynak ve alış/satış çifti; başlangıç satış, değerleme alış; kaynak takvim günü; eksik veri gösterimi; varsa birim dönüşüm formülü açık olsun.
- [ ] SRC-04 — Kullanıcıya kaynak seçimini ve kısıtlarını açıkla. Ücretli servis kullanma; erişim engeli varsa kapsam tamamlandı deme ve fake fiyatla kapatma.
- [ ] SRC-05 — Küçük ham veri örneğini saklayıp parser'ı dene. Ondalık ayırıcı, tarih formatı, null değerler, tekrarlar ve bozuk satırlar doğru ele alınsın; secret saklama.

## 9. Import'u birer varlıkla geliştir

Kaynak doğrulamasından sonra ve import'tan önce:
- [ ] MP-06 — Mevcut tek Price yerine alış/satışı saklama tasarımını kullanıcıya açıkla; alan isimleri, precision, null ve geçerli fiyat kurallarını belirle. Geçmiş kayıtta yalnız sağlayıcı satış varsa alış uydurma; bugün satışı senaryosunda geçmiş alış zorunlu değildir. Değerleme fiyatının gerçek gününü koru.
- [ ] MP-07 — Onaylanan entity/configuration/validation değişikliğini uygula; yeni incremental migration üret, SQL'i incele ve DB'ye uygula. Mevcut geçmişi koru.
- [ ] MP-08 — Alış/satış yönünü, geçersiz fiyat çiftlerini ve DB okuma/yazımını doğrula; ortalama fiyatı iki tarafa kopyalama.


- [ ] IMP-01 — Import girişini oluştur. Varlık ve başlangıç/bitiş tarihleri açık parametreler olsun; ilk aşama manuel komutla çalışsın.
- [ ] IMP-02 — USDTRY için kısa tarih aralığı import et. Kaynak fiyat/tarih/birim ile DB satırlarını örnek bazında karşılaştır.
- [ ] IMP-03 — EURTRY için aynı akışı doğrula. USD serisinin yanlışlıkla EUR sembolüne yazılamadığını kontrol et.
- [ ] IMP-04 — GRAM_GOLD_TRY import'unu doğrula. Gram/ons karışmasını ve gerekirse dönüşümün birimlerini test et; yöntem belgelenmiş olsun.
- [ ] IMP-05 — Idempotent yazımı uygula. Asset+gün unique kuralıyla aynı veri tekrarında duplicate oluşturma; revize fiyatları tanımlı politikayla işle ve raporla.
- [ ] IMP-06 — Kesinti/retry davranışını ekle. Sınırlı retry ve timeout kullan; tamamlanan parçalar tekrar çalıştırılınca bozulmasın, hatalar gizlenmesin.
- [ ] IMP-07 — Import raporu üret. Eklenen/değişen/değişmeyen/reddedilen satırlar, tarih kapsamı, kaynak ve veri boşlukları gösterilsin. Source alanı gerçek importlarda dolu olsun.
- [ ] IMP-08 — Üç varlık için en az iki yıllık veriyi yükle. Gerçek son yayımlanan güne kadar kapsamı doğrula; desteklenen dönem başlangıcı öncesi fallback için gerekiyorsa ek geçmiş getir.
- [ ] IMP-09 — Takvim boşluklarını ayır. Hafta sonu/tatil doğal boşluğu ile beklenmeyen kaynak kesintisini raporla; her gün için uydurma satır üretme.
- [ ] IMP-10 — Önceki fiyat seçimini doğrula. Exact gün, hafta sonu, resmi tatil, ilk fiyat öncesi tarih, gelecek gözlem ve eski fiyat senaryolarını test et; stale eşiğini kararlaştır.

## 10. Database foundation kabulü

- [ ] DB-01 — Küçük xUnit entegrasyon test projesi kur. Gerçek PostgreSQL'de yalnızca testin sahip olduğu ayrı veritabanını kullansın; aktif demo DB'ye dokunmasın.
- [ ] DB-02 — Tüm migration'ları boş test DB'sine uygula. Altı domain tablosu, anahtarlar, index'ler ve gerekli özel SQL nesneleri sıfırdan oluşmalı.
- [ ] DB-03 — Önceki geçici constraint kontrollerini kalıcı testlere taşı. FK, unique, enum, null, uzunluk, amount, confidence ve para birimi uyumu kapsansın.
- [ ] DB-04 — EF üzerinden entity yazma/okumayı doğrula. Private setter/backing field, nullable enum, UTC ve DateOnly değerleri doğru geri gelmeli.
- [ ] DB-05 — Seed tekrarı, paralel seed, bakiye rollback/eşzamanlılığı ve import tekrarı testlerini çalıştır.
- [ ] DB-06 — Uygulama yeniden açıldığında veri kalıcılığını kontrol et. Gerçek market kapsamı ve banking toplamlarını tekrar doğrula.
- [ ] DB-07 — Yerel kurulum adımlarını tekrarlanabilir yap. Docker/PostgreSQL, migration, seed ve import komutlarını README'ye yaz; mevcut volume/veritabanını sahipliği belirsizken silme.
- [ ] DB-08 — Foundation review yap. Altı tablo, banking dataset, üç varlıkta iki yıllık gerçek fiyat, test sonuçları ve bilinen sınırlar kullanıcıya gösterilsin. DOC-09'dan sonra yalnızca market kolunun ve OC maddelerinin ön koşuludur; API'yi engellemez.

## 11. Controller-based API temeli — foundation sonrasında

- [x] API-01a — Kullanıcı kararıyla ayrıştırma: `BankingAI.Data` kütüphane oldu; `Program.cs` git geçmişiyle yeni `BankingAI.Cli` (Exe) projesine taşındı; `Microsoft.EntityFrameworkCore.Design` CLI'a alındı (dotnet-ef startup projesi). `BankingAI.slnx` eklendi. Davranış değişmedi: 136 + 38 test tek `dotnet test BankingAI.slnx` ile geçti; EF "model değişikliği yok", 10 migration; CLI migrate/show ve 12 seed komutu `Added: 0`; demo veri 116. README/CLAUDE.md/docs komutları güncellendi.
- [x] API-01b — `BankingAI.Api` (controller-based, şablon örnekleri silindi, OpenAPI korundu) → Application → Data. `HealthController`: `GET /api/health` 200 `{status:"ok",database:true}`, DB yoksa 503. `public partial class Program` integration testler için. `BankingAI.Api.Tests` (WebApplicationFactory, 5 test: sağlıklı, DB erişilemez 503, bilinmeyen adres 404, bağlantı bilgisi yoksa açılışta hata, açılış migration geçmişini değiştirmez — sınırı: DB güncelken Migrate eklense de geçer; asıl güvence API'de Migrate çağrısı olmaması). Kasıtlı 2 hata testlerce yakalandı. Kestrel ile gerçek çalıştırma: health 200, /openapi/v1.json 200. HTTPS yönlendirmesi kaldırıldı (yerel HTTP; karar OPS-04).
- [x] API-02 — `AddDbContext<BankingDbContext>` (istek başına scoped); bağlantı `BANKINGAI_CONNECTION_STRING` yapılandırmasından, DbContext oluşturulurken okunur (test host'u değiştirebilir); yoksa API açılışta net hatayla durur. `BankingDbContext` DI constructor'ı aldı, `OnConfiguring` yalnız DI ayarı yoksa ortam değişkenine düşer (CLI/testler değişmedi). API açılışında migration yok.
- [ ] API-03 — İlk AccountDto ve CreateAccountRequest modellerini oluştur. Entity'yi doğrudan JSON'a verme; müşteri/hesap kapsamı ve kullanıcıdan alınmayacak alanları belirle.
- [ ] API-04 — İlk hesap okuma servisini oluştur. AsNoTracking, async I/O ve CancellationToken kullan; servis interface'inin gerekçesini incele, repository ekleme.
- [ ] API-05 — GET /api/accounts ekle. Müşteri kapsamı, pagination ve sıralama belirle; boş listede 200 ve [] dön.
- [ ] API-06 — GET /api/accounts/{id} ekle. Geçersiz ID, bulunamayan hesap ve başka müşteri kapsamını ayrı test et.
- [ ] API-07 — POST /api/accounts ekle. Girdi doğrulaması, Customer varlığı, unique numara ve sıfır bakiye kuralları; başarıda 201 + Location uygula.
- [ ] API-08 — TransactionDto ve create request sözleşmesini tanımla. Backend'in yönettiği kategori/created alanlarını kullanıcı girdisiyle karıştırma.
- [ ] API-09 — GET /api/accounts/{id}/transactions ekle. Tarih filtresi, pagination, deterministik sıralama ve boş sonuç davranışını test et.
- [ ] API-10 — GET /api/transactions/{id} ekle. DTO döndür ve hesabın müşteri kapsamını koru.
- [ ] API-11 — Synthetic transaction POST akışını ekle. Foundation'da doğrulanan atomik bakiye/insert yaklaşımını kullan; bunu AI write tool'u yapma.
- [ ] API-12 — Tekrar gönderilen POST davranışını belirle. Idempotency gerekiyorsa küçük ayrı tasarım yap; ağ tekrarlarında aynı işlemin iki kez oluşmasını sessizce kabul etme. KIND-03 notu: iki bacaklı yazıcı aynı PairId'de şimdilik hata veriyor; kullanıcı kararıyla burada düzeltilecek (aynı içerikli tekrar → ilk sonucu döndür, farklı içerik → 409).
- [ ] API-13 — Ortak hata cevabını oluştur. Validation, bozuk JSON, 404, 409 ve beklenmeyen hata ayrımını yap; stack trace/bağlantı bilgisi sızdırma.
- [ ] API-14 — HTTP integration testlerini yaz. Başarı, null body, invalid input, unique yarış, hata ve veri kalıcılığı senaryoları kapsansın.
- [ ] API-15 — Yerel API kullanım örneklerini ve OpenAPI sözleşmesini hazırla. DTO'lar, status kodları ve hata örneklerini README/docs içinde göster.

## 12. Deterministik analytics — bir sorgu, bir adım

- [x] ANA-01 — `BankingAI.Application` class library'si ve `PeriodResolver` eklendi. `TimeProvider` tek referans zamanıdır (demo saati sabitlenebilir); "bugün" müşterinin saat diliminden belirlenir. Anlamlar: ThisMonth = ay başı–bugün, LastMonth = önceki tam ay, Last3Months/Last6Months = bugünden geriye kayan pencere, Custom = dahil iki tarih, gelecek yok, en fazla 24 ay. Sonuç `[StartUtc, EndUtc)` (offset 0) + yerel StartDate/EndDate. Adlandırılmış dönemle tarih gönderilmesi, bilinmeyen saat dilimi ve tanımsız enum reddedilir. `tests/BankingAI.Application.Tests` 17/17, mevcut suite 22/22 geçti. Bilinen sınır: gece yarısı DST geçişi olan bölgeler desteklenmez (Europe/Istanbul etkilenmez). Demo saati config bağlantısı API adımında yapılacak.
- [x] ANA-02 — `SpendingSummaryService` (Application → Data proje referansı; Data'nın Exe/kütüphane ayrımı API-01'e bırakıldı). Yalnız Kind Purchase, varsayılan Personal hesaplar, müşterinin TimeZone'u ile PeriodResolver, para birimi başına satır, kategorisiz tutar ayrı. Başka müşterinin hesabı/olmayan müşteri `KeyNotFoundException` (varlık sızdırmaz), Business hesap `ArgumentException`, boş dönemde boş liste. Çalışan SQL yakalandı: saat dilimi sorgusu + alt sorgulu tek GROUP BY, parametreli. 11 PostgreSQL testi (Ada bu ay 735 ≠ Debit 2.235; USD testi rollback). Application 28/28, Data 38/38. Sözleşme docs/analytics.md.
- [x] ANA-03 — `CategoryBreakdownService`. Önce refactor: ortak `PurchaseScope` (internal) çıkarıldı, `SpendingSummaryRequest` → `SpendingQuery`; ANA-02 testleri değişmeden yeşil kaldı. Para birimi başına kategori listesi; kategorisiz grup `null` kod + kullanıcı kararıyla "Sınıflandırılmamış", OTHER ayrı; tutar büyükten küçüğe; yüzde backend'de 2 ondalık, 100'e zorlanmaz. Çalışan SQL: saat dilimi + LEFT JOIN'li tek GROUP BY. 8 test (9 sorguda özetle tutarlılık dahil). Application 36/36, Data 38/38.
- [x] ANA-04 — `MerchantSpendingService`: (A) merchant dağılımı, (B) tek merchant harcaması. `MerchantMatcher` (saf C#): kültürden bağımsız Türkçe sadeleştirme, tam → tek adaylı kısmi (≥3 harf, `IsPartialMatch`) → `Ambiguous` (adaylar, tutar yok) → `NotFound`; yazım hatası düzeltilmez; adaylar yalnız müşterinin kendi tüm zamanlar merchant'ları; bilinen merchant'ta dönemsel harcama yoksa boş liste. Merchant'sız grup "Sınıflandırılmamış" (ortak `AnalyticsLabels`). Refactor: PurchaseScope `ScopedPurchases` (tüm zamanlar sorgusu dahil) döner. SQL yakalandı: 3 komut, ham metin SQL'e girmez (`@canonical`). 19 eşleştirme + 9 servis testi. Application 64/64, Data 38/38.
- [x] ANA-05 — `TransactionSearchService` + refactor: ortak `CustomerScope` (müşteri/hesap yetkisi/dönem; `PurchaseScope` onun üstünde). Kullanıcı kararları: tüm türler ve tüm hesaplar (dükkân dahil), dönem zorunlu, limit 20/50, offset ≤10.000, maske "Deniz M***", serbest metin yok. Filtreler: hesap, Kind, Direction, kategori kodu (bilinmeyen → hata), yalnız sınıflandırılmamış, merchant (MerchantMatcher). DTO `TransactionItem`, `LocalDate`, karşı taraf PairId ile (kişi maskeli, dükkân/kendi hesabı adıyla). SQL: 3 komut, join'li tek sayfa sorgusu. İlk uygulamada EF'in dış satırdaki null kontrolünü düşürdüğü yakalandı ve düzeltildi (docs/analytics.md). 15 test. Application 79/79, Data 38/38.
- [x] ANA-06 — `HighestTransactionsService`. Kullanıcı kararları: varsayılan şahsi hesaplarda harcama (Purchase); "hepsi" istenirse tüm türler + tüm hesaplar; limit varsayılan 5, istenen kadar, en fazla 100; eşitlikte en yeni önce; para birimi başına ayrı liste. Refactor: `TransactionItemReader` (join/maskeleme/LocalDate, sıralama seçenekli) arama ile ortak; PurchaseScope saat dilimini de döner. SQL: saat dilimi + DISTINCT para birimi + para birimi başına sıralı sorgu (3 komut). 13 test. Application 92/92, Data 38/38.
- [x] ANA-07 — `SpendingComparisonService`. Kullanıcı kararları: "geçen aya göre" = son tam ay vs önceki ay (varsayılan), ayrıca MonthToDate (aynı günler) ve çakışmasız Custom; kategori katkıları mutlak farka göre; aylık seri takvim ayı + devam eden ay (IsPartial) + harcamasız ay 0, N 6/en fazla 24, kategori veya merchant filtresi. `PeriodResolver.ResolveComparison/ResolveMonthlyWindows` (10 saf test). Refactor: CustomerScope/PurchaseScope dönemsiz yetki yolu. Karşılaştırma tek CASE WHEN sorgusu; seri 3 kolonluk okuma + C# kovalama (Npgsql 10 DateTimeOffset TZ dönüşümünü çeviremiyor; PERF-02'de tekrar bak). 13 servis testi; özetle tutarlılık. Application 115/115, Data 38/38.
- [x] ANA-08 — Kapsam tablosu çıkarıldı; çok müşteri zaten kapsanmıştı. `AnalyticsEdgeCaseTests` (rollback'li geçici kayıtlar): hesapsız Demo Customer'da 7 servis boş sonuç; İstanbul gece yarısı sınırları (31 Ağu/1 Eyl ve 30 Eyl/1 Eki) özet, karşılaştırma CASE WHEN, aylık seri ve aramada; ikinci para birimi (USD) kategori, merchant, karşılaştırma, seri ve düzenli ödemede ayrı; demo veri 116'da kalıyor. İlk sürüm karşılaştırmanın iki dönem sınırını denemiyordu; kasıtlı hata yerleştirme bunu gösterdi, sınır eklendi. 4 kasıtlı sınır hatasının (kapsam, karşılaştırma önceki/bu dönem, seri) hepsi yakalanıyor. Application 136/136, Data 38/38.
- [ ] ANA-09 — Doğrulanan hesaplamaları controller endpointlerine tek tek bağla. Controller yalnızca HTTP sorumluluğunu taşısın. (Kullanıcı kararı: ANA bölümünde kalır; API-01'e bağımlı, API projesi kurulduktan sonra yapılır.)

## 13. Opportunity-cost hesabı

- [ ] OC-01 — Hesap sözleşmesini sabitle. TRY Debit, seçilen kategori/dönem, toplam harcamayla dönem başlangıcında tek alım ve değerleme tarihinde satış kullan; sağlayıcı satış/alış yönünü ve ek ücret/vergi/enflasyon kapsamını yaz.
- [ ] OC-02 — USDTRY için dönem başlangıcında tek alım hesabı yaz. totalSpent / başlangıç sağlayıcı satış fiyatı; ara birimi erken yuvarlama.
- [ ] OC-03 — Dönemdeki uygun harcamaları topla; tek alımdaki birim × değerleme sağlayıcı alış fiyatı ile fark/yüzde üret. İşlem tarihlerinde ayrı alım yapma.
- [ ] OC-04 — Aynı motoru EURTRY ve GRAM_GOLD_TRY ile doğrula. Asset birimi ve quote currency eşleşmeli; varlığa özel kopya hesap motorları yazma.
- [ ] OC-05 — Fiyatları toplu getir. Varlıkların başlangıç satış ve değerleme alış fiyatlarını birlikte çöz; eksik/çok eski fiyatları açık sonuç durumuyla bildir.
- [ ] OC-06 — Karşılaştırma çıktısını oluştur. Harcama toplamı, birim, değer, fark, fiyat tarihleri, kaynak, yöntem ve kapsam dönsün; sonuç DB'ye kaydedilmesin.
- [ ] OC-07 — Çoklu varlık sıralamasını ekle. Aynı harcama kümesi, tarih ve kapsam kullan; eksik veride kesin kazanan gösterme.
- [ ] OC-08 — Bağımsız örneklerle test et. Sağlayıcı alış/satış yönünün ters çevrilmesi, makas, tek alım varsayımı, tatil fallback'i, eksik ilk fiyat, sıfır harcama, negatif getiri ve gram/ons hataları kapsansın.
- [ ] OC-09 — API'ye bağla. Tarihsel varsayımı anlat; gelecek getiri garantisi veya yatırım emri üretme.
- [ ] OC-10 — Hangi sorgularda önerileceğini kullanıcıyla belirle. Her harcama sorusuna otomatik ekleme; açıkça istenen varlığı sessizce değiştirme.

## 14. SwiftUI — çalışan endpoint üzerine küçük ekranlar

- [ ] IOS-01 — SwiftUI proje temelini oluştur. MVVM, NavigationStack ve native state kullan; gereksiz paket ekleme.
- [ ] IOS-02 — Codable API modellerini yaz. Decimal/tarih/null alanların backend sözleşmesiyle eşleşmesini doğrula.
- [ ] IOS-03 — URLSession istemcisini yaz. Base URL, timeout, cancellation ve hata cevabını ele al; ağ kodunu View içine koyma.
- [ ] IOS-04 — Accounts ekranını ekle. Loading, empty, error ve başarılı liste durumları olsun.
- [ ] IOS-05 — Hesap detayından hareket listesine geçişi ekle. Pagination ve tarih filtresini backend ile eşleştir.
- [ ] IOS-06 — Transaction detayını ekle. Ham açıklama, merchant, kategori, para birimi ve yerel saati anlaşılır göster.
- [ ] IOS-07 — Home ekranını ekle. Hesaplar ve son işlemler; farklı para birimlerini kur olmadan tek toplam gibi sunma.
- [ ] IOS-08 — Kategori ve aylık grafiklerini Swift Charts ile ekle. Boş veri ve kategorisiz tutarlar doğru gösterilsin.
- [ ] IOS-09 — Dönem karşılaştırması ve opportunity-cost sonucunu sade göster. Varsayım, veri tarihi ve eksik veri durumu görünür olsun.
- [ ] IOS-10 — Temel ViewModel/JSON/ağ hata testlerini yap. SwiftUI çalışıyor diye finansal hesapları mobilde yeniden yazma.

## 15. Local AI — önce hazır sonucu açıklama

- [ ] AI-01 — Donanıma uygun local Qwen modelini seç. Türkçe, structured output ve tool calling'i küçük örneklerle ölç; model adı config olsun.
- [ ] AI-02 — Ollama bağlantısını doğrula. Yerel model yanıtı, timeout ve model bulunamadı senaryosunu kontrol et; ücretli API kullanma.
- [ ] AI-03 — Küçük FastAPI servisini oluştur. Sağlık kontrolü ve typed giriş/çıkış modeli ekle; finansal sorgu/hesap koyma.
- [ ] AI-04 — LangChain ChatOllama ile basit invoke kur. Agent gerekmiyorsa oluşturma; önce backend'in hazır sonucunu Türkçe açıklat.
- [ ] AI-05 — Açıklama prompt'unu yaz. Yalnızca verilen sayılar, belirsizlik ve veri eksikliği kuralları olsun; ham açıklamayı talimat kabul etme.
- [ ] AI-06 — Model çıktısını doğrula. Şema hatası, boş yanıt ve verilen sayılarla uyuşmama senaryolarını kontrollü ele al.
- [ ] AI-07 — .NET → FastAPI iletişimini ekle. Timeout, cancellation ve AI kapalıyken deterministik sonucu gösterebilme davranışını test et.
- [ ] AI-08 — SwiftUI chat ekranının temelini ekle. Bekleme, hata, iptal ve yeniden deneme durumları açık olsun.

## 16. Read-only MCP — her tool ayrı review

- [ ] MCP-01 — MCP server/client sınırını tasarla. .NET mevcut hesaplama servislerini çağırır; Python istemci bağlanır. DB sorgusu veya finansal hesap LLM'ye verilmez.
- [ ] MCP-02 — İlk server ve tool discovery akışını kur. Yalnızca allowlist tool'ları yayınla; development erişimini sınırla.
- [ ] MCP-03 — get_accounts tool'unu ekle; typed args, müşteri kapsamı ve sınırlı çıktı kullan.
- [ ] MCP-04 — get_transactions tool'unu ekle; pagination ve hesap kapsamını doğrula.
- [ ] MCP-05 — get_spending_summary tool'unu ekle; dönem ve para birimi kuralları backend'de kalsın.
- [ ] MCP-06 — search_transactions tool'unu ekle; filtre ve limitleri yeniden doğrula.
- [ ] MCP-07 — get_category_breakdown tool'unu ekle; kategorisiz tutar bilgisini koru.
- [ ] MCP-08 — get_merchant_breakdown tool'unu ekle; null merchant davranışını açıkla.
- [ ] MCP-09 — compare_spending_periods tool'unu ekle; sıfır payda ve dönem sınırlarını test et.
- [ ] MCP-10 — get_highest_transactions tool'unu ekle; limit ve yetki kapsamını koru.
- [ ] MCP-11 — get_spending_with_opportunity_cost tool'unu ekle; varlık allowlist'i, fiyat tarihi ve eksik veri alanları dönsün.
- [ ] MCP-12 — LangChain MCP client'ını bağla. Qwen tool seçsin; arguments backend'de tekrar doğrulansın, tool çağrı sayısı sınırlansın.
- [ ] MCP-13 — Yanlış/olmayan tool, eksik argüman, timeout, server kapalı ve boş sonuç testlerini yap.
- [ ] MCP-14 — En az beş farklı Türkçe soruda doğru tool + doğru argüman + backend'e dayalı cevap akışını göster.

## 17. Merchant normalization ve kategorilendirme

- [ ] CAT-01 — Ayrı evaluation fixture hazırla. rawDescription, expectedMerchant, expectedCategory alanları tests/data içinde olsun; production-like tabloya ground truth koyma.
- [ ] CAT-02 — Bilinen örnekler için küçük deterministik kurallar yaz. Starbucks, Zara, Migros, Netflix, Shell gibi örnekleri doğrula; ayrı Merchant tablosu oluşturma.
- [ ] CAT-03 — Qwen structured output sözleşmesini oluştur. normalizedMerchant, mevcut categoryCode ve confidence; bilinmeyen kategori/bozuk confidence reddedilsin.
- [ ] CAT-04 — Hybrid akışı kur. Önce rule; çözülemeyen örnekte model. Her işlemde gereksiz LLM çağrısı yapma.
- [ ] CAT-05 — LLM otomatik kabul eşiğini ölçerek seç ve uygulama ayarında tut; DB'ye koyma. VAL-04'e göre düşük skorda mevcut kategori korunur, kategorisiz işlem kategorisiz kalır; otomatik OTHER atanmaz. Confidence doğruluk kanıtı değildir; ham skor aralık/precision doğrulamasından sonra eşikle karşılaştırılır.
- [ ] CAT-06 — Sonucu atomik olarak kaydet. CategoryId/source/confidence/CategorizedAt birlikte güncellensin; RawDescription korunsun.
- [ ] CAT-07 — Tekrar kategorilendirme politikasını belirle. Manual sonucu otomatik işlemle ezme; retry ve eşzamanlı güncellemeyi test et.
- [ ] CAT-08 — Fixture üzerinde merchant ve kategori doğruluğunu ölç. Yanlış örnekleri kaydet; evaluation örnekleriyle başarıyı yapay şişirecek veri sızıntısını önle.

## 18. Recurring payment ve açıklama

- [x] REC-01 — Kullanıcı kararlarıyla kural: son 6 ay, şahsi hesap Purchase, hesap+para birimi+merchant grubu; ≥3 ödeme, ardışık aralıklar 26–35 gün (ekstra ödeme ve atlanan ay da elenir), tutarlar ortancanın ±%10'u, son ödeme ≤35 gün. `RecurringPaymentDetector` (saf C#); tahmini sonraki ödeme (ay sonu sabitlemeli).
- [x] REC-02 — `RecurringPaymentService`: tablo yok, Transactions'tan hesaplanır; Ada için Netflix + Spotify, aylık toplam 329,98; Deniz ve Ege için boş. SQL: saat dilimi + tek join'li okuma.
- [x] REC-03 — 12 dedektör testi (sık kahve, tek/iki ödeme, eksik ay, küçük/büyük fiyat değişimi, ayda iki kez, iptal, aynı gün, ay sonu, sıra bağımsızlığı) + 5 servis testi. Bilinen sınır belgelendi: ekstrası olmayan aylık sabit kahve abonelik gibi görünür. Application 132/132, Data 38/38.
- [ ] REC-04 — find_recurring_payments tool'unu ekle; backend sonucunu LLM yalnızca açıklasın.
- [ ] REC-05 — SwiftUI recurring listesini ekle; tahminin gerekçesi ve son ödeme tarihi gösterilsin.
- [ ] EXP-01 — Transaction açıklama akışını ekle. Verilen işlem bilgisi dışına çıkma; kesin olmayan merchant/amaç yorumunu kesin gerçek gibi sunma.
- [ ] EXP-02 — TransactionDetailView içine açıklama isteği ekle; loading/error ve yeniden deneme durumlarını doğrula.

## 19. Güvenilirlik, erişim sınırları ve değerlendirme

- [x] SEC-01a — Kullanıcı kararlarıyla gerçek giriş: ayrı `CustomerCredentials` tablosu (onaylı migration `AddCustomerCredentials`), kullanıcı adı (CUST-n değil), ASP.NET `PasswordHasher` özeti, `UpdatedAt` sunucu saati, `CredentialStore` (çakışma = hata), CLI `users set-password` (gizli/iki kez/borudan). Demo kullanıcıları ada/deniz/ege; rastgele şifreler yalnız yerel `.env`'de, `.env.example`'da boş satırlar. 17 test + kasıtlı 2 güvenlik hatası yakalandı. docs/security.md.
- [x] SEC-01b — `POST /api/auth/login` → `{ token, expiresAt }` (HS256 JWT, 60 dk, `ClockSkew` 0), yanlış şifre ve bilinmeyen kullanıcı aynı 401 (bilinmeyen kullanıcı da bir PBKDF2 doğrulaması öder), eksik/boş alan 400, IP başına dakikada 5 deneme → 429 (başarılılar da sayılır), `GET /api/me` (müşteri kimliği cevapta yok). Kullanıcı kararlarıyla: bütün endpoint'ler varsayılan olarak token ister (FallbackPolicy; tokensız bilinmeyen route 401), `BANKINGAI_JWT_SIGNING_KEY` yoksa/32 bayttan kısaysa API açılmaz. `CredentialStore.VerifyAsync`, `TokenIssuer`, `User.GetCustomerId()`. Testler: geçici commit'li "Test Api" müşterisi + `tests/Shared/CommittedRowsLock` (paralel test projeleri arası advisory lock); paralel koşuda ortaya çıkan iki eski test sorunu düzeltildi (Ada'nın girişi olduğu için yanlış kısıta takılan credential testi; "ilk müşteri"ye geçici hesap açan iki test → sabit Ada/Deniz). 4 kasıtlı bozma (limit, anahtar kontrolü, ClockSkew, FallbackPolicy) + kilitsiz koşu kırmızı; tam paket 10/10 yeşil. Data 58, Api 22, Application 136. Gerçek API'de ada ile giriş/me/429 denendi. Hesap kilitleme, logout/iptal, refresh token sonraya.
- [ ] SEC-02 — Hesap/müşteri izolasyonunu API, servis ve MCP boyunca test et. Başka hesaba ID vererek erişim mümkün olmamalı; UI'da gizlemek yeterli değil.
- [ ] SEC-03 — Prompt injection örneklerini dene. Kullanıcı mesajı, RawDescription ve tool verisindeki talimatlar yetkiyi veya allowlist'i değiştiremesin.
- [ ] SEC-04 — LLM'nin SQL veya banking write operasyonu yaptıramadığını doğrula. Bilinmeyen tool ve ayrıcalık iddiaları reddedilsin.
- [ ] SEC-05 — Secret/log kontrolü yap. Bağlantı parolası, API anahtarı ve gereksiz müşteri detayları log/fixture/repo içine girmesin.
- [ ] EVAL-01 — En az 50, tercihen 75–100 synthetic soru hazırla. Beklenen tool, arguments, sonuç ve belirsizlik davranışı bağımsız olarak tanımlansın.
- [ ] EVAL-02 — Tool selection ve argument accuracy ölç. Yanlış tarih, kategori, merchant ve hesap seçimini ayrı raporla.
- [ ] EVAL-03 — Finansal doğruluk ve grounded response ölç. Modelin eklediği uydurma sayı, eksik fiyatı gizleme ve yanlış birimi hata say.
- [ ] EVAL-04 — Opportunity-cost fiyat seçimi, birim ve hesap sonuçlarını ayrı ölç; yalnızca cevap akıcı diye başarılı sayma.
- [ ] EVAL-05 — PostgreSQL, Ollama, FastAPI ve MCP kesintilerini ayrı test et. Timeout/iptal kontrollü bitsin; yanlış başarı cevabı verilmesin.
- [ ] EVAL-06 — Latency, başarı oranı ve hallucination sonuçlarını model/prompt sürümüyle raporla. Değişikliklerden sonra aynı fixture ile karşılaştır.

## 20. Çalıştırma, gözlemlenebilirlik ve performans

- [ ] OPS-01 — .NET API için Dockerfile hazırla ve yerel çalıştırmayı doğrula.
- [ ] OPS-02 — FastAPI için Dockerfile hazırla; bağımlılık sürümleri ve sağlık kontrolünü doğrula.
- [ ] OPS-03 — MCP servisini container içinde çalıştır; yalnızca gerekli portları aç.
- [ ] OPS-04 — Compose ile PostgreSQL/API/FastAPI/MCP bağlantılarını kur. Ollama Mac host üzerinde kalabilir; secret ve volume yönetimini belgele. Not (API-01b): API'de HTTPS yönlendirmesi kaldırıldı; TLS/HTTPS kararı burada verilecek.
- [ ] OPS-05 — Sıfırdan yerel kurulum denemesi yap. DB migration/seed/import ayrı açık adımlar olarak çalışsın; mevcut veriyi yeniden başlatmada kaybetme.
- [ ] OBS-01 — .NET tarafına trace/correlation kimliği ekle; DB sorgusu ve HTTP çağrıları ilişkilendirilsin.
- [ ] OBS-02 — FastAPI ve MCP üzerinden trace bağlamını taşı; tek sorunun uçtan uca izini örnekle doğrula.
- [ ] OBS-03 — Süre, hata, tool çağrı sayısı ve DB sorgu sayısı ölçümlerini ekle; finansal içerikleri gereksiz loglama.
- [ ] PERF-01 — 100, 1.000 ve 10.000 işlemle deterministik test dataset'leri hazırla; gerçek market fiyatlarını fake veriye dönüştürme.
- [ ] PERF-02 — Analytics SQL sorgularını ölç. N+1, gereksiz belleğe çekme, fazla sorgu ve yavaş sorguları bul. Not (ANA-07): aylık seri satırları C#'ta aylara dağıtıyor; büyük veri setinde `AT TIME ZONE` ham SQL ile değiştirmeyi ölç.
- [ ] PERF-03 — Gerçek query planlarına göre index'leri değerlendir. Ölçüm olmadan index çoğaltma; gereksiz örtüşen index'leri incele.
- [ ] PERF-04 — API/MCP/LLM sürelerini ayrı raporla. DB iyileştirmesiyle model gecikmesini birbirine karıştırma.

## 21. Final review ve demo

- [ ] FIN-01 — README'yi kurulum, migration, seed, import, servisler ve demo akışıyla tamamla. Yeni makinede takip edilebilir olsun.
- [ ] FIN-02 — Gerçek uygulanmış mimari ve ER diyagramını çiz. Gelecek fikirleri mevcut sistem gibi gösterme.
- [ ] FIN-03 — API, MCP ve AI sözleşmelerini güncelle. Örnek istek/cevap, hata, limit ve yetki sınırları bulunsun.
- [ ] FIN-04 — Test/evaluation sonuçlarını ve bilinen sınırları tek raporda topla. Tarih, dataset, model ve prompt sürümleri görülsün.
- [ ] FIN-05 — Kullanılmayan kod, gereksiz interface, eski doküman, uyarı ve secret kontrolünü tamamla.
- [ ] FIN-06 — Demo: bu ayki harcama, dönem farkı, kategori dağılımı ve Starbucks sorgularını göster.
- [ ] FIN-07 — Demo: recurring payments, işlem açıklaması ve merchant normalization akışını göster.
- [ ] FIN-08 — Demo: USD/EUR/altın tarihsel karşılaştırmasını kaynak/fiyat tarihiyle göster; eksik veri ve negatif fark örneklerini de dene.
- [ ] FIN-09 — Demo: başka müşteriye erişim, bozuk input ve AI servis kesintisinde kontrollü davranışı göster.
- [ ] FIN-10 — Kullanıcıyla son kabul review'ü yap. Açık maddeleri yapılmış işaretleme; ertelenen işleri gerekçesiyle ayır.

## Kapsam dışında kalanlar

Ücretli AI/API servisleri, RAG, fine-tuning, LangGraph, Kubernetes, microservices,
cloud deployment, LLM üzerinden para transferi/kart kapatma, gereksiz repository/CQRS,
erken Merchant/MerchantMappings/RecurringPayments/InvestmentSimulations/AI log tabloları.
Günlük market scheduler'ı historical import tamamlanmadan eklenmeyecek; ihtiyaç ve
kullanıcı kararıyla daha sonra ayrı iş olarak planlanabilir.

## Şu anki sonraki adım

SYN-01–13 banking seed adımları tamamlandı ve banking foundation kabul edildi (DOC-09).
ANA-01 (PeriodResolver) ve SYN-14 (müşteri bazlı demo verisi) tamamlandı. KIND-01–04 tamamlandı. ANA-01–08 ve REC-01–03 tamamlandı; demo sorularının deterministik backend'i hazır ve kenar durumları test edildi. ANA-09 API-01'e bağlı. API-01a/01b, API-02, SEC-01a ve SEC-01b tamamlandı (BankingAI.Api + DI + health; CustomerCredentials + CLI users set-password + demo kullanıcıları; login + JWT + /api/me, varsayılan olarak token zorunlu). Sıradaki iş: API-03+ ve ANA-09 (müşteri her zaman `User.GetCustomerId()` ile token'dan). Market kolu
(SRC-02 altın kaynağı) paralel bekliyor.
Bu dosyanın oluşturulması listedeki işleri otomatik başlatmaz; kullanıcının seçtiği
madde üzerinden ilerlenir.
