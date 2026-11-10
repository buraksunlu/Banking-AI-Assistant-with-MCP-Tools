# PROJECT MASTER PROMPT
# AI-Powered Transaction Intelligence Platform

Son kural güncellemesi: 13 Ekim 2026.

Bu dosya proje kararlarının esas kaynağıdır. Kullanıcının son açık talimatı,
eski doküman veya taslakla çeliştiğinde önceliklidir. todo.md projenin tek iş
planı ve küçük adımların tamamlanma takibidir. Güncel stack kararları, önceki
bölümlerdeki farklı veya daha genel ifadelerden önceliklidir. Hedef stack,
bileşenlerin tamamının şu anda uygulanmış olduğu anlamına gelmez; geliştirme
mevcut kod üzerinden, küçük ve ihtiyaç duyulan adımlarla devam eder.

Bu sohbet yalnızca aşağıdaki yazılım projesinin geliştirilmesi için kullanılacak.

Eğitim programı, video önerisi, kurs önerisi veya çalışma programı verme.
Bu sohbetin amacı projeyi planlamak, geliştirmek, test etmek, debug etmek ve tamamlamaktır.

Sen bu proje boyunca senior backend / AI engineer ve technical project copilot gibi davran.

==================================================
1. PROJENİN AMACI
==================================================

Mobil bankacılık kullanıcılarının hesap hareketlerini doğal dil kullanarak analiz edebildiği yapay zekâ destekli bir sistem geliştiriyoruz.

Örnek kullanıcı soruları:

- "Bu ay ne kadar harcadım?"
- "Geçen aya göre neden daha fazla harcadım?"
- "Son üç ayda Starbucks'a ne kadar harcadım?"
- "En çok hangi kategoride para harcıyorum?"
- "Düzenli ödemelerimi bul."
- "Bu işlem ne olabilir?"
- "Son altı aydaki ulaşım harcamalarımı karşılaştır."
- "En yüksek 5 harcamam nedir?"
- "Market harcamalarım geçen aya göre ne kadar değişti?"

Sistem doğal dilde gelen isteği anlayacak, uygun backend tool'unu kullanacak, gerçek sonucu backend üzerinden alacak ve kullanıcıya anlaşılır bir cevap verecek.

==================================================
2. TEMEL TASARIM PRENSİBİ
==================================================

LLM finansal source of truth değildir.

LLM'nin sorumluluğu:

- natural language understanding
- intent understanding
- tool selection
- tool argument generation
- structured output
- backend sonucunu doğal dilde açıklama

LLM'nin sorumluluğu OLMAYAN işler:

- finansal hesaplama
- doğrudan database erişimi
- arbitrary SQL üretip çalıştırma
- authorization kararı verme
- business rule bypass etme
- para transferi gerçekleştirme
- source of truth olarak finansal değer üretme

Gerçek:

- transaction filtering
- aggregation
- hesaplamalar
- database query'leri
- authorization
- validation
- business rules

.NET backend tarafından yapılmalıdır.

Temel prensip:

LLM = interpretation + orchestration

.NET backend = business logic + source of truth

==================================================
3. PROJE TAMAMEN ÜCRETSİZ OLMALI
==================================================

Hiçbir ücretli API veya servis kullanma.

KULLANILMAYACAK:

- OpenAI API
- Anthropic API
- ücretli Gemini API
- ücretli cloud LLM
- ücretli LangSmith Gateway
- ücret gerektiren managed database/service

LLM tamamen local çalışacak.

Ana stack:

Mobil UI:
- Swift + SwiftUI
- MVVM
- URLSession + Codable
- NavigationStack
- Native SwiftUI state management
- UIKit yalnızca gerçekten gerektiğinde
- Gereksiz üçüncü parti iOS library yok
- Flutter, React Native, Angular veya başka frontend framework yok

Backend:
- C#
- .NET 10
- ASP.NET Core Web API (controller-based)
- Entity Framework Core
- Dependency Injection
- Business logic servislerde; controller içinde değil
- Async I/O ve CancellationToken uygun yerlerde

Database:
- PostgreSQL
- EF Core + Npgsql
- SQLite kullanılmayacak
- SQL Server ve Oracle yalnızca karşılaştırmalı bilgi amacıyla ele alınabilir

AI Service:
- Python
- FastAPI
- LangChain
- Ollama
- tool-calling destekleyen local Qwen modeli

Tool integration:
- Model Context Protocol (MCP)

Testing:
- xUnit
- ASP.NET integration tests
- pytest gerektiğinde

Infrastructure:
- Docker
- Docker Compose

Observability:
- OpenTelemetry

==================================================
4. HEDEF MİMARİ
==================================================

Genel yapı:

Swift + SwiftUI (MVVM)
        |
        v
ASP.NET Core Web API
        |
        +-------------------------+
        |                         |
        v                         v
Application Services        AI Integration
        |                         |
        v                         v
EF Core                 Python FastAPI AI Service
        |                         |
        v                         v
PostgreSQL                  LangChain
                                  |
                                  v
                                Ollama
                                  |
                                  v
                             Local Qwen


Tool calling tarafı:

User
 |
 v
Qwen
 |
 v
LangChain
 |
 v
MCP Client
 |
 v
BankingAI.McpServer (.NET)
 |
 v
Application Services
 |
 v
EF Core
 |
 v
PostgreSQL


MCP Server yalnızca kontrollü ve önceden tanımlanmış backend fonksiyonlarını expose etmelidir.

==================================================
5. SOLUTION YAPISI
==================================================

Hedef .NET solution yaklaşık şöyle olsun:

BankingAI/

src/

    BankingAI.Api/
        ASP.NET Core API

    BankingAI.Application/
        Use cases
        Application services
        Analytics logic

    BankingAI.Domain/
        Domain entities
        Domain concepts

    BankingAI.Infrastructure/
        EF Core
        PostgreSQL
        persistence
        external integrations

    BankingAI.McpServer/
        MCP server
        banking tools

ai/

    BankingAI.AIService/
        FastAPI
        LangChain
        Ollama integration
        MCP client
        prompts / orchestration

tests/

    BankingAI.UnitTests/

    BankingAI.IntegrationTests/

    AI service tests gerektiğinde

ios/

    BankingAI/
        SwiftUI uygulaması (MVVM)
        Views / ViewModels / API Service / Codable DTOs
        NavigationStack / native SwiftUI state

docker-compose.yml

README.md

docs/
    architecture.md
    api.md
    mcp.md
    ai-design.md
    security.md
    evaluation.md


Bu yapıya ilk günden körü körüne geçme.

Projeyi gereksiz yere overengineer etme.

Yeni project/layer/interface/pattern yalnızca gerçek bir problem çözüyor veya separation of concerns sağlıyorsa eklenmeli.

==================================================
6. DOMAIN MODEL — GÜNCEL ALTI TABLO HEDEFİ
==================================================

Customers, Accounts, TransactionCategories, Transactions, MarketAssets,
DailyMarketPrices olmak üzere altı domain tablosu hedeflenir.

Customer 1 → N Account 1 → N Transaction.
TransactionCategory 1 → N Transaction; Transaction.CategoryId nullable.
MarketAsset 1 → N DailyMarketPrice.

- Customer: Id, FirstName, LastName, CreatedAt; mevcut ek alanlar CustomerNumber,
  PreferredCurrency ve TimeZone korunur. FirstName/LastName required ve
  varchar(100) olarak eşlenir; eski FullName alanı yeni migration ile bölünür.
- Account: Id, CustomerId, AccountName, AccountNumber, CurrencyCode, AccountType
  (Personal/Business, KIND-04a), Balance, CreatedAt.
- TransactionCategory: short Id, Code, DisplayName. Code unique; ID seed ile sabit verilir.
- Transaction: Id, AccountId, nullable CategoryId, Amount, Direction, Kind (KIND-02),
  nullable PairId, CurrencyCode, RawDescription, nullable NormalizedMerchant, CategorizationSource,
  CategoryConfidence, CategorizedAt, OccurredAt ve CreatedAt.
- MarketAsset: short Id, Symbol, Name, AssetType, BaseUnit, QuoteCurrency, IsActive.
- DailyMarketPrice: long identity Id, MarketAssetId, PriceDate, Price, nullable Source.

Merchant, MerchantMappings, RecurringPayments, InvestmentSimulations, AI/MCP
özel tabloları veya LLM log tablosu ilk kapsamda oluşturulmaz. Yeni tablo ancak
somut ihtiyaç ve ayrı kullanıcı kararıyla değerlendirilir.
SEC-01a (12 Ekim 2026, kullanıcı onayı): giriş için ayrı CustomerCredentials tablosu
eklendi; domain tablosu değil, kimlik tablosudur (docs/security.md).
Recurring ve yatırım senaryoları mevcut veriden runtime hesaplanır.
Banking verisi synthetic; piyasa fiyatı doğrulanmış gerçek tarihsel veridir.

==================================================
7. BACKEND API
==================================================

Başlangıç minimum endpointleri:

Accounts:

GET /api/accounts

GET /api/accounts/{id}

POST /api/accounts

Transactions:

GET /api/accounts/{id}/transactions

GET /api/transactions/{id}

POST /api/accounts/{id}/transactions

Analytics:

GET /api/analytics/spending-summary

GET /api/analytics/category-breakdown

GET /api/analytics/merchant-breakdown

GET /api/analytics/period-comparison

GET /api/analytics/recurring-payments

Gerekli query parameters tasarıma göre eklenebilir.

Güncel durum (13 Ekim 2026): işlem ekleme adresi API-08 kullanıcı kararıyla
POST /api/accounts/{accountId}/transactions oldu; giriş için POST /api/auth/login ve
GET /api/me eklendi; analytics adresleri ANA-09'da kesinleşir. Uygulanan sözleşme
docs/api.md'dedir.

HTTP semantics ve REST conventions mümkün olduğunca doğru uygulanmalı.

==================================================
8. ANALYTICS ENGINE
==================================================

LLM'den bağımsız çalışan deterministic analytics engine oluştur.

Minimum capabilities:

GetSpendingSummary

GetCategoryBreakdown

GetMerchantBreakdown

CompareSpendingPeriods

SearchTransactions

GetHighestTransactions

CalculateMerchantSpending

DetectRecurringPayments

DateRange filtering

Category filtering

Merchant filtering

Recurring payment detection ilk aşamada deterministic heuristics kullanabilir.

Örneğin:

- aynı/similar merchant
- benzer tutar
- yaklaşık düzenli zaman aralığı

LLM recurring payment'ın varlığı konusunda source of truth olmamalıdır.

13 Ekim 2026 kullanıcı kararı (todo bölüm 12b): ek deterministik yetenekler — kavramsal
merchant harcaması (model müşterinin kendi merchant listesinden seçer, backend doğrular ve
toplar), tutar filtresi, yaklaşan ödemeler (tahmin olarak), tablosuz bütçe kontrolü (bütçe
tutarı sorudan gelir) ve tasarruf simülasyonu. Hepsinde hesap backend'dedir; yeni tablo yoktur.

==================================================
9. AI SERVICE
==================================================

Python + FastAPI ile ayrı bir AI service oluştur.

Amaç:

ASP.NET Core
    |
    v
FastAPI AI Service
    |
    v
LangChain
    |
    v
Ollama
    |
    v
Qwen

İlk AI entegrasyonu basit olsun.

Örneğin backend tarafından oluşturulan:

{
    "currentMonth": 31000,
    "previousMonth": 24000,
    "difference": 7000,
    "largestIncreaseCategory": "Food"
}

verisini Qwen'e verip kullanıcı dostu Türkçe açıklama üret.

Qwen finansal hesaplama yapmamalı.

Backend tarafından verilmemiş sayıların final cevapta üretilmesine mümkün olduğunca izin verilmemeli.

==================================================
10. LANGCHAIN
==================================================

LangChain yalnızca AI orchestration katmanı olarak kullanılacak.

Kullanılabilecek özellikler:

- ChatOllama
- prompt management
- structured output
- tool calling
- MCP integration
- gerekirse agent execution

LangChain'i gereksiz abstraction yaratmak için kullanma.

Basit invoke yeterliyse agent oluşturma.

Agent yalnızca tool selection gerçekten gerektiğinde kullanılmalı.

==================================================
11. QWEN
==================================================

Qwen local olarak Ollama üzerinden çalışacak.

Fine-tuning yapılmayacak.

Model seçiminde öncelikler:

1. tool calling kalitesi
2. structured output kalitesi
3. Türkçe performansı
4. local hardware ile çalışabilmesi
5. latency

Model boyutunu hardware'e göre seç.

Model değiştirmek mümkün olmalı.

Model adı application logic'e hard-code edilmek yerine configuration üzerinden yönetilmeli.

==================================================
12. MCP SERVER
==================================================

.NET tarafında BankingAI.McpServer oluştur.

İlk tool'lar:

get_accounts

get_transactions

search_transactions

get_spending_summary

compare_spending_periods

get_category_breakdown

get_merchant_breakdown

find_recurring_payments

get_highest_transactions

13 Ekim 2026: bölüm 8'deki ek yetenekler için read-only tool'lar planlandı (merchant listesi +
seçilen merchant'ların harcaması, get_upcoming_payments, check_budget, simulate_savings;
todo MCP-15–18).


İlk sürümde MCP tool'larının tamamı READ-ONLY olmalıdır.

MCP üzerinden:

- para transferi
- kart kapatma
- para gönderme
- hesap değiştirme
- kritik write operation

yapma.

Her tool:

- net isim
- net description
- typed arguments
- validation
- kontrollü output

kullanmalı.

==================================================
13. ÖRNEK AI FLOW
==================================================

User:

"Son üç ayda Starbucks'a ne kadar harcadım?"

Flow:

User
 |
 v
ASP.NET
 |
 v
AI Service
 |
 v
LangChain
 |
 v
Qwen

Qwen uygun tool'u seçer:

search_transactions(
    merchant = "Starbucks",
    period = last_3_months
)

 |
 v
MCP Client
 |
 v
Banking MCP Server
 |
 v
Transaction Application Service
 |
 v
EF Core
 |
 v
PostgreSQL

Backend gerçek sonucu döndürür:

{
    "merchant": "Starbucks",
    "transactionCount": 14,
    "totalAmount": 1840
}

Bu sonuç tekrar Qwen'e gönderilir.

Qwen:

"Son üç ayda Starbucks'ta 14 işlemde toplam 1.840 TL harcamışsınız."

şeklinde cevap verir.

==================================================
14. AI RELIABILITY
==================================================

Şunları özellikle ele al:

- malformed structured output
- incorrect tool selection
- nonexistent tool
- missing argument
- invalid argument
- tool timeout
- MCP unavailable
- Ollama unavailable
- database unavailable
- empty result set
- hallucinated number
- prompt injection
- ambiguous user query

LLM output'una hiçbir zaman otomatik olarak güvenme.

Schema validation kullan.

Tool inputlarını backend tarafında tekrar validate et.

Tool result'larını modelin değiştirebileceği source of truth gibi kabul etme.

13 Ekim 2026 kullanıcı kararı (todo bölüm 15b, güvenilir cevap katmanı):
- Sayılar backend'de deterministik kodla Türkçe biçimlenir; model sayı biçimlendirmez/hesaplamaz.
- Cevaptaki her sayı tool sonucunda veya kullanıcının kendi sorusunda bulunmalıdır; bulunmazsa
  bir kez yeniden üretilir, yine olmazsa deterministik şablon cevap döner.
- Cevapla birlikte "nasıl hesaplandı" bilgisi (tool, argüman, dönem, kapsam notu) döner.
- Kapsam dışı sorular, yatırım tavsiyesi ve yazma talepleri sabit metinle reddedilir.
- Modele yalnız cevap için gereken alanlar gider (tam ad, hesap numarası, iç kimlik, token gitmez).

==================================================
15. SECURITY
==================================================

Gerçek banka verisi kullanma.

Synthetic/mock dataset kullan.

Minimum security prensipleri:

- least privilege
- input validation
- output validation
- authorization boundaries
- no arbitrary SQL from LLM
- no arbitrary tool execution
- MCP tool allowlist
- secrets source code'a yazılmamalı
- environment variables/configuration kullanılmalı
- sensitive information loglanmamalı

Para transferi, kart kapatma ve para gönderme gibi kritik write işlemleri scope dışındadır.
MCP tool’ları ilk sürümde read-only kalır. Synthetic veri için tanımlanan
Account/Transaction oluşturma API endpointleri bu kritik işlem yasağına dahil değildir.

==================================================
16. DATABASE
==================================================

PostgreSQL kullan.

EF Core + Npgsql üzerinden access et. SQLite kullanma.

Şunlara dikkat et:

- proper relationships
- migrations
- indexes
- AsNoTracking read query'lerinde gerektiğinde
- async database calls
- cancellation token
- query efficiency
- unnecessary materialization
- N+1
- pagination gerektiğinde
- transaction boundaries

Synthetic transaction generator/seed aşamasında Bogus kullan.
Sabit seed ve referans tarihle tekrar üretilebilir veri oluştur. Dataset tamamen
random olmamalı: aylık harcama artışı/azalışı, recurring payments, merchant ve
category bazlı harcamalar ile edge case’ler için kontrollü pattern’ler içermeli.

Analytics test edebilmek için yeterli miktarda synthetic veri oluştur.

==================================================
17. TESTING
==================================================

Minimum:

Backend testleri xUnit ve ASP.NET integration tests kullanır.
iOS tarafında gerektiğinde minimum ViewModel / API service testleri eklenir;
UI testleri scope’u gereksiz büyütmemeli.

Unit tests:

- Spending analytics
- Period comparison
- Category breakdown
- Merchant spending
- Recurring payment detection

Integration tests:

- Account API
- Transaction API
- PostgreSQL persistence
- Analytics endpoints

AI tests:

- intent parsing
- structured output
- tool selection
- tool arguments
- grounded answer

MCP tests:

- tool discovery
- valid request
- invalid arguments
- backend failure
- empty results

==================================================
18. AI EVALUATION DATASET
==================================================

Minimum 50 synthetic user question oluştur.

Farklı kategoriler:

Spending:

"Bu ay ne kadar harcadım?"

Period comparison:

"Geçen aya göre harcamam arttı mı?"

Merchant:

"Starbucks'a son üç ayda ne kadar verdim?"

Category:

"Market harcamalarım ne kadar?"

Recurring:

"Düzenli ödemelerimi bul."

Transaction explanation:

"Bu işlem ne olabilir?"

Edge cases:

- transaction yok
- merchant bulunamadı
- ambiguous date
- ambiguous merchant
- invalid account
- unknown category

Ölçülebilecek metrikler:

- correct tool selection
- correct arguments
- financial correctness
- grounded response
- hallucination
- latency
- failure rate

==================================================
19. OBSERVABILITY
==================================================

OpenTelemetry kullan.

Mümkün olduğunca:

HTTP Request

→ ASP.NET API

→ AI Service

→ Qwen

→ MCP

→ .NET service

→ PostgreSQL

→ MCP

→ Qwen

→ ASP.NET response

flow'unun izlenebilir olmasını sağla.

Logs, traces ve metrics arasında ayrım yap.

Correlation/trace ID kullan.

==================================================
20. DOCKER
==================================================

Final sistem mümkün olduğunca Docker Compose ile ayağa kalkmalı.

Container adayları:

- ASP.NET API
- PostgreSQL
- FastAPI AI Service
- MCP Server

Ollama host makinede veya uygun bir local configuration'da çalışabilir.

Ama bütün sistemi gereksiz Docker complexity ile bozma.

==================================================
21. GÜNCEL 7 HAFTALIK YOL HARİTASI
==================================================

Ana roadmap ve iş takibi todo.md dosyasındadır. Eski haftalık sıralama ve
taslaklar yalnızca tarihsel referans olarak kabul edilir; yeni işler todo.md'ye eklenir.
Haftalık hedefler mevcut tamamlanma durumu değildir.

WEEK 1: Database-first; PostgreSQL + EF Core + altı tablo + kontrollü synthetic veri + en az iki yıllık gerçek market verisi.
WEEK 2: Controller-based API + deterministik analytics/opportunity-cost + SwiftUI iletişimi.
WEEK 3: Local Qwen + LangChain + structured output + backend sonucunu açıklama.
WEEK 4: MCP + read-only tool calling.
WEEK 5: Banking AI features + SwiftUI completion.
WEEK 6: Reliability + security + evaluation.
WEEK 7: Docker + observability + performance + final demo.

Önceki katman düzgün çalışmadan bir sonraki haftanın teknolojilerine geçme.

==================================================
22. FINAL DEMO REQUIREMENTS
==================================================

En az şu sorular çalışmalı:

1.
"Bu ay ne kadar harcadım?"

2.
"Geçen aya göre neden daha fazla harcadım?"

3.
"Son üç ayda Starbucks'a ne kadar harcadım?"

4.
"Düzenli ödemelerimi göster."

5.
"En çok hangi kategoride para harcıyorum?"

6.
"Bu işlem ne olabilir?"

7.
"Son altı aydaki ulaşım harcamalarımı karşılaştır."

==================================================
23. ÇALIŞMA ŞEKLİ — KÜÇÜK ADIMLAR VE KULLANICI REVIEW'Ü
==================================================

- Türkçe, kısa ve anlaşılır yaz. Kullanıcı ayrıntı istemedikçe uzun mimari
  açıklamalar ve bütün proje çıktıları üretme. Teknik terimi kısa açıkla.
- Kullanıcı kodu inceleyerek öğrenir. Her tur tek seçilmiş küçük adımı ele alır.
- Önce mevcut kaynak kodu ve kuralları incele; eski completed ifadelerine güvenme.
- Koddan önce ne yapılacağını, nedenini, değişecek dosyaları ve etkisini kısaca söyle.
- Entity, enum, configuration, DbContext kaydı, migration, seed ve import ayrı
  inceleme adımlarıdır. Kullanıcı açıkça birlikte isterse o kapsam uygulanabilir.
- Yalnızca entity istenirse kendiliğinden tablo/migration/seed ekleme.
- Kullanıcı “pgAdmin'e/tabloya yansıt” derse gerekli EF eşlemesi, DbContext,
  migration, uygulama ve DB kontrollerini tamamla; seed talebini varsayma.
- “Prompt yaz” talebi prompt üretmektir; implementation başlatmaz.
- Yetkilendirilmiş adımın rutin uygulama, hata düzeltme ve kontrollerinde tekrar
  izin isteme. Açıkça önce tasarım/onay istenirse kod yazmadan o noktada dur.
- Adım sonunda dosyaların görevini, önemli kod seçimlerini ve test sonucunu açıkla.
- Her görev sonunda kullanıcıya hangi dosyada ne değiştiğini, neden bu seçimin
  yapıldığını ve davranışa etkisini kısa ve somut örneklerle anlat. Yapılan
  doğrulamayı ve kalan eksikleri belirt; doküman kararı ile uygulanmış kodu ayır.
- required/init/nullability ile runtime validation ve DB constraint'lerini ayır.
  C# sınıfı oluşturmak tablo oluşturmaz; build geçmesi DB doğrulaması değildir.
- Üretilen kodu doğru varsayma; invalid input, null, concurrency, naming,
  shared state ve gereksiz abstraction açısından incele. Kalan sınırları söyle.
- Adım bitince dur. Kullanıcının review'ünü ve sonraki mesajını bekle;
  başka modüle veya todo maddesine kendiliğinden geçme.
- todo.md'de yalnızca gerçekten tamamlanan ve doğrulanan işi işaretle.
  Liste oluşturmak bütün maddeleri uygulama yetkisi değildir.
- Eski dosyaları, migration'ları veya DB'yi kendiliğinden silme/sıfırlama.

==================================================
24. IMPORTANT ENGINEERING RULES
==================================================

- YAGNI ve KISS uygula.
- Gereksiz repository pattern, CQRS veya microservice ekleme.
- Gereksiz abstraction oluşturma.
- Gereksiz design pattern kullanma.
- Sırf "enterprise" görünmek için complexity ekleme.
- Controller'a business logic koyma.
- LLM'ye business logic verme.
- LLM'ye DB access verme.
- Model output'una güvenme.
- Database calculations'ı modele yaptırma.
- Read-only MCP tools ile başla.
- Async I/O kullan.
- CancellationToken gerektiğinde kullan.
- Secrets hard-code etme.
- DTO/entity ayrımını koru.
- HTTP semantics'i doğru kullan.
- Test edilebilir kod üret.
- Logging'de sensitive data saklama.
- Her feature mümkün olduğunca küçük ve review edilebilir değişikliklerle eklensin.

==================================================
25. PROJECT STATE MANAGEMENT
==================================================

Her önemli geliştirme sonunda mevcut proje durumunu takip et.

Şunları güncel tut:

COMPLETED
- bitmiş feature'lar
- Belirli tarih ve aralık CHAT-08, PR2 kalıplar + akış (23 Ekim 2026, dal `chat-08-dates-read`; docs/local-ai.md "Tarihli sorular"):
  `dates.read_dates` (modelsiz; tek gün, aralık, ay, tam `gg.aa.yyyy`; yıl yoksa en yakın geçmiş; geçersiz/okunamayan ayrı), araç seçici tarih
  yerine "bu ay" görür (K10-A) ve dönem kodla `Custom` + tarihlerle ezilir, cevabın başına kodla `Dönem: …` satırı (K16-A), tarihsiz araçta
  sabit cevap (K11-B), araçsız bekleyen dönem sorusu (K12-C) ve takipte aralığın korunması (K7-A). Testler: Python 747 modelsiz (+90).
  Canlı (gerçek MCP + model, Murat): 1-5 Eylül 5.116,50 TL, 6-10 Eylül 3.759,90 TL, Eylül 53.388,39 TL.
- Belirli tarih ve aralık CHAT-08, PR3 göreli tarih yorumlayıcısı (23 Ekim 2026, dal `chat-08-dates-relative`; docs/local-ai.md "İkinci adım"):
  kodun okuyamadığı tarih ifadesi ("geçen salı", "3 gün önce", "geçen yıl", "Eylül'ün ilk haftası") `LangChainDateInterpreter`'a gider
  (`date-v4`, JSON şemalı, kodun hazırladığı takvim tablosu, ≤ 5 sn); aralık `dates.check_range` ile denetlenir ve kodun okuduğu tarih gibi
  çalışır. Ölçüm `ai/model_check/date_eval.py` **10/10** (v1 2/10 → v2 8/10 → v3 9/10 → v4 10/10). Canlıda Murat'ın rakamları SQL ile aynı.
  CHAT-08 tamam (PR1 #37, PR2 #38, PR3 #41; #39 yanlışlıkla ölü dala girdi, #41 ile yeniden açıldı). Testler: Python 781 modelsiz.
  Birleşmiş `main`'de (`861fb4f`, 23 Ekim 2026) araç seçim ölçümü **68/71**, ortalama 2,0 sn; eski vakalarda bozulan yok, kalan hatalar
  bilinen S40, S43, R03. Göreli tarih ölçümü (`date_eval.py`) `main`'de yeniden çalıştırılmadı (dalda 10/10).

IN PROGRESS
- üzerinde çalışılan feature

NEXT
- sıradaki mantıklı adım

KNOWN ISSUES
- bilinen problemler

TECHNICAL DEBT
- bilerek ertelenen şeyler

TEST STATUS
- testlerin durumu


Ben yeni mesaj attığımda projeyi sıfırdan başlatma.

Mevcut state üzerinden devam et.

==================================================
26. DATABASE FOUNDATION GEÇİŞ KURALI
==================================================

Mevcut projeyi baştan başlatma. Kullanıcının seçtiği todo maddesiyle devam et.

10 Ekim 2026 kullanıcı kararı: foundation iki ayrı kola bölündü.

1. Banking foundation — KABUL EDİLDİ (10 Ekim 2026).
   Altı tablo, ilişkiler, temel kısıtlar, tekrar çalıştırılabilir banking seed'i
   ve atomik bakiye tutarlılığı kanıtlandı (22/22 test). API, deterministik
   analytics, AI/MCP ve SwiftUI işleri bu temel üzerinde ilerleyebilir.
2. Market data — AYRI VE ENGELLEMEYEN KOL.
   USDTRY/EURTRY/gram altın kaynağı, alış/satış modeli (MP-06–08) ve import
   (SRC/IMP) paralel yürür. Opportunity-cost (OC) ve ona bağlı MCP/demo maddeleri
   yalnızca bu kol tamamlanınca başlar. Gerçek fiyat kapsamı kanıtlanmadan
   opportunity-cost özelliği tamamlandı sayılmaz; fiyat uydurulmaz.

Banking verisine dayanan analytics, API, AI ve MCP market verisini beklemez.
Katman sırası korunur: deterministik analytics doğrulanmadan onu açıklayan
AI/MCP tool'u yazılmaz. Gelecekteki mimari klasörlerini ihtiyaç oluşmadan oluşturma.

==================================================
27. GÜNCEL STACK VE MOBİL KARARLARI — 7 EKİM 2026
==================================================

Aşağıdaki güncelleme kullanıcı tarafından sağlanmıştır ve teknik kararlarda
önceliklidir. Önceki bölümlerdeki uyumlu proje hedefleri geçerliliğini korur.

GÜNCEL STACK KARARLARI

1. MOBİL UI
- Sadece Swift kullanılacak.
- UI framework: SwiftUI.
- Architecture: MVVM.
- Networking: URLSession.
- JSON modelleme: Codable.
- Navigation: NavigationStack.
- State management için mümkün olduğunca native SwiftUI mekanizmaları kullanılacak.
- Gereksiz üçüncü parti iOS library eklenmeyecek.
- UIKit yalnızca gerçekten gerekli bir durum çıkarsa kullanılabilir, varsayılan tercih değildir.
- Flutter, React Native, Angular veya başka frontend framework kullanılmayacak.

2. BACKEND
- C#
- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- Controller-based API kullanılacak.
- Business logic controller içine yazılmayacak.
- Dependency Injection kullanılacak.
- Async I/O ve CancellationToken uygun yerlerde kullanılacak.

3. DATABASE
- PostgreSQL kullanılacak.
- EF Core + Npgsql üzerinden bağlanılacak.
- SQLite kullanılmayacak.
- SQL Server ve Oracle yalnızca karşılaştırmalı bilgi amacıyla konuşulabilir, ana proje DB’si PostgreSQL olacak.
- Dummy/synthetic data kullanılacak.
- Gerçek müşteri veya banka verisi kullanılmayacak.

4. DUMMY DATA
- .NET tarafında Bogus gibi ücretsiz bir fake data library kullanılabilir.
- Transaction dataset’i tamamen random olmamalı.
- Analytics feature’larını test edebilmek için kontrollü pattern’ler içermeli:
  - aylık harcama artışı/azalışı
  - recurring payments
  - merchant bazlı harcamalar
  - category bazlı harcamalar
  - edge case’ler

5. AI SERVICE
- Python
- FastAPI
- LangChain
- Ollama
- Local Qwen modeli
- Hiçbir ücretli LLM API kullanılmayacak.
- OpenAI API, Anthropic API, ücretli Gemini API veya ücretli LangSmith Gateway kullanılmayacak.
- Fine-tuning yapılmayacak.
- Model tool calling ve structured output için kullanılacak.

6. LANGCHAIN
LangChain yalnızca AI orchestration için kullanılacak:
- model invocation
- prompt handling
- structured output
- tool calling
- MCP entegrasyonu
- gerekirse agent logic

Basit bir invoke yeterliyse agent oluşturma.
Gereksiz LangChain abstraction kullanma.

7. MCP
- .NET tarafında BankingAI.McpServer olacak.
- MCP tool’ları ASP.NET/Application Service katmanındaki gerçek backend fonksiyonlarını expose edecek.
- İlk MCP tool’ları read-only olacak.
- Para transferi, kart kapatma, para gönderme gibi kritik write işlemleri scope dışında tutulacak.

Örnek MCP tool’ları:
- get_accounts
- get_transactions
- search_transactions
- get_spending_summary
- compare_spending_periods
- get_category_breakdown
- get_merchant_breakdown
- find_recurring_payments
- get_highest_transactions

8. SOURCE OF TRUTH KURALI
LLM finansal source of truth değildir.

LLM:
- kullanıcı isteğini anlar
- intent çıkarır
- uygun tool’u seçer
- tool argument üretir
- backend sonucunu doğal dilde açıklar

.NET backend:
- hesaplama yapar
- filtreleme yapar
- aggregation yapar
- authorization yapar
- validation yapar
- business rules uygular
- database’den gerçek sonucu getirir

LLM’ye:
- arbitrary SQL çalıştırma
- database’e direkt erişme
- finansal rakam hesaplama
- authorization kararı verme
- business rule bypass etme

yetkisi verilmez.

9. MOBİL EKRANLAR
Minimum demo ekranları:
- Home
- Accounts
- Transactions
- Spending Analytics
- AI Assistant

Opsiyonel:
- Transaction Detail
- Recurring Payments

SwiftUI tarafı backend feature’larını gösterecek kadar güçlü ama sade tutulmalı.
UI projenin ana odağı değildir.

10. SWIFT NETWORKING AKIŞI
SwiftUI
↓
ViewModel
↓
API Service
↓
URLSession
↓
ASP.NET Core

Örnek yapı:
- HomeView
- HomeViewModel
- BankingApiService
- AccountDTO
- TransactionDTO
- AnalyticsDTO
- AIQueryRequest / AIQueryResponse

11. FİNAL SİSTEM MİMARİSİ

Swift + SwiftUI
        |
        v
ASP.NET Core Web API
        |
        +----------------------+
        |                      |
        v                      v
Application Services      AI Integration
        |                      |
        v                      v
EF Core               Python FastAPI AI Service
        |                      |
        v                      v
PostgreSQL               LangChain
                               |
                               v
                             Ollama
                               |
                               v
                          Local Qwen
                               |
                               v
                           MCP Client
                               |
                               v
                      .NET MCP Server
                               |
                               v
                      Application Services
                               |
                               v
                          PostgreSQL

12. TESTING
Backend:
- xUnit
- integration tests

AI:
- structured output tests
- tool selection tests
- invalid argument tests
- hallucination checks
- evaluation dataset

iOS:
- minimum düzeyde ViewModel / API service testleri gerekirse eklenebilir
- UI testleri scope’u gereksiz büyütmemeli

13. OBSERVABILITY
- OpenTelemetry
- logs
- traces
- metrics
- correlation/trace ID

14. INFRASTRUCTURE
- Docker
- Docker Compose
- ASP.NET API
- PostgreSQL
- FastAPI AI Service
- MCP Server

Ollama host makinede çalışabilir.

15. GENEL ENGINEERING KURALLARI
- YAGNI
- KISS
- gereksiz abstraction yok
- gereksiz repository pattern yok
- gereksiz CQRS yok
- gereksiz microservice yok
- controller’da business logic yok
- secrets hard-code edilmez
- DTO/entity ayrımı korunur
- async I/O tercih edilir
- query performance kontrol edilir
- N+1 kontrol edilir
- validation zorunludur
- LLM output’una güvenilmez
- MCP tool input’ları backend’de tekrar validate edilir
- gerçek banka verisi kullanılmaz

16. PROJE CHAT DAVRANIŞI
Bundan sonra proje ile ilgili bir şey istediğimde:
- önce mevcut architecture’ı dikkate al
- stack’i değiştirme
- Flutter önermeme
- Angular önermeme
- SQLite önermeme
- OpenAI API önermeme
- gereksiz teknoloji eklememe
- koddan önce kısa plan çıkar
- hangi dosyaların değişeceğini söyle
- sonra implementation ver
- implementation sonrası olası bug, security, performance ve test noktalarını belirt

Bu güncel kuralları projenin yeni source of truth’u olarak kabul et.

==================================================
28. GÜNCEL DATABASE VE HESAPLAMA KARARLARI — 8 EKİM 2026
==================================================

- Aktif DB bankingai_foundation; bankingai önceki uygulamadan kalmıştır.
  Hedef bağlantıyı kontrol et; eski DB'yi kendiliğinden silme veya ona migration uygulama.
- BankingAI.Data kütüphanedir; komut satırı BankingAI.Cli, HTTP API BankingAI.Api'dir
  (API-01a/01b). API DbContext'i DI ile istek başına alır ve açılışta migration çalıştırmaz (API-02).
- Şema EF Core migrations ile yönetilir. Entity → configuration → DbContext →
  migration → PostgreSQL akışı açıklanır; pgAdmin yalnızca görüntüleme/yönetim aracıdır.
- Incremental migration yaklaşımı kullanılır. Her schema değişikliği için yeni
  migration eklenir; mevcut migration geçmişi korunur. Migration içeriği
  uygulanmadan incelenir. Uygulanmış migration dosyaları geriye dönük değiştirilmez
  ve veritabanı resetlenerek tek başlangıç migration'ına dönülmez.
- AccountNumber unique dummy identifier; gerçek IBAN/banka numarası kullanılmaz.
- Accounts ve Transactions CurrencyCode: TRY, USD, EUR, GBP; TL kullanılmaz.
  Mevcut Customer.PreferredCurrency farklı bir tercih alanıdır ve TRY/USD/EUR kabul eder.
- Transaction para birimi hesabıyla eşleşir. Mevcut birleşik FK
  (AccountId, CurrencyCode) → Accounts(Id, CurrencyCode) bunu DB'de korur.
- Amount pozitif decimal, Direction smallint enum: Debit=1, Credit=2.
  Debit tek başına alışveriş olduğunu kanıtlamaz; ekonomik tür Kind alanındadır.
- VAL-05 (KIND-05 ile güncellendi, 10 Ekim 2026): Kind = Purchase/Sale/Transfer/
  Salary/OpeningFunding. Harcama OccurredAt döneminde tek para birimindeki
  Kind = Purchase toplamıdır; bütün Debit'leri toplamak transferleri yanlışlıkla
  harcama sayar. Credit çıkarılmaz, kategorisiz Purchase kaybolmaz. Transfer ve
  iç dükkân ödemeleri PairId'li iki bacakla yazılır; Sale yalnız Business hesaba.
  Nakit, döviz/farklı para birimli transfer, yatırım, kredi/borç, komisyon/faiz,
  refund/iptal ve bekleyen hareketler üretilmez. Refund/net harcama ayrı model
  kararı gerektirir. Ayrıntılar docs/input-validation.md içinde; analytics ANA adımlarında.
- Balance ve Amount numeric(18,2). Fazla basamaklı girdiyi sessiz yuvarlamama
  politikası yazma yolunda doğrulanmalıdır; numeric sütun tek başına bunu sağlamaz.
- VAL-02: Amount/Balance iki ondalıkla kayıpsız temsil edilebilmeli. 12.3400
  kabul edilir, 12.345 reddedilir; otomatik yuvarlama/kesme yapılmaz. Temsil
  sınırı mutlak 9999999999999999.99; Amount pozitiftir. Kredili hesap yoktur,
  negatif bakiye reddedilir. C# ve DB kontrolleri VAL-07 ile uygulanmıştır;
  bakiye taşmasında atomik yazma BAL adımlarındadır.
- Yeni bakiye 0; açılış fonlaması Credit işlemle temsil edilir. Insert ve atomik bakiye
  değişimi aynı DB transaction'ındadır; bakiye DB içinde Balance + delta olarak güncellenir,
  çünkü yalnız transaction açmak lost update'i engellemez (BAL-01–04 ve KIND-03; API-11'den beri
  Data/Writing/TransactionWriter ve PairedTransactionWriter). API'den işlem yazımı (API-11) aynı yolu kullanır;
  sınır dışı bakiye BalanceLimitException ile bildirilir (API'de 409).
- Negatif bakiye politikası kesinleşti: kredili hesap yok, negatif bakiye ve
  yetersiz bakiye reddedilir. Future/backdated işlem kontrolleri uygulanmıştır.
- Olay zamanları UTC DateTimeOffset/timestamptz. Npgsql yazımı offset 0;
  timestamptz saat dilimi adını/orijinal offset'i saklamaz. Market günü DateOnly/date.
- VAL-03: açık offset girişte UTC'ye çevrilir; saat dilimi belirsiz, default
  ve MinValue/MaxValue/sonsuz tarihler reddedilir. Backend yazım başında tek
  referenceUtc alır; ileri OccurredAt reddedilir (tolerans 0), geçmiş import
  kabul edilir. OccurredAt olay zamanı, CreatedAt backend kayıt zamanıdır;
  yeni kayıtta OccurredAt <= CreatedAt, tekrar importta CreatedAt korunur.
  PriceDate kaynak günüdür; ilk kapsamda Türkiye takviminde gelecekte olamaz.
  Güvenilen synthetic seed sabit geçmiş UTC referansı kullanabilir. Ayrıntılar
  docs/input-validation.md içinde; kod uygulaması VAL-07 ile yapılmıştır.
- Kategori lookup listesi: FOOD, GROCERY, CLOTHING, TRANSPORTATION, ENTERTAINMENT,
  SUBSCRIPTION, TRAVEL, FUEL, BILLS, HEALTH, OTHER. Code unique.
- CategoryId null kategorize edilmemiştir; OTHER bunun yerine otomatik atanmaz.
- RawDescription korunur; NormalizedMerchant nullable string olarak Transaction'dadır.
- Hybrid kategorilendirme: önce deterministik kural, gerekirse Qwen structured output.
  LLM kategori yaratamaz; backend mevcut code'u doğrular ve ID'ye eşler.
- CategorizationSource: Rule=1, Llm=2, Manual=3; nullable. CategoryConfidence
  nullable numeric(5,4), 0–1. CategorizedAt nullable UTC. Alanlar birlikte tutarlı
  güncellenmelidir; confidence eşiği application kararıdır, DB business logic'i değildir.
- VAL-04: kategorisiz işlemde CategoryId/source/confidence/CategorizedAt tümü
  null olur. Rule ve Manual atamalarında kategori+tarih zorunlu, confidence
  null; Llm atamasında kategori+tarih+0–1 aralığında confidence zorunludur.
  Confidence numeric(5,4) ile kayıpsız temsil edilmeli; sessiz yuvarlama yok.
  Atama/kaldırma dört alanı birlikte günceller; düşük skorlu/başarısız öneri
  mevcut kategoriyi silmez ve otomatik OTHER atanmaz. Manuel atama otomatik
  akışla kendiliğinden ezilmez. CategorizedAt backend zamanıdır ve CreatedAt'tan
  önce olamaz. Eşik CAT-05'te ölçümle seçilir, DB'ye yazılmaz. Ayrıntılar
  docs/input-validation.md içinde; C# ve DB uygulaması VAL-07 ile yapılmıştır.
- Kategorilendirme ground truth'u tests/data fixture'ında tutulur;
  Transactions'a ExpectedCategoryId gibi test-only alanlar eklenmez.
- Banking seed: Bogus + sabit seed/tarih + kontrollü pattern'ler. Maaş, abonelik,
  merchant tekrarları ve kategori/dönem farkları bağımsız beklenen sonuçlarla doğrulanır.
- Seed sırası: Categories → MarketAssets → Customers → Accounts → Transactions.
  Ardından historical prices import edilir. Tekrar/paralel çalışma duplicate
  kayıt veya ikinci bakiye etkisi oluşturmamalıdır.
- Market seed: USDTRY (USD), EURTRY (EUR), GRAM_GOLD_TRY (GRAM); quote TRY.
  AssetType Currency/Commodity; Symbol unique. Seed ID'leri sabit short.
- DailyMarketPrice: bigint identity Id, short FK, date PriceDate,
  numeric(20,8) pozitif sonlu Price, nullable varchar(100) Source.
  (MarketAssetId, PriceDate) unique. OHLC/volume eklenmez.
- En az iki yıllık USDTRY/EURTRY/gram altın günlük verisi ücretsiz ve güvenilir
  kaynaktan gelir. Kaynak, seri, birim, saflık, fiyat tanımı ve erişim koşulları
  implementation öncesi açıklanır. İki yıllık altın erişimi doğrulanmadan tamamlandı denmez.
- Fiyat uydurma, random boşluk doldurma ve farklı serileri sessiz karıştırma yok.
  Import duplicate-safe/idempotent/retry edilebilir; gerçek import Source'u doldurur.
  Revizyonlar ve eksik günler raporlanır. Önce manuel import; scheduler daha sonra.
- 10 Ekim 2026 kullanıcı kararı: seçilen kategori/dönemdeki TRY Debit toplamı,
  dönem başlangıcında tek seferde yatırılmış varsayılır. Her harcama tarihinde
  ayrı alım yapılmaz. Harcama toplamı backend tarafından hesaplanır.
- Kullanıcıya anlatım müşteri yönündedir: geçmiş alım fiyatı ve bugünkü satış
  fiyatı. Kaynak alan adları bu anlatımla karıştırılmaz. Bugün değerlemesi için
  geçmiş sağlayıcı satış + güncel sağlayıcı alış yeterlidir; geçmiş alış
  arşivi yalnızca geçmiş değerleme tarihi desteği istenirse gerekir.
- Müşteri başlangıçta alırken sağlayıcının SATIŞ fiyatı, değerleme tarihinde
  satarken sağlayıcının ALIŞ fiyatı kullanılır. USDTRY, EURTRY ve gram altın/TRY
  aynı kurala tabidir; sağlayıcının isimlendirme yönü ayrıca doğrulanır.
- Birim = totalSpent / başlangıç satış fiyatı; currentValue = birim × değerleme
  alış fiyatı; difference = currentValue - totalSpent. Bu, toplam paranın baştan
  mevcut olduğunu varsayan tarihsel senaryodur; gerçek nakit akışı getirisi değildir.
- Exact gün fiyatı yoksa başlangıç ve değerleme için ayrı ayrı PriceDate <=
  hedef gün koşuluyla en yakın önceki uygun fiyat kullanılır; gelecek fiyat
  geriye uygulanmaz. Önceki fiyat yoksa sonuç eksik veri olarak bildirilir.
- Dönem ve değerleme piyasa günleri Türkiye takvimine göre belirlenir.
  Kullanılan gerçek fiyat tarihleri gösterilir; günlük veri anlık fiyat diye sunulmaz.
- 9 Kasım 2026 kullanıcı kararı (yukarıdaki alış/satış maddelerinin yerine geçer):
  varlık başına günde TEK değer saklanır ve hem başlangıçta hem değerlemede o seri
  kullanılır; makas hesaba girmez. USD/EUR değeri TCMB döviz alış (ForexBuying).
  Gram altın: gold-api ortalama ons × aynı günün USDTRY'si ÷ 31,1034768 (IMP-b; resmî
  TCMB gram altını 30 Ocak 2026'da kesildi, yalnız doğrulamada kullanılır).
  DailyMarketPrice tek Price alanıyla kalır, migration yok. Formül:
  birim = totalSpent / başlangıç günü değeri; currentValue = birim × değerleme günü
  değeri. Cevapta "referans kur; banka işlem fiyatı değildir" yazılır.
- Backend totalSpent, birimler, currentValue, difference, percentageDifference,
  yöntem, kaynak ve kullanılan fiyat tarihlerini üretir. LLM yalnızca açıklar.
  Ara birimler erken yuvarlanmaz; harcama sıfırsa yüzde fark null olur.
- Alış/satış makası iki farklı fiyatla hesaba katılır. Stale fiyat sınırı ve
  ek ücret/vergi/enflasyon kapsamı hesaplama öncesi netleşir.
- Altın zorunlu varsayılan değildir. Aynı kapsamda USD/EUR/altın tarihsel olarak
  karşılaştırılabilir; en yüksek geçmiş değer gelecek kazanç garantisi değildir.
- Karşılaştırma her sorguya eklenmez. Tetiklenecek sorgular/sunum daha sonra
  kullanıcıyla netleşir; açıkça istenen varlık korunur. Eksik veride kazanan uydurulmaz.

==================================================
29. MEVCUT PROJE DURUMU — 9 KASIM 2026
==================================================

COMPLETED
- iPhone gelir payı kartı (INC-04, 10 Kasım 2026; docs/ios.md "iOS-s"): ana ekranda "Bu ay harcama"nın altında
  "Gelire göre harcama" kartı; tek istek `spending-advice?period=LastMonth`, para birimi başına Temel / İsteğe bağlı / Bilinmiyor
  çubuğu (paylar backend'den, %100'ü aşan kırpılır, metin tam), en fazla 2 öneri satırı (dokununca backend'in sabit cümlesi),
  öneri yoksa sabit sebep, maaş yoksa sebep başlıkta. Model yok (AI kapalıyken de aynı); backend değişmedi. Kararlar K1-A K2-B
  K3-A K4-B K5-B K6-A K7-A. iOS 313 → 338 test durumu; 10 kasıtlı bozmanın hepsi yakalandı.
- Farklı yazılışlara kural düzeltmeleri (CHAT-09 PR2, 9 Kasım 2026; docs/local-ai.md "Kural düzeltmeleri"): tek Türkçe harf katlama
  (metin + kurallar, K11-A), mesajda adı geçen dönem modelinkini ezer (K12-A), zenginlik grubunda senaryo kelimeleriyle tek araç
  (K5-D) ve orada model seçmezse kod seçer (K13-A), aylık birikimde çıplak tutar TL ve para birimi sorusu yok (K6-B), "sınır" ve
  "alayım" kuralları (K14-A, K15-A), açıklayana kodun sorusu ve değer–ad kontrolü (K7-A, K16-A). Açık 68 → 0 (695 ifade); araç seçimi
  90/91, ifade örneklemi 104/105, cevap doğruluğu 23/23. Python 979 → 1066 modelsiz; 21 kasıtlı bozmanın 20'si yakalandı (biri eşdeğer).
- Farklı yazılışlara dayanıklılık ölçümü (CHAT-09 PR1, 9 Kasım 2026; docs/local-ai.md "Farklı yazılışlar"): `ai/model_check/phrasings.py`
  20 niyetin şablonlarından + gerçek ifadelerden + kısa diyaloglardan 663 ifade üretir (Türkçe karaktersiz, büyük harf, küçük harf/noktalamasız,
  konuşma dili; yazım hatası yalnız rapor) ve her birini gerçek `ChatService`'ten geçirir (araç çağrısında durur; eval'de sohbet kopyası yok).
  Modelsiz test (model yerine hep doğru seçen oracle) 595 geçer + 68 gerekçeli bilinen açık (`phrasing_gaps.json`; yeni kırık da düzelen açık
  da testi kırar). Modelli örneklem 89/100, yazım hatası 13/20 (rapor); `tool_selection_eval.py` aynı düzeneğe taşındı, 89/91 (değişmedi).
  Bulunan açıklar PR2'nin iş listesi: Türkçe karaktersiz yazım, aylıkta çıplak tutar (K6-B), "X mı Y mı alayım", "sınır", modelin yanlış
  dönemi (geçen hafta → son 7 gün) kontrolden geçiyor. Python 935 → 979 modelsiz; 14 kasıtlı bozma yakalandı.
- Sohbet gelir payı, tasarruf önerisi ve "ne olurdu" sorularını cevaplıyor (INC-03 PR2, 9 Kasım 2026; docs/local-ai.md "Gelir, öneri ve
  \"ne olurdu\" sohbeti"): `wealth` grubu yalnız kelime kuralıyla (-seydim/-saydım, maaş/gelir); tavsiye/tahmin isteği modelsiz sabit metin;
  gelir/öneri yalnız tam ay (aksi geri soru); aylık birikimde tutar ve bitişik TL mesajdan, TL dışı sabit metin; `fromAdvice` yalnız
  "önerdiğin"/öneri sonrası ve kodla seçilir; öneri cümleleri ve yatırım uyarısı kodla cevabın altında; tavsiye kalıbı bulunursa yalnız kalemler.
  Python 782 → 935 modelsiz, 12 kasıtlı bozma yakalandı; araç seçimi eski vakalarda bozulan yok, yeni 20/20; cevap doğruluğu 22/23 (yeni 5/5).
- Gelir, öneri ve "ne olurdu" MCP araçları (INC-03 PR1, 9 Kasım 2026; docs/mcp.md "INC-03 PR1"): IncomeTools (get_income_share,
  get_spending_advice; yalnız tam ay) ve InvestmentTools (what_if_one_off, what_if_monthly; tutar yalnız TRY ya da fromAdvice);
  15 salt okunur araç. Filtre boolean'ı da denetliyor (SDK "true" metnini sessizce true yapıyordu). AI servisinde wealth grubu var
  ama kural yok: sohbet PR2'ye kadar değişmez. McpServer 156 → 195; 8 kasıtlı bozma yakalandı. PR2 aşağıda.
- "Biriktirseydim" senaryosu (INV-01 PR2, 9 Kasım 2026; docs/analytics.md "INV-01 PR2"): saf WhatIfMath.Monthly (12 aylık alım,
  aynı tek motor) + dalgalanma (fiyatta en kötü/en iyi ay ve tepe→dip düşüş, birikimin en kötü anı) ve
  GET /api/analytics/what-if/monthly. Tutar ya kullanıcıdan (yalnız TRY, ≤ 1.000.000) ya INC-02'nin son tam ay önerisinden
  (fromAdvice=true, backend okur; ikisi birden 400; öneri yoksa NoAdvice). Tek eski/eksik fiyat → o varlıkta rakam yok. Kararlar
  K13-B … K22-A (todo §13). Application 442 → 468, Api 554 → 569; 11 kasıtlı bozmanın hepsi yakalandı. INV-01 tamam; sırada INC-03.
- "Harcamasaydım" senaryosu (INV-01 PR1, 9 Kasım 2026; docs/analytics.md "INV-01 PR1"): Application/Investment/ altında
  MarketPriceReader (en yakın önceki gün, 10 gün sınırı ölçülerek, Stale/Missing), saf WhatIfMath (tek motor, birim tam, sabit
  uyarı metni) ve WhatIfService + GET /api/analytics/what-if/one-off. Yalnız TRY harcama, dönemin ilk günü alım, bugünkü fiyatla
  değer; alış-satış farkı yok (tek günlük değer). OC-01–10 K10-B'ye göre yeniden yazıldı (todo §13). Application 417 → 442,
  Api 545 → 554; 11 kasıtlı bozmanın hepsi yakalandı. Sırada PR2 (aylık birikim + dalgalanma).
- Seçici tasarruf önerisi (INC-02, 9 Kasım 2026; docs/analytics.md "INC-02"): SpendingGroups (C# tablosu: Temel / İsteğe bağlı /
  Bilinmiyor; K1-B Yeme & İçme temel) + saf SpendingAdviceRules + SpendingAdviceService + GET /api/analytics/spending-advice.
  Yalnız isteğe bağlı harcama: toplam > %30 (50/30/20) ya da kategori önceki 6 ayın ≥ 1,5 katı; fark gelirin ≥ %2'si; TL farkına
  göre en fazla 2 (para birimi başına); tasarruf ANA-14 aritmetiği (%20, ortak `SavingsSimulationService.Saving`); metin sabit
  Türkçe şablon; maaş yoksa reason NoIncome. Demo verisinde yalnız Selin öneri alır (Eğlence 2,43 katı, ayda 236,18). Migration
  yok. Application 365 → 417, Api 533 → 545; 12 kasıtlı bozmanın 11'i yakalandı, kaçan biri ölü kod çıktı (silindi).
- Ürün yönü (9 Kasım 2026 kullanıcı kararı, todo 12f): harcama ve yatırım asistanı; ilk seviye K10-B (geçmişe dönük karşılaştırma
  + birikim senaryosu, gelecek tahmini/ürün önerisi yok), sonra INC-03'te sohbet; özellik havuzu INV-02.
- Gelire göre harcama payı (INC-01, 9 Kasım 2026; docs/analytics.md "INC-01"): IncomeShareService + GET
  /api/analytics/income-share. Tek tamamlanmış ay (LastMonth ya da ayın ilk–son günü); taban = o ayla biten 3 tam ayın ortalama
  maaşı (Salary, kişisel hesaplar, maaşsız ay 0); pay tam toplamdan, 2 ondalık; maaş yoksa pay null; kategori tutarları
  CategoryBreakdownService'ten. Application 339 → 365, Api 521 → 533; 9 kasıtlı bozma yakalandı. Sırada INC-02.
- USD/EUR market verisi (IMP-a, 9 Kasım 2026; docs/market-data-sources.md "USD/EUR import"): Data/Market/ altında TCMB bülten
  okuyucu (döviz alış, birim 1, gün kontrolü, DTD yok), yerel ham arşiv (yalnız hafta içi; eksik/reddedilen gün raporlanır,
  doldurulmaz), sıralı indirici (404 = yayın yok, 3 deneme, yanlış gövde kaydedilmez) ve idempotent importer (farklı değer
  çelişki, üzerine yazılmaz). CLI `market download|import tcmb --from --to`. Kullanıcı kararı: günde tek değer, alış/satış yok,
  migration yok. 990 satır; Data 84 → 129 test, 13 kasıtlı bozma yakalandı.
- Gram altın (IMP-b, 9 Kasım 2026; docs/market-data-sources.md "Gram altın import"): TCMB'nin saatlik gram altını 30 Ocak
  2026'da kesildiği için iki yılın tamamı tek yöntemle türetilir: gold-api ortalama ons × TCMB USDTRY ÷ 31,1034768, 8 ondalık
  (K1-B). Yalnız USDTRY günleri (K3-A), migration yok (K2-A). Sıçrama kontrolü ons üzerinde: >%10 yazılır + dikkat, >%25
  yazılmaz + şüpheli (K6). Resmî TCMB değerinden ortalama %0,53 sapma (21 gün). CLI `market download goldapi`,
  `market import gold`. 495 satır; Data 129 → 185 test, 15 kasıtlı bozma yakalandı.
- IOS-05 hesap detayı ve tarih filtresi (23 Ekim 2026; docs/ios.md "iOS-g"): karttan AccountDetailView, başlangıç verisi + hesap
  yenileme; güncel bakiye dönemden bağımsız. Hesaba ait imleçli liste (20), AllTime varsayılan; beş hazır dönem ve özel aralık,
  alt panelde Uygula/Vazgeç. Başarıda liste/dönem/imleç birlikte değişir, iptal/geç cevap nesil kontrolü, hata eski sonucu korur;
  yenileme tekrarı ilk sayfadan. ListLoader/ListState hesaplar ve son işlemler için, TransactionRows iki sayfalı ekran için ortak.
  ProblemBody.errors + APIError.validation, 404/401 ve boş durum ayrımı. Backend/migration değişmedi; filtre yalnız hesap detayında.
  Kararlar K1-A K2-C K3-A K4-B K5-B K6-B K7-A K8-B K9-B K10-B; 151 iOS test durumu (+26), mevcut testler değişmeden yeşil.
  Gerçek API kapalı: sahte ağla test; canlı demo kontrolü bekler. İşlem detayı IOS-06.
- Proje yapısı (API-01a/01b, API-02): BankingAI.Data kütüphanesi, BankingAI.Cli
  (migrate/show/seed/users), BankingAI.Application (servisler), BankingAI.Api (controller
  tabanlı, DI ile istek başına DbContext, açılışta migration yok) ve üç test projesi; hepsi
  BankingAI.slnx içinde.
- Veritabanı: altı domain tablosu + CustomerCredentials; 12 artımlı migration (son ikisi
  AddCustomerCredentials, AddAccountNumberSequence). Kind/PairId/AccountType kuralları
  C# + DB CHECK ile korunur.
- Dört katmanlı doğrulama (InputRules → EntityValidation → SaveChanges → DB kısıtı) ve atomik
  bakiye yazımı (Writing/TransactionWriter, PairedTransactionWriter; küçük AccountId önce;
  sınır dışı bakiye BalanceLimitException).
- Demo veri: 4 Customer (Ada, Deniz, Ege + hesapsız Demo Customer), 7 Account (1 Business
  dükkân), 11 kategori, 116 Transaction (44 Credit, 72 Debit, 9 çift), 3 MarketAsset;
  DailyMarketPrices 1.485 satır (USDTRY + EURTRY TCMB döviz alış, GRAM_GOLD_TRY türetilmiş; her biri 495 gün,
  2024-11-11 … 2026-11-06; IMP-a/IMP-b, 9 Kasım 2026).
- Deterministik analytics (ANA-01–08, REC-01–03): PeriodResolver, CustomerScope/PurchaseScope,
  özet, kategori, merchant (MerchantMatcher), arama, en yüksek işlemler, karşılaştırma +
  aylık seri, düzenli ödemeler (docs/analytics.md).
- Güvenlik (SEC-01a/01b): CustomerCredentials + PasswordHasher, CLI users set-password,
  POST /api/auth/login → HS256 JWT 60 dk, IP başına dakikada 5 deneme, FallbackPolicy ile
  varsayılan token zorunlu, GET /api/me (docs/security.md).
- API (API-03–10): AccountDto/CreateAccountRequest, IAccountReadService/IAccountWriteService,
  GET/POST /api/accounts, GET /api/accounts/{id}, işlem sözleşmesi (TransactionItem +
  CreateTransactionRequest), GET /api/accounts/{id}/transactions (sayfalı),
  GET /api/transactions/{id} (docs/api.md).
- İşlem ekleme (API-11): POST /api/accounts/{accountId}/transactions → 201 + Location; yalnız sentetik
  Purchase/Salary, yön türden, para birimi hesaptan, zaman sunucudan; yetersiz bakiye ve üst sınır 409;
  Business hesap da kabul edilir (kullanıcı kararı 3-B); ITransactionWriteService/TransactionWriteService.
- Ortak hata cevabı (API-13): her hata ProblemDetails + Türkçe başlık + makine kodu (`code`) + traceId; eşleme
  tek yerde (ApiExceptionHandler, UseStatusCodePages, ApiValidation); controller'larda try/catch yok; silinmiş
  müşteri her endpoint'te 401 session_invalid (CustomerNotFoundException); katı enum adları; her ortamda genel 500.
- Tekrar gönderilen istek (API-12): Idempotency-Key (işlemde zorunlu, hesapta isteğe bağlı); kayıt kimliği
  müşteri + tür + anahtardan türetilir (migration yok); aynı istek → aynı 201 + Idempotent-Replayed, farklı → 409
  idempotency_conflict; yazıcılar aynı kimlik / PairId tekrarında WriteOutcome.AlreadyExisted döner.
- Analytics ve arama endpoint'leri (ANA-09, 13–16 Ekim 2026): ortak dönem okuma (PeriodParameters) ve tam adla enum
  okuma (ExactEnum), iş saati (isteğe bağlı BANKINGAI_DEMO_NOW; token gerçek saatte); /api/analytics/ spending-summary,
  category-breakdown, merchant-breakdown, merchant-spending (dönem zorunlu), period-comparison (karşılaştırma zorunlu:
  tam ad ya da dört tarih), monthly-trend (months zorunlu), highest-transactions (limit, includeAllTransactions),
  recurring-payments (dönem parametresi yok, kural 6 ay); GET /api/transactions (bütün hesaplarda filtreli arama:
  kind/direction/categoryCode/onlyUnclassified/merchant/accountId; dönem verilmezse son 3 ay).
- Tutar filtresi (ANA-11, 16 Ekim 2026): arama ve en yüksek işlemlerde dahil minAmount/maxAmount; verilirse currencyCode
  zorunlu (eşik para birimleri arasında karşılaştırılmaz); ortak AmountFilter, InputRules.Currency; API'de tutar katı
  okunur (AmountParameter: ASP.NET "500,50"yi binlik ayırıcıyla 50050 okurdu).
- Yaklaşan ödemeler (ANA-12, 16 Ekim 2026): GET /api/analytics/upcoming-payments; UpcomingPaymentService düzenli ödeme
  sonucundan önümüzdeki 1–31 günü (varsayılan 30) süzer, kendi sorgusu yok; gecikmişler IsOverdue ile listede; tarih ve
  tutar tahmindir (EstimatedDate/EstimatedAmount = son tutar) ve kanıt (Pattern) ile döner.
- Bütçe kontrolü (ANA-13, 16 Ekim 2026): GET /api/analytics/budget-check; bütçe sorudan gelir, saklanmaz; yalnız bu ay,
  toplam/kategori/merchant, para birimi zorunlu; harcanan mevcut analiz servislerinden (rakamlar aynı), tahmin doğrusal
  (LinearDailyAverage, tam orandan). Kategori kodu kontrolü ortak CategoryLookup'ta.
- Tasarruf simülasyonu (ANA-14, 16 Ekim 2026): GET /api/analytics/savings-simulation; son N tam ayın (varsayılan 6,
  harcamasız aylar dahil) ortalaması × oran (1–100), aylık ve yıllık, para birimi başına; aylık seriyi kullanır, tasarruf
  tam toplamdan; yatırım getirisi değil, varsayım Method alanında.
- Kavramsal merchant harcaması (ANA-10, 16 Ekim 2026): GET /api/analytics/merchants (kapalı küme: müşterinin tüm zamanlardaki
  merchant'ları) ve merchant-group-spending (listeden seçilen 1–20 ad; MerchantMatcher.MatchExact ile harf/Türkçe işaret
  toleranslı tam eşleşme, kısmi yok; liste dışı ad 400). Yeni soru tipleri (todo 12b) tamamlandı.
- HTTP testleri (API-14, 16 Ekim 2026): EndpointSweepTests route tablosunu okur; her korumalı endpoint'in örnek isteği
  olmalı (yeni endpoint örneksiz testleri geçemez), token'sız 401 ve var olmayan müşteride session_invalid tarar; null
  gövde, eşzamanlı hesap açma ve veritabanından kalıcılık testleri. Üretim kodu değişmedi.
- OpenAPI sözleşmesi (API-15, 16 Ekim 2026): Api/OpenApi/ApiDocument transformer'ları (enum adları, decimal para, Bearer,
  Problem şeması, gerçek cevap kodları, yalnız JSON); commit'lenmiş docs/openapi/v1.json ile birebir karşılaştırma testi;
  katı sayı okuma (tırnaklı tutar 400); Scalar /scalar (geliştirmede); api-examples.http. API bölümü (todo 11) tamamlandı.
- Local model seçimi (AI-01, 16 Ekim 2026): Ollama 0.40.5 (modeller harici SSD'de); ai/model_check (Python, yalnız standart
  kütüphane): 20 sabit vaka (tool seçimi, structured output, açıklama) + deterministik puanlama, 21 unittest. Varsayılan
  qwen3.5:4b (BANKINGAI_LLM_MODEL), yedek qwen3.5:9b; thinking kapalı, temperature 0; MCP-12/14'te gerçek tool listesiyle
  yeniden ölçülür. Ayrıntı ve gerekçe docs/local-ai.md.
- Ollama bağlantısı (AI-02, 16 Ekim 2026): ai/service/ollama_client.py (standart kütüphane); OllamaSettings.from_env
  (BANKINGAI_LLM_MODEL zorunlu, BANKINGAI_OLLAMA_URL, BANKINGAI_LLM_TIMEOUT_SECONDS varsayılan 30); dört hata türü
  (OllamaUnavailable, ModelNotFound, OllamaTimeout, OllamaBadResponse); check_ready() modeli yüklemeden ≤3 sn; chat() stream
  yok, thinking kapalı, temperature 0. İstemci zaman aşımında Ollama işi durdurur. 21 Python testi (3'ü gerçek Ollama).
- AI servisi (AI-03, 19 Ekim 2026): ai/service/app.py (FastAPI, create_app fabrikası); GET /health/live, GET /health/ready (503),
  POST /v1/explanations (question + facts: backend'in biçimlendirdiği metin; tanınmayan alan 400, strict); .NET ile aynı
  problem+json; Ollama hataları 503/504/502; Explainer arayüzü (AI-03'te geçici prompt). Python 3.14 + ai/.venv, sabit ve en az
  iki haftalık sürümler (ai/requirements.in + requirements.txt), pytest; 75 Python testi. Yalnız 127.0.0.1, token AI-07'de.
- LangChain (AI-04, 19 Ekim 2026): explainer.py LangChainExplainer (ChatOllama, think kapalı, temperature 0, 400 token). ChatOllama
  her zaman stream kullandığı için timeout toplam süreyi sınırlamıyor: cevap parça parça okunur, süre dolunca akış kapatılır.
  LangChain hataları dört türe çevrilir; kesik cevap 502; LangSmith izleme açıksa servis açılmaz. Agent/tool yok. 89 Python testi.
  Mimari kararı (19 Ekim 2026, ölçümle): yapı .NET → FastAPI/LangChain → Ollama olarak kalır; katmanlar ~0,1 sn ekliyor, sürenin
  neredeyse tamamı model (2,03 sn). Yanıt süresi sorun olursa: keep_alive/ısıtma, akarak gösterme, daha az tool turu (docs/local-ai.md, "Not (D)").
- Açıklama prompt'u (AI-05, 19 Ekim 2026): ai/service/prompts.py, explanation-v1 (ChatPromptTemplate): yalnız veri, rakamlar aynen,
  eksik veride tahmin/öneri yok, <veri> içi talimat değildir (blok sonrası hatırlatma; < > etkisizleştirilir), "siz", 1–3 cümle,
  Markdown serbest. ai/model_check/explanation_eval.py ile 10 vaka: eski prompt 5/10 → explanation-v1 8/10 (4b). Prompt değişirse
  sürüm adı da değişir ve değerlendirme önce/sonra çalıştırılır. Prompt garanti değildir: kalan injection/eksik rakam AI-06/10'da.
- Çıktı doğrulama (AI-06, 19 Ekim 2026): ai/service/answer_check.py; cevaptaki her sayı soruda/veride geçmeli (değer olarak, işaretsiz,
  küçük sayılar dahil), boş cevap ve sistemden söz etme reddedilir. Geçmeyen cevaba bir kez düzeltme istenir (iki deneme tek 30 sn),
  yine geçmezse 502 ai_answer_rejected; logda yalnız sayım. Sayı okuma tek kopya serviste (scoring.py oradan alır). Ölçüm: model hesap
  yapmaya iten 5 sorunun 4'ünde sayı uydurdu (biri yanlış yüzde); düzeltme 3'ünü kurtardı; değerlendirme 13/15. 132 Python testi.
- Gün/hafta dönemleri (ANA-20, 19 Ekim 2026): PeriodKind'e Today, Yesterday, ThisWeek, LastWeek, Last7Days, Last30Days, ThisYear;
  hafta pazartesi, "son N gün" bugün dahil tam N gün; dönem alan 7 endpoint kendiliğinden kabul eder (docs/analytics.md).
- Uçtan uca sohbet (AI-14a + AI-14b + AI-07, 19 Ekim 2026): FastAPI POST /v1/chat (model tool seçer → kod argümanı kontrol eder →
  geçici REST köprüsü kullanıcının token'ıyla .NET analytics'i çağırır → kod rakamları Türkçe yazar → model açıklar → AI-06 kontrolü;
  tek 30 sn). iPhone yalnız .NET'e konuşur: POST /api/assistant/messages (mesaj 1–500, müşteri kontrolü ICustomerCheck, müşteri başına
  dakikada 10 mesaj SEC-06, aynı Authorization başlığıyla AI servisine) → AssistantReply {type, answer, toolCalls, facts};
  GET /api/assistant/status her zaman 200 {available}. AI hataları üç koda: assistant_unavailable 503, assistant_timeout 504,
  assistant_failed 502. AssistantClient kendi süresini tutar (BANKINGAI_AI_TIMEOUT_SECONDS, varsayılan 35), adres
  BANKINGAI_AI_URL (varsayılan http://127.0.0.1:8000/). Mesaj ve token loglanmaz. UseRateLimiter kimlik doğrulamadan sonra.
  Testler: Application 306, Data 63, Api 445, Python 220 (docs/api.md "Sohbet asistanı", docs/local-ai.md).
- Read-only MCP server — MCP-a (MCP-01/02 + MCP-05/07/25 server tarafı, 19 Ekim 2026, docs/mcp.md): BankingAI.McpServer ayrı süreç,
  yalnız 127.0.0.1:5249/mcp, ModelContextProtocol.AspNetCore 2.2.0, durumsuz Streamable HTTP, aynı JWT zorunlu. Üç salt okunur araç
  (get_spending_summary, get_category_breakdown, get_merchant_spending) Application servislerini doğrudan çağırır, REST ile aynı JSON
  döner; müşteri/hesap/tarih argümanı yok, dönem 11 ad. Argümanlar ToolCallFilter'da yayınlanan şemaya göre uygulanır (SDK şemayı
  uygulamaz); hatalar {code: invalid_arguments|session_invalid|internal_error, message}. Ortak kurulum BankingAI.Hosting (JWT, iş saati,
  JSON kuralı, AddBankingCore yalnız okuma servisleri; yazma servisleri yalnız API'de). McpServer.Tests 40 (gerçek C# MCP istemcisi).
  MCP-b (MCP-12/13/14, 20 Ekim 2026): AI servisi araç listesini her turda kullanıcının token'ıyla MCP'den alır, modelin seçtiği aracı
  MCP'de çalıştırır (resmi Python mcp 2.3.0, ai/service/mcp_gateway.py, protokol 2026-07-28 sabit, anyio.run ile senkron sohbetten);
  argümanları yalnız MCP server doğrular, invalid_arguments → sabit geri soru; sonuç metinden Decimal (SDK structuredContent'i float
  yapıyor). REST köprüsü ve Python araç tanımları silindi; MCP server'a tokensız /health, AI servisinin hazır olma kontrolü ona da bakar.
  Araç seçimi önce/sonra 12/12 (ai/model_check/tool_selection_eval.py); tur başına ~10 ms. Python 231, McpServer 43.
- iPhone uygulaması iOS-a (IOS-01, IOS-13, 20 Ekim 2026, docs/ios.md): ios/BankingAI (Xcode 26.5, iOS 17+, SwiftUI + MVVM,
  URLSession, kütüphane yok). Giriş ekranı; token ve bitişi Keychain'de (WhenUnlockedThisDeviceOnly); token'lı çağrıda 401 veya süre
  dolunca oturum kapanır; tek ağ kapısı APIClient (problem gövdesi, Retry-After, iptal, ağ yok). 34 Swift Testing test durumu.
- iPhone sohbet ekranı AI-08 (iOS-b, 20 Ekim 2026, docs/ios.md): ana ekranda "Asistana sor" → ChatView (K11-B); GET /api/assistant/status
  ekran açılınca ve "Yeniden kontrol et"le (K7-A), POST /api/assistant/messages yalnız bu istekte 40 sn (K12-A; 30 → 35 → 40). Sohbet
  yalnız bellekte, ana ekranla yaşar, çıkışta silinir (K4-D); tek seferde bir soru; Durdur, ekrandan çıkınca/arka planda iptal ("Durduruldu" +
  "Tekrar gönder", K5-C); hata koda göre (K6-A: 503 → bant + kapalı giriş, 504/502 → balonda "Tekrar dene", 429 → "N sn", 401 → giriş);
  yeniden deneme aynı balonda; yerleşik satır içi Markdown (K8-A); boş sohbette üç örnek soru (K9-A); kaynak/rakam paneli yok (K13-A,
  IOS-11). Backend değişmedi. 54 iOS test durumu (ChatViewModel 20).
- Soğuk model ısıtma AI-15 (20 Ekim 2026, docs/local-ai.md): AI servisi açılırken lifespan'da arka planda ollama_client.warm_up
  (POST /api/generate {model, keep_alive}, prompt yok; açılış beklemez, hata yalnız uyarı logu). BANKINGAI_LLM_KEEP_ALIVE (varsayılan
  30m; süre ya da -1, 0 reddedilir) ısıtma, araç seçimi ve açıklamada gönderilir: Ollama alansız isteğe 5 dk uygular; "-1" yazısı 400
  alır, sayı -1 gider. Soğuk ilk cevap ~8 sn → ~2,5 sn. Python 249.
- Yeni MCP araçları MCP-08 (MCP-08/09/10/16/24/27, 20 Ekim 2026, docs/mcp.md): get_merchant_breakdown, get_highest_transactions (yalnız
  harcama, 5 kalem, ham açıklama modele gitmez), compare_spending (PreviousFullMonth | MonthToDate), get_monthly_trend (months 1–12),
  get_recurring_payments, get_upcoming_payments (days 1–31, isteğe bağlı) → 9 araç. ToolCallFilter tam sayı kuralı (JSON tam sayısı,
  minimum/maximum). Python facts.py altı üretici (ilk 5 kalem + "N daha" notu, tahminler "tahmini" etiketli). Sohbet cevabında argümanlar
  yazı (_as_text; sözleşme dict[str, str]); seçici num_predict 200. Seçim ölçümü 26 vaka 26/26 (açıklama ayarından sonra). McpServer
  85, Python 271.
- Belirsizlikte geri soru CHAT-03 (21 Ekim 2026, docs/local-ai.md): grounding.check_arguments modelin doldurduğu dönem/sayının mesajda
  geçmesini ister (yoksa araç çalışmaz, geri soru; isteğe bağlı dayanaksız argüman düşer); belirsiz merchant facts → candidates. Bekleyen
  soru AI servisinde bellekte (clarification.PendingStore, anahtar sha256(token), 5 dk, kullanıcı başına bir); ≤ 4 kelimelik cevap kodda
  eşleşir ve model seçimi olmadan araç tamamlanır, eşleşmeyen mesaj yeni sorudur. Soruyu model yazar (clarify-v1), check_question geçmezse
  sabit şablon; seçenekler backend'in kapalı kümesi ya da sabit liste. Seçim ölçümü 31/31. Python 392.
- Takip soruları CHAT-02 (21 Ekim 2026, docs/local-ai.md): son başarılı çağrı AI servisinde bellekte (conversation.ConversationStore,
  memory.TokenMemory, sha256(token), 10 dk); yalnız ≤ 4 kelimelik ya da peki/ya/ayrıca/bir de ile başlayan mesaj seçiciye bağlam satırıyla
  gider (tool-selection-v2); argüman mesajda ya da son çağrıdan aynen olmalı, takipte söylenmeyen işyeri son çağrınınki; bağlama ve
  açıklamaya yalnız son mesaj; cevabın rakamları saklanmaz. Seçim ölçümü 45/45.
- Gerçekçi yıl SYN-15 (21 Ekim 2026, docs/synthetic-seed.md): 6 yeni persona (müşteri …0004–0009, Elif Europe/Berlin), 9 hesap (…0008–0016),
  1 Ekim 2025 – 10 Ekim 2026 arası 1.710 işlem (`seed transactions realistic`; hikâye elle, arka plan sabit seed'li üretici). Ada/Deniz/Ege
  ve 116 işlem değişmedi; bütün veri setini sayan testler ilk demo hesaplarına daraltıldı. Yeni fixture: RealisticDatasetTests (Credit elle,
  Debit altın), RealisticPersonaTests (düzenli ödeme zammı/iptali, benzer adlı Migros, Berlin ay sınırı). Ölçüm: seçim 48/48, cevap doğruluğu
  (answer_eval.py, bağımsız SQL rakamları) 10/10. İade yok: KIND-06.
- Bütçe/tasarruf araçları MCP-17/18 (21 Ekim 2026, docs/mcp.md "MCP-17/18"): PlanningTools (check_budget, simulate_savings; 11 araç),
  kategori/para birimi kapalı kümeleri, filtrede tutar (number) ve null kuralı, tasarruf sonucunda CategoryDisplayName. Kullanıcının
  rakamları (tutar, para birimi, oran) mesajda aranır; söylenmeyen sabit metinle sorulur ve zincirlenir. Ölçüm: seçim 60/63 (eski 48 tam;
  uzun kategori açıklaması eski araçların dönemini bozmuştu), cevap doğruluğu 14/15 (A12'nin
  cevap şartı soruyla uyumsuzdu, düzeltildi).
- Niyete göre araç daraltma CHAT-01 (21-22 Ekim 2026, docs/local-ai.md "Niyete göre araç daraltma"): `routing.Router` (kod önce:
  kapsam dışı → sabit metin, modelsiz; planlama kelimesi → bütçe/tasarruf araçları; harcama/ödeme kelimesi → MCP-08'in 9 aracı; takipte
  son çağrının grubu; kural yoksa `intent-v1` JSON sınıflayıcı, ≤ 5 sn; karar yoksa genel grup). İki grup (K2-C): üç küçük grup S15/F01'i
  her koşuda bozdu. Ölçüm 4b: 67/71 → 68/71, eski vakalarda bozulan yok; 4b kalır. Bulgu: model belleği araç sayısından bağımsız
  12,7 GB'a büyüyor (AI-16).
- Önerilen sorular CHAT-06 (22 Ekim 2026, docs/local-ai.md "Önerilen sorular", docs/ios.md "iOS-c"): arka uç `suggestions.py` (takip
  tablosu; sunulan öneri aynı metinle gelirse model seçmeden çalışır, yalnız bir sonraki mesaj ve 10 dk; başlangıçlar üç modelsiz MCP
  kontrolünden), `/v1/chat` `suggestions`, `GET /api/assistant/suggestions` (hız sınırı dışında); iPhone son cevabın altında düğmeler,
  boş sohbette başlangıç soruları (olmazsa sabit üç örnek). Kararlar PR1 K1-A…K8-A, PR2 K1-B, K2-A, K3-B, K4-B, K5-A, K6-A.
- Hesaplar ekranı IOS-04 (22 Ekim 2026, docs/ios.md "iOS-d"): ana ekranın gövdesi `GET /api/accounts` listesi (K1-D; `AccountsViewModel`
  dört durum + yenileme hatası bandı, `AccountRow` kartı), tutar yazımı sohbetle aynı (`Formatting/Money`: `577.490,18 TL`), aşağı çekip
  yenileme, yenileme hatası listeyi silmez (K6-A), vurgu rengi lacivert. Backend değişmedi; hesaba dokunma IOS-05'te. 85 iOS test durumu.
- Son işlemler IOS-07 (22 Ekim 2026, docs/ios.md "iOS-e"): ana ekranda hesapların altında `GET /api/transactions?limit=5` (bütün hesaplar,
  işletme dahil), "Tüm işlemler" ekranı kaydırdıkça 20'şer (`offset`; tekrar eden işlem bir kez). Satır kuralları `TransactionDisplay`'de
  (işyeri ya da ham açıklama, maskeli karşı taraf, "Sınıflandırılmamış" ≠ "Diğer", "+"/"−" `direction`'dan, gün `localDate`'ten).
  K11-D: dönemsiz arama son 3 ay ve telefon iş gününü bilmiyor → bütün geçmiş API-17'de. Backend değişmedi. 125 iOS test durumu.
- İmleçli sayfalama + bütün geçmiş API-17 (23 Ekim 2026, docs/api.md, docs/analytics.md, docs/ios.md "iOS-f"): iki liste endpoint'inde
  `offset` yerine `cursor` (opak `TransactionCursor`: an + Id; SQL `(OccurredAt, Id) < (…)`), cevaba `nextCursor`; `period=AllTime` (yalnız
  işlem listelerinde; analytics 400) iş gününün sonuna kadar, dönemin başı en eski işlemin yerel günü. Kararlar K1-B K2-A K3-A K4-A K5-A
  K6-A. iPhone "Tüm işlemler" bütün geçmişi imleçle gezer, tekrar ayıklaması kalktı. Testler: Application 332, Data 84, Api 478,
  McpServer 131; iOS 125. Gerçek API'de Ada 49 / Selin 646 işlem tekrarsız.
- Kategori grafiği IOS-08 PR1 (23 Ekim 2026, docs/ios.md "iOS-i"): ana ekranda "Harcama analizi" kartı, `AnalyticsView` içinde `GET /api/analytics/category-breakdown`
  halkası (Swift Charts `SectorMark`), dört adlı dönem (Bu ay / Geçen ay / Son 3 ay / Bu yıl), para birimi başına ayrı halka (toplanmaz), Sınıflandırılmamış
  ayrı gri dilim, kategori koduna sabit renk. Telefon yüzde ve toplam hesaplamaz. Backend değişmedi. 212 iOS test durumu. Aylık grafik PR2.
- Aylık grafik IOS-08 PR2 (23 Ekim 2026, docs/ios.md "iOS-j"): analiz ekranında kategori halkasının altında `GET /api/analytics/monthly-trend?months=6`
  çubukları (7 çubuk, para birimi başına ayrı, devam eden ay soluk, harcamasız ay sıfır çubuk, değer ekseni yok). Backend değişmedi. 232 iOS test durumu. IOS-08 tamam.
- Arayüz kimliği UI-01 (26 Ekim 2026, docs/ios.md "iOS-k"): siyah tema (her zaman koyu, zemin `#0A0A0B`), tek vurgu şampanya altını, `DesignSystem/` (Palette, Typography, Layout, Components); renkler yalnız `Palette`'ten, kontrast testle korunur. Düzen değişmedi (UI-02+). 249 iOS test durumu.
- Sohbet ekranı UI-02 (26 Ekim 2026, PR #54, docs/ios.md "iOS-l"): asistan cevabı balonsuz + "A" monogramı, kullanıcı mesajı koyu kapsül, rakamlar kalın (yalnız görünüm), yatay öneri çipleri, üç nokta, yüzen giriş kapsülü; ViewModel davranışı aynı. 261 iOS test durumu.
- Ana ekran UI-03 (26 Ekim 2026, docs/ios.md "iOS-m"): üst satır (selam + backend günü + profil menüsü), "Bu ay harcama" kartı (`GET /api/analytics/spending-summary?period=ThisMonth`, para birimi başına), hesap kartları (numara "•••• 001", tam numara hesap detayında), son işlemler Palette'te, "Asistana sor" yüzen kapsül. Telefon tarih/rakam hesaplamaz. Backend değişmedi. 280 iOS test durumu.
- Giriş ekranı ve kimlik UI-04 (26 Ekim 2026, docs/ios.md "iOS-n"): uygulama adı Compass, asistan adı Kılavuz (`DesignSystem/Brand.swift`; backend'in üç sohbet hata başlığı da "Kılavuz …"), siyah giriş ekranı (koyu dolu alanlar, İleri/Git akışı, ikonlu hata satırı), simge kullanıcının görselinden kırpıldı (K10-C), vurgu rengi altından simgenin mavisine `#3D8BFF` geçti (K11-B; yeşil yalnız gelen para), açılış rengi `LaunchBackground` = Palette.background. 288 iOS test durumu.
- İşlem ekranları UI-05 PR1 (26 Ekim 2026, docs/ios.md "iOS-o"): tüm işlemler, bağlantılı işlemler, işlem detayı, hesap detayı ve tarih filtresi paneli siyah dilde; uzun listeler tek kart (`LazyVStack`), filtre çipi, `HairlineDivider`/`DetailRow`/`BusinessTag` ortak parçalar. Backend ve ViewModel değişmedi. 
- Harcama analizi UI-05 PR2 (26 Ekim 2026, docs/ios.md "iOS-p"): kategori renkleri Palette'te (`CategoryTone.swatch`, kod→ton eşlemesi `AnalyticsDisplay.tone`'da), kontrast ve renk farkı (Lab ΔE ≥ 25; birbirine, vurguya, gelen paraya, hataya) testle korunur; çubuklar vurgu mavisi, devam eden ay %70; halka ortasında backend toplamı. UI-05 tamam; 303 iOS test durumu.
- Durumlar UI-06 PR1 (26 Ekim 2026, docs/ios.md "iOS-q"): yükleniyor iskeletleri (`Skeleton`, `.skeleton()`) ve ortak boş/hata görünümü (`StateMessage`) dönen çark ve sistem `ContentUnavailableView` yerine; metinler ve ViewModel'ler aynı. 309 iOS test durumu. PR2 (geçiş, titreşim, şifre gözü, erişilebilirlik) sırada.
- His ve erişilebilirlik UI-06 PR2 (26 Ekim 2026, docs/ios.md "iOS-r"): bütün animasyonlar `Motion` üzerinden ("Hareketi azalt" → yok), gönderim ve yanlış girişte titreşim, şifre gözü; AX5 taramasında 7 düzen/dokunma sorunu düzeltildi. UI-06 tamam; 312 iOS test durumu.
- Belirli tarih ve aralık CHAT-08, PR1 altyapı (23 Ekim 2026, dal `chat-08-dates-backend`; docs/mcp.md "Tarih aralığı", docs/api.md,
  docs/local-ai.md "Müşterinin günü"): MCP'de beş dönem aracı isteğe bağlı `from`/`to` ve `Custom` dönem alır (`ToolDate`: yalnız
  `YYYY-MM-DD` gerçek gün; aralık kuralları `PeriodResolver`'da). `ICustomerCheck` artık `CustomerDay` (müşterinin bugünü + saat dilimi)
  döndürür, .NET `AssistantClient` bunu `/v1/chat` gövdesine `today`/`timeZone` olarak koyar (iPhone değişmedi, OpenAPI değişmedi);
  Python bu iki alanı zorunlu ve doğrulanmış alır (`dates.CustomerDay`). Araç seçen model `from`/`to`/`Custom`'u görmez
  (`routing.hide_dates`); modelin yine de yazdığı tarih düşer. Davranış değişmedi: tarih okuyan kalıplar PR2'de. Kararlar K10-A K11-B K12-C
  K13-C K14-A K15-B K16-A (üç PR). Testler: Application 335, Data 84, Api 480, McpServer 156; Python 657 modelsiz. 6 kasıtlı bozma yakalandı.

IN PROGRESS
- Market data ayrı kol: SRC-02 (altın kaynağı) dış yanıt bekliyor.

NEXT
- CHAT-07 PR1 arka uç tamam (23 Ekim 2026, dal `chat-07a-links-backend`; docs/api.md): cevaba `links` (.NET `AssistantLinks`, kontrol edilmiş
  `toolCalls`'tan; okunamayan argüman bağlantısız bırakır, cevap bozulmaz) ve aramaya `accountType` filtresi (harcama rakamları yalnız
  Personal hesapları sayar; liste aynı kapsamı ister). `Custom` dönem bağlantıda `from`/`to` ile gider, istemci `period` göndermez.
  IOS-05 özel tarih filtresi `period=Custom` gönderdiği için API'de 400 alıyordu; `fix/ios-custom-period` ile düzeltildi (yalnız `from`/`to`).
- CHAT-07 PR2 iPhone tamam (23 Ekim 2026, dal `chat-07b-links-iphone`; docs/ios.md "iOS-h"): cevap balonunun altında "İşlemleri gör" → aynı
  aramayı soran sayfalı liste. CHAT-07 tamam. **Tuzak:** sohbetin kendi hedefli (`NavigationLink { … }`) linkinden sonra değer-tabanlı link itmek adsız
  yığını hemen geri sarıyordu; sohbet içinden hedefli link kullanılır (birim testleri bunu göremez, simülatörde bulundu).
- Kullanıcı seçer (23 Ekim; CHAT-08 ve IOS-05 tamam): AI-16 model belleği (12,7 GB, ısınma); KIND-06 iade; işlem detayı (IOS-06; IOS-05 tamam); yeni
  MCP araçları (MCP-03…26); SEC-07 kısa ömürlü yalnız-MCP token'ı. Her madde kendi dalında, PR ile. Ayrıntı todo.md "Şu anki sonraki adım".
- 13 Ekim 2026 genişletme kararları: yeni soru tipleri (todo 12b: ANA-10–14) tamam, tool'ları MCP-15–18'de; güvenilir cevap katmanı (todo 15b: AI-09–13, IOS-11) AI adımlarıyla birlikte.
- 17 Ekim 2026 genişletme kararı: sohbet yetenekleri (todo 15c: CHAT-01–05; niyete göre tool daraltma, takip sorusu,
  geri soru, en fazla 3 tool'luk çok adımlı soru), yeni analizler (todo 12c: ANA-15–19, MCP-19–23) ve içgörü kartları
  (todo 15d: INS-01–03, IOS-12; kartı ve sırasını backend seçer, LLM yalnız cümleyi kurar). Sıra: AI/MCP omurgası →
  15c → 12c → 15d. Yeni tablo, LangGraph ve RAG yok.
- 19 Ekim 2026 todo taraması ve kullanıcı kararı: eksik maddeler eklendi (AI-14 sohbet uç noktası, IOS-13 giriş ekranı,
  MCP-24/25 eksik tool'lar, SEC-06 sohbet hız sınırı, API-16 OpenAPI parametreleri, API-17 sayfalama kayması, OPS-06
  isteğe bağlı CI) ve öneriler: ANA-20 gün/hafta dönemleri, ANA-21 + MCP-26 tek çağrıda özet, CHAT-06 önerilen sorular,
  CHAT-07 cevaptan ekrana bağlantı, 12d kalıcı bütçe ve hedef (BUD-01–04; yeni tablo migration onayıyla, LLM yazamaz).
  Karar bekleyen: SRC-02 (altın kaynağı).
- Gün/hafta dönemleri (ANA-20, 19 Ekim 2026): PeriodKind'e Today, Yesterday, ThisWeek, LastWeek, Last7Days, Last30Days,
  ThisYear eklendi; hafta pazartesi başlar, "son N gün" bugün dahil tam N gündür ("son N ay" eski kuralıyla korunur). Dönem
  alan bütün endpoint'ler kabul eder. AI-14 kararları: K1-B (önce FastAPI /v1/chat, sonra .NET + AI-07), K2-A (MCP'ye kadar
  geçici REST köprüsü), K3-A (biçimlendirme AI servisinde), K4-C (model kapalıyken 503 + sohbet açılışında hazır olma kontrolü),
  K5-A (tool seçilmezse sabit metin), K6-A (ANA-20 önce).
- Sohbet uç noktası, FastAPI yarısı (AI-14a, 19 Ekim 2026): POST /v1/chat (Bearer zorunlu) → tool seçimi (LangChain bind_tools,
  tool-selection-v1) → argüman kontrolü → geçici REST köprüsü (kullanıcının token'ıyla .NET analytics endpoint'leri; JSON sayıları
  Decimal) → kalemler (formatting.py: AI-09 bu tool'lar için) → açıklama + kontrol; tek 30 sn. Tool yoksa sabit kapsam dışı metni;
  çoklu/geçersiz çağrı ve belirsiz merchant sabit geri soru. Token yalnız köprüye gider, modele ve loga girmez. 220 Python testi.

KNOWN ISSUES
- Servislerden gelen her ArgumentException 400 sayılıyor; bir programlama hatasından doğan ArgumentException da
  400 görünür (API-13 bilinçli sınırı; ayrı tip InputRules'u ve 70 testi etkilerdi).
- Eski `offset` sorgu parametresi (API-17'de kalktı) hata vermeden yok sayılır; tek istemci iPhone aynı PR'da imlece geçti.
- İş saati varsayılan olarak gerçek saattir; demo için BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z verilmeli (ANA-09). Ayar açıkken
  eklenen işlemler aynı ana düşer.
- Tek komutluk seed synthetic ve market fiyatı import'u yok.

TECHNICAL DEBT
- Ayrı test veritabanı yok (DB-01); testler seed edilmiş demo DB'yi rollback/geçici kayıtla kullanıyor.
- Hesap sayısı sınırı ertelendi (ACC-LIMIT); anahtarsız hesap açma isteği iki kez gönderilirse iki hesap açılır.
- Aylık seri satırları C#'ta aylara dağıtıyor (PERF-02'de ölçülecek).
- Logout/token iptali, refresh token ve hesap kilitleme yok; HTTPS ve proxy IP kararları OPS-04'te.

TEST STATUS
- Tam paket yeşil (16 Ekim 2026, API-15 sonrası; 3 koşu): Data 63, Application 277, Api 371. fix/seeder-test-lock sonrası
  art arda 10 koşu yeşildi.
  SyntheticAccountSeederTests'in ara sıra kırmızısı giderildi: test bütün Accounts tablosunu karşılaştırdığı hâlde
  CommittedRowsLock.ExclusiveAsync() almıyordu; yarış bekleme + eşzamanlı ekleme deneyiyle önce kırmızı, kilitle yeşil
  gösterildi. dotnet build
  --no-incremental 0 uyarı.
- PostgreSQL testleri seed edilmiş demo DB üzerinde çalışır: rollback ya da geçici DUMMY-*TEST-*
  hesap / "Test Api" müşterisi açıp silme. Test projeleri paralel koşar; commit eden ve satır sayan
  testler tests/Shared/CommittedRowsLock (advisory lock 2026101201) ile sıralanır; müşteri ayırmadan sayan
  testler özel kilit alır. İşlem yazan testler demo hesabını hiç hedef almaz (TestUser / TemporaryCustomer).
- Beklenen değerler elle hesaplanmış fixture'lardır (SyntheticDatasetIntegrityTests,
  docs/synthetic-seed.md). Mevcut davranışı kapsayan yeni testler kasıtlı bozmayla doğrulanır.
- Henüz yok: market import, AI, MCP ve SwiftUI testleri; ayrı test DB'si.
