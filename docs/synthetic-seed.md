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
