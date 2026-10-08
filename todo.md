# Proje TODO — AI-Powered Transaction Intelligence Platform

Son güncelleme: 8 Ekim 2026.

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
- Database foundation doğrulanmadan API, SwiftUI, AI veya MCP geliştirilmez.
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
| Uygulanmış migration | 7 |
| Veri | 1 Customer; 0 Account; 0 Category; 0 Transaction |
| Market tabloları | MarketAssets ve DailyMarketPrices mevcut; testler geri alındı, ikisi de boş |
| Bakiye güncelleme akışı | Henüz yok; SQL ile transaction eklemek bakiyeyi otomatik değiştirmez |
| Kalıcı otomatik test projesi | Henüz yok; önceki kontroller geçici program/SQL ile yapıldı |
| API / SwiftUI / AI / MCP | Henüz yok |

Migration geçmişi:
1. 20261007142113_CreateCustomers
2. 20261007143550_AddCustomerPreferences
3. 20261008083922_AddAccountsAndTransactionCategories
4. 20261008090052_AddTransactions
5. 20261008111025_SplitCustomerName
6. 20261008124728_AddMarketAssets
7. 20261008132653_AddDailyMarketPrices

## 1. Mevcut kararları ve belgeleri tutarlı hale getir

- [x] DOC-01 — Customer kapsamı netleştirildi: `FirstName` ve `LastName` required alanları kullanılacak; mevcut CustomerNumber, PreferredCurrency, TimeZone ve CreatedAt korunacak.
- [x] DOC-02 — Customer entity, DbContext eşlemesi, console çıktısı ve veri dönüşümlü `SplitCustomerName` migration'ı güncellendi. Mevcut `Demo Customer` kaydı `Demo` + `Customer` olarak taşınacak; eski migration dosyaları değiştirilmedi.
- [x] DOC-03 — README gerçek mevcut duruma getirildi. Silinmiş API, script, seed ve test sonuçları kaldırıldı; mevcut console ve migration komutları yazıldı.
- [x] DOC-04 — RULE.md ile docs/rule-update.md kuralları 8 Ekim 2026 tarihinde birleştirildi. Küçük adım/review, database-first, altı tablo ve hesaplama kararları güncellendi; eski tamamlanma kayıtları düzeltildi.
- [x] DOC-05 — docs/database-design.md altı tablo kararına göre güncellendi. Nullable CategoryId, FirstName/LastName, güncel tipler ve gerçek mevcut durum ayrı gösteriliyor.
- [x] DOC-06 — Eski ayrı yol haritası dosyası kaldırıldı; todo.md projenin tek planı oldu. Kategori ve opportunity-cost kararları son kullanıcı kararıyla uyumlu tutuldu.
- [x] DOC-07 — Incremental migration yaklaşımı seçildi. Mevcut migration geçmişi korunacak; her schema değişikliği yeni migration olarak eklenecek. Veritabanı resetlenmeyecek ve uygulanmış migration dosyaları geriye dönük değiştirilmeyecek.
- [x] DOC-08 — Yerel Git takibi oluşturuldu. .env ve build çıktıları dışarıda doğrulandı; remote/push ayrı karar olarak bırakıldı.

## 2. MarketAsset — tek sınıf, sonra eşleme, sonra tablo

- [x] MA-01 — MarketAsset.cs oluşturuldu ve açıklandı. Id: short; Symbol: string; Name: string; AssetType: string; BaseUnit: string; QuoteCurrency: string; IsActive: bool. Proje 0 hata, 0 uyarıyla derlendi. Yalnızca entity hazır; EF eşlemesi, tablo ve veri doğrulama kuralları henüz eklenmedi.
- [x] MA-02 — MarketAssetConfiguration oluşturuldu. Id smallint ve otomatik üretilmez; Symbol varchar(30) unique; Name varchar(100); AssetType varchar(30); BaseUnit varchar(20); QuoteCurrency char(3); IsActive boolean. Zorunluluk, pozitif ID, boş metin, Currency/Commodity ve TRY kısıtları tanımlandı. Derleme: 0 hata, 0 uyarı. DbContext kaydı ve migration henüz yok; DB kısıtları uygulanmadı/doğrulanmadı.
- [x] MA-03 — Tür/para birimi kısmı tamamlandı: Currency/Commodity ve quote TRY kısıtları aktif PostgreSQL'de doğrulandı. Üç geçerli kayıt kabul edildi; beş geçersiz tür/quote örneği beklenen CHECK kısıtlarınca reddedildi, tüm denemeler geri alındı. Symbol/BaseUnit eşleşme sözleşmesi docs/database-design.md içinde yazıldı. DB bu eşleşmeyi korumuyor; gerçek seed incelemesi SEED-04/SEED-05'i bekliyor. Bu nedenle madde bütünüyle tamamlandı işaretlenmedi. Ek lookup tablo yok.
- [x] MA-04 — DbSet ve configuration kaydını ekle; yalnızca MarketAssets tablosunu ekleyen migration üretip SQL içeriğini incele.
- [x] MA-05 — Migration'ı aktif DB'ye uygula; pgAdmin'de kolonları göster. Unique, required ve uzunluk kısıtlarını rollback edilen kayıtlarla doğrula.

## 3. DailyMarketPrice — günlük fiyat tablosu

- [x] MP-01 — DailyMarketPrice.cs oluşturuldu: Id long (private set), MarketAssetId short, PriceDate DateOnly, Price decimal, Source string?. DateOnly'nin takvim gününü, UTC timestamp'in belirli bir anı temsil ettiği açıklandı. Derleme: 0 hata, 0 uyarı. Yalnızca entity hazır; identity, FK, fiyat ve unique kuralları henüz eşlenmedi, tablo oluşturulmadı.
- [x] MP-02 — DailyMarketPriceConfiguration oluşturuldu. Id bigint identity; MarketAssetId smallint; PriceDate date; Price numeric(20,8); Source varchar(100) nullable. Price için pozitiflik ve NaN/Infinity/-Infinity reddi CHECK kısıtı tanımlandı. Derleme: 0 hata, 0 uyarı. FK/unique ilişki, DbContext kaydı ve migration MP-03/04 adımlarında tamamlandı; DB kontrolleri MP-05'te yapıldı.
- [x] MP-03 — MarketAsset 1:N DailyMarketPrice ilişkisi RESTRICT ile kuruldu. MarketAssetId + PriceDate unique index eklendi; DB'de yalnızca PK index'i ve bu birleşik unique index bulunduğu doğrulandı.
- [x] MP-04 — DbSet/configuration kaydı ve AddDailyMarketPrices migration'ı oluşturuldu. C# migration, model farkı ve üretilen SQL incelendi; yalnızca DailyMarketPrices tablosu, kısıtları/index'i ve migration geçmişi kaydı eklendi. bankingai_foundation üzerinde uygulandı; kolon tipleri ve identity doğrulandı.
- [x] MP-05 — Geçici .NET/EF doğrulama programı ile gerçek PostgreSQL'de aynı asset/gün, olmayan asset, sıfır/negatif/NaN/sonsuz fiyat ve RESTRICT silme reddi denendi. Farklı gün ve farklı asset kabul edildi; DateOnly UTC ve Europe/Istanbul oturumlarında aynı gün olarak okundu. Identity, nullable Source ve 8 ondalıklı decimal okuması doğrulandı. Denemeler ROLLBACK edildi; iki market tablosu da boş. Kalıcı test projesi henüz yok.

## 4. Banking modelinin kalan doğrulama kararları

- [ ] VAL-01 — Girdi kurallarını tablo halinde belirle. Boş GUID, null/whitespace, maksimum metin uzunluğu, para birimi, enum ve confidence sınırları için beklenen davranışı yaz.
- [ ] VAL-02 — Para precision politikasını belirle. Amount ve Balance iki ondalık basamakta; fazla basamaklı giriş sessizce yuvarlanmasın. PostgreSQL numeric tipi tek başına bu giriş politikasını sağlamaz.
- [ ] VAL-03 — Tarih kurallarını belirle. UTC/offset 0, default timestamp, gelecekteki işlem ve geçmiş import davranışlarını açıklaştır; OccurredAt ile CreatedAt farklı olabilir.
- [ ] VAL-04 — Kategorilendirme tutarlılığını kararlaştır. CategoryId, source, confidence ve CategorizedAt alanlarının birlikte hangi kombinasyonlarda geçerli olduğunu belirle; LLM threshold'u DB'ye koyma.
- [ ] VAL-05 — Harcama kapsamını belirle. Debit transferi alışveriş sanmamak için ilk dataset'te hangi işlem türlerinin yer alacağını yaz; refund/net harcama ihtiyacı varsa ayrıca model kararı al. Kendiliğinden TransactionKind ekleme.
- [ ] VAL-06 — Negatif bakiye politikasını netleştir. Kredili hesap simülasyonu var mı, yetersiz bakiye reddedilecek mi? Karar gelmeden Balance >= 0 kuralı ekleme.
- [ ] VAL-07 — Kararlaştırılan kontrolleri küçük parçalarla uygula. Entity, veri yazma yolu ve DB sorumluluklarını ayır; her yeni kural için geçersiz örnek dene.

## 5. Sabit lookup seed'leri

- [ ] SEED-01 — Manuel seed komutunun tasarımını yap. migrate, seed ve import ayrı çalıştırılsın; uygulama her açıldığında veri eklenmesin.
- [ ] SEED-02 — TransactionCategories seed'ini yaz. Sabit 1–11 ID ile FOOD, GROCERY, CLOTHING, TRANSPORTATION, ENTERTAINMENT, SUBSCRIPTION, TRAVEL, FUEL, BILLS, HEALTH, OTHER ve Türkçe görünen adları ekle.
- [ ] SEED-03 — Kategori seed'ini iki kez çalıştır. Satır sayısı 11 kalmalı; aynı ID altında farklı kod varsa sessizce üzerine yazılmamalı. Eşzamanlı çalıştırmanın davranışını da doğrula.
- [ ] SEED-04 — MarketAssets seed'ini yaz. USDTRY/USD/Currency, EURTRY/EUR/Currency, GRAM_GOLD_TRY/GRAM/Commodity; quote TRY ve IsActive true olsun.
- [ ] SEED-05 — Market seed tekrarını doğrula. Üç varlık çoğalmamalı; Symbol/AssetType/BaseUnit/QuoteCurrency birlikte docs/database-design.md içindeki MA-03 sözleşmesiyle eşleşmeli. Hem seed girdisinde hem mevcut DB kaydında USDTRY/EUR gibi yanlış eşleşmeleri dene; sessiz düzeltme/atlama yerine açıklayıcı hata ver. Bu doğrulama geçince MA-03'ün kalan kısmını tamamlandı işaretle.

## 6. Kontrollü synthetic müşteri ve hesaplar

- [ ] SYN-01 — Bogus'u yalnızca seed ihtiyacı için ekle. Sabit random seed, sabit referans tarih ve dataset sürümü seç; sonuçların tekrar üretilebilir olmasını sağla.
- [ ] SYN-02 — Küçük müşteri profilleri tanımla. Kahve/abonelik ağırlıklı, market/aile, ulaşım/seyahat gibi farklı pattern'leri yaz; gerçek müşteri bilgisi kullanma.
- [ ] SYN-03 — Customer seed'ini yaz. Mevcut demo kayıtla çakışmayı yönet; sabit ID'ler kullan, gerçek telefon/TCKN/adres üretme.
- [ ] SYN-04 — Account seed'ini yaz. Aynı müşteri için TRY ve döviz hesapları, başka müşterinin hesabı ve boş hesap örneği oluştur; numaralar açıkça dummy olsun.
- [ ] SYN-05 — Seed bütünlüğünü doğrula. Her hesap mevcut müşteriye ait olsun; numaralar unique, para birimleri whitelist içinde, başlangıç bakiyesi sıfır olsun.

## 7. Synthetic hareket ve bakiye tutarlılığı

- [ ] BAL-01 — Seed için en küçük atomik yazma yolunu tasarla. İşlem insert'i ile Account.Balance artışı/azalışı aynı DB transaction'ında olsun; bu aşamada API veya application service katmanı kurma.
- [ ] BAL-02 — Atomik bakiye değişimini uygula. Eski bakiyeyi okuyup üzerine yazmak yerine DB'de fark ekleme veya uygun eşzamanlılık kontrolü kullan; başarısızlıkta tamamı rollback olsun.
- [ ] BAL-03 — İki eşzamanlı işlemle lost update testi yap. Son bakiye başlangıç + Credit − Debit ile aynı olmalı.
- [ ] BAL-04 — Kasıtlı insert hatası üret. Transaction reddedildiğinde bakiye değişmemeli; ters yöndeki başarısızlık da yarım kayıt bırakmamalı.
- [ ] SYN-06 — Aylık maaş ve açılış fonlaması Credit hareketlerini ekle. Tarihler, tutarlar ve hesap para birimleri açık olsun.
- [ ] SYN-07 — Netflix ve Spotify recurring Debit pattern'lerini ekle. En az üç farklı ay ve benzer ödeme günleri olsun.
- [ ] SYN-08 — Starbucks, market, giyim, ulaşım, yakıt ve fatura örneklerini ayrı küçük gruplarla ekle. RawDescription ile NormalizedMerchant ayrımını koru.
- [ ] SYN-09 — Kategori bazlı dönem artışını oluştur. Tamamlanmış iki ay için 24.000 → 31.000 gibi bağımsız beklenen toplamlar belirle; dönemler ilerledikçe kendiliğinden kaymasın.
- [ ] SYN-10 — Kategorisiz ve merchant'ı bulunmayan hareketleri ekle. OTHER ile null kategori birbirinden ayrı test edilsin.
- [ ] SYN-11 — Banking seed idempotency'sini uygula. Sabit işlem ID'leri ve DB transaction sınırı kullan; tekrar çalıştırınca bakiye ikinci kez artıp azalmasın. Paralel seed yarışını engelle.
- [ ] SYN-12 — Dataset toplamlarını bağımsız fixture ile doğrula. Her hesabın bakiyesi bütün Credit toplamı eksi Debit toplamına eşit olsun; yanlış sonucu aynı generator ile tekrar hesaplayıp test etme.
- [ ] SYN-13 — Tarih kapsamını kontrol et. Son altı ay ve 2025 gibi demo sorguları için anlamlı veri bulunmalı; market tarih kapsamıyla örtüşmeli.

## 8. Gerçek historical market kaynağını seç

- [ ] SRC-01 — USDTRY ve EURTRY ücretsiz kaynak adaylarını incele. Resmî kaynak, erişim koşulları, API anahtarı ihtiyacı ve iki yıllık günlük kapsamı örnek yanıtla göster.
- [ ] SRC-02 — Gram altın kaynağını ayrı doğrula. Gram/ons, saflık, TRY yönü, fiyat tanımı ve iki yıllık erişim kanıtlanmadan kaynağı kesinleştirme.
- [ ] SRC-03 — Fiyat sözleşmesini yaz. Varlık başına tek seri; alış/satış/referans tercihi; kaynak takvim günü; eksik veri gösterimi; varsa birim dönüşüm formülü açık olsun.
- [ ] SRC-04 — Kullanıcıya kaynak seçimini ve kısıtlarını açıkla. Ücretli servis kullanma; erişim engeli varsa kapsam tamamlandı deme ve fake fiyatla kapatma.
- [ ] SRC-05 — Küçük ham veri örneğini saklayıp parser'ı dene. Ondalık ayırıcı, tarih formatı, null değerler, tekrarlar ve bozuk satırlar doğru ele alınsın; secret saklama.

## 9. Import'u birer varlıkla geliştir

- [ ] IMP-01 — Import girişini oluştur. Varlık ve başlangıç/bitiş tarihleri açık parametreler olsun; ilk aşama manuel komutla çalışsın.
- [ ] IMP-02 — USDTRY için kısa tarih aralığı import et. Kaynak fiyat/tarih/birim ile DB satırlarını örnek bazında karşılaştır.
- [ ] IMP-03 — EURTRY için aynı akışı doğrula. USD serisinin yanlışlıkla EUR sembolüne yazılamadığını kontrol et.
- [ ] IMP-04 — GRAM_GOLD_TRY import'unu doğrula. Gram/ons karışmasını ve gerekirse dönüşümün birimlerini test et; yöntem belgelenmiş olsun.
- [ ] IMP-05 — Idempotent yazımı uygula. Asset+gün unique kuralıyla aynı veri tekrarında duplicate oluşturma; revize fiyatları tanımlı politikayla işle ve raporla.
- [ ] IMP-06 — Kesinti/retry davranışını ekle. Sınırlı retry ve timeout kullan; tamamlanan parçalar tekrar çalıştırılınca bozulmasın, hatalar gizlenmesin.
- [ ] IMP-07 — Import raporu üret. Eklenen/değişen/değişmeyen/reddedilen satırlar, tarih kapsamı, kaynak ve veri boşlukları gösterilsin. Source alanı gerçek importlarda dolu olsun.
- [ ] IMP-08 — Üç varlık için en az iki yıllık veriyi yükle. Gerçek son yayımlanan güne kadar kapsamı doğrula; ilk transaction öncesi fallback için gerekiyorsa ek geçmiş getir.
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
- [ ] DB-08 — Foundation review yap. Altı tablo, banking dataset, üç varlıkta iki yıllık gerçek fiyat, test sonuçları ve bilinen sınırlar kullanıcıya gösterilsin. Bunlar tamamlanmadan API'ye geçme.

## 11. Controller-based API temeli — foundation sonrasında

- [ ] API-01 — Küçük ASP.NET Core controller projesi oluştur. Veri projesinin console/persistence sorumluluklarını birlikte incele; yalnızca gerekli referans veya ayrıştırmayı yap.
- [ ] API-02 — DbContext bağlantısını DI üzerinden yapılandır. Scoped yaşam süresi kullan; singleton DbContext/shared List kullanma. Migration'ı her HTTP başlangıcında otomatik çalıştırma.
- [ ] API-03 — İlk AccountDto ve CreateAccountRequest modellerini oluştur. Entity'yi doğrudan JSON'a verme; müşteri/hesap kapsamı ve kullanıcıdan alınmayacak alanları belirle.
- [ ] API-04 — İlk hesap okuma servisini oluştur. AsNoTracking, async I/O ve CancellationToken kullan; servis interface'inin gerekçesini incele, repository ekleme.
- [ ] API-05 — GET /api/accounts ekle. Müşteri kapsamı, pagination ve sıralama belirle; boş listede 200 ve [] dön.
- [ ] API-06 — GET /api/accounts/{id} ekle. Geçersiz ID, bulunamayan hesap ve başka müşteri kapsamını ayrı test et.
- [ ] API-07 — POST /api/accounts ekle. Girdi doğrulaması, Customer varlığı, unique numara ve sıfır bakiye kuralları; başarıda 201 + Location uygula.
- [ ] API-08 — TransactionDto ve create request sözleşmesini tanımla. Backend'in yönettiği kategori/created alanlarını kullanıcı girdisiyle karıştırma.
- [ ] API-09 — GET /api/accounts/{id}/transactions ekle. Tarih filtresi, pagination, deterministik sıralama ve boş sonuç davranışını test et.
- [ ] API-10 — GET /api/transactions/{id} ekle. DTO döndür ve hesabın müşteri kapsamını koru.
- [ ] API-11 — Synthetic transaction POST akışını ekle. Foundation'da doğrulanan atomik bakiye/insert yaklaşımını kullan; bunu AI write tool'u yapma.
- [ ] API-12 — Tekrar gönderilen POST davranışını belirle. Idempotency gerekiyorsa küçük ayrı tasarım yap; ağ tekrarlarında aynı işlemin iki kez oluşmasını sessizce kabul etme.
- [ ] API-13 — Ortak hata cevabını oluştur. Validation, bozuk JSON, 404, 409 ve beklenmeyen hata ayrımını yap; stack trace/bağlantı bilgisi sızdırma.
- [ ] API-14 — HTTP integration testlerini yaz. Başarı, null body, invalid input, unique yarış, hata ve veri kalıcılığı senaryoları kapsansın.
- [ ] API-15 — Yerel API kullanım örneklerini ve OpenAPI sözleşmesini hazırla. DTO'lar, status kodları ve hata örneklerini README/docs içinde göster.

## 12. Deterministik analytics — bir sorgu, bir adım

- [ ] ANA-01 — Ortak dönem sözleşmesini oluştur. Tek referans zamanı, Türkiye takvimi ve [başlangıç, bitiş) UTC sınırları; bu ay/son üç ay/geçen yıl anlamlarını netleştir.
- [ ] ANA-02 — Harcama özeti hesapla. Onaylanan Debit kapsamını ve para birimini filtrele; farklı para birimlerini dönüştürmeden toplama.
- [ ] ANA-03 — Kategori dağılımını hesapla. Null kategori ayrı grup olsun; toplam dağılım harcama özetiyle tutarlı olsun.
- [ ] ANA-04 — Merchant dağılımını hesapla. NormalizedMerchant üzerinden grupla; bilinmeyen merchant'ı kaybetme.
- [ ] ANA-05 — Transaction aramasını ekle. Hesap/tarih/kategori/merchant filtreleri ve sayfalama; boş veya belirsiz arama davranışı açık olsun.
- [ ] ANA-06 — En yüksek işlemleri getir. Limit sınırı ve eşit tutarlarda tutarlı sıralama olsun.
- [ ] ANA-07 — Dönem karşılaştırmasını ekle. Tutar farkı, yüzde farkı ve kategori katkıları; önceki dönem sıfırsa yüzdeyi uydurma.
- [ ] ANA-08 — Her hesabı bağımsız fixture ile test et. Boş veri, dönem sınırı, birden çok müşteri/para birimi ve kategorisiz kayıtlar kapsansın.
- [ ] ANA-09 — Doğrulanan hesaplamaları controller endpointlerine tek tek bağla. Controller yalnızca HTTP sorumluluğunu taşısın.

## 13. Opportunity-cost hesabı

- [ ] OC-01 — Hesap sözleşmesini sabitle. TRY Debit, seçilen kategori/dönem, her harcama tarihinde varsayımsal alım ve değerleme tarihi kullan; ücret/vergi/makas/enflasyon kapsamını yaz.
- [ ] OC-02 — USDTRY için tek işlem hesabı yaz. Amount / alış tarihinin referans fiyatı; ara birimi erken yuvarlama.
- [ ] OC-03 — Birden çok işlemi topla. Toplam birim × değerleme tarihine kadar son fiyat; fark ve yüzde farkını üret.
- [ ] OC-04 — Aynı motoru EURTRY ve GRAM_GOLD_TRY ile doğrula. Asset birimi ve quote currency eşleşmeli; varlığa özel kopya hesap motorları yazma.
- [ ] OC-05 — Fiyatları toplu getir. İşlem başına ayrı sorgu üretme; eksik/çok eski fiyatları açık sonuç durumuyla bildir.
- [ ] OC-06 — Karşılaştırma çıktısını oluştur. Harcama toplamı, birim, değer, fark, fiyat tarihleri, kaynak, yöntem ve kapsam dönsün; sonuç DB'ye kaydedilmesin.
- [ ] OC-07 — Çoklu varlık sıralamasını ekle. Aynı harcama kümesi, tarih ve kapsam kullan; eksik veride kesin kazanan gösterme.
- [ ] OC-08 — Bağımsız örneklerle test et. Tatil fallback'i, eksik ilk fiyat, sıfır harcama, negatif getiri ve gram/ons hataları kapsansın.
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
- [ ] CAT-05 — Düşük confidence politikasını ölçerek belirle. OTHER veya kategorisiz bırakma kararı application katmanında olsun; confidence'ı doğruluk kanıtı sayma.
- [ ] CAT-06 — Sonucu atomik olarak kaydet. CategoryId/source/confidence/CategorizedAt birlikte güncellensin; RawDescription korunsun.
- [ ] CAT-07 — Tekrar kategorilendirme politikasını belirle. Manual sonucu otomatik işlemle ezme; retry ve eşzamanlı güncellemeyi test et.
- [ ] CAT-08 — Fixture üzerinde merchant ve kategori doğruluğunu ölç. Yanlış örnekleri kaydet; evaluation örnekleriyle başarıyı yapay şişirecek veri sızıntısını önle.

## 18. Recurring payment ve açıklama

- [ ] REC-01 — Deterministik recurring kuralını tanımla. Aynı hesap/para birimi/merchant, benzer tutar ve yaklaşık aralık; ayların farklı gün sayılarını dikkate al.
- [ ] REC-02 — Netflix/Spotify pattern'leriyle detector yaz. Sonuç Transactions'tan hesaplansın; RecurringPayments tablosu oluşturma.
- [ ] REC-03 — Yanlış pozitif testleri ekle. Sık kahve alışverişi, tek seferlik ödeme, eksik ay ve fiyat değişimi senaryolarını dene.
- [ ] REC-04 — find_recurring_payments tool'unu ekle; backend sonucunu LLM yalnızca açıklasın.
- [ ] REC-05 — SwiftUI recurring listesini ekle; tahminin gerekçesi ve son ödeme tarihi gösterilsin.
- [ ] EXP-01 — Transaction açıklama akışını ekle. Verilen işlem bilgisi dışına çıkma; kesin olmayan merchant/amaç yorumunu kesin gerçek gibi sunma.
- [ ] EXP-02 — TransactionDetailView içine açıklama isteği ekle; loading/error ve yeniden deneme durumlarını doğrula.

## 19. Güvenilirlik, erişim sınırları ve değerlendirme

- [ ] SEC-01 — Basit demo kullanıcı bağlamı ve erişim sözleşmesini belirle. Müşteri kimliği model argümanından yetki sayılmasın; karmaşık production auth scope'u açma.
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
- [ ] OPS-04 — Compose ile PostgreSQL/API/FastAPI/MCP bağlantılarını kur. Ollama Mac host üzerinde kalabilir; secret ve volume yönetimini belgele.
- [ ] OPS-05 — Sıfırdan yerel kurulum denemesi yap. DB migration/seed/import ayrı açık adımlar olarak çalışsın; mevcut veriyi yeniden başlatmada kaybetme.
- [ ] OBS-01 — .NET tarafına trace/correlation kimliği ekle; DB sorgusu ve HTTP çağrıları ilişkilendirilsin.
- [ ] OBS-02 — FastAPI ve MCP üzerinden trace bağlamını taşı; tek sorunun uçtan uca izini örnekle doğrula.
- [ ] OBS-03 — Süre, hata, tool çağrı sayısı ve DB sorgu sayısı ölçümlerini ekle; finansal içerikleri gereksiz loglama.
- [ ] PERF-01 — 100, 1.000 ve 10.000 işlemle deterministik test dataset'leri hazırla; gerçek market fiyatlarını fake veriye dönüştürme.
- [ ] PERF-02 — Analytics SQL sorgularını ölç. N+1, gereksiz belleğe çekme, fazla sorgu ve yavaş sorguları bul.
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

Önerilen sonraki küçük iş: VAL-01 ile girdi doğrulama kurallarını tablo halinde belirlemek. MA-03'ün seed eşleşme doğrulaması seed aşamasını bekliyor; diğer açık maddeler listede korunuyor.
Bu dosyanın oluşturulması listedeki işleri otomatik başlatmaz; kullanıcının seçtiği
madde üzerinden ilerlenir.
