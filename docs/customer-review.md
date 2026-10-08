# Modül 1 — Customer tablosu

Durum: PostgreSQL üzerinde oluşturuldu ve kontrol edildi. Bu belge Customer
modelini açıklar; Account, TransactionCategory ve Transaction tabloları da
ayrı adımlarda oluşturulmuştur.

## Oluşan tablo

Database: bankingai_foundation. Tablo: public."Customers".

| Alan | Tip | Kural |
| --- | --- | --- |
| Id | uuid | Primary key, NOT NULL |
| CustomerNumber | varchar(32) | NOT NULL, unique; sequence varsayılanı CUST-1, CUST-2… |
| FirstName | varchar(100) | NOT NULL, boş/yalnızca normal boşluk olamaz |
| LastName | varchar(100) | NOT NULL, boş/yalnızca normal boşluk olamaz |
| PreferredCurrency | varchar(3) | NOT NULL; TRY/USD/EUR; varsayılan TRY |
| TimeZone | varchar(100) | NOT NULL; PostgreSQL tarafından tanınan saat dilimi; varsayılan Europe/Istanbul |
| CreatedAt | timestamp with time zone | NOT NULL |

Bir synthetic örnek kayıt var: Id = 11111111-1111-1111-1111-111111111111,
FirstName = Demo, LastName = Customer, CreatedAt = 2026-10-07T12:00:00Z.
Yeni alanlar mevcut kayıtta CustomerNumber = CUST-1, PreferredCurrency = TRY,
TimeZone = Europe/Istanbul olarak dolduruldu; Id ve CreatedAt korundu.
Bu tek kayıt tabloyu incelemek içindir; tam synthetic generator değildir.

## Dosyaları hangi sırayla inceleyelim?

1. `src/BankingAI.Data/Entities/Customer.cs`: Normal C# sınıfı. Guid.NewGuid()
   yeni nesneye ID verir. FirstName ve LastName için required, nesne oluştururken
   alanların atanmasını derleme aşamasında ister; tek başına runtime validation değildir.
   init, alanın normal kullanımda oluşturma sırasında atanmasını sağlar.
   CreatedAt varsayılanı DateTimeOffset.UtcNow’dur.
   CustomerNumber private setter ile normal çağıran kodun atamasına kapalıdır;
   DB insert sırasında üretir, EF dönen değeri nesneye aktarır. null! başlangıç
   ifadesi runtime doğrulaması değildir; kayıt öncesi numara henüz yoktur.
   PreferredCurrency ve TimeZone C# varsayılanları TRY ve Europe/Istanbul’dur.
2. `src/BankingAI.Data/BankingDbContext.cs`: EF Core’un veritabanı oturumu ve
   eşlemesi. DbSet<Customer> sorgu/kayıt giriş noktasıdır. OnConfiguring bağlantıyı
   ortam değişkeninden alır. OnModelCreating tablo adı, PK, zorunluluk, uzunluk
   ve boş isim kısıtını tanımlar. ValueGeneratedNever, ID’yi uygulamanın verdiğini
   belirtir. Bu küçük console aşamasında DI host’u gerekmiyor; API aşamasında
   DbContext yaşam süresi ve DI ayrıca ele alınacak.
   Yeni alanlar için DB varsayılanları, unique index ve CHECK kısıtları burada
   tanımlıdır. CustomerNumberSequence numarayı PostgreSQL üzerinde üretir.
3. `src/BankingAI.Data/Migrations/*_CreateCustomers.cs`: Up tabloyu oluşturur;
   Down bu migration geri alınırsa tabloyu siler. Down şu anda çalıştırılmadı.
   Migration, entity değişikliklerini DB’ye uygulayan sürümlenmiş şema adımıdır.
4. `src/BankingAI.Data/Migrations/*_CreateCustomers.Designer.cs`: EF’nin o
   migration’a ait ürettiği model bilgisi.
5. `src/BankingAI.Data/Migrations/BankingDbContextModelSnapshot.cs`: EF’nin
   sonraki model değişikliklerini karşılaştıracağı son şema modeli.
6. `src/BankingAI.Data/Program.cs`: Sadece migrate ve show komutları var.
   migrate mevcut migration’ları uygular; show ilk 10 müşteriyi UTC tarihleriyle
   okur. AsNoTracking, değişiklik yapılmayacak nesneleri takip etmez.
7. `src/BankingAI.Data/BankingAI.Data.csproj`: .NET 10 console proje ayarları,
   EF Core/Npgsql ve migration araç bağımlılıkları.
8. `.config/dotnet-tools.json`: Projeye özel dotnet-ef sürümü. `.gitignore`
   build çıktıları ve `.env` dosyasını dışarıda tutar. `.env.example` örnek
   bağlantıdır; `.env` gerçek yerel parolayı içerir ve paylaşılmamalıdır.

Entity dosyası tablonun kendisi değildir. DbContext eşlemeyi tanımlar;
migration PostgreSQL’de gerçek tabloyu oluşturur.

### İkinci migration: AddCustomerPreferences

`src/BankingAI.Data/Migrations/20261007143550_AddCustomerPreferences.cs`
üç sütunu, sequence'i, unique index'i ve yeni CHECK kısıtlarını ekler.
Mevcut kayıtların yeni sütunları DB varsayılanlarıyla doldurulur.
PostgreSQL CHECK içinde doğrudan alt sorgu kullanılamadığı için migration'a
`public.is_valid_customer_time_zone(text)` SQL fonksiyonu eklendi. Bu fonksiyon
`pg_catalog.pg_timezone_names` üzerinden adı doğrular; CHECK fonksiyonu çağırır.
Fonksiyon SQL'i elle eklenmiştir; EF modelinden otomatik üretilmez.
Down önce bu alanların kısıtlarını/sütunlarını, ardından sequence ve fonksiyonu
kaldırır. Geri alma bu alanlardaki verileri siler; bu adımda çalıştırılmadı.
İkinci Designer dosyası ve ModelSnapshot da EF tarafından güncellendi.
Program.cs show çıktısı yeni alanları içerir.

## Yerel kullanım

Bu makinede SDK `/usr/local/share/dotnet` altında bulundu. Terminal PATH’inde
dotnet yoksa aşağıdaki ayarlar kullanılabilir. Komutlar proje kökünde çalıştırılır.

```sh
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="$DOTNET_ROOT:$PATH"
set -a
source .env
set +a

dotnet run --project src/BankingAI.Data -- show
```

Migration zaten uygulandı. Tekrar uygulamak gerekirse:

```sh
dotnet run --project src/BankingAI.Data -- migrate
```

Bu komut migration dosyası üretmez; projede bulunan uygulanmamış migration’ları
uygular. Aynı migration ikinci kez tablo oluşturmaya çalışmaz.

PostgreSQL bağlantısı: host 127.0.0.1, port 55432, database/user
bankingai_foundation. Parola `.env` dosyasındadır. Ayrı database ve superuser
olmayan rol oluşturuldu; önceki bankingai database daha sonra temizlendi. Mevcut yerel
Docker PostgreSQL sunucusu kullanılıyor. Yeni compose yapısı bu adımda eklenmedi.

pgAdmin/query ekranında görmek için:

```sql
SET TIME ZONE 'UTC';
SELECT "Id", "CustomerNumber", "FirstName", "LastName", "PreferredCurrency", "TimeZone", "CreatedAt"
FROM public."Customers"
ORDER BY "Id";
```

## Doğrulananlar

- .NET build: sıfır uyarı, sıfır hata.
- Migration boş foundation database üzerinde çalıştı.
- Customers, Accounts, TransactionCategories ve Transactions tabloları var; ayrıca EF’nin __EFMigrationsHistory metadata tablosu var.
- NULL FirstName, NULL LastName ve NULL CreatedAt reddedildi.
- Yalnızca normal boşluk içeren isim ve 101 karakterlik isim reddedildi.
- Aynı ID ile ikinci kayıt primary key tarafından reddedildi.
- CreatedAt gerçek timestamptz sütunu; EF üzerinden UTC okunması doğrulandı.
- Migration ikinci kez çalıştırıldı; duplicate tablo/kayıt oluşmadı.
- Kontrol kayıtları transaction sonunda geri alındı; yalnızca bir demo kayıt bırakıldı.
- Ayrı console çalıştırmasıyla demo kayıt yeniden okundu.
- İkinci migration uygulandı; mevcut demo kaydın Id/CreatedAt değerleri korundu.
- Yeni alanlar verilmeden SQL insert yapıldığında numara ve varsayılanlar oluştu.
- USD/America/New_York ve EUR/UTC kabul edildi; GBP ve Europe/Imaginary reddedildi.
- Tekrarlanan veya biçimi geçersiz müşteri numarası reddedildi.
- NULL PreferredCurrency ve NULL TimeZone reddedildi.
- 12 eşzamanlı insert farklı müşteri numaraları üretti; test kayıtları geri alındı.

Bu adımda xUnit projesi eklenmedi; kontroller gerçek PostgreSQL üzerinde
transaction içinde yapıldı. Kalıcı otomatik test altyapısı ayrıca incelenecek.

## Review noktaları ve sınırlar

- required C# özelliği kullanıcı girdisinin geçerli olduğunu garanti etmez.
  DB kısıtları ilk korumadır; ileride giriş doğrulaması ayrıca yazılacak.
- btrim kontrolü normal boşluk içindir; tüm Unicode whitespace çeşitlerini
  kapsayan isim politikası değildir.
- Id ve CreatedAt varsayılanları C# tarafındadır. Manuel SQL insert bunları
  açıkça vermelidir. DateTimeOffset.UtcNow yerine non-UTC değer atayan bir
  kod yolunda Npgsql UTC gereksinimi ayrıca dikkate alınmalı.
- İsim unique değildir; farklı müşteriler aynı isme sahip olabilir.
- CustomerNumber primary key değildir; ilişkiler ileride Id üzerinden kurulacak.
  Sequence numaraları boşluksuz değildir: rollback ve testler de numara tüketir.
- PreferredCurrency yalnızca tercihtir; hesap para birimi veya kur dönüşümü değildir.
- TimeZone tercihi CreatedAt değerini yerel saate dönüştürerek saklamaz; UTC korunur.
  Gösterim ve tarih aralığı hesaplama mantığı henüz yazılmadı.
- Saat dilimi doğrulaması PostgreSQL'in tanıdığı adları kabul eder; yalnızca
  belirli IANA bölgeleriyle sınırlı bir ürün politikası tanımlanmış değildir.
- DB rolü migration için kendi database’inde şema oluşturma yetkisine sahip.
  İleride runtime ve migration rollerini ayırmak değerlendirilecek.
- Ek isim index’i, repository veya servis katmanı yok. Mevcut küçük okuma
  ilk 10 satırla sınırlı; büyük veri performans testi yapılmadı.

COMPLETED: Customer entity/configuration/migration, ayrı database, bir örnek kayıt ve kontroller.
IN PROGRESS: Customer alan değişikliğinin review’ü.
NEXT: Kullanıcının seçtiği bir sonraki database-foundation adımı; otomatik geçiş yok.
