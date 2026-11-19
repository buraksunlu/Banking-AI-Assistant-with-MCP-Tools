# Rutin / rutin olmayan döküm (DEMO-02) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harcama listesini "Rutin ve temel" / "Rutin olmayan" diye gruplayan, rutin olmayanların toplamına üç varlıklı yatırım karşılaştırması ve
alışkanlık önerisi ekleyen iki uç (PR1) ve bunları gösteren iPhone ekranları (PR2).

**Architecture:** Saf parçalar (`RoutineClassifier`, `WhatIfMath.Basket`, `HabitAdviceRules`) DB'siz test edilir; `SpendingBreakdownService`
bunları `PurchaseScope` + `RecurringPaymentDetector` + `MarketPriceReader` üzerinden birleştirir; `TransactionsController` iki yeni GET ucu açar.
iPhone yalnız gösterir.

**Tech Stack:** .NET 10, EF Core/Npgsql, xUnit; SwiftUI (iOS 17+), XCTest.

**Spec:** `docs/superpowers/specs/2026-11-19-demo-02-routine-breakdown-design.md`

## Global Constraints

- Telefon toplama/yüzde yapmaz; para birimleri toplanmaz; yatırım rakamı yalnız TRY ve her zaman `WhatIfMath.Disclaimer` ile.
- Yetki yalnız `CustomerScope`/`PurchaseScope` üzerinden; başka müşterinin işlemi olmayanla aynı 404.
- Rutin = taksit kalıbı `TAKSIT (\d{2})/(\d{2})` ya da dönemin son gününe göre `RecurringPaymentDetector` (pencere `Last6Months`); temel =
  `SpendingGroups.Of(...) == Essential`; rutin olmayan + `Discretionary` = uygun (eligible); `Unknown` öneri ve yatırım almaz.
- Yatırım: uygun ve ≥ 7 günlük satırlar kendi günlerinde alınır, iş günü "bugün"ün fiyatıyla değerlenir; öneri yalnız dönem ≥ 28 gün,
  ≥ 1,5 × son 6 tam ayın aylık ortalaması ve fark ≥ 500, en fazla 2.
- Kod yorumları İngilizce, public üyede `/// <summary>`; OpenAPI kopyası `BANKINGAI_UPDATE_OPENAPI=1` ile yenilenir; commit/PR'da AI imzası yok.
- Dallar: PR1 `demo-02a-breakdown-api`, PR2 `demo-02b-breakdown-iphone`.

## Review Focus

1. Dönem sınırında rutin kararı: dönemin son günü "bugün" sayılmazsa iptal edilmiş abonelik eski ayda rutin dışı görünür → Task 4'te Netflix
   Eylül'de rutin testi.
2. İki para birimli persona (Zeynep TRY + USD): USD grubunda yatırım `null`, toplamlar karışmaz → Task 4 testi.
3. Bugün yapılmış tek isteğe bağlı harcama → yatırım `null` + `excludedRecent` dolu, "erken" notu → Task 1 (`Investable`) ve Task 5 testi.
4. Kategorisi belirsiz rutin olmayan satır (Can Kırtasiye, OTHER) → `eligible=false`, yatırıma girmez → Task 4.
5. Başka müşterinin işlem kimliğiyle `insight` → olmayanla aynı 404 → `IsolationTests` örneği (Task 5).

## Ruling (plan yazılırken)

- Spec'teki 400 `period_too_long` yerine 200 + `periodTooLong: true` + boş `currencies`: yeni hata kodu ve eşleme gerekmez, telefon tek alana
  bakar. Bedel: istemci alanı kontrol etmek zorunda.

## Elle hesaplanmış beklenen değerler (sabit DB, iş "bugün"ü 10 Ekim 2026, Murat `LastMonth` = Eylül 2026)

- 20 harcama, toplam **53.388,39**. Rutin olmayan: Can Kırtasiye 1.250,00 (OTHER, uygun değil) + Sinema 642,50 (13 Eylül, uygun) = **1.892,50**;
  rutin ve temel **51.495,89** (18 satır).
- Rutin nedenleri: Netflix 229,99 → Subscription; Su Faturası 380,00 → Bill (düzenli); Ev İnterneti 449,90 → Essential (Ağustos zammı
  düzenliliği bozdu); Okul Taksidi 25.000 → Essential (açıklama `OKUL TAKSIT ODEMESI`, kalıba uymaz); market/akaryakıt/lokanta/doğalgaz/
  elektrik → Essential.
- Yatırım (Sinema 642,50; alış 11 Eylül fiyatı, değer 9 Ekim fiyatı): USD 48,4305 → 49,1274: birim 13,266433, değer **651,75**, fark **9,25**,
  **%1,44**; EUR 56,1754 → 55,1112: 11,437391, **630,33**, **−12,17**, **−%1,89**; altın 6.770,38376015 → 6.613,19001236: 0,094899,
  **627,58**, **−14,92**, **−%2,32**. `excludedRecent` 0.
- Öneri: Sinema son 6 ay (Mart–Ağustos) 505 + 581,5 + 670,5 + 499,5 + 606,5 + 579 = 3.442 / 6 = 573,67; 642,50 / 573,67 = 1,12 < 1,5 → **öneri yok**.
- `ThisMonth` (1–10 Ekim): rutin olmayan yok → yatırım `null`. `Last3Months` → `periodTooLong`.

---

## PR1 — backend (`demo-02a-breakdown-api`)

### Task 1: Saf parçalar

**Files:**
- Create: `src/BankingAI.Application/Analytics/RoutineClassifier.cs`, `src/BankingAI.Application/Analytics/HabitAdviceRules.cs`
- Modify: `src/BankingAI.Application/Investment/WhatIfMath.cs` (`Basket`)
- Test: `tests/BankingAI.Application.Tests/RoutineClassifierTests.cs`, `HabitAdviceRulesTests.cs`, `WhatIfBasketTests.cs`

**Interfaces (Produces):**
- `public enum RoutineReason { Subscription, Bill, Installment, Recurring, Essential }`
- `public static class RoutineClassifier`: `public static (int Number, int Count)? Installment(string rawDescription)`;
  `public static RoutineReason? Classify(string rawDescription, string? categoryCode, bool recurring)` (taksit → Installment; recurring →
  SUBSCRIPTION ? Subscription : BILLS ? Bill : Recurring; Essential grup → Essential; değilse null).
- `public sealed record HabitInput(string Merchant, int Count, decimal Amount, decimal UsualTotal)`;
  `public sealed record HabitAdvice(string Merchant, int Count, decimal Amount, decimal UsualMonthly, decimal Excess, string Message)`;
  `public static class HabitAdviceRules { public const decimal UsualRatio = 1.5m; public const decimal MinimumExcess = 500m; public const int UsualMonths = 6; public const int MinimumPeriodDays = 28; public const int MaximumAdvice = 2; public static IReadOnlyList<HabitAdvice> Evaluate(IEnumerable<HabitInput> inputs, int periodDays) }`.
  Mesaj: `"{Merchant} için bu dönemde {Count} kez {Amount} harcadınız; son 6 ayın aylık ortalaması {UsualMonthly}. Ortalamanızda kalsaydınız {Excess} size kalırdı."` (tutarlar `AnalyticsLabels`/mevcut TL biçimiyle "1.234,50 TL").
- `public sealed record BasketBuy(DateOnly Date, decimal Amount)`; `WhatIfMath.Basket(MarketAssetInfo asset, IReadOnlyList<(BasketBuy Buy, PricePoint Price)> buys, PricePoint value) -> AssetWhatIf` (BuyDate = ilk alım günü; birimler tam toplanır; herhangi bir fiyat `Ok` değilse rakam yok, Status en kötüsü).
- `public static class SpendingBreakdownRules { public const int MinimumAgeDays = 7; public const int MaximumPeriodDays = 31; }`

- [ ] **Step 1: Testleri yaz**
  - `Installment("APPLE STORE TAKSIT 03/12") == (3, 12)`; `"OKUL TAKSIT ODEMESI"` → null; `"TAKSIT 3/12"` → null (iki hane şart).
  - `Classify`: taksit + OTHER → Installment; recurring + SUBSCRIPTION → Subscription; recurring + BILLS → Bill; recurring + ENTERTAINMENT →
    Recurring; recurring yok + GROCERY → Essential; + BILLS → Essential; + ENTERTAINMENT → null; + OTHER → null; + null kategori → null.
  - `HabitAdviceRules.Evaluate`: (Trendyol, 3, 6.100, 12.000) 30 gün → bir öneri: ortalama 2.000, fark 4.100, mesaj tam metin; oran tam 1,5 ve
    fark ≥ 500 → öneri (sınır dahil); fark 499,99 → yok; üç aday → büyük fark önce iki tane; 27 gün → boş; `UsualTotal` 0 → ortalama 0,
    oran sınırsız → fark = tutar ≥ 500 ise öneri.
  - `Basket`: tek alım Sinema (642,50; 48,4305 → 49,1274) → 651,75 / 9,25 / 1,44 (Task başındaki değerler); iki alım (100 @ 50, 200 @ 40,
    değer 45) → birim 7, değer 315,00, fark 15,00, %5,00; biri `Stale` → Value null, Status Stale.
- [ ] **Step 2:** `dotnet test tests/BankingAI.Application.Tests --filter "FullyQualifiedName~RoutineClassifierTests|FullyQualifiedName~HabitAdviceRulesTests|FullyQualifiedName~WhatIfBasketTests"` → derleme hatası (tipler yok).
- [ ] **Step 3:** Uygula → aynı komut PASS.
- [ ] **Step 4:** `git switch main && git pull && git switch -c demo-02a-breakdown-api`; spec + plan + Task 1 commit: `classify routine spending, check habits and value a basket of buys (DEMO-02)`.

### Task 2: `SpendingBreakdownService` ve `TransactionInsight`

**Files:**
- Create: `src/BankingAI.Application/Analytics/SpendingBreakdownService.cs`
- Modify: `src/BankingAI.Hosting/…` (`AddBankingCore`: `services.AddScoped<SpendingBreakdownService>();`)
- Test: `tests/BankingAI.Application.Tests/SpendingBreakdownServiceTests.cs` (PostgreSQL, `FixedTimeProvider(DemoNowUtc)`)

**Interfaces (Produces):**
- `public sealed record SpendingBreakdownQuery(Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, string? CategoryCode = null, string? Merchant = null)`
- `public sealed record RoutineItem(TransactionItem Transaction, RoutineReason Reason, string? Installment)` (Installment "03/12")
- `public sealed record NonRoutineItem(TransactionItem Transaction, bool Eligible)`
- `public sealed record BreakdownInvestment(decimal Amount, decimal ExcludedRecent, string Method, DateOnly ValueDate, IReadOnlyList<AssetWhatIf> Assets, string Disclaimer)` (`Method = "EachOnItsOwnDay"`)
- `public sealed record RoutineGroup(decimal Total, int Count, IReadOnlyList<RoutineItem> Items)`;
  `public sealed record NonRoutineGroup(decimal Total, int Count, IReadOnlyList<NonRoutineItem> Items, BreakdownInvestment? Investment, IReadOnlyList<HabitAdvice> Advice)`
- `public sealed record CurrencyBreakdown(string CurrencyCode, RoutineGroup Routine, NonRoutineGroup NonRoutine)`
- `public sealed record SpendingBreakdown(ResolvedPeriod Period, bool PeriodTooLong, MerchantMatch? MerchantMatch, IReadOnlyList<CurrencyBreakdown> Currencies)`
- `public sealed record TransactionInsight(TransactionItem Transaction, RoutineReason? Routine, string? Installment, SpendingGroup Group, BreakdownInvestment? Investment, string? Note)`
- `SpendingBreakdownService(BankingDbContext db, PeriodResolver periodResolver, MarketPriceReader prices)`:
  `Task<SpendingBreakdown> GetAsync(SpendingBreakdownQuery query, CancellationToken ct)`, `Task<TransactionInsight> InsightAsync(Guid customerId, Guid transactionId, CancellationToken ct)`.
  Satırlar `TransactionItemReader` ile (kopya join yok); merchant filtresi `MerchantMatcher` ile (arama ile aynı davranış). Notlar sabit:
  "Rutin ödeme", "Temel harcama", "Kategorisi belirsiz", "Karşılaştırmak için erken", "Yalnız TL harcamalarda", "Yalnız harcamalarda".

- [ ] **Step 1: Testleri yaz** (değerler yukarıdaki tablodan):
  - `Murat_last_month_splits_into_routine_and_non_routine`: toplamlar 51.495,89 / 1.892,50, sayılar 18 / 2; Netflix Subscription, Su Faturası
    Bill, Ev İnterneti ve Okul Taksidi Essential; Can Kırtasiye `Eligible=false`, Sinema `true`.
  - `Murat_last_month_investment_is_the_cinema_alone`: Amount 642,50, ExcludedRecent 0, üç varlık değerleri/farkları/yüzdeleri tablodaki gibi,
    Disclaimer = `WhatIfMath.Disclaimer`, Method "EachOnItsOwnDay"; Advice boş.
  - `This_month_has_no_non_routine_spending`: Murat `ThisMonth` → NonRoutine.Count 0, Investment null.
  - `A_long_period_is_not_broken_down`: `Last3Months` → `PeriodTooLong` true, Currencies boş.
  - `Two_currencies_stay_apart`: Zeynep `LastMonth` → TRY ve USD ayrı satır; USD'de Investment null.
  - `Insight_of_the_cinema`: Murat'ın Sinema işlemi (13 Eylül; kimliği SQL ile doğrula, beklenen `…000000012207`) → Group Discretionary,
    Routine null, Investment üç varlık tablodaki gibi; Netflix (`…000000012205`) → Routine Subscription, Investment null, Note "Rutin ödeme".
  - `Insight_of_another_customers_transaction_is_not_found`: Ada ile Murat'ın işlemi → `KeyNotFoundException`.
- [ ] **Step 2:** Çalıştır → derleme hatası. **Step 3:** Uygula → PASS. **Step 4:** Kasıtlı bozma: `MinimumAgeDays` 7 → 40 (Sinema dışarıda kalır → yatırım testi FAIL); recurring "bugün"ünü dönem sonu yerine iş günü yap (Netflix yine rutin olur mu? Olursa bozma yakalanmamıştır: Ruling yaz ve Netflix yerine Eylül'de iptal edilen bir abonelik örneği bul); `shasum` ile geri al.
- [ ] **Step 5:** Commit: `break a period's spending into routine and non-routine with a basket comparison (DEMO-02)`.

### Task 3: İki uç, sözleşme, tarama ve izolasyon

**Files:**
- Modify: `src/BankingAI.Api/Controllers/TransactionsController.cs` (`[HttpGet("api/transactions/breakdown")] BreakdownAsync(period, from, to, categoryCode, merchant)`, `[HttpGet("api/transactions/{id}/insight")] InsightAsync(Guid id)`; dönem `PeriodParameters.Resolve(period, from, to, null)` — zorunlu, AllTime yok; kategori `ExactEnum`/mevcut kategori doğrulamasıyla aynı)
- Modify: `tests/BankingAI.Api.Tests/EndpointSweepTests.cs` (iki örnek), `tests/BankingAI.Api.Tests/IsolationTests.cs` (`["GET api/transactions/{id}/insight"] = "/api/transactions/{0}/insight"`), `docs/openapi/v1.json`, `api-examples.http`
- Test: `tests/BankingAI.Api.Tests/SpendingBreakdownEndpointTests.cs`

- [ ] **Step 1:** Testler: `breakdown?period=LastMonth` Murat token'ı (`TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow))`) → 200, `currencies[0].routine.total` 51495.89 (JSON sayı), `nonRoutine.investment.assets` 3 eleman, `routine.items[*].reason` metin ("Subscription"); `period` yok → 400; `period=AllTime` → 400; `period=Last3Months` → 200 `periodTooLong: true`; `insight` Sinema → 200 `group: "Discretionary"`; tokensız → 401.
- [ ] **Step 2:** FAIL gör → uygula → PASS. `BANKINGAI_UPDATE_OPENAPI=1 dotnet test tests/BankingAI.Api.Tests --filter "FullyQualifiedName~The_document_is_the_same"` ile sözleşmeyi yenile, diff'i oku.
- [ ] **Step 3:** Tam paket: `dotnet build BankingAI.slnx --no-incremental` 0 uyarı; `dotnet test BankingAI.slnx` yeşil.
- [ ] **Step 4:** Canlı kontrol (ayrı port 5348, canlı DB, Murat yerel token): `LastMonth` (Ekim 2026) ve `period=Custom&from=2026-09-05&to=2026-09-05` dökümleri; rutin olmayan toplamı SQL ile karşılaştır; Ekim'de en az bir alışkanlık önerisi beklenir (yoksa sebebini rakamla yaz). Sonuç PR'a.
- [ ] **Step 5:** Belgeler: `docs/api.md` (iki uç), `docs/analytics.md` (kurallar), `todo.md` DEMO-02 notu, `RULE.md` §29. Commit, push, PR (Türkçe, bölümlü), squash merge, `git switch main && git pull`.

## PR2 — iPhone (`demo-02b-breakdown-iphone`)

### Task 4: Modeller ve istekler

**Files:** Modify `ios/BankingAI/BankingAI/Networking/APIModels.swift`, `APIClient.swift`; Test `ios/BankingAI/BankingAITests/BreakdownTests.swift`

**Interfaces (Produces):** `struct SpendingBreakdown: Decodable` (+ `CurrencyBreakdown`, `RoutineGroup`, `NonRoutineGroup`, `RoutineItem`, `NonRoutineItem`, `BreakdownInvestment`, `AssetWhatIf` yeniden kullanılır ya da eşi, `HabitAdvice`, `TransactionInsight`); `enum RoutineReason: String, Decodable`; `static func breakdown(link: AssistantLink) -> APIRequest<SpendingBreakdown>`; `static func insight(id: UUID) -> APIRequest<TransactionInsight>`; `RoutineItem.label` → "Abonelik" / "Fatura" / "Taksit 3/12" / "Düzenli ödeme" / "Temel".

- [ ] Testler: örnek JSON (PR1'in gerçek cevabından kısaltılmış) çözülür; bilinmeyen `reason` hata verir; istek yolu ve sorgu bağlantının filtresiyle aynı (dönem adı ya da iki tarih, ikisi birden asla). Kırmızı → uygula → yeşil:
  `xcodebuild test -project ios/BankingAI/BankingAI.xcodeproj -scheme BankingAI -destination 'platform=iOS Simulator,name=iPhone 17' -only-testing:BankingAITests/BreakdownTests` (komut `.claude/rules/ios.md`'den doğrulanır).

### Task 5: Ekranlar

**Files:** Modify `Transactions/LinkedTransactionsView.swift`, `Transactions/TransactionDetailView.swift`; Create `Transactions/BreakdownView.swift`, `Transactions/BreakdownViewModel.swift`, `Transactions/InvestmentCard.swift`

- [ ] ViewModel testleri: harcama bağlantısı → önce döküm; `periodTooLong` → düz liste modu; hata → mevcut ortak hata görünümü + Tekrar dene; telefon toplama yapmaz (görünen toplamlar cevabın alanları). Detay: `insight` yatırımı varsa kart, yoksa `note` satırı.
- [ ] Görünüm: iki bölüm başlığı (ad · toplam · adet), mevcut `TransactionRow` + rutin etiketi, rutin olmayanın altında `InvestmentCard` (üç varlık değer + fark, fiyat günü, yöntem satırı "Her harcama kendi gününde alınmış sayıldı", `excludedRecent` > 0 ise "Son 7 günün X'i dahil değil", sabit uyarı) ve öneri satırları; tasarım sistemi (`Palette`, `Typography`, `Layout`), "Hareketi azalt", en büyük yazı boyutu.
- [ ] Tam iOS testi yeşil; kasıtlı bozma (etiket eşlemesi, `periodTooLong` dalı); simülatörde canlı API ile Murat: "Geçen ay ne kadar harcadım?" → İşlemleri gör, "5 Eylül'de ne harcadım?" → İşlemleri gör, Sinema/Trendyol detayı.
- [ ] Belgeler (`docs/ios.md`), todo DEMO-02 [x], RULE §29; commit, PR, squash merge.
