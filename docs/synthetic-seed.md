# Deterministic synthetic seed

SYN-01 yalnızca gelecekte üretilecek sentetik verinin değişmeyen ayarlarını tanımlar. Bu adımda Customer, Account veya Transaction kaydı üretilmez ve veritabanına yazılmaz.

- `RandomSeed`: `20261009`
- `ReferenceUtc`: `2025-10-01T00:00:00Z`
- `DatasetVersion`: `synthetic-v1`

`SyntheticSeedSettings.CreateFaker<T>()` her çağrıda aynı seed ile yeni bir Bogus `Faker<T>` oluşturur. Faker global bir değişkende tutulmadığı için farklı üretim akışları ortak mutable rastgelelik durumu paylaşmaz. Aynı sürüm, seed ve kurallar ile çalıştırılan üretim aynı değer dizisini tekrar üretir.

Bu ayarlar veri setinin kimliğini ve tarih referansını sabitler; tek başına veritabanı idempotency'si, gerçek veri doğrulaması veya seed komutu sağlamaz. Synthetic kayıt üretimi SYN-02 ve sonraki adımlarda eklenecektir.

## SYN-02 profilleri

`SyntheticCustomerProfileCatalog` üç kararlı profil tanımlar:

| Kod | Ana pattern | İkincil pattern |
| --- | --- | --- |
| `COFFEE_SUBSCRIPTIONS` | `FOOD`, `SUBSCRIPTION` | `ENTERTAINMENT`, `BILLS` |
| `FAMILY_MARKET` | `GROCERY`, `BILLS` | `CLOTHING`, `HEALTH`, `FOOD` |
| `TRANSPORT_TRAVEL` | `TRANSPORTATION`, `TRAVEL` | `FUEL`, `ENTERTAINMENT`, `FOOD` |

Merchant isimleri de sabit recurring ve tek seferlik listeler olarak tanımlıdır. Bunlar gerçek müşteri bilgisi değildir; ilerideki üreticiye yalnızca kontrollü örnek seçimi sağlar.

VAL-05 gereği bu profiller yalnızca tamamlanmış mal/hizmet, abonelik ve fatura Debit hareketleri için kullanılacaktır. Maaş/açılış fonlaması Credit olarak ayrı adımda ele alınır. Transfer, refund/iptal, nakit, yatırım ve bekleyen hareket üretilmez; bu adımda `TransactionKind` eklenmemiştir.

## SYN-03 Customer seed'i

`seed customers` komutu üç sabit GUID kullanır. İsimler ve merchant kalıpları demo amaçlıdır; telefon, TCKN, adres veya gerçek kişi bilgisi üretilmez. `CustomerNumber` PostgreSQL sequence tarafından oluşturulur, seed tarafından elle verilmez.

Seed mevcut demo kaydını silmez veya güncellemez. Sabit GUID mevcut değilse kayıt eklenir; aynı GUID aynı içerikle zaten varsa atlanır. Aynı GUID farklı ad, soyad, para birimi veya saat dilimiyle mevcutsa seed sessizce üzerine yazmak yerine hata verir. Advisory lock eşzamanlı çalıştırmaları sıraya alır.

## SYN-04 / SYN-05 Account seed'i

Önce `seed customers`, ardından `seed accounts` çalıştırılır. Eksik müşteri varsa
Account seed açıklayıcı hata verir; kendiliğinden müşteri oluşturmaz.

| Sabit Account ID | Müşteri | Dummy numara | Para birimi | Amaç |
| --- | --- | --- | --- | --- |
| `20000000-0000-0000-0000-000000000001` | Ada Kahve | `DUMMY-ADA-TRY-001` | TRY | Günlük harcama |
| `20000000-0000-0000-0000-000000000002` | Ada Kahve | `DUMMY-ADA-USD-001` | USD | Aynı müşteride ikinci para birimi |
| `20000000-0000-0000-0000-000000000003` | Deniz Market | `DUMMY-DENIZ-TRY-001` | TRY | Başka müşteri sahipliği |
| `20000000-0000-0000-0000-000000000004` | Ege Yolcu | `DUMMY-EGE-EUR-001` | EUR | Seyahat profili |
| `20000000-0000-0000-0000-000000000005` | Ada Kahve | `DUMMY-ADA-EMPTY-001` | TRY | Gelecek transaction seed'lerinde de boş bırakılacak hesap |

Customer ID'leri SYN-03'teki `10000000-...0001/0002/0003` değerleridir.
Numaralar gerçek IBAN değildir. Yeni hesapların tamamı `Balance = 0` ile açılır;
bu adımda hiçbir hesaba transaction eklenmez. İleride yalnızca EMPTY hesabı
özellikle boş tutulur, diğerlerine hareket eklenebilir.

Seed girdileri mevcut entity doğrulamasından geçer (TRY/USD/EUR/GBP whitelist'i
dahil); kendi içinde duplicate ID/numara ve sıfır başlangıç bakiyesi de kontrol edilir.
DB'de eksik Customer önceden kontrol edilir, FK son korumayı sağlar.
Aynı ID'de farklı sahiplik, numara, para birimi veya ad varsa hata verilir.
Dummy numara farklı bir ID'de kullanılıyorsa hata verilir. Bütün kontrol ve insert'ler
tek transaction içindedir; advisory lock aynı account seed komutlarını sıraya alır.

Tekrar çalıştırma mevcut hesabın bakiyesini veya CreatedAt değerini değiştirmez.
Sıfır kuralı **yeni hesap açılışı** içindir; ilerideki işlemlerin bakiyesi silinmez.
CreatedAt mevcut DbContext tarafından kayıt anında UTC atanır; sabit ID ve
numaralar tekrar üretilebilir olsa da kayıt zamanı sabit dataset tarihi değildir.

9 Ekim 2026 doğrulaması: ilk çalıştırmada 5, ikincide 0 eklendi. DB'deki beş
hesabın sahipleri/para birimleri doğrulandı; tüm bakiyeler 0, hareket sayıları 0.
PostgreSQL testinde tekrar, bakiye/tarih koruma, sahiplik ve numara çelişkileri,
unique numara, olmayan müşteri FK'si ve geçersiz currency kontrol edildi.
Test değişiklikleri transaction rollback ile geri alındı.

## BAL-01 / BAL-02 atomik transaction yazımı

`TransactionSeedWriter.AddAsync` seed için tek transaction yazma sınırıdır.
`BankingDbContext` üzerinde önceden bekleyen değişiklik varsa çağrı reddedilir;
EF `SaveChanges` bu değişiklikleri de kaydedebileceği için writer temiz bir context ister.
Mevcut DB transaction'ı olan context de kabul edilmez; writer kendi transaction'ının
rollback sorumluluğunu taşır.

Akış:

1. Transaction alanları doğrulanır ve sabit Transaction ID'nin daha önce yazılıp
   yazılmadığı kontrol edilir. Duplicate ID ikinci balance etkisini alamaz.
2. Transaction aynı DB transaction'ı içinde insert edilir.
3. Account için `UPDATE ... SET Balance = Balance + delta` biçiminde atomik SQL
   çalışır. Debit'te delta negatiftir; Credit'te pozitiftir. WHERE koşulu yeni
   bakiyenin 0..9999999999999999.99 aralığında olmasını gerektirir.
4. Hesap/para birimi eşleşmiyorsa, bakiye yetersizse veya üst sınır aşılırsa hata
   verilir ve transaction insert'i rollback edilir. Commit yalnızca iki yazma da
   başarılı olunca yapılır.

PostgreSQL aynı hesabın eşzamanlı UPDATE işlemlerini satır kilidiyle sıraya alır;
her UPDATE güncel satır bakiyesine delta ekler. Uygulamada önceden okuma ve tüm
bakiye değerini üzerine yazma yapılmadığı için klasik lost update oluşmaz.
Bu davranışın eşzamanlı iki işlem ve kasıtlı insert hatasıyla kabul testleri
BAL-03/04'te doğrulanacaktır. Writer başarısızlıkta EF change tracker'ı temizler;
çağıran aynı context ile kayda devam etmeden önce entity'leri yeniden yüklemelidir.
