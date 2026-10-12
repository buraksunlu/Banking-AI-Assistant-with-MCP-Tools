# AI-Powered Transaction Intelligence Platform

Bu proje tamamen synthetic banka verileriyle çalışan bir transaction intelligence
sistemi olacak. Nihai hedef .NET 10, PostgreSQL, SwiftUI ve local Qwen tabanlı
bir uygulamadır. Database foundation ve deterministik analytics tamamlandı; sırada HTTP API var.

## Mevcut durum

Bütün projeler `BankingAI.slnx` içindedir:

| Proje | Görevi |
| --- | --- |
| `src/BankingAI.Data` | Kütüphane: DbContext, entity, configuration, migration, seed, doğrulama |
| `src/BankingAI.Cli` | Komut satırı aracı: `migrate`, `show`, `seed ...`; dotnet-ef startup projesi |
| `src/BankingAI.Application` | Deterministik analytics servisleri (docs/analytics.md) |
| `tests/BankingAI.Data.Tests` | Doğrulama, seed, bakiye ve demo veri testleri |
| `tests/BankingAI.Application.Tests` | Analytics testleri |

ASP.NET Core API, SwiftUI, FastAPI, LangChain, Qwen ve MCP henüz oluşturulmadı.

Aktif PostgreSQL veritabanı `bankingai_foundation`dır. Eski `bankingai`
veritabanı temizlendi. Bu projede gerçek banka veya müşteri verisi kullanılmaz.

Mevcut domain tabloları:

- `Customers`
- `Accounts`
- `TransactionCategories`
- `Transactions`
- `MarketAssets`
- `DailyMarketPrices`

EF Core ayrıca migration geçmişi için `__EFMigrationsHistory` tablosunu yönetir.
Market tabloları oluşturuldu ve üç başlangıç MarketAsset seed kaydı eklendi;
historical market import henüz yok.

## Gereksinimler

- .NET 10 SDK
- Docker Desktop
- Çalışan yerel PostgreSQL container'ı

Bu makinede .NET SDK `/usr/local/share/dotnet` altında bulunuyor. Terminalde
`dotnet` komutu bulunmazsa:

```sh
export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="$DOTNET_ROOT:$PATH"
```

`.env.example` dosyasını `.env` olarak kopyalayıp yerel bağlantı bilgisini
ayarlayın. `.env` Git'e eklenmez ve paylaşılmamalıdır.

Uygulama `.env` dosyasını kendiliğinden okumaz; komutlardan önce değişkeni
terminal ortamına yüklemek gerekir:

```sh
set -a
source .env
set +a
```

## Console komutları

Komutlar proje kökünde çalıştırılır:

```sh
dotnet run --project src/BankingAI.Cli -- migrate
dotnet run --project src/BankingAI.Cli -- show
dotnet run --project src/BankingAI.Cli -- seed categories
dotnet run --project src/BankingAI.Cli -- seed market-assets
dotnet run --project src/BankingAI.Cli -- seed customers
dotnet run --project src/BankingAI.Cli -- seed accounts
dotnet run --project src/BankingAI.Cli -- seed transactions credits
dotnet run --project src/BankingAI.Cli -- seed transactions subscriptions
dotnet run --project src/BankingAI.Cli -- seed transactions category-examples
dotnet run --project src/BankingAI.Cli -- seed transactions grocery-growth
dotnet run --project src/BankingAI.Cli -- seed transactions categorization-boundaries
dotnet run --project src/BankingAI.Cli -- seed transactions recent-spending
dotnet run --project src/BankingAI.Cli -- seed transactions demo-coverage
dotnet run --project src/BankingAI.Cli -- seed transactions pairs
```

`migrate`, projede bulunan ve veritabanına henüz uygulanmamış EF Core
migration'larını uygular. Migration dosyası üretmez ve API başlangıcında
kendiliğinden çalışmaz.

`show`, ilk 10 Customer kaydını okur ve CustomerNumber, FirstName, LastName,
PreferredCurrency, TimeZone ve UTC CreatedAt değerlerini gösterir.

Kategori, market asset, müşteri, hesap ve seçili transaction seed komutları uygulanmıştır. Bütün synthetic
dataset'i tek seferde üreten `seed synthetic` ve market import henüz uygulanmadı; aşağıdaki komutlar tasarım sözleşmesidir ve
şu anda çalıştırılmamalıdır:

```sh
dotnet run --project src/BankingAI.Cli -- seed synthetic
dotnet run --project src/BankingAI.Cli -- import market-prices --asset USDTRY --from 2024-01-01 --to 2024-01-31
```

`seed categories` yalnızca bu komut açıkça çalıştırıldığında kategori ekler.
`seed market-assets` USDTRY, EURTRY ve GRAM_GOLD_TRY kayıtlarını açıkça ekler.
`seed customers` üç dummy müşteri ekler; mevcut demo müşteriyi korur.
`seed accounts`, bu müşteriler mevcutsa yedi sabit dummy hesap ekler (biri Deniz'in Business dükkân hesabı); yeni bakiyeler
sıfırdır. Aynı müşterinin TRY/USD hesapları, diğer müşteri hesapları ve boş hesap
örneği vardır. Tekrar çalıştırmak hesapları çoğaltmaz ve mevcut bakiyeyi sıfırlamaz.
Hesap listesi ve doğrulamalar [synthetic seed belgesinde](docs/synthetic-seed.md) açıklanır.
`seed transactions credits` altı aylık maaş ve iki döviz hesabının açılış
fonlamasını; `subscriptions` Netflix/Spotify ödemelerini ekler.
`category-examples` Starbucks, market, giyim, ulaşım, yakıt ve fatura örneklerini;
`grocery-growth` ise Ağustos 2025 için 24.000 TRY ve Eylül 2025 için 31.000 TRY
GROCERY toplamını ekler. Her kapsam sabit transaction ID kullanır; aynı kayıt
tekrar yazılmaz ve bakiye ikinci kez değiştirilmez. Credit işlemleri kategorisizdir;
açılış fonlamalarına karşı başka hesapta Debit oluşturulmaz. RawDescription ham
banka açıklamasını, NormalizedMerchant sadeleştirilmiş merchant adını taşır.
`categorization-boundaries`, henüz kategorize edilmemiş/merchant'ı bulunmayan
işlem ile `OTHER` kategorisine atanmış işlemi ayrı örnekler. Transaction seed
grupları DB advisory lock ile sıraya alınır; aynı scope'un paralel çalıştırılması
aynı kaydı ve bakiye delta'sını iki kez yazmaz.
`recent-spending`, Nisan–Eylül 2026 için Ada/Deniz maaşlarını ve
Starbucks/Netflix/Spotify/market harcamalarını ekler: 12 Credit ve 24 Debit.
2025 kayıtlarını korur. Tarihler sabittir; bu kapsam 10 Ekim 2026 demo tarihi
için son altı ay harcama verisi sağlar, ilerleyen tarihlerde kendiliğinden güncellenmez.
`demo-coverage` (önce `seed accounts` ile Ege TRY hesabı gerekir) üç müşteriye farklı
karakter verir: Ada'ya Ekim kahve harcamaları ve belirsiz/bilinmeyen bir işlem, Deniz'e
Ekim market/fatura, Ege'ye Nisan–Ekim maaş, ulaşım ve seyahat. 9 Credit, 19 Debit.
`pairs`, iki bacaklı yazıcıyla 5 "Kahve Durağı" alışverişini (Purchase→Sale) ve 4 transferi
(müşteriler arası ve Deniz'in kendi dükkânına) ekler: 18 kayıt. Açıklamalarda isim yoktur.
`migrate` seed çalıştırmaz. Diğer `seed` ve `import` komutları da uygulama
açılışında otomatik çalışmayacak; her biri kullanıcı komutuyla başlayacak.

Yeni migration oluşturmak için `dotnet-ef` aracı kullanılacak:

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add MigrationName --project src/BankingAI.Data --startup-project src/BankingAI.Cli
```

Migration oluşturmak ile migration'ı veritabanına uygulamak farklı işlemlerdir.
Üretilen migration dosyası incelenmeden aktif veritabanına uygulanmaz.

## Mevcut dosya düzeni

```text
BankingAI.slnx                 # Bütün projeler: dotnet build / dotnet test BankingAI.slnx
src/BankingAI.Data/            # Kütüphane
├── Entities/                  # Normal C# entity sınıfları
├── Enums/                     # TransactionDirection, TransactionKind, AccountType, CategorizationSource
├── Configurations/            # EF Core Fluent API eşlemeleri ve DB kuralları
├── Migrations/                # Uygulanmış şema değişikliklerinin geçmişi
├── Seeding/                   # Seed'ler ve atomik işlem yazıcıları
├── Validation/                # InputRules ve EntityValidation
└── BankingDbContext.cs        # Entity'leri ve EF configuration'ları bağlar
src/BankingAI.Cli/Program.cs   # migrate / show / seed komutları
src/BankingAI.Application/     # Analytics servisleri
tests/                         # xUnit test projeleri
```

Kök dosyaları:

- `RULE.md`: Projenin kalıcı çalışma ve teknik karar kuralları.
- `todo.md`: Küçük, sırayla uygulanacak iş listesi.
- `docs/`: Tasarım ve review açıklamaları.
- `.env.example`: Gerçek parola içermeyen bağlantı şablonu.
- `.gitignore`: `.env`, `bin`, `obj` ve macOS geçici dosyalarını dışarıda tutar.

## Veri modelinin mevcut kısmı

`Customer` synthetic müşteriyi temsil eder. `FirstName` ve `LastName` required
alanlardır. `CustomerNumber` PostgreSQL sequence ile üretilir. `PreferredCurrency`
görüntüleme tercihidir; `TimeZone` müşteri gösterim tercihidir. Kayıt tarihleri
UTC `DateTimeOffset`/`timestamptz` olarak tutulur.

`Account`, müşteriye ait hesabı temsil eder. `CustomerId` zorunlu foreign key,
`AccountNumber` unique dummy identifier, `CurrencyCode` TRY/USD/EUR/GBP ve
`Balance` numeric(18,2)'dir.

`TransactionCategory`, izin verilen kategori lookup tablosudur. Henüz kategori
seed'i eklenmedi.

`Transaction`, hesap hareketidir. `Amount` pozitiftir; yön `Debit` veya
`Credit` enum'uyla tutulur. Kategori henüz belirlenmemişse `CategoryId` null
kalabilir. İşlem para birimi hesap para birimiyle database ilişkisinde doğrulanır.

Bakiye güncelleyen transaction yazma akışı henüz oluşturulmadı; şu anda bir
Transaction satırı eklemek Account.Balance değerini otomatik değiştirmez.

`MarketAsset`, fiyatı izlenen varlığı tanımlar. `DailyMarketPrice`, bu varlığın
belirli takvim günündeki fiyatını tutar. Varlık/gün çifti unique'dir; fiyat
kaydı bulunan varlık RESTRICT nedeniyle silinemez. Tarih `DateOnly`/`date`,
fiyat `decimal`/`numeric(20,8)` olarak tutulur. Geçici EF/PostgreSQL kontrolleri
geçti; MarketAssets tablosunda üç seed kaydı, DailyMarketPrices tablosunda henüz
fiyat kaydı yoktur.

## Migration geçmişi

Aktif veritabanında sırasıyla şu migration'lar uygulanmıştır:

1. `CreateCustomers`
2. `AddCustomerPreferences`
3. `AddAccountsAndTransactionCategories`
4. `AddTransactions`
5. `SplitCustomerName`
6. `AddMarketAssets`
7. `AddDailyMarketPrices`
8. `EnforceInputValidation`
9. `AddTransactionKind`
10. `AddAccountType`

Eski migration dosyaları tarihçedir; son modelde kullanılmayan eski sütunları
göstermeleri normaldir. Uygulanmış migration dosyaları silinmez veya geriye
dönük değiştirilmez.

## Sonraki adım

Banking foundation 10 Ekim 2026'da kabul edildi. Market data ayrı ve
engellemeyen bir kol olarak yürür; yalnızca opportunity-cost özelliği onu bekler.

1. Deterministik analytics: ortak dönem sözleşmesi ve demo saati (ANA-01), ardından harcama hesapları.
2. Controller-based API.
3. Local Qwen + FastAPI + LangChain, ardından read-only MCP tool'ları.
4. Paralel: USDTRY/EURTRY/gram altın kaynağı ve historical import.

Her adımda önce açıklama yapılır, sonra yalnızca seçilen küçük kapsam uygulanır
ve kullanıcı review'ü için durulur. Ayrıntılı sıra [todo.md](todo.md) içinde,
kalıcı kurallar [RULE.md](RULE.md) içindedir.
