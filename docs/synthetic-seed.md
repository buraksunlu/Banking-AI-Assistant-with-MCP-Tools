# Deterministic synthetic seed

SYN-01 sentetik veri ayarlarını sabitler. Aşağıdaki sonraki adımlar kontrollü
Customer, Account ve Transaction kayıtlarını ayrı komutlarla veritabanına yazar.

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
| `20000000-0000-0000-0000-000000000006` | Ege Yolcu | `DUMMY-EGE-TRY-001` | TRY | SYN-14 ile eklendi; Türkiye içi ulaşım/seyahat harcamaları |

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

SYN-05 aşamasındaki 9 Ekim 2026 doğrulaması: ilk çalıştırmada 5, ikincide 0
hesap eklendi. O aşamada beş hesabın bakiyesi 0 ve hareket sayısı 0 idi; SYN-06/07
sonrasında aşağıdaki bakiye özeti günceldir.
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
BAL-03 testinde 0 başlangıç + 100/25 Credit − 40 Debit sonucu 85 doğrulandı.
BAL-04 testinde insert FK hatası ve sonradan reddedilen yetersiz Debit ayrı ayrı
denendi; her ikisinde de kısmi kayıt/bakiye etkisi kalmadı. Writer başarısızlıkta EF change tracker'ı temizler;
çağıran aynı context ile kayda devam etmeden önce entity'leri yeniden yüklemelidir.

## SYN-06 gelir ve açılış fonlamaları

`seed transactions credits` 14 sabit ID'li hareket üretir:

- Ada'nın TRY hesabına 2025 Nisan–Eylül aylarının her birinde ayın 5'i 06:00 UTC'de 45.000 TRY maaş Credit'i.
- Deniz'in TRY hesabına aynı günlerde 55.000 TRY maaş Credit'i.
- 1 Nisan 2025 08:00 UTC'de Ada'nın USD hesabına 500 USD ve Ege'nin EUR hesabına 1.000 EUR açılış Credit'i.

Her Credit'in `CategoryId`, `CategorizationSource`, `CategoryConfidence` ve
`CategorizedAt` alanları null'dır. Açılış fonlaması bağımsız para girişi olarak
kalır; başka hesapta karşı Debit/transfer yaratılmaz. Para birimi hesapla eşleşir.

## SYN-07 recurring subscriptions

`seed transactions subscriptions` Ada'nın TRY hesabında şu Debit hareketlerini
Temmuz, Ağustos ve Eylül 2025'te üretir:

| Merchant | Tutar | Gün ve saat | Kategori |
| --- | --- | --- | --- |
| Netflix | 229.99 TRY | Ayın 12'si, 07:00 UTC | `SUBSCRIPTION`, Rule |
| Spotify | 99.99 TRY | Ayın 18'i, 07:00 UTC | `SUBSCRIPTION`, Rule |

Bu örnekler tamamlanmış abonelik ödemeleridir. Her iki merchant üç ayrı ayda
aynı güne ve aynı tutara sahiptir. Sentetik tutarlar güncel ticari fiyat iddiası
değildir.

Her iki komut da sabit Transaction ID kullanır. Aynı içerik zaten varsa atlanır;
aynı ID farklı içerikle bulunursa hata verir. Her kayıt `TransactionSeedWriter`
üzerinden yazıldığı için transaction ve bakiye aynı DB transaction'ında değişir.
9 Ekim 2026 doğrulamasında ilk çalıştırma 14 Credit ve 6 Debit ekledi; tekrar
çalıştırma 0/0 ekledi. DB bakiyeleri transaction'lardan yeniden hesaplanan
Credit − Debit toplamıyla eşleşti: Ada TRY 269.010,06; Ada USD 500; Deniz TRY
330.000; Ege EUR 1.000; boş hesap 0. Credit kategorileri null ve açılış
fonlaması için eşleşen Debit sayısı sıfırdır.

## SYN-08 tekil kategori örnekleri

`seed transactions category-examples` aşağıdaki altı sentetik Debit'i ekler.
RawDescription, banka ekstresinden gelebilecek ham açıklama biçimini;
NormalizedMerchant ise eşleştirme/analiz için sade merchant adını temsil eder.

| Tarih (UTC) | Kategori | Tutar | RawDescription | NormalizedMerchant |
| --- | --- | ---: | --- | --- |
| 2025-05-10 | FOOD | 185 TRY | `POS 4412 STARBUCKS KADIKOY` | Starbucks |
| 2025-05-14 | GROCERY | 2.500 TRY | `POS 1820 AILE MARKETI KADIKOY` | Aile Marketi |
| 2025-06-02 | CLOTHING | 3.200 TRY | `POS 0731 COCUK GIYIM` | Çocuk Giyim |
| 2025-06-04 | TRANSPORTATION | 450 TRY | `ISTANBULKART ULASIM ODEME` | Şehir Ulaşım |
| 2025-06-08 | FUEL | 1.900 TRY | `POS 6621 AKARYAKIT ISTASYONU` | Akaryakıt İstasyonu |
| 2025-06-15 | BILLS | 1.450 TRY | `ELEKTRIK FATURA TAHSILATI` | Elektrik Faturası |

Komut hesap/para birimi eşleşmesini ve kategori ID'sinin beklenen Code değerine
karşılık geldiğini eklemeden önce kontrol eder. Tutarlar sentetiktir, gerçek fiyat
iddiası değildir.

## SYN-09 sabit GROCERY dönem toplamları

`seed transactions grocery-growth` Deniz'in TRY hesabına aşağıdaki sabit üçer
hareketi ekler. İki dönem de tamamlanmış aydır; hedef toplamlar çalışan sistem
saatinden bağımsızdır.

| Dönem | Hareket tutarları | Beklenen GROCERY toplamı |
| --- | --- | ---: |
| 2025-08 | 9.800 + 8.200 + 6.000 TRY | **24.000 TRY** |
| 2025-09 | 12.500 + 10.500 + 8.000 TRY | **31.000 TRY** |

Altı kaydın tamamında kategori GROCERY, NormalizedMerchant `Aile Marketi`, yön
Debit'tir. RawDescription içindeki POS/şube kodları değişirken merchant adı aynı
kalır. Sabit beklenen değerler, dönem karşılaştırmasının 7.000 TRY artışı
bulması için bağımsız kontrol sağlar.

## SYN-08 / SYN-09 tekrar ve bakiye kontrolü

9 Ekim 2026'da iki komutun ilk çalıştırması ayrı ayrı 6 kayıt ekledi; tekrar
çalıştırmalarının her biri 0 ekledi. PostgreSQL'de GROCERY toplamları Ağustos'ta
24.000 TRY, Eylül'de 31.000 TRY olarak okundu. Altı tekil örneğin açıklaması,
merchant'ı ve kategorisi de DB'den kontrol edildi. Tüm hareketler
`TransactionSeedWriter` üzerinden eklendiğinden bakiye etkisi de aynı DB
transaction'ında yazıldı. Güncel hesap bakiyeleri todo.md'de yer alır.

## SYN-10 kategorisiz ve OTHER ayrımı

`seed transactions categorization-boundaries` Ada'nın TRY hesabına iki sabit
Debit ekler:

| ID sonu | Tutar | RawDescription | NormalizedMerchant | Kategori metadata'sı |
| --- | ---: | --- | --- | --- |
| `401` | 420 TRY | `POS 9055 KADIKOY HARCAMA` | `null` | CategoryId, source, confidence ve CategorizedAt `null` |
| `402` | 780 TRY | `POS 3182 HOBI MALZEME DUKKANI` | Hobi Dükkânı | CategoryId `11` (`OTHER`), source `Rule`, confidence `null`, CategorizedAt dolu |

`OTHER`, kategori değerlendirmesinden sonra diğer gruplara uymayan işlemdir.
İlk satır ise henüz kategorize edilmemiştir. Merchant normalizasyonu kategori
durumundan bağımsızdır: ilk işlemde merchant yoktur, fakat ham açıklama saklıdır.
Komut kategori ID 11'in `OTHER` koduna karşılık geldiğini doğrular.

## SYN-11 transaction seed eşzamanlılığı

Transaction seed grupları sabit ID'lerle mevcut kayıtları kontrol eder. Aynı
ID'de aynı içerik varsa atlanır; farklı içerik varsa sessizce değiştirilmez ve
hata verilir. Her yeni kayıt `TransactionSeedWriter` içinde kendi DB transaction'ı
ile eklenir; balance delta başarısız olursa işlem insert'i de rollback olur.

`SyntheticTransactionSeeder` existence check ve yazma akışının tamamı boyunca
sabit PostgreSQL session-level advisory lock tutar. Böylece iki ayrı süreç aynı
scope'u eşzamanlı başlatsa bile ikinci süreç kayıt kontrolünü ilk süreç bittikten
sonra yapar. 10 Ekim 2026'da aynı komut iki ayrı süreçte birlikte çalıştırıldı:
biri 2 kayıt ekledi, diğeri 0 ekledi; DB'de iki satır vardı ve 1.200 TRY bakiye
delta'sı yalnızca bir kez uygulandı. Bu değişiklik yeni tablo veya migration
gerektirmez. Farklı uygulama kodunun doğrudan `TransactionSeedWriter` çağrıları
bu seed lock'una dahil değildir; bu kilit özellikle synthetic seed komutlarını
sıralar.

## SYN-12 bağımsız dataset/bakiye fixture'ı

`tests/BankingAI.Data.Tests/SyntheticDatasetIntegrityTests.cs` beş sabit dummy
hesabı DB'den okur. Beklenen Credit/Debit toplamları test dosyasında açık sabitler
olarak tutulur; `SyntheticTransactionSeeder` üzerinden hesaplanmaz. Test hem
Credit/Debit fixture değerlerini hem de `Balance = Credit − Debit` eşitliğini
kontrol eder:

| Hesap | Beklenen Credit | Beklenen Debit | Beklenen bakiye |
| --- | ---: | ---: | ---: |
| `DUMMY-ADA-TRY-001` | 540.000 TRY | 5.464,82 TRY | 534.535,18 TRY |
| `DUMMY-ADA-USD-001` | 500 USD | 0 USD | 500 USD |
| `DUMMY-DENIZ-TRY-001` | 660.000 TRY | 179.500 TRY | 480.500 TRY |
| `DUMMY-EGE-EUR-001` | 1.000 EUR | 0 EUR | 1.000 EUR |
| `DUMMY-ADA-EMPTY-001` | 0 TRY | 0 TRY | 0 TRY |

Bu test seeded `bankingai_foundation` PostgreSQL verisini okur; seed komutları
önceden çalıştırılmamışsa hesaplar veya beklenen tutarlar uyuşmadığı için başarısız olur.
Beklenen tablo artık 2025 ve 2026 hareketlerinin tamamını içerir. Ada'ya yeni
270.000 TRY maaş ve 3.089,88 TRY harcama; Deniz'e 330.000 TRY maaş ve 115.000 TRY
market harcaması eklendi. Eski Debit toplamları sırasıyla 2.374,94 ve 64.500 TRY idi.

## SYN-13 harcama tarih kapsamı

Kullanıcı, altın/USD/EUR fiyatlarını sonraki adıma bıraktı ve SYN-13'ün mevcut
kapsamını banking harcama verisi olarak onayladı. Gerçek market fiyatı ekleme ve
banking/market dönem örtüşmesi ilerideki SRC/IMP çalışmalarına aittir.

`seed transactions recent-spending` 36 sabit ID'li işlem ekler. Nisan–Eylül 2026
aylarının her birinde:

- Ayın 5'inde Ada TRY hesabına 45.000 TRY, Deniz TRY hesabına 55.000 TRY maaş Credit'i.
- Ada için ayın 12'sinde Netflix 229,99 TRY, 18'inde Spotify 99,99 TRY ve 20'sinde
  Starbucks 185 TRY Debit'i. Ay başına toplam 514,98 TRY harcama.
- Deniz için ayın 24'ünde `Aile Marketi` GROCERY Debit'i:

| Ay (2026) | Market harcaması | Ada harcaması |
| --- | ---: | ---: |
| Nisan | 12.000 TRY | 514,98 TRY |
| Mayıs | 14.000 TRY | 514,98 TRY |
| Haziran | 16.000 TRY | 514,98 TRY |
| Temmuz | 18.000 TRY | 514,98 TRY |
| Ağustos | 24.000 TRY | 514,98 TRY |
| Eylül | 31.000 TRY | 514,98 TRY |

Maaşlar kategorisiz Credit; harcamalar Rule kaynaklı FOOD/SUBSCRIPTION/GROCERY
kategorilerindedir. RawDescription ham POS/ödeme açıklamasını, NormalizedMerchant
sade adı taşır. Tutarlar gerçek fiyat iddiası olmayan dummy değerlerdir.

2025 seed tarihleri ve `SyntheticSeedSettings.ReferenceUtc` değiştirilmedi.
Yeni scope da sabit 2026 tarihlerine sahiptir; tekrar çalıştırma mevcut kaydı ve
bakiyeyi değiştirmez. İlk çalıştırma 36, tekrar 0 kayıt ekledi.

`SyntheticDatasetIntegrityTests` içinde 10 Nisan–10 Ekim 2026 demo penceresi
UTC olarak `[2026-04-10, 2026-10-11)` aralığıyla doğrulanır: 24 Debit, altı ayın
her birinde 4 harcama, her merchant'ta 6 ödeme ve toplam 118.089,88 TRY.
Beklenen aylık tutarlar seed kodundan alınmaz. 2025'teki 34 kayıt ayrıca sayılır;
dataset'in tümü 70 kayıttır. Test suite'i 22/22 geçti.

Bu, sabit 10 Ekim 2026 demosunun kapsamıdır. Gelecekte gerçek saate göre “son
altı ay” aralığı ilerler; sabit dataset kendiliğinden yeni işlem üretmez. Ekim
2025–Mart 2026 için işlem yoktur. `DailyMarketPrices` hâlâ boştur; fiyatlı
karşılaştırmaların tarih kapsamı bu adımda doğrulanmış sayılmaz.

## SYN-14 müşteri bazlı demo kapsamı

Kullanıcı kararı (10 Ekim 2026): demo tek müşteriye bağlanmaz; her müşteri farklı
bir harcama karakteri taşır. Müşteriler arası transfer ve dükkân satışı bu adımda yoktur;
`TransactionKind` tasarımıyla KIND adımlarında eklenecektir.

Önce `seed accounts` Ege'nin `DUMMY-EGE-TRY-001` hesabını ekler. Ardından
`seed transactions demo-coverage` sabit ID 601–628 ile 28 işlem yazar (9 Credit, 19 Debit).
Liste sırayla yazıldığından Ege'nin maaşları harcamalardan önce gelir; boş yeni hesapta
yetersiz bakiye oluşmaz.

| Müşteri | Karakter | Eklenenler |
| --- | --- | --- |
| Ada | Kahve/abonelik | 5 Eki maaş 45.000; 6 Eki Starbucks 210; 8 Eki Starbucks 165; 9 Eki `POS 6604 ONLINE ODEME 4471` 145 TRY (merchant ve kategori null) |
| Deniz | Aile/market | 4 Eki Aile Marketi 9.500; 5 Eki maaş 55.000; 7 Eki elektrik faturası 1.850 |
| Ege | Ulaşım/seyahat | Nisan–Ekim her ayın 5'i 40.000 maaş; ayın 11/25'inde İstanbulkart; 14 Ağu otobüs bileti 1.200 (TRAVEL); 3 Eki ulaşım 700 |

Ege'nin aylık ulaşım toplamları: Nisan 600, Mayıs 650, Haziran 700, Temmuz 650,
Ağustos 900, Eylül 1.400, Ekim 700. Ağustos seyahat nedeniyle 2.100 olduğundan Eylül
bir azalış örneğidir; Deniz'in Ağustos→Eylül market artışı (+7.000) artış örneğidir.

Belirsiz açıklamalı işlem bilerek bilinmeyen bırakılır: `CategoryId`, source, confidence,
CategorizedAt ve NormalizedMerchant null'dır. İleride kural ve LLM çözemezse de null kalır;
otomatik `OTHER` atanmaz (VAL-04).

Bağımsız fixture (`SyntheticDatasetIntegrityTests`): 1–10 Ekim Istanbul penceresinde üç maaş
(140.000 TRY) ve 12.570 TRY harcama (Ada 520, Deniz 11.350, Ege 700). 10 Nisan–10 Ekim
UTC penceresinde 43 Debit, 136.759,88 TRY. Hesap bakiyeleri: Ada TRY 579.015,18;
Deniz TRY 524.150; Ege TRY 273.200. İlk çalıştırma 28, tekrar 0 kayıt ekledi; Data suite 23/23.
