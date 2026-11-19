# Murat demo verisi (DEMO-01) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Murat'ın canlı veritabanındaki verisini 15 Mayıs 2024 – bugün aralığına genişletip taksit, abonelik, rutin dışı harcama, transfer ve
belirli günlerle zenginleştirmek; döviz/altın fiyatlarını aynı aralığa yalnız canlıda uzatmak.

**Architecture:** Murat'ın ek planı `DayPlan`'in yanında, aynı `Builder` yardımcılarıyla (`DayPlan.Murat.cs`, partial) yazılır. Üç katman:
(1) mevcut `MuratDay` (11 Ekim 2026 sonrası, satırları **değişmez**), (2) `MuratExtras` (15 Mayıs 2024'ten her gün; kendi rastgele
üreticisi), (3) `MuratBaseline` (yalnız 15 Mayıs 2024 – 30 Eylül 2025; sabit verinin olmadığı dönemin maaş, fatura ve alışkanlıkları).
`LiveSimulator.CatchUpAsync` plana sabit verinin bitişinden önceki Murat satırlarını da katar. Sabit veritabanı ve seed'ler değişmez.

**Tech Stack:** .NET 10, EF Core, PostgreSQL (canlı `bankingai_live`), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-19-murat-demo-data-design.md`

## Global Constraints

- Sabit veritabanı `bankingai_foundation`, `SyntheticTransactionSeeder` ve test fixture'ları değişmez; canlıya yalnız `scripts/live-env.sh` ile.
- Canlıda zaten yazılmış her plan satırı (kimlik, tutar, açıklama, zaman) birebir aynı kalır; aksi halde yazıcı `IdempotencyConflictException` atar.
- Kimlikler `simulator:{yyyy-MM-dd}:{slot}` adından (`DayPlan.NameId`); yeni slot adları `murat-x-` önekiyle başlar.
- Murat'ın bakiyesi (sabit satırlar + ek + gün planı birlikte, zaman sırasıyla, aynı anda önce gelir) hiçbir anda 0'ın altına inmez.
- Kod yorumları İngilizce, `/// <summary>` her public üyede; satır satır "ne yapıyor" yorumu yok.
- Canlıya kalıcı yazım ve dış veri indirimi kullanıcı onayından sonra.
- Commit/PR'da AI imzası yok; dal `demo-01-murat-data`.

## Review Focus

1. Mevcut `MuratDay` rastgele çekiliş sırasının bozulması → canlıdaki 11 Ekim – 16 Kasım satırları çakışır. Task 1'in altın dosya testi korur.
2. Taksit işyerinin başka bir alışverişle aynı adı taşıması (ör. Black Friday "Teknosa") → düzenli ödeme kuralı medyan ±%10'u tutturamaz. Task 2'de "taksit işyeri adları başka satırda geçmez" testi.
3. Ek geçmişin bir ayında harcamanın gelirden önce gelmesi (15 Mayıs 2024'ten önce harcama) → eksi bakiye. Task 2'nin bakiye testi.
4. Sabit verinin günlerine (1 Ekim 2025 – 10 Ekim 2026) taban satırı (maaş, Netflix, fatura) eklenmesi → çift maaş/çift fatura. Task 2'de "o aralıkta yalnız ek satır var" testi.
5. Fiyat verisinin canlıya değil sabit veritabanına aktarılması → sabit DB'nin 1.485 satırı değişir. Task 4'te önce/sonra sayım.

---

### Task 1: Canlıda yazılmış satırları altın dosyayla kilitle

**Files:**
- Create: `tests/BankingAI.Data.Tests/Simulation/day-plan-golden.txt`
- Create: `tests/BankingAI.Data.Tests/Simulation/DayPlanGoldenTests.cs`
- Modify: `tests/BankingAI.Data.Tests/BankingAI.Data.Tests.csproj` (altın dosya çıktıya kopyalanır)

**Interfaces:**
- Produces: altın dosya biçimi, satır başına `{Id}|{AccountId}|{Amount}|{Currency}|{Kind}|{RawDescription}|{At:O}` (yalnız `DayPlan.Between(RealisticCutoff, 2026-11-19T21:00Z)` satırları ve onların `ToTransactions()` bacakları).

- [ ] **Step 1:** Test `Every_row_already_planned_for_the_live_database_is_still_planned_the_same_way`: altın dosyanın her satırı güncel `DayPlan.Between(...)` çıktısında birebir bulunur (yeni satır serbest, eksik ya da değişmiş satır hata; mesaj ilk 5 farkı yazar). Altın dosya yoksa ya da `BANKINGAI_UPDATE_DAYPLAN_GOLDEN=1` ise dosyayı üretir ve test başarısız olur ("generated, commit it").
- [ ] **Step 2:** Değişiklikten **önce** üret: `BANKINGAI_UPDATE_DAYPLAN_GOLDEN=1 dotnet test tests/BankingAI.Data.Tests --filter "FullyQualifiedName~DayPlanGoldenTests"` → dosya oluşur; sonra değişkensiz çalıştır → PASS.
- [ ] **Step 3:** Kasıtlı bozma: `MuratDay`'de `Lira(rng, 180, 380)` → `Lira(rng, 181, 380)` → FAIL; `shasum` ile geri dönüşü doğrula → PASS.
- [ ] **Step 4:** `git switch main && git pull && git switch -c demo-01-murat-data`, spec + plan + Task 1 dosyalarını commit: `lock the live day plan rows already written (DEMO-01)`.

### Task 2: Murat hikâyesinin kabul testleri (kırmızı)

**Files:**
- Create: `tests/BankingAI.Application.Tests/Demo/MuratStoryTests.cs` (DB'siz; `Application` ve `Data`'ya erişir)

**Interfaces:**
- Consumes: `SyntheticTransactionSeeder.RealisticDefinitions()`, `DayPlan.Between(...)`, Task 3'ün `DayPlan.MuratBefore(DateTimeOffset cutoff)`, `RecurringPaymentDetector.Detect(IEnumerable<PaymentPoint>, DateOnly)`, `SpendingGroups.Of(string?)`.
- Produces: yardımcı `MuratTimeline(DateTimeOffset until)` → Murat hesabının (`20000000-…-000000000009`) bütün bacakları (sabit + `MuratBefore(RealisticCutoff)` + `Between(RealisticCutoff, until)`), zaman sırasıyla, aynı anda Credit önce. `Today = 2026-11-19`, `Until = 2026-11-19T21:00Z`. Kategori kodu `CategoryId`'den sabit tabloyla (1 FOOD … 11 OTHER).

- [ ] **Step 1:** Testleri yaz (beklenen değerler spec'ten, elle):
  - `Murat_has_two_and_a_half_years`: ilk satır 15 Mayıs 2024, toplam satır ≥ 2.000.
  - `The_balance_never_goes_below_zero` ve `The_history_hands_over_a_modest_balance`: koşan bakiye her an ≥ 0; 1 Ekim 2025 03:00Z'den hemen önce 0–250.000 arası.
  - `Salary_rises_over_the_years`: 15'inde maaş 68.000 (Haz–Ara 2024), 76.000 (2025 Ocak–Eylül), 85.000 (Ekim 2025+); 15 Aralık 2024 ikramiye 34.000.
  - `Routine_payments_on_19_november_2026` (Theory): servisle aynı pencerede (`PeriodKind.Last6Months`, `PeriodResolver` ile İstanbul'da 19 Kasım 2026 için çözülür) `Detect(...) != null` → Apple Store, IKEA, Spotify, iCloud, MACFit, Turkcell, Netflix, Su Faturası; `== null` → MediaMarkt (bitmiş), Disney+ (iptal), Ev İnterneti (zam).
  - `Installments_are_numbered`: Apple Store 12 satır `APPLE STORE TAKSIT 01/12`…`12/12`, 4.250 (5'inde, Haziran 2026'dan); IKEA 9 × 2.800 (25'inde, Ağustos 2026'dan); MediaMarkt 6 × 3.000 (18'inde, Haz–Kas 2024); 19 Kasım'a kadar Apple 6, IKEA 3, MediaMarkt 6 satır.
  - `Installment_merchants_appear_nowhere_else`: Apple Store / IKEA / MediaMarkt yalnız `TAKSIT` satırlarında.
  - `October_2026_is_a_discretionary_month`: Ekim 2026'da `SpendingGroups.Of(...) == Discretionary` toplamı ≥ 27.200 (30 % × 85.000 + 2 % × 85.000).
  - `School_starts_on_5_september_2026`: o gün Murat'ın ≥ 3 harcaması (D&R, LC Waikiki, Migros Jet).
  - `Murat_sends_can_pocket_money`: 2025 Ocak'tan her ayın 1'inde Can'a (`…000000000015`) 2.500 transfer; 3 Kasım 2026'da Selin'den (`…000000000008`) 3.000 gelen transfer.
  - `Rows_before_the_fixed_year_and_inside_it`: `MuratBefore` içinde 1 Ekim 2025 – cutoff arasında "Maaş ödemesi", Netflix, Su, Ev İnterneti, doğalgaz, elektrik satırı yok; hiçbir satırın `At`'ı cutoff'tan sonra değil; kimlikler tekrarsız ve `RealisticDefinitions()` kimlikleriyle kesişmiyor.
- [ ] **Step 2:** `dotnet test tests/BankingAI.Application.Tests --filter "FullyQualifiedName~MuratStoryTests"` → derleme hatası (`MuratBefore` yok): beklenen kırmızı.

### Task 3: Murat'ın ek planı ve geçmişi

**Files:**
- Create: `src/BankingAI.Data/Simulation/DayPlan.Murat.cs`
- Modify: `src/BankingAI.Data/Simulation/DayPlan.cs` (`partial`; `For` sonunda `plan.MuratExtras()`; `MuratDay`'in alışkanlık kısmı `MuratHabits(Random rng)` olarak **çekiliş sırası korunarak** ayrılır)

**Interfaces:**
- Produces: `public static IReadOnlyList<PlannedTransaction> DayPlan.MuratBefore(DateTimeOffset cutoff)` — 15 Mayıs 2024'ten `cutoff`'a kadar (dahil) Murat'ın taban + ek satırları, zaman sırasıyla. Builder içinde `MuratBaseline()` (yalnız `day < 2025-10-01`) ve `MuratExtras()` (yalnız `day >= 2024-05-15`).

- [ ] **Step 1:** `MuratBaseline` (spec tablosu): maaş 15'inde (yukarıdaki tutarlar), ikramiye 15 Aralık 2024 34.000; Ev İnterneti 299,90 (7'si), Netflix 149,99 (2024) / 199,99 (2025) (12'si), Su 380 (22'si), doğalgaz ve elektrik `Gas`/`Power` dizileri (20'si), kira 20.000 "KIRA ODEMESI" → "Ev Sahibi", Bills (1'i; Eylül 2025'te biter; bakiyeyi makul tutar), takvim olayları `MuratDay` ile aynı tarih ve tutarla, alışkanlıklar `MuratHabits(Rng(Murat))`.
- [ ] **Step 2:** `MuratExtras` kendi üreticisiyle: `new Random(SyntheticSeedSettings.RandomSeed + day.DayNumber * 1000 + 509)` (mevcut çekilişlere dokunmaz). İçerik:
  - Taksitler: yukarıdaki üç plan, açıklama `"{MERCHANT} TAKSIT {n:D2}/{N:D2}"`, kategori Other; işyeri adları "Apple Store", "IKEA", "MediaMarkt".
  - Abonelikler: Spotify Aile 99,99 → Mayıs 2026'dan 119,99 (3'ü), iCloud 39,99 (9'u), MACFit 1.250 Ocak 2025'ten (5'i, Entertainment), Disney+ 134,99 Mayıs 2024 – Mart 2025 (14'ü), Turkcell 450 (10'u, Bills).
  - Rutin dışı, sıklık/aylık tutar aralığı: Trendyol 1,5/ay 300–2.500 Clothing; Hepsiburada 1/ay 200–3.000 Other; Zara 0,5/ay 800–3.500 Clothing; D&R 0,7/ay 150–700 Entertainment; Biletix her 3 ayda 1.200–3.000 Entertainment; yaz tatili 10 Ağustos 2024 32.000 ve 10 Ağustos 2025 38.000 "Otel" Travel; 11 Kasım indirimi Trendyol 2.400.
  - Ekim 2026 artışı: 17 Ekim Biletix 2.800, 24 Ekim Zara 4.200, 28 Ekim Trendyol 3.600, 31 Ekim "PlayStation Store" 2.100 Entertainment.
  - 5 Eylül 2026: D&R 1.450, LC Waikiki 2.300 Clothing, Migros Jet 1.850 Grocery.
  - Transferler: Can'a her ayın 1'i 10:30 2.500 (Ocak 2025'ten; `Transfer`), 3 Kasım 2026 Selin'den 3.000 (`Transfer(Selin, Murat, …)`).
- [ ] **Step 3:** `dotnet test tests/BankingAI.Application.Tests --filter "FullyQualifiedName~MuratStoryTests"` → PASS; `dotnet test tests/BankingAI.Data.Tests --filter "FullyQualifiedName~Simulation"` → PASS (altın dosya dahil).
- [ ] **Step 4:** Kasıtlı bozma (her biri tek düzenleme, `shasum` ile geri): kira satırını kaldır → bakiye üst sınır testi FAIL; IKEA sayısı 9 → 8 → taksit testi FAIL; `MuratExtras` üreticisini `Rng(Murat)` yap → altın dosya testi FAIL.
- [ ] **Step 5:** Commit: `give murat two and a half years with installments, subscriptions and discretionary spending (DEMO-01)`.

### Task 4: Yakalama komutu geçmişi de yazar + fiyatlar yalnız canlıda

**Files:**
- Modify: `src/BankingAI.Data/Simulation/LiveSimulator.cs` (`plan = DayPlan.MuratBefore(RealisticCutoff).Concat(DayPlan.Between(RealisticCutoff, until))`)
- Modify: `tests/BankingAI.Data.Tests/Simulation/LiveSimulatorTests.cs`

**Interfaces:**
- Consumes: `DayPlan.MuratBefore`.

- [ ] **Step 1:** Test `The_catch_up_plan_starts_with_murats_history`: `LiveSimulator.PlanUntil(DateTimeOffset until)` (yeni `internal static`, `InternalsVisibleTo` mevcutsa onunla; yoksa `public`) ilk satırı 15 Mayıs 2024, son satırı `until`'den sonra değil → önce FAIL, sonra uygula → PASS.
- [ ] **Step 2:** Tam paket: `dotnet build BankingAI.slnx --no-incremental` 0 uyarı; `dotnet test BankingAI.slnx` hepsi yeşil (Data 223 + yeni testler).
- [ ] **Step 3:** Fiyatlar (dış indirme, onaylı plan): `.env`'de `GOLD_API_KEY` var mı bak (değerini yazdırma). İndir: `market download tcmb --from 2024-05-01 --to 2024-11-08` ve `--from 2026-11-07 --to 2026-11-18`; aynı aralıklar `goldapi`. gold-api 2024'ü vermezse dur, kullanıcıya söyle.
- [ ] **Step 4:** Sabit DB'nin `DailyMarketPrices` sayısını not et (1.485). `source scripts/live-env.sh` ile `market import tcmb` ve `market import gold` aynı aralıklar, **canlıya**. Sonra: canlıda en eski gün ≤ 2024-05-02, en yeni 2026-11-18; sabit DB hâlâ 1.485.
- [ ] **Step 5:** Commit: `write murat's history in the catch-up (DEMO-01)`.

### Task 5: Kuru çalıştırma, onay, canlıya yazım, doğrulama

- [ ] **Step 1:** `source scripts/live-env.sh && dotnet run --project src/BankingAI.Cli -- simulate catch-up --dry-run` (komutun gerçek bayrağını `Program.cs`'ten oku). Sayıları (planlanan / yazılacak / zaten var) kullanıcıya göster; **onay bekle**.
- [ ] **Step 2:** Onaydan sonra kuru olmadan çalıştır; `Skipped` 0 olmalı. Tekrar çalıştır → `Written` 0. Sabit DB'de Murat 227 işlem, toplam satır sayıları önce/sonra aynı.
- [ ] **Step 3:** Canlı API (`scripts/run-live.sh`) ve Murat'ın girişiyle (girişi yoksa kullanıcıya `users set-password murat` komutunu ver) uçları çağır, SQL ile karşılaştır: düzenli ödemeler (8 kalem), yaklaşan ödemeler (Apple 5 Aralık, IKEA 25 Kasım), `spending-advice?period=LastMonth` (≥ 1 öneri), `what-if/one-off?period=LastMonth` (üç varlıkta rakam), transferler (Can, Selin), `transactions?from=2026-09-05&to=2026-09-05` (≥ 3 Murat harcaması). Sonuç tablosu PR'a.
- [ ] **Step 4:** Yedek: `docker exec bankingai-week1-postgres-1 pg_dump -U bankingai_foundation -Fc bankingai_live > ~/bankingai-live-2026-11-19.dump` (repo dışı).

### Task 6: Belgeler, todo, PR

**Files:**
- Modify: `docs/synthetic-seed.md` (yeni bölüm "DEMO-01 Murat, yalnız canlı"), `todo.md` (yeni madde DEMO-01 [x] + bitiş yolu C sırasına not), `RULE.md` §29, `docs/market-data-sources.md` (canlıdaki fiyat aralığı).

- [ ] **Step 1:** Belgeleri yaz (rakamlar Task 5'in ölçtükleri).
- [ ] **Step 2:** Commit, `git push -u origin demo-01-murat-data`, `gh pr create` (Türkçe, bölümlü: ne, kararlar, doğrulama tablosu, bilinen sınırlar), `gh pr merge --squash --delete-branch`, `git switch main && git pull`.
