# AI-Powered Transaction Intelligence Platform

Tamamen synthetic banka verisiyle çalışan, Türkçe sorulara cevap veren bir AI harcama asistanı
("Bu ay ne kadar harcadım?", "Son üç ayda Starbucks'a ne kadar verdim?", "Düzenli ödemelerim neler?").

Temel ilke: **LLM yorumlar ve tool seçer; .NET backend hesaplar, filtreler, yetkilendirir ve finansal
source of truth'tur.** LLM'e SQL, veritabanı erişimi, para hesabı veya yazma yetkisi verilmez; MCP tool'ları
read-only olacaktır. Her şey ücretsiz ve yereldir (local Qwen + Ollama). Gerçek banka/müşteri verisi kullanılmaz.

## Durum (19 Ekim 2026)

| Katman | Durum |
| --- | --- |
| Veritabanı + synthetic demo veri | Tamam: 6 domain tablosu + giriş tablosu, 12 migration, 3 demo müşteri, 7 hesap, 116 işlem |
| Deterministik analytics | Tamam: özet, kategori, merchant, arama, en yüksek işlemler, dönem karşılaştırması, aylık seri, düzenli ödemeler |
| HTTP API | Tamam: giriş/JWT, hesaplar, işlemler, analytics endpoint'leri, ortak hata cevabı, OpenAPI + Scalar |
| Local AI (FastAPI + LangChain + Qwen) | Sohbet uçtan uca çalışıyor: iPhone → .NET `POST /api/assistant/messages` → AI servisi (model araç seçer, kod rakamı yazar, model açıklar, cevap kontrol edilir) ([docs/local-ai.md](docs/local-ai.md)) |
| MCP | Çalışıyor: salt okunur `BankingAI.McpServer`, 3 araç; AI servisi araçları oradan keşfedip çalıştırıyor ([docs/mcp.md](docs/mcp.md)) |
| SwiftUI | Başladı: giriş ekranı ve oturum (Keychain) hazır, simülatörde gerçek API'yle çalışıyor ([docs/ios.md](docs/ios.md)); sohbet ekranı sırada |
| Market verisi (USDTRY/EURTRY/gram altın) | Ayrı kol; altın kaynağı dış yanıt bekliyor, import yok |

Ayrıntılı iş planı ve sıradaki adım [todo.md](todo.md), kalıcı kurallar [RULE.md](RULE.md) içindedir.

## Projeler

Bütün projeler `BankingAI.slnx` içindedir:

| Proje | Görevi |
| --- | --- |
| `src/BankingAI.Data` | Kütüphane: DbContext, entity, configuration, migration, seed, doğrulama, giriş bilgisi deposu |
| `src/BankingAI.Cli` | Komut satırı: `migrate`, `show`, `seed …`, `users set-password`; dotnet-ef startup projesi |
| `src/BankingAI.Application` | Hesap okuma/yazma servisleri ve deterministik analytics ([docs/analytics.md](docs/analytics.md)) |
| `src/BankingAI.Hosting` | API ile MCP server'ın ortak kurulumu: JWT doğrulama, iş saati, JSON kuralları, yalnız okuma servisleri |
| `src/BankingAI.Api` | ASP.NET Core controller API ([docs/api.md](docs/api.md), [docs/security.md](docs/security.md)) |
| `src/BankingAI.McpServer` | Modelin araçları: salt okunur MCP server, yalnız `127.0.0.1:5249` ([docs/mcp.md](docs/mcp.md)) |
| `tests/BankingAI.Data.Tests` | Doğrulama, seed, atomik bakiye, giriş bilgisi ve demo veri bütünlüğü testleri |
| `tests/BankingAI.Application.Tests` | Analytics ve hesap servisi testleri |
| `tests/BankingAI.Api.Tests` | HTTP integration testleri (`WebApplicationFactory`) |
| `tests/BankingAI.McpServer.Tests` | MCP server testleri: araç keşfi, rakamlar, token, argümanlar (gerçek C# MCP istemcisiyle) |
| `ios/BankingAI` | SwiftUI iPhone uygulaması (Xcode projesi + Swift Testing testleri; [docs/ios.md](docs/ios.md)) |

```text
BankingAI.Api ───────┐
BankingAI.McpServer ─┴► BankingAI.Hosting ─► BankingAI.Application ─► BankingAI.Data ─► PostgreSQL
BankingAI.Cli ───────────────────────────────────────────────────────► BankingAI.Data
```

## Kurulum

### Gereksinimler

- .NET 10 SDK. Bu makinede `/usr/local/share/dotnet` altında; `dotnet` bulunamazsa:

  ```sh
  export DOTNET_ROOT=/usr/local/share/dotnet
  export PATH="$DOTNET_ROOT:$PATH"
  ```

- Docker ve yerel PostgreSQL container'ı. Bu makinede Docker, Colima ile çalışır. Container
  `bankingai-week1-postgres-1` (127.0.0.1:55432, veritabanı ve rol `bankingai_foundation`). Mac yeniden
  başladıktan sonra ikisi de elle başlatılır (`colima start`, ardından `docker start bankingai-week1-postgres-1`).
- Depo iCloud gibi eşitlenen bir klasöre konmaz (iCloud `.git` nesnelerini buluta alıp git'i kilitliyordu).

### Ortam değişkenleri

`.env.example` dosyasını `.env` olarak kopyalayıp değerleri doldurun. `.env` git'e girmez ve paylaşılmaz.

| Değişken | Anlamı |
| --- | --- |
| `BANKINGAI_CONNECTION_STRING` | PostgreSQL bağlantısı |
| `BANKINGAI_JWT_SIGNING_KEY` | Token imza anahtarı; en az 32 bayt rastgele değer (`openssl rand -base64 48`). Yoksa API açılmaz |
| `BANKINGAI_DEMO_PASSWORD_ADA` / `_DENIZ` / `_EGE` | Demo giriş şifreleri; kendi rastgele değerlerinizi üretin |
| `BANKINGAI_DEMO_NOW` | İsteğe bağlı: iş saatini demo anına sabitler (ör. `2026-10-10T12:00:00Z`); token'lar gerçek saatte kalır |
| `BANKINGAI_LLM_MODEL` | Local model adı (Ollama), varsayılan önerisi `qwen3.5:4b` |
| `BANKINGAI_OLLAMA_URL` | İsteğe bağlı Ollama adresi (boşsa `http://127.0.0.1:11434`) |
| `BANKINGAI_LLM_TIMEOUT_SECONDS` | İsteğe bağlı: model cevabı için süre sınırı, saniye (varsayılan 30) |

Uygulama `.env`'yi kendiliğinden okumaz; her yeni terminalde:

```sh
set -a; source .env; set +a
```

### Veritabanı ve demo veri

Komutlar proje kökünde, sırasıyla çalıştırılır. Her seed idempotenttir: tekrar çalıştırmak kayıt
çoğaltmaz, bakiyeyi ikinci kez değiştirmez; aynı kimlik farklı içerikle bulunursa sessizce düzeltmez, hata verir.

```sh
dotnet run --project src/BankingAI.Cli -- migrate
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

- `migrate` yalnız şemayı uygular; migration dosyası üretmez, seed çalıştırmaz. API açılışında migration
  **asla** çalışmaz.
- `credits` diğer işlem kapsamlarından önce gelir: boş hesaba önce para girmelidir.
- Demo karakterleri: Ada (kahve, abonelik, bilinmeyen bir işlem), Deniz (market artışı, faturalar ve
  "Kahve Durağı – Kadıköy" Business dükkân hesabı), Ege (ulaşım ve seyahat). Ayrıntılar ve elle hesaplanmış
  beklenen rakamlar [docs/synthetic-seed.md](docs/synthetic-seed.md) içindedir.
- `show` ilk 10 müşteriyi gösterir. Tek komutluk `seed synthetic` ve market fiyatı `import` komutu henüz yoktur.

Demo kullanıcıların şifresini belirlemek için (şifre gizli ve iki kez sorulur, ekrana veya loga yazılmaz).
Kullanıcı ilk kez oluşturulurken müşteri kimliği verilir: Ada `10000000-0000-0000-0000-000000000001`,
Deniz `…0002`, Ege `…0003`.

```sh
dotnet run --project src/BankingAI.Cli -- users set-password ada --customer 10000000-0000-0000-0000-000000000001
```

## API

```sh
BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z dotnet run --project src/BankingAI.Api --launch-profile http
```

Adres `http://localhost:5248`. `BANKINGAI_DEMO_NOW` isteğe bağlıdır; verilirse "bu ay" gibi dönemler demo anına göre
hesaplanır ve rakamlar belgelerdekiyle aynı çıkar. Health, login ve geliştirme belgeleri dışındaki **her endpoint token
ister**; müşteri kimliği istekte parametre olarak alınmaz, token'dan gelir.

- **Tarayıcıda gez ve dene:** `http://localhost:5248/scalar` (yalnız geliştirmede)
- **Sözleşme (OpenAPI 3.1):** `http://localhost:5248/openapi/v1.json`; commit'lenmiş kopyası [docs/openapi/v1.json](docs/openapi/v1.json)
- **Çalıştırılabilir örnekler:** [api-examples.http](api-examples.http) (VS Code "REST Client" eklentisi; şifre `.env`'den okunur)

Hızlı başlangıç (`curl`):

```sh
set -a; source .env; set +a
TOKEN=$(curl -s -X POST localhost:5248/api/auth/login -H 'Content-Type: application/json' \
  -d "{\"username\":\"ada\",\"password\":\"$BANKINGAI_DEMO_PASSWORD_ADA\"}" | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')
curl -s "localhost:5248/api/analytics/spending-summary?period=ThisMonth" -H "Authorization: Bearer $TOKEN"
```

| Endpoint | Ne döner | Madde |
| --- | --- | --- |
| `GET /api/health` | `{ status, database }`; DB yoksa 503 (açık) | API-01b |
| `POST /api/auth/login` | 60 dakikalık JWT; IP başına dakikada 5 deneme (açık) | SEC-01b |
| `GET /api/me` | Giriş yapan müşteri (iç kimlik olmadan) | SEC-01b |
| `GET /api/accounts`, `GET /api/accounts/{id}` | Müşterinin hesapları; başkasının hesabı = olmayan hesap (404) | API-05, API-06 |
| `POST /api/accounts` | Yeni şahsi hesap (bakiye 0, numara `ACC-n`); 201 | API-07 |
| `GET /api/accounts/{accountId}/transactions` | Hesabın işlemleri, sayfalı; varsayılan son 3 ay | API-09 |
| `GET /api/transactions` | Bütün hesaplarda arama: tür, yön, kategori, merchant, tutar aralığı | ANA-09, ANA-11 |
| `GET /api/transactions/{id}` | Tek işlem | API-10 |
| `POST /api/accounts/{accountId}/transactions` | Sentetik Purchase/Salary; atomik bakiye; yetersiz bakiye 409; `Idempotency-Key` zorunlu | API-11, API-12 |
| `GET /api/analytics/spending-summary`, `category-breakdown`, `merchant-breakdown`, `merchant-spending` | Harcama özeti, kategori ve merchant dağılımı, tek merchant | ANA-09 |
| `GET /api/analytics/period-comparison`, `monthly-trend` | "Geçen aya göre" karşılaştırma, aylık seri | ANA-09 |
| `GET /api/analytics/highest-transactions`, `recurring-payments`, `upcoming-payments` | En büyük işlemler, düzenli ve yaklaşan ödemeler | ANA-09, ANA-12 |
| `GET /api/analytics/budget-check`, `savings-simulation` | Bütçe durumu ve ay sonu tahmini; "%N azaltsam" | ANA-13, ANA-14 |
| `GET /api/analytics/merchants`, `merchant-group-spending` | Merchant listesi (kapalı küme) ve "kahveye ne kadar?" | ANA-10 |
| `POST /api/assistant/messages`, `GET /api/assistant/status` | Sohbet asistanı: serbest Türkçe soru → cevap + kullanılan sorgu + rakamlar; asistan hazır mı (AI servisi ve Ollama açık olmalı; müşteri başına dakikada 10 mesaj) | AI-14b, AI-07 |

JSON kuralları: enum'lar yazıyla ve tam adla, para JSON sayısı (`577490.18`; tırnaklı tutar 400), yazma isteğinde
tanınmayan alan 400. Her hata ProblemDetails biçimindedir: Türkçe `title`, makine kodu `code` (ör. `validation_failed`,
`session_invalid`, `balance_limit`) ve `traceId`. Ayrıntılar [docs/api.md](docs/api.md) içindedir.

## Testler

```sh
dotnet build BankingAI.slnx
dotnet test BankingAI.slnx                                              # bütün testler (paralel projeler)
dotnet test tests/BankingAI.Data.Tests --filter "Category!=PostgreSQL"  # DB'siz testler
```

PostgreSQL testleri seed edilmiş demo veritabanını kullanır ve `.env` yüklenmiş olmalıdır. Testler demo
verisini değiştirmez: ya kendi DB transaction'ında rollback yapar ya da geçici kayıt açıp siler. Beklenen
değerler seeder'dan türetilmez, elle hesaplanmış fixture'lardır. Güncel test sayıları `todo.md` içindedir.

AI servisi ve model ölçümü (Python 3.14; ayrıntı [docs/local-ai.md](docs/local-ai.md)):

```sh
/opt/homebrew/bin/python3.14 -m venv ai/.venv                         # bir kez
ai/.venv/bin/python -m pip install -r ai/requirements.txt             # sabit sürümler
ai/.venv/bin/python -m pytest ai                                      # bütün Python testleri (Ollama kapalıysa gerçek-Ollama testleri atlanır)
python3 ai/model_check/run.py --model qwen3.5:4b --model qwen3.5:9b   # modelleri karşılaştır (Ollama açık olmalı)
ai/.venv/bin/python ai/model_check/explanation_eval.py              # açıklama prompt'unu 10 vakada değerlendir
python3 ai/service/ollama_client.py                                  # elle kontrol: Ollama ve model hazır mı?
ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000   # AI servisi
```

AI servisi: `GET /health/live`, `GET /health/ready`, `POST /v1/explanations` (soru + backend'in biçimlendirdiği `facts`),
`POST /v1/chat` (serbest soru; `Authorization: Bearer` ile, tool'ları .NET API'de çalıştırır; .NET API'nin açık olması gerekir);
etkileşimli belge `http://127.0.0.1:8000/docs`. Uygulama AI servisini doğrudan değil, .NET'in `/api/assistant/...` uç noktaları üzerinden kullanır
(adres `BANKINGAI_AI_URL`, yoksa `http://127.0.0.1:8000/`). Hatalar API'ninkiyle aynı biçimdedir (`title`, `status`, `code`, `traceId`).

## Veri modeli

```text
Customer 1 ── N Account 1 ── N Transaction N ── 0..1 TransactionCategory
Customer 1 ── 0..1 CustomerCredential
MarketAsset 1 ── N DailyMarketPrice
```

- **Customer:** synthetic müşteri; `CustomerNumber` PostgreSQL sayacından (`CUST-n`), `TimeZone` "bu ay"ın
  nerede başladığını belirler.
- **Account:** müşteri hesabı; `AccountType` Personal veya Business (demo dükkânı), para birimi
  TRY/USD/EUR/GBP, bakiye `numeric(18,2)`; negatif bakiye yok.
- **Transaction:** `Direction` paranın yönü (Debit/Credit), `Kind` ekonomik türü (Purchase, Sale, Transfer,
  Salary, OpeningFunding). **Harcama = `Kind == Purchase`**; transferler harcama sayılmaz. Transfer ve iç
  dükkân alışverişi aynı `PairId`'yi taşıyan iki kayıttır. Kategori yalnız Purchase'ta bulunur;
  kategorisiz işlem "Sınıflandırılmamış"tır ve `OTHER` ("Diğer") ile birleştirilmez.
- **CustomerCredential:** giriş bilgisi; şifre saklanmaz, yalnız `PasswordHasher` özeti tutulur.
- **MarketAsset / DailyMarketPrice:** USDTRY, EURTRY, GRAM_GOLD_TRY tanımlı; fiyat kaydı henüz yok.

İşlem satırı doğrudan `db.Add` ile eklenmez. `Writing/TransactionWriter` (tek kayıt; seed'ler ve API) ve
`Writing/PairedTransactionWriter` (iki kayıt) işlemi ve bakiye değişimini aynı DB transaction'ında yazar.
Bakiye DB içinde `Balance + delta` olarak güncellenir. Tablolar ve kurallar
[docs/database-design.md](docs/database-design.md) ve [docs/input-validation.md](docs/input-validation.md) içindedir.

Migration geçmişi (artımlıdır: uygulanmış migration silinmez, değiştirilmez; DB resetlenmez):
`CreateCustomers`, `AddCustomerPreferences`, `AddAccountsAndTransactionCategories`, `AddTransactions`,
`SplitCustomerName`, `AddMarketAssets`, `AddDailyMarketPrices`, `EnforceInputValidation`,
`AddTransactionKind`, `AddAccountType`, `AddCustomerCredentials`, `AddAccountNumberSequence`.

Yeni migration:

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project src/BankingAI.Data --startup-project src/BankingAI.Cli
```

Üretilen migration SQL'i incelenmeden aktif veritabanına uygulanmaz.

## Belgeler

| Dosya | İçerik |
| --- | --- |
| [RULE.md](RULE.md) | Kalıcı kurallar, stack ve hesaplama kararları, güncel durum (§29) |
| [todo.md](todo.md) | Projenin tek iş planı; "Şu anki sonraki adım" bölümü |
| [CLAUDE.md](CLAUDE.md) | Claude Code çalışma kılavuzu (komutlar, mimari, tuzaklar) |
| [docs/api.md](docs/api.md) | API sözleşmesi, durum kodları, örnek JSON |
| [docs/security.md](docs/security.md) | Giriş, JWT, müşteri bağlamının AI/MCP'ye taşınması |
| [docs/analytics.md](docs/analytics.md) | Analytics sözleşmeleri ve kabul örnekleri |
| [docs/database-design.md](docs/database-design.md) | Tablolar, ilişkiler, seed/import sınırları |
| [docs/input-validation.md](docs/input-validation.md) | Girdi kuralları (VAL), harcama tanımı, işlem türleri |
| [docs/synthetic-seed.md](docs/synthetic-seed.md) | Demo veri ve beklenen rakamlar |
| [docs/local-ai.md](docs/local-ai.md) | Local AI: model seçimi, Ollama bağlantısı, AI servisi sözleşmesi |
| [docs/mcp.md](docs/mcp.md) | Salt okunur MCP server: sınır, araçlar, argüman kontrolü, hata kodları |
| [docs/ios.md](docs/ios.md) | iPhone uygulaması: yapı, oturum ve hata kuralları, çalıştırma |
| [docs/market-data-sources.md](docs/market-data-sources.md) | Döviz/altın fiyat kaynağı araştırması |
| [docs/customer-review.md](docs/customer-review.md) | İlk modülün (Customer tablosu) tarihsel incelemesi |

## Yol haritası

1. ~~API~~ tamam (16 Ekim 2026).
2. Local AI: Qwen model seçimi (tamam: `qwen3.5:4b`), Ollama bağlantısı, FastAPI + LangChain; önce backend sonucunu Türkçe açıklama.
3. Read-only MCP tool'ları; Qwen tool seçer, argümanlar backend'de yeniden doğrulanır.
   Genişletmeler (13 Ekim 2026): yeni soru tipleri (kavramsal merchant, tutar filtresi, yaklaşan ödemeler,
   bütçe kontrolü, tasarruf simülasyonu) ve güvenilir cevap katmanı (sayı doğrulayıcı + şablon cevap,
   "nasıl hesaplandı" açıklaması, kapsam koruması, veri minimizasyonu); todo bölüm 12b ve 15b.
4. SwiftUI uygulaması, güvenilirlik/güvenlik/değerlendirme, Docker, gözlemlenebilirlik, demo.
5. Paralel: USDTRY/EURTRY/gram altın kaynağı ve historical import (yalnız opportunity-cost bunu bekler).

Her adımda önce plan yapılır, kullanıcı onaylar, sonra yalnız seçilen küçük kapsam uygulanır.
