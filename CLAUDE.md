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

iPhone uygulaması (`ios/BankingAI`, docs/ios.md; Xcode 26.5, iOS 17+, Swift Testing). Simülatör `http://localhost:5248`'e ulaşır:

```sh
xcodebuild -project ios/BankingAI/BankingAI.xcodeproj -scheme BankingAI -destination 'platform=iOS Simulator,name=iPhone 17 Pro,OS=26.5' test
```

iOS kuralları: `View → @Observable ViewModel → APIClient → URLSession`; View'da ağ kodu yok, kütüphane yok. Proje "eşitlenen klasör"
kullanır: `ios/BankingAI/BankingAI/` altına eklenen dosya projeye kendiliğinden girer, `project.pbxproj` elle düzenlenmez. Kod varsayılan
olarak `MainActor` (Xcode 26 ayarı). Token yalnız Keychain'de; token'lı çağrıda 401 → oturum kapanır (giriş çağrısında değil).
Testlerde ağ `MockURLProtocol` ile, test başına ayrı uydurma sunucu adıyla (paralel güvenli). Sohbet (AI-08): `ChatViewModel` ana
ekranın `@State`'inde yaşar (geçmiş yalnız bellekte, çıkışta silinir); yalnız sohbet isteği 40 sn bekler (30 → 35 → 40); 503 bant + kapalı
giriş, diğer hatalar balonda "Tekrar dene" (aynı balon); Durdur ve ekrandan çıkış isteği iptal eder. Demo şifresini Claude yazmaz/okumaz:
simülatörde girişi kullanıcı yapar.

Console CLI (`src/BankingAI.Cli`), seed sırası önemlidir:

```sh
dotnet run --project src/BankingAI.Cli -- migrate
dotnet run --project src/BankingAI.Cli -- seed categories        # sonra: market-assets, customers, accounts
dotnet run --project src/BankingAI.Cli -- seed transactions credits
# diğer transaction scope'ları: subscriptions, category-examples, grocery-growth,
# categorization-boundaries, recent-spending, demo-coverage, pairs
dotnet run --project src/BankingAI.Cli -- users set-password <username> [--customer <customer-id>]   # gizli sorar
```

Local model ölçümü (`ai/model_check`, Python 3.14, yalnız standart kütüphane; Ollama modelleri harici SSD'de, SSD takılı
olmadan Ollama modelleri bulamaz). Thinking modunda temperature 0 sonsuz düşünmeye girer; ayrıntı `docs/local-ai.md`:

```sh
/opt/homebrew/bin/python3.14 -m venv ai/.venv && ai/.venv/bin/python -m pip install -r ai/requirements.txt   # bir kez
ai/.venv/bin/python -m pytest ai                                      # bütün Python testleri; Ollama kapalıysa gerçek-Ollama testleri atlanır
python3 ai/model_check/run.py --model qwen3.5:4b --model qwen3.5:9b   # model ölçümü (yalnız standart kütüphane)
ai/.venv/bin/python ai/model_check/explanation_eval.py              # açıklama prompt'u değerlendirmesi (AI-05, Ollama açık)
python3 ai/service/ollama_client.py                                  # elle kontrol (BANKINGAI_LLM_MODEL gerekir)
ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000   # AI servisi (AI-03)
```

Python bağımlılıkları (AI-03): `ai/requirements.in` doğrudan bağımlılıklar (elle), `ai/requirements.txt` `pip freeze` kilidi. Sürümler
tam sabitlenir ve seçildiği gün **en az iki haftalık** olmalıdır; pip'in seçtiği dolaylı paket daha yeniyse `requirements.in`'e eski bir
sürümle eklenir. Starlette'in `TestClient`'ı `httpx` yerine `httpx2` ister.

`ai/service/ollama_client.py` (AI-02) Ollama'yla tek konuşma yeridir: her hata dört türden birine çevrilir (`OllamaUnavailable`,
`ModelNotFound`, `OllamaTimeout`, `OllamaBadResponse`); `check_ready()` modeli yüklemez. Testler gerçek Ollama yerine sahte HTTP
sunucusu kullanır; gerçek Ollama testleri Ollama kapalıysa atlanır.

Sohbet (AI-14a, MCP-b): `POST /v1/chat` (Bearer zorunlu) → `chat.ChatService`: `mcp_gateway.McpToolGateway.list_tools` (her turda,
kullanıcının token'ıyla; MCP kapalıysa model çalışmaz) → `LangChainToolSelector` (o turun listesiyle `bind_tools`, `tool-selection-v1`) →
araç adı listede mi → `McpToolGateway.call` (argümanları MCP server şemaya göre kontrol eder; `invalid_arguments` → sabit geri soru) →
`facts.outcome_for` (`formatting.py` ile Türkçe metin; yok merchant modelsiz sabit cevap) → açıklama. CHAT-03: seçimden sonra
`grounding.check_arguments` (dönem/sayı mesajda yoksa araç çalışmaz, geri soru; isteğe bağlı dayanaksız argüman düşer), belirsiz merchant
`facts` → `candidates`; soru `clarification.PendingStore`'da (anahtar `sha256(token)`, 5 dk) saklanır, sonraki kısa cevap (≤ 4 kelime)
`resolve` ile kodda eşleşirse model seçimi olmadan araç tamamlanır, eşleşmezse yeni soru. Soruyu `LangChainClarificationWriter` yazar
(`clarify-v1`), `check_question` geçmezse sabit şablon; seçenekler hiçbir zaman modelden gelmez. CHAT-02: son başarılı çağrı
`conversation.ConversationStore`'da (10 dk; ortak `memory.TokenMemory`); `is_follow_up` (≤ 4 kelime ya da peki/ya…) mesajlar seçiciye
bağlamla gider (`tool-selection-v2`), `check_arguments(…, previous)` son çağrıdan aynen geleni kabul eder, söylenmeyen işyeri son
çağrınınki olur; bağlama ve açıklamaya yalnız son mesaj gider (zincirleşen soru modeli ve açıklamayı yanıltıyordu). Araç tanımı Python'da
yok (MCP'den keşif). Bütün tur tek süre; token yalnız MCP başlığına gider (testte modele gitmediği doğrulanır). Uçtan uca deneme için
.NET API ve MCP server `BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z` ile açık olmalı. Tool seçimi ve açıklama aynı `explainer.stream_message`'ı kullanır.

MCP istemcisi (MCP-b, `ai/service/mcp_gateway.py`, `mcp` 2.3.0): `BANKINGAI_MCP_URL` (yoksa `http://127.0.0.1:5249/mcp`; yanlış biçimde
servis açılmaz). Protokol `2026-07-28`'e sabit (el sıkışma/keşif isteği yok); her çağrı kendi kısa olay döngüsünde (`anyio.run`, sohbet
senkron). **Tuzaklar:** (1) SDK `structuredContent`'i float'a çevirir (145.00 → 145.0); sonuç **metin içeriğinden** `Decimal` ile okunur.
(2) SDK 401 ve 5xx'i genel "Server returned an error response" hatasına gömer; `httpx2` cevap kancası bunları ayırır. (3) Hatalar iç içe
ExceptionGroup gelir; `_cause` ilk yaprağı alır. (4) SDK bir çağrının çevresinde kendiliğinden `tools/list` yapabilir. Süre hem
`anyio.fail_after` (toplam) hem HTTP zaman aşımıyla (istek başına) sınırlı. Hazır olma (`/health/ready`) MCP server'ın tokensız
`/health`'ine de bakar. Testlerde `FakeMcp` gerçek sunucunun 2026-07-28 cevaplarını (`resultType`, `ttlMs`, `cacheScope`) taklit eder;
gerçek .NET server açıksa (ve demo anındaysa) iki test daha çalışır. Araç seçim ölçümü:
`ai/.venv/bin/python ai/model_check/tool_selection_eval.py` (45 vaka; S27–S31 geri soru, F01–F14 takip, MCP server + Ollama açık, `.env` yüklü; MCP server yeni kodla
yeniden başlatılmış olmalı). Araç ekleyince/açıklama değişince önce ve sonra çalıştırılır (MCP-08 K2-A: eski vakalar bozulmaz, toplam
≥ %90). Sohbet cevabında araç argümanları **yazı** olarak döner (`app._as_text`: sözleşme ve .NET DTO `dict[str, str]`); araç seçici
`num_predict=200` (metin yazan model süreyi doldurmasın). Her yeni araca `facts.outcome_for`'da kalem üretici gerekir; ham
`rawDescription` asla kalem olmaz.

Sohbet kapısı (AI-14b + AI-07, .NET): iPhone yalnız .NET'e konuşur. `POST /api/assistant/messages` → `ICustomerCheck` (silinmiş
müşteri model çalışmadan 401) → `IAssistantClient` (`Api/Assistant/AssistantClient`, typed `HttpClient`) aynı `Authorization`
başlığıyla `/v1/chat`'e → `AssistantReply`; `GET /api/assistant/status` hep 200 `{available}`. AI hataları `AssistantException`
(`Unavailable`/`Timeout`/`Failed`) → `ApiExceptionHandler`'da 503/504/502. Ayarlar `BANKINGAI_AI_URL` (yoksa `http://127.0.0.1:8000/`),
`BANKINGAI_AI_TIMEOUT_SECONDS` (35). Müşteri başına dakikada 10 mesaj: **`UseRateLimiter` `UseAuthorization`'dan sonra** gelmeli
(anahtar token'daki müşteri; önce gelirse `GetCustomerId` patlar). Mesaj ve token loglanmaz. Testler gerçek model yerine
`FakeAssistantService` (HttpMessageHandler, `ConfigureTestServices`) kullanır.

AI servisi (AI-03, `ai/service/app.py`, FastAPI): `GET /health/live` (Ollama'ya sormaz), `GET /health/ready` (`check_ready`; değilse 503),
`POST /v1/explanations` (`question` + `facts`: backend'in biçimlendirdiği etiket/metin listesi; tanınmayan alan 400, `strict`). Hatalar
API'ninkiyle aynı problem+json (`title`, `status`, `code`, `traceId`; 422 yerine 400). Ollama hataları: 503 `ai_unavailable`/`model_missing`,
504 `ai_timeout`, 502 `ai_bad_response`. Uygulama `create_app()` fabrikasıyla kurulur (`--factory`; model ayarı yoksa açılmaz); açıklamayı
`Explainer` arayüzü üretir (`explainer.py`), testler `app.dependency_overrides` ile sahte explainer/readiness verir.
Servis yalnız 127.0.0.1'de dinler; token AI-07'de.

Isıtma (AI-15): açılışta lifespan arka planda `ollama_client.warm_up` çağırır (prompt yok, açılış beklemez, hata yalnız uyarı logu;
testlerde yalnız `with TestClient(app)` tetikler, `create_app(env, warm_up=...)` sahte alır). `BANKINGAI_LLM_KEEP_ALIVE` (varsayılan `30m`)
**her** Ollama isteğinde gönderilmeli: alansız istek süreyi 5 dk'ya çeker. Süresiz `-1` sayı olarak gider (`"-1"` yazısı 400 alır).

LangChain (AI-04): `LangChainExplainer` `ChatOllama` kullanır (`reasoning=False`, `temperature=0`, `num_predict=400`). **Tuzak:** ChatOllama
`invoke`'ta bile stream eder; `client_kwargs` timeout'u yalnız iki parça arası beklemeyi sınırlar. Toplam süre bu yüzden parçalar okunurken
kontrol edilir ve süre dolunca akış kapatılır (Ollama da işi bırakır). LangChain/Ollama/httpx hataları `_translate` ile dört türe çevrilir;
yalnız `json.JSONDecodeError` yakalanır (geniş `ValueError` gerçek hatayı gizlerdi). `LANGSMITH_TRACING`/`LANGCHAIN_TRACING_V2` açıksa
servis açılmaz (veri buluta gitmesin). Testler Ollama'nın satır satır JSON akışını taklit eden sahte sunucu kullanır (`test_explainer.py`).

Prompt (AI-05): `ai/service/prompts.py`, `PROMPT_VERSION` ile. Prompt metni değişince sürüm adı da değişir ve
`ai/.venv/bin/python ai/model_check/explanation_eval.py` (10 vaka, gerçek model; `pytest`'e dahil değil) önce ve sonra çalıştırılır.
Soru ve kalemlerdeki `<`/`>` `‹`/`›` olur (etiketle bloktan kaçılamaz); kural hatırlatması veri bloğundan sonra gelir (4b son talimata uyuyor).
Değerlendirmede yasak metin Türkçe harf katlanarak aranır ("TALIMAT" = "talimat"). Prompt kural koyar ama garanti vermez: kod kontrolü AI-06/10.

Çıktı doğrulama (AI-06): `answer_check.check_answer` her cevabı kalemlere karşı kontrol eder (her sayı soruda/veride geçmeli; boş cevap ve
`<veri`/"backend" yasak). `LangChainExplainer.explain` geçmeyen cevaba bir kez düzeltme ister (`prompts.build_retry_messages`), iki deneme
tek süreyi paylaşır; yine geçmezse `AnswerRejected` → 502 `ai_answer_rejected`. Sayı okuma/harf katlama yalnız `answer_check.py`'da
(`scoring.py` oradan import eder; `answer_check` standart kütüphaneyle sınırlı kalmalı). Testlerde model `_ask`, saat `clock` ile değiştirilir.

Migration:

```sh
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> --project src/BankingAI.Data --startup-project src/BankingAI.Cli
dotnet tool run dotnet-ef migrations script <PreviousMigration> <Name> --project src/BankingAI.Data --startup-project src/BankingAI.Cli
docker exec bankingai-week1-postgres-1 psql -U bankingai_foundation -d bankingai_foundation -c "<SQL>"
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
`BankingAI.McpServer` (docs/mcp.md): ayrı süreç, `127.0.0.1:5249/mcp`, durumsuz Streamable HTTP, `ModelContextProtocol.AspNetCore`
2.2.0. Araçlar `Tools/SpendingTools` + `Tools/PaymentTools`, 9 araç (servisler **kurucudan**; SDK metot parametresinde servis tahminini bazen yanlış yapıyor),
argümanlar `ToolCallFilter`'da yayınlanan şemaya göre (SDK şemayı uygulamaz), hatalar `ToolErrors` ile
`{code: invalid_arguments|session_invalid|internal_error, message}`. Yeni araç → `ToolDiscoveryTests` allowlist'ine satır. Filtre tam
sayıyı da uygular (SDK `"3"`/`3.5`'i sessizce 3 yapar); `[Range]` şemaya `minimum`/`maximum` olarak, varsayılan değerli parametre zorunsuz yazılır.
Testlerin ortak yardımcıları `tests/Shared` (`TestTokens`, `CapturedLogs`, `CommittedRowsLock`), `BankingAI.TestSupport`
namespace'i proje dosyasında global using.

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
- API JSON kuralları (docs/api.md): enum'lar yazıyla ve **tam adla** (`StrictEnumConverter`: sayı ve `"purchase"` 400), para `decimal` JSON
  sayısı, yazma isteklerinde tanınmayan alan 400 (`[JsonUnmappedMemberHandling(Disallow)]`). Entity JSON'a
  verilmez: dışarı DTO (Application, EF'in çevirdiği `Projection` ifadesiyle; sıralama `Select`'ten önce),
  içeri yalnız istemcinin seçebileceği alanları taşıyan request (Api). DTO'larda `CustomerId` bulunmaz.
  İşlemin dış biçimi analytics'in `TransactionItem`'ıdır (ayrı DTO yazma). `CreateTransactionRequest` yalnız olayı taşır
  (Purchase/Salary, tutar, açıklama, isteğe bağlı merchant); yön, para birimi, hesap, zaman ve kategori backend'indir.
  Request'lerde tutar ve metin kuralları `InputRules.Money`/`InputRules.Text` çağrılarak `ApiValidation.Rule` ile yeniden
  kullanılır (kopyalanmaz); `[StringLength]` yerine (DB gibi karakter sayar, NUL'u istemcinin alanında yakalar).
  Analytics servisleri controller'a doğrudan (somut sınıf, Scoped) verilir; `TransactionsController` işlem listesini,
  bütün hesaplarda filtreli aramayı (`SearchAsync`, `GET /api/transactions`, ANA-09) ve tek işlemi (`GetAsync`, API-10)
  `TransactionSearchService` ile döndürür (yeni sorgu yazma), işlem eklemeyi
  (`CreateAsync`, API-11, `POST /api/accounts/{accountId}/transactions` → 201 + Location `/api/transactions/{id}`)
  `ITransactionWriteService` ile yapar; route'lar farklı köklerde olduğu için sınıfta `[Route]` yok, her action tam
  route'unu taşır. Dönem parametreleri her endpoint'te `Api/Queries/PeriodParameters` ile okunur (hata → `ArgumentException`
  → 400); query'deki enum'lar `Api/Queries/ExactEnum` ile yalnız tam adla. `AnalyticsController` (`/api/analytics/...`, ANA-09) analytics servislerini (Scoped, somut sınıf) çağırır; dönem
  zorunlu. İş saati (`Api/Time/BusinessClock`): `BANKINGAI_DEMO_NOW` verilirse analytics dönemleri ve yeni işlem zamanı o
  ana sabitlenir; `PeriodResolver` ve `TransactionWriteService` bunu kullanır, `TokenIssuer`/JWT gerçek `TimeProvider`'da
  kalır. Analytics API testleri `TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow))` ile sembolik
  dönem kullanır; ayarsız testlerde rakam kontrolü sabit `from`/`to` ile yapılır.
- Hesap okuma `Application/Accounts`: `IAccountReadService` (kullanıcı kararı: controller'lar interface'e bağlanır;
  analytics servisleri interface'siz kalır) → `AccountReadService`. Sahiplik yine `CustomerScope`'ta; liste sırası
  `CreatedAt`, eşitse `AccountNumber`. Repository eklenmez.
- DI (`Program.cs`): DbContext kullanan servisler **Scoped** (`AddScoped<IAccountReadService, AccountReadService>`);
  durumsuzlar Singleton (`PeriodResolver`, `TimeProvider`, `TokenIssuer`). Singleton'a Scoped servis verilirse API açılmaz.
  Controller kalıbı: `User.GetCustomerId()` → servis → DTO; müşteri parametresi yoktur; **`try/catch` yazılmaz**.
  Başka müşterinin kaynağı olmayan kaynakla birebir aynı 404'ü alır (403 değil). GUID parametreleri `Guid id` ile
  bağlanır (bozuk biçim 400).
- Hatalar (API-13, `Api/Errors`): her cevap ProblemDetails + Türkçe `title` + `code` + `traceId`. İstisna eşlemesi tek
  yerde, `ApiExceptionHandler`: `CustomerNotFoundException` (Application; `KeyNotFoundException`'dan türer, `CustomerScope`
  atar) → 401 `session_invalid`, `KeyNotFoundException` → 404, `BalanceLimitException` → 409, `ArgumentException` → 400
  (`(Parameter …)` eki temizlenir), diğer her şey → 500 (ayrıntı yalnız logda, her ortamda). Gövdesiz cevaplar
  `UseStatusCodePages`'le, otomatik 400 `ApiValidation.InvalidModelState` ile (camelCase alan, Türkçe mesaj, JSON
  yolu/tip adı yok). Kodlar ve başlıklar `ApiProblems` tablosunda; yeni hata türü eklenirse oraya.
- Kategori yalnız Purchase'ta bulunur. `CategoryId == null` "Sınıflandırılmamış"tır ve `OTHER`
  ("Diğer") ile birleştirilmez. Kategori alanları (Id/Source/Confidence/CategorizedAt) birlikte değişir.
- Tutar `numeric(18,2)`, sessiz yuvarlama yok; negatif bakiye yok. Zaman UTC `timestamptz`;
  market günü `DateOnly`. Para birimleri asla toplanmaz/dönüştürülmez.

### İşlem yazma

`Transaction` doğrudan `db.Add` ile eklenmez:

- `Writing/TransactionWriter` (API-11'e kadar `Seeding/TransactionSeedWriter`): tek bacak; insert +
  `ExecuteUpdate(Balance + delta)` aynı DB transaction'ında. `PairId`'li işlemi reddeder. Seed'ler ve API (API-11)
  kullanır. Kendi transaction'ını yönetir: açık transaction'lı context'i reddeder, bu yüzden testler rollback yapamaz.
  `WriteOutcome` döner (API-12): aynı kimlik + aynı olay (`TransactionWriter.SameEvent`: istemcinin söylediği alanlar;
  zaman/kategori hariç) → `AlreadyExisted`, bakiye ikinci kez değişmez; farklı olay → `IdempotencyConflictException` (409).
  Eşzamanlı tekrar birincil anahtara (23505) takılır, geri alınıp aynı karşılaştırılır.
- `Writing/PairedTransactionWriter`: iki bacak + iki bakiye tek transaction'da; bakiyeler
  **her zaman küçük AccountId önce** güncellenir (aksi halde ters yönlü transferler 40P01 deadlock alır).
  Aynı PairId tekrarı (API-12, KIND-03 kararı): aynı iki bacak → `AlreadyExisted`, farklı → çakışma.
- Bakiye mantığı `TransactionWriter.ApplyBalanceDeltaAsync` içinde ortaktır. Bakiye 0'ın altına ya da para üst
  sınırının üstüne çıkacaksa `BalanceLimitException` (InvalidOperationException'dan türer) atılır ve hiçbir şey
  yazılmaz; API bunu 409'a çevirir. Ön bakiye kontrolü yapılmaz: tek karar veren atomik güncellemedir.
- API'den yazma: `Application/Transactions/TransactionWriteService` (`ITransactionWriteService`; yalnız Purchase/Salary,
  yön türden, para birimi hesaptan, zaman `TimeProvider`'dan; Business hesap da kabul edilir — kullanıcı kararı 3-B).
  Saat entity kurulmadan **önce** okunur: entity'nin `CreatedAt` varsayılanı kurulurken alınır ve yazıcı
  `OccurredAt > CreatedAt`'i reddeder. LLM/MCP'ye yazma verilmez. `Idempotency-Key` (API-12) işlem eklemede zorunlu,
  hesap açmada isteğe bağlı; kayıt kimliği `IdempotentId.For(kaynak, müşteri, anahtar)` ile türetilir, tekrar
  `Idempotent-Replayed: true` başlığıyla aynı 201'i alır. Testlerdeki POST yardımcıları her çağrıda yeni anahtar üretir.

### Seed'ler

Sabit GUID'ler: müşteri `10000000-…-000000000001..3` (Ada Kahve, Deniz Market, Ege Yolcu), hesap
`20000000-…-00000000000N` (7 = Deniz'in Business "Kahve Durağı" hesabı), işlem
`30000000-…-{ordinal:D12}`, çift `40000000-…`. Demo hesap numaraları sabit (`DUMMY-…`); API'den açılan hesaplar
`AccountNumberSequence` sayacından `ACC-n` alır (sayaç rollback'te geri dönmez, boşluk normaldir). Kullanılan ordinal aralıkları: 1–14, 101–106,
201–206, 301–306, 401–402, 501–536, 601–628, 701–718. Seed'ler idempotenttir; aynı ID farklı
içerikle bulunursa sessizce düzeltmez, hata verir. Transaction seed'leri advisory lock kullanır
ve liste **sırasıyla** yazılır (boş hesaba Credit'ler Debit'lerden önce gelmeli).

### Analytics (`BankingAI.Application/Analytics`)

- `PeriodResolver` + `TimeProvider`: sembolik dönem (`PeriodKind`) müşterinin `TimeZone`'una göre
  `[StartUtc, EndUtc)` aralığına çevrilir. ANA-20: gün/hafta dönemleri (hafta pazartesi; "son N gün" bugün dahil tam N gün,
  "son N ay" N ay önceki aynı günden; anlam tablosu docs/analytics.md). Testler ve demo "şimdi"yi
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
- ASP.NET tuzağı: `SuppressAsyncSuffixInActionNames = false` ayarlıdır; `CreatedAtAction(nameof(GetAsync), …)` buna dayanır
  (varsayılan ayarda action adı "Get" olur ve Location üretimi 500 verir).
- EF tuzakları: (1) constructor'la kurulan record'a projeksiyondan **sonra** `OrderBy` çevrilemez;
  `GroupBy(...).OrderBy(g => g.Key).Select(...)` kullan. (2) Nullable kolonda korelasyonlu join'de
  EF C# null semantiğini korur (`null == null` true) ve dış satırdaki null kontrolünü düşürebilir;
  kontrolü join'lenen satıra koy (`o.PairId != null && o.PairId == t.PairId`). (3) Npgsql 10
  `DateTimeOffset` saat dilimi dönüşümünü (`ConvertTimeBySystemTimeZoneId`) SQL'e çeviremez; yerel ay
  gruplaması için UTC pencere sınırlarını kullan.

### Testler

- `BankingAI.Data.Tests`: PostgreSQL testleri `[Collection(PostgreSqlCollection.Name)]` ile sıralı
  çalışır; ya kendi transaction'ında rollback yapar ya da geçici `DUMMY-*TEST-*` hesap açıp siler. Geçici
  hesaplar sabit demo müşterisine (Ada/Deniz) açılır; "ilk müşteri"yi seçme (başka test projesinin geçici
  müşterisi olabilir) ve `EmptyCustomer`'a hesap açma (analytics testleri onu hesapsız bekler).
- **Yeni endpoint eklenince** `tests/BankingAI.Api.Tests/EndpointSweepTests.cs`'teki örnek istek tablosuna geçerli bir
  istek eklenir (API-14); eklenmezse kapsam bekçisi kırmızı olur. Tablo token zorunluluğunu ve var olmayan müşteride
  `session_invalid`'i bütün korumalı endpoint'lerde tarar; yazma örnekleri yalnız var olmayan müşteri için gönderilir.
- **Sözleşme (API-15):** `docs/openapi/v1.json` commit'lenmiş OpenAPI belgesidir; `OpenApiContractTests` üretilenle birebir
  karşılaştırır. Endpoint, DTO, parametre ya da cevap kodu değişince test kırmızı olur; bilerek değiştiyse
  `BANKINGAI_UPDATE_OPENAPI=1 dotnet test tests/BankingAI.Api.Tests --filter "FullyQualifiedName~The_document_is_the_same"`
  ile yenile ve dosyayı commit'e ekle. Belge kuralları `Api/OpenApi/ApiDocument`'ta; `AddOpenApi` lambda biçiminde kalmalı
  (XML yorum üreticisi metot adını yakalamaz). Para JSON sayısıdır (`JsonNumberHandling.Strict`; tırnaklı tutar 400).
- `BankingAI.Api.Tests`: `DemoData.cs` Application.Tests'ten link'lenir (kopyalanmaz); `TestApi.JsonOptions` (API'nin JSON ayarları, DB'siz); `TestApi.Create(...)` (WebApplicationFactory + ayar ezme), `TestTokens` (sahte/süresi
  geçmiş token), `TestUser` (commit edilen geçici "Test Api" müşterisi + `apitest.*` girişi, dispose'da silinir,
  10 dk'dan eski kalıntılar temizlenir; silme sırası işlemler → hesaplar → giriş → müşteri, müşterinin bütün hesapları
  silinir). Yazma testleri demo müşterisine değil `TestUser`'a yapılır.
- **İşlem yazan testler (API-11) demo hesabını hiç hedef almaz, ret bekleyen testler dahil:** kasıtlı bozmada kontrol
  kalkınca yazma gerçekten olur. "Başkasının hesabı" testi ters kurulur (Ada'nın token'ıyla geçici hesaba yazma).
  Application.Tests'te bunun için `TemporaryCustomer` ("Test Application" müşterisi + hesapları, paylaşımlı kilit,
  dispose'da işlem → hesap → müşteri silinir) vardır.
- Ada/Deniz'e geçici hesap açan Data testleri paralel çalışır: Application/Api testleri müşterinin **bütün**
  hesaplarını listeleyip saymamalı (sabit hesap ID'leriyle seçmeli) ya da kilit kullanmalıdır.
- Test projeleri `dotnet test BankingAI.slnx`'te **paralel** çalışır. Commit eden testler
  `tests/Shared/CommittedRowsLock.SharedAsync()`, müşteri ayırmadan satır sayan/toplayan testler `ExclusiveAsync()` alır
  (PostgreSQL advisory lock 2026101201; dosya üç test projesine `<Compile Include … Link>` ile bağlı). Sınıf boyunca
  gerekiyorsa `IAsyncLifetime` ile (yazıcı testleri paylaşımlı, `SyntheticDatasetIntegrityTests` özel). **Bir test en
  fazla bir kilit alır:** ikinci paylaşımlı istek, ilkini bekleyen bir özel isteğin arkasında kuyruğa girer (kilitlenme).
- `BankingAI.Application.Tests`: seed edilmiş demo DB'yi okur. Demo müşteri/hesap GUID'leri ve
  `DemoNowUtc` tek yerde, `DemoData.cs` içindedir (`using static ...DemoData;`); test dosyasına kopyalama.
- Beklenen değerler **elle hesaplanmış fixture**'lardır; seeder'dan türetilmez
  (`SyntheticDatasetIntegrityTests`, `docs/synthetic-seed.md`). Seed eklemek bakiye/sayı fixture'larını değiştirir.
- Mevcut davranışı kapsayan yeni testler yeşil başlarsa, kodu geçici bozup (ör. sınırda `<` → `<=`)
  kırmızıyı gör, sonra geri al. Değişikliği dosyanın kendisinde yap ve `git checkout -- <dosya>` ile
  geri al; `cp`/`mv` ile yedek dosya kullanma (proje klasörüne fazladan `.cs` kopyası derlemeyi bozar).
  Dosyada commit edilmemiş yeni kod varsa (TDD adımında normal durum) `git checkout` onu da siler: o zaman
  bozmayı tersine çevrilebilir tek düzenlemeyle yap ve geri dönüşü `shasum` önce/sonra karşılaştırmasıyla doğrula.
  Proje 13 Ekim 2026'da iCloud'lu Masaüstünden `~/Projects/VAKIFBANK/AI_Assistant`'ye taşındı; iCloud `.git`
  nesnelerini buluta alıp git'i kilitliyordu. Depoyu iCloud/eşitlenen klasöre geri koyma. Sonunda `dotnet build --no-incremental` ile
  doğrula; eski dosya zamanı MSBuild'in bozuk derlemeyi kullanmasına yol açabilir.
- Yeni enum kolonunda EF migration'ı `NOT NULL DEFAULT 0` üretir: elle nullable ekle → backfill →
  belirsiz kayıtta `RAISE EXCEPTION` → `NOT NULL` yap. DB varsayılanı olmadığı için testlerdeki ham
  `INSERT`'ler `Kind` ve `AccountType`'ı açıkça vermelidir.

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
seçer (todo "Şu anki sonraki adım"). iOS-a (IOS-01/13: `ios/BankingAI`, giriş + Keychain), AI-08 (sohbet ekranı), AI-15 (soğuk model ısıtma), MCP-08 (altı yeni araç, 9 araç) CHAT-03 (belirsizlikte geri soru) ve CHAT-02 (takip soruları) tamam; sırada SYN-15 (demo verisini genişletme).
13 Ekim 2026 genişletme kararları: yeni soru tipleri (todo 12b, ANA-10–14 + MCP-15–18) ve güvenilir cevap
katmanı (todo 15b, AI-09–13 + IOS-11). 17 Ekim 2026: sohbet yetenekleri (15c, CHAT-01–05), yeni analizler (12c, ANA-15–19 + MCP-19–23) ve
içgörü kartları (15d, INS-01–03 + IOS-12); sıra omurga → 15c → 12c → 15d. 19 Ekim 2026 todo taraması: AI-14 (uçtan uca
sohbet uç noktası, AI-07 ile), IOS-13 (giriş ekranı), MCP-24–26, SEC-06, API-16/17, OPS-06 eklendi; öneriler ANA-20 (gün/hafta
dönemleri), ANA-21 (tek çağrıda özet), CHAT-06/07 (önerilen sorular, ekrana bağlantı), 12d kalıcı bütçe/hedef (BUD-01–04).
Güncel ayrıntı her zaman `todo.md`'dedir.
