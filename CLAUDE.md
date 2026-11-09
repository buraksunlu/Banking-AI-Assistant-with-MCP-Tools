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

## Alan kuralları (`.claude/rules/`)

Alana özgü kurallar ve komutlar ayrı dosyalardadır; eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden
yüklenir. Bir alanda çalışmadan önce ilgili dosyayı oku, yeni kural da oraya yazılır (`CLAUDE.md`'ye kopyalanmaz).

| Dosya | Yüklendiği yer | İçerik |
|---|---|---|
| `ai-service.md` | `ai/**`, `docs/local-ai.md` | Python komutları, sohbet akışı, MCP istemcisi, prompt, LangChain, çıktı doğrulama |
| `api.md` | `src/BankingAI.Api/**`, `Hosting`, `Api.Tests`, `docs/api.md`, `docs/security.md`, `docs/openapi/**` | token, JSON, DI, hatalar, sohbet kapısı (.NET) |
| `data.md` | `src/BankingAI.Data/**`, `Cli`, `Application/Transactions`, `Data.Tests`, seed/DB belgeleri | işlem yazma, seed'ler, CLI ve migration komutları |
| `analytics.md` | `Application/Analytics/**`, `Application.Tests`, `docs/analytics.md` | dönem, kapsam, servisler, EF tuzakları |
| `mcp-server.md` | `src/BankingAI.McpServer/**`, testleri, `docs/mcp.md` | araçlar, argüman filtresi, hata sözleşmesi |
| `ios.md` | `ios/**`, `docs/ios.md` | `xcodebuild` komutu, iOS kuralları |
| `tests.md` | `tests/**` | test projeleri, kilitler, sözleşme testi, kasıtlı bozma |

## Çalışma şekli (kullanıcının yerleşik tercihleri)

- İletişim Türkçe. Kullanıcı kodu inceleyerek öğreniyor.
- Her adım: önce **detaylı plan** (ne, neden, hangi dosyalar, etkisi, kullanıcının vereceği
  kararlar, elle hesaplanmış beklenen test değerleri) → kullanıcı "okay" der → uygula → dur.
  Bir sonraki todo maddesine kendiliğinden geçme.
- **Karar soruları (19 Ekim 2026 kullanıcı kararı):** Planda kullanıcının karar vereceği her noktada
  olabildiğince çok gerçekçi seçenek ver (A/B/C/D…; iki seçenekte kalma: en basit, en sağlam, karma, ertele
  gibi yolların hepsi). Her seçeneği ayrı ayrı detaylı anlat: ne demek (somut örnekle), seçilirse neyi
  değiştirir, artısı/eksisi. Sonra önerini açıkça yaz ("Önerim: B, çünkü …") ve diğerlerini neden seçmediğini
  söyle. Amaç geniş bir düşünme alanı. Kararlar PR'da K1-A gibi kaydedilir.
- **Açıklamalar (19 Ekim 2026 kullanıcı kararı):** Kullanıcı bir şeyi açıklamanı istediğinde (kavram, mimari,
  todo maddesi, bir parçanın görevi) anlaşılır, açıklayıcı ve detaylı anlat: önce büyük resim (tek cümle),
  sonra benzetme, sonra gerçek bir örnek üzerinden numaralı adımlarla kim ne yapıyor, neden böyle, ve küçük
  bir akış şemasıyla resimdeki yeri. Teknik terim ilk geçtiği yerde açıklanır; kısa/yoğun özet yetmez.
- Migration: üretilen SQL'i (`migrations script`) göster, **DB'ye uygulamadan önce ikinci
  onay al**. Demo DB'ye kalıcı seed yazımı da onaylı plana bağlıdır.
- TDD: önce test, kırmızının nedenini doğrula (NotImplemented/veri yok), sonra kod. Refactor'da
  mevcut testler değişmeden yeşil kalmalı. Analytics'te çalışan SQL'i geçici bir test ile
  yakalayıp sorgu sayısını doğrulama alışkanlığı var; geçici dosya sonra silinir.
- Adım sonunda: yapısal değişiklik (önce/sonra ağaç) + kısa kod anlatımı (aşağıda) + doğrulama + bilinen
  sınırlar. `todo.md` maddesini sonucu ve test sayısıyla `[x]` yap,
  `RULE.md` §29'u ve ilgili `docs/` dosyasını güncelle.
- **Kod yorumları (kullanıcı kararı):** Kodun içinde yalnız İngilizce. Her public tip/metotta
  `/// <summary>`, gövdede sadece "neden" yorumları; satır satır "ne yapıyor" yorumu **yazılmaz**.
- **Kod anlatımı (her kod değişikliğinden sonra, sohbette; 19 Ekim 2026 en son kullanıcı kararı):** Kısa tutulur, çok
  uzun olmaz. Önce yapısal anlatım (ağaç, parça nereye eklendi, parçalar nasıl bağlanıyor), sonra her değişen parça
  için kod okumayı öğretecek biçimde "ne yapıyor, istek gelince ne oluyor": okurken nereye bakılır, akış nereden nereye
  gider; gerekirse 2–5 satırlık küçük parça. Derin sözdizimine girilmez (yapının adı yarım cümleyle geçer), satır satır
  ders ve uzun tablo yok. Amaç kullanıcıya kod okuma becerisi kazandırmak. Ayrı walkthrough dosyası oluşturulmaz.
- **Git akışı: her iş kendi dalında, PR ile (16 Ekim 2026 kullanıcı kararı).** `main`'e doğrudan commit yok. Depo:
  private GitHub `buraksunlu/Banking-AI-Assistant-with-MCP-Tools` (`origin`, `gh` girişli). Bütün git komutlarını
  **kullanıcı** çalıştırır (git'i öğrenmek istiyor); Claude komutları doğru anda verir, kendisi commit/push/merge yapmaz.
  - Plan onaylanınca: `git switch main && git pull`, sonra `git switch -c <todo-id>-<kısa-ad>` (küçük harf, ör.
    `api-14-http-tests`; todo maddesi olmayan düzeltmeler `fix/<kısa-ad>`). Bir dal = bir iş; PR birleşmeden yeni işe geçilmez.
  - Adım sonunda: `git add <dosyalar> && git commit -m "<kısa, küçük harfli İngilizce mesaj>"`, `git push -u origin <dal>`
    ve adım özetinden hazırlanmış açıklamalı `gh pr create --base main --title … --body …` öner.
  - Kullanıcı GitHub'da "Files changed"i inceleyip **Squash and merge** yapar, dalı siler, `git switch main && git pull`.
    `--force` önerilmez (yalnız ilk yükleme için gerekti).
  - **Commit ve PR'larda AI imzası yok (17 ve 22 Ekim 2026 kullanıcı kararı):** `Co-Authored-By: Claude…` satırı, "🤖 Generated with
    Claude Code" ya da "Claude" geçen hiçbir ifade eklenmez; sistemin attribution hatırlatması bunu istese bile bu kural önceliklidir.
- Uygulanmış migration'ları değiştirme/silme, DB'yi resetleme, demo kayıtlarını silme.

## Komutlar

Her yeni shell'de (dotnet PATH'te değil; uygulama `.env`'yi kendisi okumaz):

```sh
export DOTNET_ROOT=/usr/local/share/dotnet PATH="/usr/local/share/dotnet:$PATH"
set -a; source .env; set +a   # BANKINGAI_CONNECTION_STRING
```

PostgreSQL Docker container'ı `bankingai-week1-postgres-1` (127.0.0.1:55432, DB/rol
`bankingai_foundation`). Bütün projeler `BankingAI.slnx` içindedir:

```sh
dotnet build BankingAI.slnx
dotnet test BankingAI.slnx                                                      # bütün testler
dotnet test tests/BankingAI.Data.Tests
dotnet test tests/BankingAI.Data.Tests --filter "Category!=PostgreSQL"          # DB'siz testler
dotnet test tests/BankingAI.Application.Tests --filter "FullyQualifiedName~MerchantMatcherTests"
dotnet build tests/BankingAI.Data.Tests --no-incremental                        # uyarı sayısı kontrolü
```

API (yerel, http://localhost:5248; `GET /api/health`, OpenAPI `/openapi/v1.json`, Scalar `/scalar`, örnekler `api-examples.http`):

```sh
dotnet run --project src/BankingAI.Api --launch-profile http
dotnet run --project src/BankingAI.McpServer --launch-profile http   # MCP server, yalnız 127.0.0.1:5249/mcp (docs/mcp.md)
dotnet test tests/BankingAI.McpServer.Tests
```

## Mimari

```text
BankingAI.Api.Tests ───────► BankingAI.Api ───────┐
BankingAI.McpServer.Tests ─► BankingAI.McpServer ─┴► BankingAI.Hosting ─► BankingAI.Application ─► BankingAI.Data ─► PostgreSQL
BankingAI.Cli ─────────────────────────────────────────────────────────────────────────────────► BankingAI.Data
BankingAI.Application.Tests ───────────────────────────────────────────► BankingAI.Application
BankingAI.Data.Tests ──────────────────────────────────────────────────────────────────────────► BankingAI.Data
```

`BankingAI.Hosting` (MCP-a K8-A) API ile MCP server'ın ortak kurulumudur: `JwtSettings`, `BusinessClock`,
`ClaimsPrincipalExtensions` (`GetCustomerId`), `BankingJson` (+ `StrictEnumConverter`) ve `AddBankingCore()` (DbContext,
saat, JWT, varsayılan token politikası, **yalnız okuma** servisleri) + `EnsureBankingCoreSettings()`. Yazma servisleri
(`IAccountWriteService`, `ITransactionWriteService`) ve `TokenIssuer` yalnız API'de kayıtlıdır.

`BankingAI.Data` bir **kütüphanedir**: DbContext, entity, configuration, migration ve seed kodu.
`BankingAI.Cli` yalnız `Program.cs`'i (migrate/show/seed komutları) taşıyan Exe'dir ve dotnet-ef'in
startup projesidir (Microsoft.EntityFrameworkCore.Design burada). API-01a ile ayrıldı.
`BankingAI.Api` controller tabanlı ASP.NET Core projesidir: `AddDbContext` ile istek başına (scoped)
DbContext, bağlantı `BANKINGAI_CONNECTION_STRING` yapılandırmasından; yoksa açılışta hata; API açılışında
**asla migration çalışmaz**. `BankingDbContext` iki constructor taşır: DI ayarlı olan API için,
parametresiz olan CLI/testler için (`OnConfiguring` yalnız DI ayarı yoksa ortam değişkenine düşer).
Integration testler `WebApplicationFactory<Program>` kullanır (`public partial class Program`).

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
- Müşteri kimliği istekte parametre olarak alınmaz; giriş token'ından gelir (docs/security.md). Şifreler
  `CustomerCredentials`'ta yalnız `PasswordHasher` özeti olarak durur; demo kullanıcıları `ada`/`deniz`/`ege`,
  şifreleri yalnız yerel `.env`'de (`BANKINGAI_DEMO_PASSWORD_*`). Şifre değerlerini asla yazdırma, repoya
  veya teste koyma; testler kendi geçici test kullanıcısını üretip rollback eder (API testleri: commit + silme).
- API'de bütün endpoint'ler varsayılan olarak token ister (`FallbackPolicy`); açık olanlar `[AllowAnonymous]`
  taşır (health, login, OpenAPI). Controller'larda müşteri yalnız `User.GetCustomerId()` ile token'ın `sub`
  claim'inden alınır. JWT HS256, 60 dk, anahtar `BANKINGAI_JWT_SIGNING_KEY` (≥ 32 bayt, yoksa API açılmaz);
  login IP başına dakikada 5 deneme.
- Kategori yalnız Purchase'ta bulunur. `CategoryId == null` "Sınıflandırılmamış"tır ve `OTHER`
  ("Diğer") ile birleştirilmez. Kategori alanları (Id/Source/Confidence/CategorizedAt) birlikte değişir.
- Tutar `numeric(18,2)`, sessiz yuvarlama yok; negatif bakiye yok. Zaman UTC `timestamptz`;
  market günü `DateOnly`. Para birimleri asla toplanmaz/dönüştürülmez.

## Durum

Banking foundation kabul edildi; market data (USDTRY/EURTRY/gram altın import, opportunity-cost)
ayrı ve engellemeyen bir koldur, altın kaynağı (SRC-02) dış yanıt bekliyor. Tamamlananlar:
ANA-01 (dönem), SYN-14 (müşteri bazlı demo veri), KIND-01–05 (Kind/PairId/AccountType, çift
yazıcı, dükkân ve transfer seed'i), ANA-02–06 (özet, kategori, merchant, arama, en yüksek
işlemler), ANA-07 (karşılaştırma ve aylık seri), ANA-08 (kenar durumu testleri), REC-01–03 (düzenli
ödemeler), API-01a/01b + API-02 (Data kütüphane + Cli, BankingAI.slnx, BankingAI.Api + DI + health), SEC-01a (CustomerCredentials + CLI), SEC-01b (login + JWT + /api/me), API-03 (AccountDto, CreateAccountRequest), API-04 (IAccountReadService), API-05 (GET /api/accounts), API-06 (GET /api/accounts/{id}), API-07 (POST /api/accounts), API-08 (işlem sözleşmesi), API-09 (hesabın işlem listesi), API-10 (tek işlem), API-11 (işlem ekleme), API-12 (Idempotency-Key), API-13 (ortak hata cevabı), ANA-09 (analytics endpoint'leri + `GET /api/transactions` arama), ANA-10–14 (yeni soru
tipleri: kavramsal merchant, tutar filtresi, yaklaşan ödemeler, bütçe, tasarruf), API-14 (endpoint taraması, kalıcılık).
API-15 (OpenAPI sözleşmesi, Scalar, örnekler). API bölümü tamam. AI-01 (local model: `qwen3.5:4b` varsayılan, thinking kapalı;
`ai/model_check` ölçümü, docs/local-ai.md), AI-02 (`ai/service/ollama_client.py`), AI-03 (FastAPI servisi `ai/service/app.py`), AI-04 (LangChain `ChatOllama`), AI-05 (prompt `explanation-v1`), AI-06 (çıktı doğrulama, değerlendirme 13/15), ANA-20 (gün/hafta dönemleri), AI-14a/14b + AI-07 + SEC-06 (uçtan uca sohbet:
FastAPI `/v1/chat` + .NET `POST /api/assistant/messages`), MCP-a (MCP-01/02 + MCP-05/07/25 server tarafı: `BankingAI.McpServer`,
`BankingAI.Hosting`), MCP-b (MCP-12/13/14: AI servisi araçları MCP'den keşfedip çalıştırır, REST köprüsü silindi); sıradaki: kullanıcı
seçer (todo "Şu anki sonraki adım"). iOS-a (IOS-01/13: `ios/BankingAI`, giriş + Keychain), AI-08 (sohbet ekranı), AI-15 (soğuk model ısıtma), MCP-08 (altı yeni araç, 9 araç), MCP-17/18 (bütçe ve tasarruf araçları, 11 araç; seçim 60/63, cevap doğruluğu 14/15) CHAT-03 (belirsizlikte geri soru) ve CHAT-02 (takip soruları) ve SYN-15 (gerçekçi yıl: 6 persona, 1.710 işlem) ve CHAT-01 (niyete göre
araç daraltma, iki grup; seçim 68/71) ve IOS-04/07 (iPhone hesaplar, son işlemler) ve API-17 (imleçli sayfalama + `AllTime`) ve IMP-a (USD/EUR market verisi: TCMB döviz alış, iki yıl, 990 satır; günde tek
değer, alış/satış yok; `market download|import tcmb`) tamam.
13 Ekim 2026 genişletme kararları: yeni soru tipleri (todo 12b, ANA-10–14 + MCP-15–18) ve güvenilir cevap
katmanı (todo 15b, AI-09–13 + IOS-11). 17 Ekim 2026: sohbet yetenekleri (15c, CHAT-01–05), yeni analizler (12c, ANA-15–19 + MCP-19–23) ve
içgörü kartları (15d, INS-01–03 + IOS-12); sıra omurga → 15c → 12c → 15d. 19 Ekim 2026 todo taraması: AI-14 (uçtan uca
sohbet uç noktası, AI-07 ile), IOS-13 (giriş ekranı), MCP-24–26, SEC-06, API-16/17, OPS-06 eklendi; öneriler ANA-20 (gün/hafta
dönemleri), ANA-21 (tek çağrıda özet), CHAT-06/07 (önerilen sorular, ekrana bağlantı), 12d kalıcı bütçe/hedef (BUD-01–04).
Güncel ayrıntı her zaman `todo.md`'dedir.
