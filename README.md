# AI-Powered Transaction Intelligence Platform

Bu proje tamamen synthetic banka verileriyle çalışan bir transaction intelligence
sistemi olacak. Nihai hedef .NET 10, PostgreSQL, SwiftUI ve local Qwen tabanlı
bir uygulamadır. Şu an yalnızca database foundation üzerinde çalışıyoruz.

## Mevcut durum

Şu anda çalışan proje `src/BankingAI.Data` adlı küçük bir .NET 10 console
projesidir. ASP.NET Core API, controller, service, SwiftUI, FastAPI, LangChain,
Qwen ve MCP henüz oluşturulmadı.

Aktif PostgreSQL veritabanı `bankingai_foundation`dır. Eski `bankingai`
veritabanı temizlendi. Bu projede gerçek banka veya müşteri verisi kullanılmaz.

Mevcut domain tabloları:

- `Customers`
- `Accounts`
- `TransactionCategories`
- `Transactions`

EF Core ayrıca migration geçmişi için `__EFMigrationsHistory` tablosunu yönetir.
Market tabloları, seed verisi ve historical market import henüz yapılmadı.

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
dotnet run --project src/BankingAI.Data -- migrate
dotnet run --project src/BankingAI.Data -- show
```

`migrate`, projede bulunan ve veritabanına henüz uygulanmamış EF Core
migration'larını uygular. Migration dosyası üretmez ve API başlangıcında
kendiliğinden çalışmaz.

`show`, ilk 10 Customer kaydını okur ve CustomerNumber, FirstName, LastName,
PreferredCurrency, TimeZone ve UTC CreatedAt değerlerini gösterir.

Yeni migration oluşturmak için `dotnet-ef` aracı kullanılacak:

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add MigrationName --project src/BankingAI.Data
```

Migration oluşturmak ile migration'ı veritabanına uygulamak farklı işlemlerdir.
Üretilen migration dosyası incelenmeden aktif veritabanına uygulanmaz.

## Mevcut dosya düzeni

```text
src/BankingAI.Data/
├── Entities/              # Normal C# entity sınıfları
├── Enums/                 # TransactionDirection ve CategorizationSource
├── Configurations/        # EF Core Fluent API eşlemeleri ve DB kuralları
├── Migrations/            # Uygulanmış şema değişikliklerinin geçmişi
├── BankingDbContext.cs    # Entity'leri ve EF configuration'ları bağlar
├── Program.cs             # migrate/show console giriş noktası
└── BankingAI.Data.csproj  # .NET ve NuGet paket ayarları
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

## Migration geçmişi

Aktif veritabanında sırasıyla şu migration'lar uygulanmıştır:

1. `CreateCustomers`
2. `AddCustomerPreferences`
3. `AddAccountsAndTransactionCategories`
4. `AddTransactions`
5. `SplitCustomerName`

Eski migration dosyaları tarihçedir; son modelde kullanılmayan eski sütunları
göstermeleri normaldir. Uygulanmış migration dosyaları silinmez veya geriye
dönük değiştirilmez.

## Sonraki adım

Database-first sırası devam edecek:

1. `MarketAsset` entity ve configuration.
2. `DailyMarketPrice` entity ve configuration.
3. Kategori ve market asset seed'i.
4. Kontrollü synthetic Customer, Account ve Transaction seed'i.
5. Üç varlık için en az iki yıllık gerçek historical market data import'u.
6. Database foundation testleri.
7. Foundation kabulünden sonra controller-based API.

Her adımda önce açıklama yapılır, sonra yalnızca seçilen küçük kapsam uygulanır
ve kullanıcı review'ü için durulur. Ayrıntılı sıra [todo.md](todo.md) içinde,
kalıcı kurallar [RULE.md](RULE.md) içindedir.
