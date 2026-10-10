# PROJECT MASTER PROMPT
# AI-Powered Transaction Intelligence Platform

Son kural güncellemesi: 10 Ekim 2026.

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
- Account: Id, CustomerId, AccountName, AccountNumber, CurrencyCode, Balance, CreatedAt.
- TransactionCategory: short Id, Code, DisplayName. Code unique; ID seed ile sabit verilir.
- Transaction: Id, AccountId, nullable CategoryId, Amount, Direction, CurrencyCode,
  RawDescription, nullable NormalizedMerchant, CategorizationSource,
  CategoryConfidence, CategorizedAt, OccurredAt ve CreatedAt.
- MarketAsset: short Id, Symbol, Name, AssetType, BaseUnit, QuoteCurrency, IsActive.
- DailyMarketPrice: long identity Id, MarketAssetId, PriceDate, Price, nullable Source.

Merchant, MerchantMappings, RecurringPayments, InvestmentSimulations, AI/MCP
özel tabloları veya LLM log tablosu ilk kapsamda oluşturulmaz. Yeni tablo ancak
somut ihtiyaç ve ayrı kullanıcı kararıyla değerlendirilir.
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

POST /api/transactions

Analytics:

GET /api/analytics/spending-summary

GET /api/analytics/category-breakdown

GET /api/analytics/merchant-breakdown

GET /api/analytics/period-comparison

GET /api/analytics/recurring-payments

Gerekli query parameters tasarıma göre eklenebilir.

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
- Şu an küçük BankingAI.Data console projesi kullanılır. API/DI katmanı henüz yok.
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
  Debit tek başına alışveriş olduğunu kanıtlamaz; ilk dataset kapsamı VAL-05 ile sınırlıdır.
- VAL-05: ilk synthetic dataset'te yalnızca tamamlanmış mal/hizmet, abonelik
  ve fatura ödemeleri Debit; maaş/açılış fonlaması Credit olur. Transfer, nakit,
  yatırım, kredi/borç hareketleri, komisyon/faiz, refund/iptal ve bekleyen
  hareketler üretilmez. Harcama OccurredAt döneminde tek para birimindeki
  brüt Debit toplamıdır; Credit çıkarılmaz, kategorisiz Debit kaybolmaz.
  Bu varsayım gerçek banka verisine genellenmez. Refund/net harcama ayrı model
  kararı gerektirir; TransactionKind kendiliğinden eklenmez. Ayrıntılar
  docs/input-validation.md içinde; seed/analytics kodu henüz uygulanmadı.
- Balance ve Amount numeric(18,2). Fazla basamaklı girdiyi sessiz yuvarlamama
  politikası yazma yolunda doğrulanmalıdır; numeric sütun tek başına bunu sağlamaz.
- VAL-02: Amount/Balance iki ondalıkla kayıpsız temsil edilebilmeli. 12.3400
  kabul edilir, 12.345 reddedilir; otomatik yuvarlama/kesme yapılmaz. Temsil
  sınırı mutlak 9999999999999999.99; Amount pozitiftir. Kredili hesap yoktur,
  negatif bakiye reddedilir. C# ve DB kontrolleri VAL-07 ile uygulanmıştır;
  bakiye taşmasında atomik yazma BAL adımlarındadır.
- Yeni bakiye 0; açılış fonlaması Credit işlemle temsil edilir. İleride insert ve
  atomik bakiye değişimi aynı DB transaction'ında olacak; sadece transaction açmak
  lost update'i engellemez. Bu yazma akışı henüz implement edilmedi.
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
- Mevcut DailyMarketPrice tek Price alanlıdır; alış/satış model değişikliği henüz
  yapılmadı. Kaynak doğrulamasından sonra alanlar ve incremental migration ayrı
  adımda incelenecek. Ortalama XAU verisinden alış/satış makası uydurulmaz.
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
29. MEVCUT PROJE DURUMU — 10 EKİM 2026
==================================================

COMPLETED
- .NET 10 BankingAI.Data console, EF Core/Npgsql ve ortam değişkenli bağlantı.
- Customer, Account, TransactionCategory, Transaction, MarketAsset ve DailyMarketPrice entity'leri; iki transaction enum'u.
- Account, TransactionCategory, Transaction, MarketAsset ve DailyMarketPrice configuration'ları; Customer eşlemesi DbContext içinde.
- bankingai_foundation üzerinde altı domain tablosu ve __EFMigrationsHistory.
- On uygulanmış migration: CreateCustomers, AddCustomerPreferences,
  AddAccountsAndTransactionCategories, AddTransactions, SplitCustomerName,
  AddMarketAssets, AddDailyMarketPrices, EnforceInputValidation, AddTransactionKind, AddAccountType.
- Transactions.Kind (Purchase/Sale/Transfer/Salary/OpeningFunding) ve nullable PairId; kurallar C# + DB CHECK ile korunur (KIND-01/02).
- DailyMarketPrice → MarketAsset RESTRICT ilişkisi ve varlık/gün unique index'i uygulandı; MarketAssets'te üç seed kaydı, DailyMarketPrices'ta henüz fiyat kaydı yok.
- PK/FK/unique/check/index eşlemeleri ve hesap/işlem currency eşleşmesi.
- Bir demo Customer, 11 TransactionCategory ve 3 MarketAsset seed kaydı; SYN-03–05 ve SYN-14 ile üç dummy Customer ve altı Account; SYN-06–14 ile 98 Transaction (35 Credit, 63 Debit) vardır. DailyMarketPrices'ta fiyat kaydı yoktur.
- todo.md oluşturuldu; tamamlanan ve bekleyen küçük adımlar ayrıldı.

IN PROGRESS
- Banking foundation kabul edildi (§26). Deterministik analytics'e geçiliyor.
- Market data ayrı kol: SRC-02 (altın kaynağı) dış yanıt bekliyor.

NEXT
- ANA-01 (PeriodResolver) ve SYN-14 (Ada/Deniz/Ege farklı demo karakterleri) tamamlandı.
- KIND-01/02/03/04a tamamlandı (Account.AccountType Personal/Business; Sale yalnız Business). KIND-04b: dükkân hesabı + örnek transfer/satış seed'i.
- Ardından ANA-02 harcama özeti; harcama yalnızca Purchase türünü sayar.

KNOWN ISSUES
- Synthetic seed kapsamları parça parça hazırdır: categories, market-assets, customers, accounts ve seçili transaction komutları. Transaction seed grupları sabit ID, atomik bakiye yazımı ve PostgreSQL advisory lock kullanır. Tek komutluk `seed synthetic` ve historical market import henüz yoktur.
- `TransactionSeedWriter` seed işlem insert'i ile Account.Balance delta'sını aynı
  DB transaction'ında yazar. BAL-03/04 concurrency/rollback PostgreSQL kontrolleri geçti.
- Customer FirstName/LastName geçişi SplitCustomerName migration'ıyla uygulandı.
- Dış API giriş sözleşmesi API adımlarında; atomik seed yazma yolu BAL-01/02'de eklendi.
- README güncel console/database durumuna getirildi; database-design içindeki kalan uyarlamalar todo.md'de takip ediliyor.
- Controller/API/DTO/service, SwiftUI, AI ve MCP henüz oluşturulmadı.

TECHNICAL DEBT
- `tests/BankingAI.Data.Tests` otomatik test projesi vardır; her veri/DB değişikliği için uygun kontroller sürdürülür.
- Tek başlangıç migration isteğiyle mevcut artımlı geçmişin nasıl uzlaştırılacağı ayrı karar.
- Veri ve connection yönetiminin tekrarlanabilir yerel kurulum dokümanı tamamlanacak.

TEST STATUS
- Entity build'leri ve migration build/uygulamaları geçti; bu tüm foundation'ın bittiği anlamına gelmez.
- InputRules/EntityValidation ve AccountId/Amount guard'ları C# testleriyle,
  DB CHECK'leri PostgreSQL integration testleriyle kontrol edildi.
- Customer kısıtları ve 12 paralel numara üretimi; Account/Category temel DB kısıtları;
  Transaction nullable/kategorili yazımı ve 13 geçersiz veri kontrolü PostgreSQL'de denendi.
- Test kayıtları rollback edildi. Seed/market import/bakiye concurrency/HTTP/AI/MCP testleri henüz yok.
- DailyMarketPrice duplicate varlık/gün, olmayan varlık, geçersiz fiyat ve RESTRICT kontrolleri gerçek PostgreSQL'de geçti. EF DateOnly okuması UTC ve Europe/Istanbul oturumlarında, decimal/identity/nullable Source ile birlikte doğrulandı; tüm deneme kayıtları geri alındı.
- Önceki silinmiş uygulamanın 48 xUnit testi ve API sonuçları bu projeye ait tamamlanma kanıtı değildir.
