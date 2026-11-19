using BankingAI.Data.Seeding;

namespace BankingAI.Data.Simulation;

/// <summary>
/// DEMO-01: Murat, the demo persona, for the live database only. Two layers on top of the fixed year and the day plan: the history before
/// the fixed year (15 May 2024 – 30 September 2025: salary, bills, rent, the calendar and the everyday habits) and the extras of every day
/// from 15 May 2024 (installments, subscriptions, discretionary shopping, transfers, a few hand-placed days). The fixed seed is never changed.
/// </summary>
public static partial class DayPlan
{
    /// <summary>The first day of Murat's live history: his salary that morning opens it.</summary>
    public static readonly DateOnly MuratFirstDay = new(2024, 5, 15);

    // The fixed year starts here; the history writes its salary, bills and habits only before it.
    private static readonly DateOnly MuratFixedFirstDay = SyntheticTransactionSeeder.RealisticFirstDay;

    // Installments: merchant, statement text, number of payments, amount, first payment and its day of the month.
    private static readonly (string Merchant, string Raw, int Count, decimal Amount, DateOnly First)[] MuratInstallments =
    [
        ("MediaMarkt", "MEDIAMARKT", 6, 3000m, new(2024, 6, 18)),
        ("Apple Store", "APPLE STORE", 12, 4250m, new(2026, 6, 5)),
        ("IKEA", "IKEA", 9, 2800m, new(2026, 8, 25)),
    ];

    /// <summary>
    /// Murat's rows that the live database gets for the time before <paramref name="cutoff"/>: the history before the fixed year and the
    /// extras up to the cutoff, in time order. Nothing before his first salary, nothing after the cutoff.
    /// </summary>
    public static IReadOnlyList<PlannedTransaction> MuratBefore(DateTimeOffset cutoff)
    {
        var result = new List<PlannedTransaction>();
        for (var day = MuratFirstDay; day <= DateOnly.FromDateTime(cutoff.UtcDateTime).AddDays(1); day = day.AddDays(1))
        {
            var plan = new Builder(day);
            plan.MuratBaseline();
            plan.MuratExtras();
            result.AddRange(plan.Ordered());
        }
        var opening = result.First(p => p.Kind == Enums.TransactionKind.Salary).At;
        return [.. result.Where(p => p.At >= opening && p.At <= cutoff)
            .OrderBy(p => p.At).ThenBy(p => p.Kind == Enums.TransactionKind.Purchase ? 1 : 0)];
    }

    private sealed partial class Builder
    {
        // Before the fixed year only: what the fixed year itself holds from October 2025 on.
        public void MuratBaseline()
        {
            if (day < MuratFirstDay || day >= MuratFixedFirstDay)
                return;
            var before2025 = day < new DateOnly(2025, 1, 1);
            OnDay(15, () => Income(Murat, before2025 ? 68000m : 76000m, "TRY", "Maaş ödemesi", At(9, 0), "murat-salary"));
            // Rent until the family moved in September 2025; it also keeps the hand-over balance of October 2025 modest.
            if (day >= new DateOnly(2024, 6, 1))
                OnDay(1, () => Buy(Murat, 8000m, "TRY", "KIRA ODEMESI", "Ev Sahibi", Bills, At(9, 0), "murat-rent"));
            OnDay(7, () => Buy(Murat, 299.90m, "TRY", "EV INTERNETI FATURA", "Ev İnterneti", Bills, At(9, 0), "murat-internet"));
            OnDay(12, () => Buy(Murat, before2025 ? 149.99m : 199.99m, "TRY", "Netflix aylık abonelik ödemesi", "Netflix", Subscription,
                At(7, 0), "murat-netflix"));
            OnDay(20, () =>
            {
                Buy(Murat, Gas[day.Month - 1], "TRY", "DOGALGAZ FATURA TAHSILATI", "Doğalgaz Faturası", Bills, At(9, 0), "murat-gas");
                Buy(Murat, Power[day.Month - 1], "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(9, 30), "murat-power");
            });
            OnDay(22, () => Buy(Murat, 380m, "TRY", "SU FATURA TAHSILATI", "Su Faturası", Bills, At(9, 0), "murat-water"));
            MuratCalendar(34000m);
            MuratHabits(Rng(Murat));
        }

        // Every day from the first one: its own random numbers, so the days already in the live database keep theirs.
        public void MuratExtras()
        {
            if (day < MuratFirstDay)
                return;
            foreach (var (merchant, raw, count, amount, first) in MuratInstallments)
            {
                var number = (day.Year - first.Year) * 12 + day.Month - first.Month + 1;
                if (day.Day == first.Day && number is >= 1 && number <= count)
                    Buy(Murat, amount, "TRY", $"{raw} TAKSIT {number:D2}/{count:D2}", merchant, Other, At(10, 5),
                        $"murat-x-installment-{raw}");
            }
            OnDay(3, () => Buy(Murat, day < new DateOnly(2026, 5, 1) ? 99.99m : 119.99m, "TRY", "SPOTIFY AILE ABONELIK", "Spotify",
                Subscription, At(7, 10), "murat-x-spotify"));
            OnDay(9, () => Buy(Murat, 39.99m, "TRY", "APPLE ICLOUD DEPOLAMA", "iCloud", Subscription, At(7, 20), "murat-x-icloud"));
            OnDay(10, () => Buy(Murat, 450m, "TRY", "TURKCELL FATURA TAHSILATI", "Turkcell", Bills, At(9, 15), "murat-x-phone"));
            if (day >= new DateOnly(2025, 1, 1))
            {
                OnDay(5, () => Buy(Murat, 1250m, "TRY", "MACFIT UYELIK", "MACFit", Entertainment, At(7, 30), "murat-x-gym"));
                OnDay(1, () => Transfer(Murat, Can, 2500m, At(10, 30), "murat-x-can"));
            }
            // Disney+ was cancelled after March 2025: no longer a routine payment.
            if (day < new DateOnly(2025, 4, 1))
                OnDay(14, () => Buy(Murat, 134.99m, "TRY", "DISNEY PLUS ABONELIK", "Disney+", Subscription, At(7, 40), "murat-x-disney"));
            OnDate(11, 11, () => Buy(Murat, 2400m, "TRY", "TRENDYOL ONLINE SIPARIS", "Trendyol", Clothing, At(12, 11), "murat-x-11-11"));
            OnYearDate(2024, 8, 10, () => Buy(Murat, 32000m, "TRY", "OTEL REZERVASYONU BODRUM", "Otel", Travel, At(22, 4), "murat-x-summer"));
            OnYearDate(2025, 8, 10, () => Buy(Murat, 38000m, "TRY", "OTEL REZERVASYONU FETHIYE", "Otel", Travel, At(21, 37), "murat-x-summer"));
            // School starts: the day the demo asks about ("5 Eylül'de ne harcadım?").
            OnYearDate(2026, 9, 5, () =>
            {
                Buy(Murat, 1850m, "TRY", "POS MIGROS JET ATASEHIR", "Migros Jet", Grocery, At(10, 42), "murat-x-school-market");
                Buy(Murat, 1450m, "TRY", "POS D&R ATASEHIR", "D&R", Entertainment, At(12, 18), "murat-x-school-books");
                Buy(Murat, 2300m, "TRY", "POS LC WAIKIKI ATASEHIR", "LC Waikiki", Clothing, At(14, 6), "murat-x-school-clothes");
            });
            // October 2026, the demo's "last month": a concert, clothes, an order and a game make it a discretionary month.
            OnYearDate(2026, 10, 17, () => Buy(Murat, 2800m, "TRY", "BILETIX KONSER BILETI", "Biletix", Entertainment, At(19, 3), "murat-x-concert"));
            OnYearDate(2026, 10, 24, () => Buy(Murat, 4200m, "TRY", "ZARA ONLINE SIPARIS", "Zara", Clothing, At(15, 22), "murat-x-zara"));
            OnYearDate(2026, 10, 28, () => Buy(Murat, 3600m, "TRY", "TRENDYOL ONLINE SIPARIS", "Trendyol", Clothing, At(21, 48), "murat-x-trendyol"));
            OnYearDate(2026, 10, 31, () => Buy(Murat, 2100m, "TRY", "PLAYSTATION STORE", "PlayStation Store", Entertainment, At(22, 31),
                "murat-x-game"));
            OnYearDate(2026, 11, 3, () => Transfer(Selin, Murat, 3000m, At(18, 12), "murat-x-selin"));
            var rng = new Random(SyntheticSeedSettings.RandomSeed + day.DayNumber * 1000 + 509);
            Monthly(rng, 2, 300, 2500, Murat, "TRENDYOL ONLINE SIPARIS", "Trendyol", Clothing, 20, 23, "murat-x-trendyol-habit");
            Monthly(rng, 1, 200, 3000, Murat, "HEPSIBURADA ONLINE SIPARIS", "Hepsiburada", Other, 12, 23, "murat-x-hepsiburada");
            Chance(rng, 0.5, 800, 3500, Murat, "ZARA ONLINE SIPARIS", "Zara", Clothing, 13, 20, "murat-x-zara-habit");
            Chance(rng, 0.7, 150, 700, Murat, "POS D&R ATASEHIR", "D&R", Entertainment, 15, 20, "murat-x-books");
            Chance(rng, 1.0 / 3, 1200, 3000, Murat, "BILETIX KONSER BILETI", "Biletix", Entertainment, 11, 22, "murat-x-tickets");
            // Small everyday payments: a quick grocery order, parking on weekdays, a taxi, the pharmacy, a weekend breakfast.
            var weekend = !_weekday;
            Daily(rng, 0.35, 150, 600, Murat, "GETIR SIPARIS", "Getir", Grocery, 18, 23, "murat-x-getir");
            Daily(rng, _weekday ? 0.3 : 0, 40, 150, Murat, "ISPARK OTOPARK", "İspark", Transportation, 8, 19, "murat-x-parking");
            Daily(rng, 0.1, 120, 450, Murat, "BITAKSI YOLCULUK", "BiTaksi", Transportation, 7, 23, "murat-x-taxi");
            Chance(rng, 1.5, 120, 900, Murat, "POS ECZANE ATASEHIR", "Eczane", Health, 10, 20, "murat-x-pharmacy");
            Daily(rng, weekend ? 0.45 : 0, 350, 1100, Murat, "POS SIMIT SARAYI ATASEHIR", "Simit Sarayı", Food, 9, 12, "murat-x-breakfast");
        }

        // A daily habit with its own chance; the draws are taken on every day, so other habits keep their numbers.
        private void Daily(Random rng, double chance, int min, int max, Guid account, string raw, string merchant, short category,
            int fromHour, int toHour, string slot)
        {
            var goes = rng.NextDouble() < chance;
            var amount = Lira(rng, min, max);
            var at = Clock(rng, fromHour, toHour);
            if (goes)
                Buy(account, amount, "TRY", raw, merchant, category, at, slot);
        }

        // One event on one date of one year.
        private void OnYearDate(int year, int month, int dayOfMonth, Action plan)
        {
            if (day == new DateOnly(year, month, dayOfMonth))
                plan();
        }

        // A habit of a fraction of a time a month (0,5 = every other month on average).
        private void Chance(Random rng, double timesAMonth, int min, int max, Guid account, string raw, string merchant, short category,
            int fromHour, int toHour, string slot)
        {
            var goes = rng.NextDouble() < timesAMonth / 30;
            var amount = Lira(rng, min, max);
            var at = Clock(rng, fromHour, toHour);
            if (goes)
                Buy(account, amount, "TRY", raw, merchant, category, at, slot);
        }
    }
}
