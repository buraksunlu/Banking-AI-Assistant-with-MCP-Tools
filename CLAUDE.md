# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Proje ve kaynak belgeler

Synthetic banka verisi üzerinde çalışan, Türkçe sorulara cevap veren bir AI harcama asistanı
(VakıfBank backend stajı). Temel ilke: **LLM yorumlar ve tool seçer; .NET backend hesaplar,
filtreler, yetkilendirir ve finansal source of truth'tur.** LLM'e SQL, DB erişimi, para hesabı
veya yazma yetkisi verilmez; MCP tool'ları read-only'dir.

- `RULE.md`: kalıcı kurallar ve stack kararları. §26 (foundation kolları), §28 (DB/hesap
  kararları) ve §29 (güncel durum) en sık güncellenen bölümlerdir.
- `todo.md`: projenin **tek** iş planı. Maddeler ID'lidir (SYN-, KIND-, ANA-, API-, MCP-…);
  "Şu anki sonraki adım" bölümü nereden devam edileceğini söyler.
- `docs/input-validation.md` (VAL-xx kuralları, harcama tanımı), `docs/analytics.md`
  (analytics sözleşmeleri), `docs/synthetic-seed.md` (demo veri ve beklenen rakamlar),
  `docs/database-design.md`, `docs/market-data-sources.md`.
- En son açık kullanıcı kararı eski belgelerden önceliklidir.

Stack sınırları (RULE.md): .NET 10 + EF Core + Npgsql + PostgreSQL; ileride ASP.NET Core
controller API, Python FastAPI + LangChain + Ollama (local Qwen), .NET MCP server, SwiftUI.
Ücretli LLM/API (OpenAI, Anthropic API vb.), SQLite, Flutter/React Native önerilmez.

## Çalışma şekli (kullanıcının yerleşik tercihleri)

- İletişim Türkçe. Kullanıcı kodu inceleyerek öğreniyor.
- Her adım: önce **detaylı plan** (ne, neden, hangi dosyalar, etkisi, kullanıcının vereceği
  kararlar, elle hesaplanmış beklenen test değerleri) → kullanıcı "okay" der → uygula → dur.
  Bir sonraki todo maddesine kendiliğinden geçme.
- Migration: üretilen SQL'i (`migrations script`) göster, **DB'ye uygulamadan önce ikinci
  onay al**. Demo DB'ye kalıcı seed yazımı da onaylı plana bağlıdır.
- TDD: önce test, kırmızının nedenini doğrula (NotImplemented/veri yok), sonra kod. Refactor'da
  mevcut testler değişmeden yeşil kalmalı. Analytics'te çalışan SQL'i geçici bir test ile
  yakalayıp sorgu sayısını doğrulama alışkanlığı var; geçici dosya sonra silinir.
- Adım sonunda: yapısal değişiklik (önce/sonra ağaç) + dosya dosya kod açıklaması (snippet'li)
  + doğrulama + bilinen sınırlar. `todo.md` maddesini sonucu ve test sayısıyla `[x]` yap,
  `RULE.md` §29'u ve ilgili `docs/` dosyasını güncelle.
- **Kod yorumları (kullanıcı kararı):** Kodun içinde yalnız İngilizce. Her public tip/metotta
  `/// <summary>`, gövdede sadece "neden" yorumları; satır satır "ne yapıyor" yorumu **yazılmaz**.
- **Review açıklaması sohbette, sözdizimine kadar:** Kullanıcı C#'a hakim değil ve kod okumayı
  öğreniyor. Değişen kod sohbette satır satır Türkçe açıklanır (Türkçe yorumlu kod blokları,
  DB'ye giden satırlar 🗄️ ile işaretli); `;`, `{ }`, `=>`, `<T>`, `?`, `!`, `??`, `new(...)`,
  `var`, `static`, `readonly` gibi her sembol ve anahtar kelime anlatılır. Jargon ilk geçtiği yerde
  tek cümleyle tanımlanır; kısa cümleler, Python karşılığı. Ayrı walkthrough dosyası oluşturulmaz.
- **Commit'i kullanıcı atar.** Adım sonunda yalnız `git add <dosyalar> && git commit -m "<kısa,
  küçük harfli İngilizce mesaj>"` öner; kullanıcı açıkça istemedikçe commit atma.
- Uygulanmış migration'ları değiştirme/silme, DB'yi resetleme, demo kayıtlarını silme.

## Komutlar

Her yeni shell'de (dotnet PATH'te değil; uygulama `.env`'yi kendisi okumaz):

```sh
export DOTNET_ROOT=/usr/local/share/dotnet PATH="/usr/local/share/dotnet:$PATH"
set -a; source .env; set +a   # BANKINGAI_CONNECTION_STRING
```

PostgreSQL Docker container'ı `bankingai-week1-postgres-1` (127.0.0.1:55432, DB/rol
`bankingai_foundation`). Solution dosyası yok; test projeleri ayrı çalıştırılır:

```sh
dotnet test tests/BankingAI.Data.Tests
dotnet test tests/BankingAI.Application.Tests
dotnet test tests/BankingAI.Data.Tests --filter "Category!=PostgreSQL"          # DB'siz testler
dotnet test tests/BankingAI.Application.Tests --filter "FullyQualifiedName~MerchantMatcherTests"
dotnet build tests/BankingAI.Data.Tests --no-incremental                        # uyarı sayısı kontrolü
```

Console CLI (`src/BankingAI.Data`), seed sırası önemlidir:

```sh
dotnet run --project src/BankingAI.Data -- migrate
dotnet run --project src/BankingAI.Data -- seed categories        # sonra: market-assets, customers, accounts
dotnet run --project src/BankingAI.Data -- seed transactions credits
# diğer transaction scope'ları: subscriptions, category-examples, grocery-growth,
# categorization-boundaries, recent-spending, demo-coverage, pairs
```

Migration:

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project src/BankingAI.Data
dotnet tool run dotnet-ef migrations script <PreviousMigration> <Name> --project src/BankingAI.Data
docker exec bankingai-week1-postgres-1 psql -U bankingai_foundation -d bankingai_foundation -c "<SQL>"
```

## Mimari

```text
BankingAI.Application.Tests ─► BankingAI.Application ─► BankingAI.Data ─► PostgreSQL
BankingAI.Data.Tests ─────────────────────────────────► BankingAI.Data
```

`BankingAI.Data` hem DbContext/entity/migration/seed kodunu hem de `Program.cs` CLI'ını taşıyan
bir **Exe**. Kütüphane + ayrı CLI olarak bölünmesi bilinçli olarak API-01'e bırakıldı.
`BankingDbContext` bağlantıyı `OnConfiguring` içinde ortam değişkeninden alır (DI yok).

### Doğrulama katmanları

Her kural genellikle hem C#'ta hem DB'de vardır: `Validation/InputRules` (saf, düzeltme yapmaz,
`ArgumentException`) → `Validation/EntityValidation` (entity bazlı) → `BankingDbContext.SaveChanges`
override'ı (doğrulamayı çalıştırır, `CreatedAt`/`CategorizedAt`'i sunucu zamanıyla atar) →
configuration'lardaki DB CHECK/unique/FK kısıtları. Yeni kural eklerken dört katmanı birlikte düşün.

### Domain kuralları (birden çok dosyaya yayılmış)

- `Direction` paranın yönü, `Kind` ekonomik türüdür (Purchase, Sale, Transfer, Salary,
  OpeningFunding). **Harcama = `Kind == Purchase`**; bütün Debit'leri toplamak transferleri
  yanlışlıkla harcama sayar (Ada'nın Ekim 2026'sı: 735 değil 2.235 olur).
- Transfer ve iç dükkân alışverişi aynı `PairId`'yi taşıyan iki bacaktır; `UNIQUE (PairId,
  Direction)`. `Sale` yalnız `AccountType.Business` hesaba yazılır. Karşı taraf adı DB'ye
  kopyalanmaz; okumada PairId ile bulunur ve maskelenir ("Deniz M***").
- Kategori yalnız Purchase'ta bulunur. `CategoryId == null` "Sınıflandırılmamış"tır ve `OTHER`
  ("Diğer") ile birleştirilmez. Kategori alanları (Id/Source/Confidence/CategorizedAt) birlikte değişir.
- Tutar `numeric(18,2)`, sessiz yuvarlama yok; negatif bakiye yok. Zaman UTC `timestamptz`;
  market günü `DateOnly`. Para birimleri asla toplanmaz/dönüştürülmez.

### İşlem yazma

`Transaction` doğrudan `db.Add` ile eklenmez:

- `Seeding/TransactionSeedWriter`: tek bacak; insert + `ExecuteUpdate(Balance + delta)` aynı DB
  transaction'ında. `PairId`'li işlemi reddeder.
- `Seeding/PairedTransactionSeedWriter`: iki bacak + iki bakiye tek transaction'da; bakiyeler
  **her zaman küçük AccountId önce** güncellenir (aksi halde ters yönlü transferler 40P01 deadlock alır).
  Aynı PairId tekrarında şimdilik hata verir (API-12'de düzeltilecek).
- Bakiye mantığı `TransactionSeedWriter.ApplyBalanceDeltaAsync` içinde ortaktır.

### Seed'ler

Sabit GUID'ler: müşteri `10000000-…-000000000001..3` (Ada Kahve, Deniz Market, Ege Yolcu), hesap
`20000000-…-00000000000N` (7 = Deniz'in Business "Kahve Durağı" hesabı), işlem
`30000000-…-{ordinal:D12}`, çift `40000000-…`. Kullanılan ordinal aralıkları: 1–14, 101–106,
201–206, 301–306, 401–402, 501–536, 601–628, 701–718. Seed'ler idempotenttir; aynı ID farklı
içerikle bulunursa sessizce düzeltmez, hata verir. Transaction seed'leri advisory lock kullanır
ve liste **sırasıyla** yazılır (boş hesaba Credit'ler Debit'lerden önce gelmeli).

### Analytics (`BankingAI.Application/Analytics`)

- `PeriodResolver` + `TimeProvider`: sembolik dönem (`PeriodKind`) müşterinin `TimeZone`'una göre
  `[StartUtc, EndUtc)` aralığına çevrilir. Testler ve demo "şimdi"yi
  `2026-10-10T12:00:00Z` olarak sabitler (`FixedTimeProvider`); demo verisi bu tarihe göredir.
- `CustomerScope` (internal) müşteri/saat dilimi, hesap sahipliği (başka müşterinin hesabı =
  `KeyNotFoundException`, varlık sızdırılmaz) ve dönem çözümünün tek yeridir. `PurchaseScope` onun
  üstüne yalnız Personal hesap + `Kind == Purchase` ekler. Çalıştırılmamış `IQueryable` dönerler;
  her servis tek bir `GROUP BY`/sayfa sorgusu ekler. Yetki kuralını servislerde kopyalama.
- Servisler: `SpendingSummaryService`, `CategoryBreakdownService`, `MerchantSpendingService`,
  `TransactionSearchService` (ekstre: tüm türler ve tüm hesaplar), `HighestTransactionsService`
  (varsayılan harcama; istenirse tüm işlemler; limit ≤ 100), `SpendingComparisonService`
  ("geçen aya göre" = son tam ay vs önceki ay; tek CASE WHEN sorgusu; aylık seri takvim ayı +
  devam eden ay, boş ay 0), `RecurringPaymentService` + saf `RecurringPaymentDetector` (son 6 ay;
  ≥3 ödeme, 26–35 gün aralık, ±%10 tutar, son ödeme ≤35 gün; tablo yok). İşlem listesi döndüren servisler
  `TransactionItemReader` (internal) ile aynı DTO'yu üretir: join'ler, maskeli karşı taraf
  (PairId), `LocalDate`, join'lerden sonra uygulanan sıralama. Join/maskeleme kodunu kopyalama.
  Sonuçlar para birimi başına satırdır; harcama yoksa boş liste döner (uydurma "0" satırı yok);
  yüzde backend'de 2 ondalık `AwayFromZero` ile hesaplanır ve 100'e zorlanmaz.
- `MerchantMatcher`: modelden gelen merchant metni güvenilmezdir. Kültürden bağımsız Türkçe
  sadeleştirme (sabit tablo; tr-TR'de `"I".ToLower()` = `"ı"`), tam → tek adaylı kısmi (≥3 harf) →
  Ambiguous → NotFound; yazım hatası düzeltilmez; adaylar yalnız müşterinin kendi merchant'ları.
  SQL'e ham metin değil eşleşen kanonik ad parametre olarak gider.
- EF tuzakları: (1) constructor'la kurulan record'a projeksiyondan **sonra** `OrderBy` çevrilemez;
  `GroupBy(...).OrderBy(g => g.Key).Select(...)` kullan. (2) Nullable kolonda korelasyonlu join'de
  EF C# null semantiğini korur (`null == null` true) ve dış satırdaki null kontrolünü düşürebilir;
  kontrolü join'lenen satıra koy (`o.PairId != null && o.PairId == t.PairId`). (3) Npgsql 10
  `DateTimeOffset` saat dilimi dönüşümünü (`ConvertTimeBySystemTimeZoneId`) SQL'e çeviremez; yerel ay
  gruplaması için UTC pencere sınırlarını kullan.

### Testler

- `BankingAI.Data.Tests`: PostgreSQL testleri `[Collection(PostgreSqlCollection.Name)]` ile sıralı
  çalışır; ya kendi transaction'ında rollback yapar ya da geçici `DUMMY-*TEST-*` hesap açıp siler.
- `BankingAI.Application.Tests`: seed edilmiş demo DB'yi okur. Demo müşteri/hesap GUID'leri ve
  `DemoNowUtc` tek yerde, `DemoData.cs` içindedir (`using static ...DemoData;`); test dosyasına kopyalama.
- Beklenen değerler **elle hesaplanmış fixture**'lardır; seeder'dan türetilmez
  (`SyntheticDatasetIntegrityTests`, `docs/synthetic-seed.md`). Seed eklemek bakiye/sayı fixture'larını değiştirir.
- Yeni enum kolonunda EF migration'ı `NOT NULL DEFAULT 0` üretir: elle nullable ekle → backfill →
  belirsiz kayıtta `RAISE EXCEPTION` → `NOT NULL` yap. DB varsayılanı olmadığı için testlerdeki ham
  `INSERT`'ler `Kind` ve `AccountType`'ı açıkça vermelidir.

## Durum

Banking foundation kabul edildi; market data (USDTRY/EURTRY/gram altın import, opportunity-cost)
ayrı ve engellemeyen bir koldur, altın kaynağı (SRC-02) dış yanıt bekliyor. Tamamlananlar:
ANA-01 (dönem), SYN-14 (müşteri bazlı demo veri), KIND-01–05 (Kind/PairId/AccountType, çift
yazıcı, dükkân ve transfer seed'i), ANA-02–06 (özet, kategori, merchant, arama, en yüksek
işlemler), ANA-07 (karşılaştırma ve aylık seri), REC-01–03 (düzenli ödemeler). Sıradaki: analytics
kapanışı (ANA-08), ardından API-01 (Data'nın kütüphane/CLI ayrımı + ilk controller), AI, MCP.
Güncel ayrıntı her zaman `todo.md`'dedir.
