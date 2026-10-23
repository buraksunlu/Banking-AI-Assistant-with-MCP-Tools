---
paths:
  - "src/BankingAI.Api/**"
  - "src/BankingAI.Hosting/**"
  - "tests/BankingAI.Api.Tests/**"
  - "docs/api.md"
  - "docs/security.md"
  - "docs/openapi/**"
---

# ASP.NET Core API (src/BankingAI.Api)

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.

## API kuralları

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
  route'unu taşır. İşlem listeleri imleçle sayfalanır (API-17: `cursor` → `nextCursor`, opak `TransactionCursor`, SQL
  `(OccurredAt, Id) < (…)`; offset yok); `period=AllTime` bütün geçmiştir ve yalnız işlem listelerinde geçerlidir
  (`PeriodParameters.Resolve(…, allowAllTime: true)`; `PeriodResolver.Resolve` onu reddeder, başı `ResolveAllTime` en eski işlemden çözer). Dönem parametreleri her endpoint'te `Api/Queries/PeriodParameters` ile okunur (hata → `ArgumentException`
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

## Sohbet kapısı (.NET)

Sohbet kapısı (AI-14b + AI-07, .NET): iPhone yalnız .NET'e konuşur. `POST /api/assistant/messages` → `ICustomerCheck` (silinmiş
müşteri model çalışmadan 401) → `IAssistantClient` (`Api/Assistant/AssistantClient`, typed `HttpClient`) aynı `Authorization`
başlığıyla `/v1/chat`'e → `AssistantReply` (`Suggestions` dahil, eksikse 502); `GET /api/assistant/status` hep 200 `{available}`;
`GET /api/assistant/suggestions` → `/v1/suggestions` (hatalar mesajınkiyle aynı, hız sınırı dışında). AI hataları `AssistantException`
(`Unavailable`/`Timeout`/`Failed`) → `ApiExceptionHandler`'da 503/504/502. Ayarlar `BANKINGAI_AI_URL` (yoksa `http://127.0.0.1:8000/`),
`BANKINGAI_AI_TIMEOUT_SECONDS` (35). Müşteri başına dakikada 10 mesaj: **`UseRateLimiter` `UseAuthorization`'dan sonra** gelmeli
(anahtar token'daki müşteri; önce gelirse `GetCustomerId` patlar). Mesaj ve token loglanmaz. Testler gerçek model yerine
`FakeAssistantService` (HttpMessageHandler, `ConfigureTestServices`) kullanır.
