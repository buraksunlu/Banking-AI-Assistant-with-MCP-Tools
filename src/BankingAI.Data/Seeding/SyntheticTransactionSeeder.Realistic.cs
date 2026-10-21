using BankingAI.Data.Entities;
using BankingAI.Data.Enums;

namespace BankingAI.Data.Seeding;

/// <summary>
/// SYN-15: a year of realistic data (1 October 2025 – 10 October 2026) for six new personas, so tests and model
/// evaluations do not only see the three tidy demo customers. Ada, Deniz and Ege are never touched (decision K1-B).
/// </summary>
/// <remarks>
/// Two layers (K2-C): <b>story</b> transactions are written out by hand (incomes, subscriptions, seasonal bills, a price
/// increase, a cancellation, a large one-off purchase, a Berlin month boundary, shop purchases and transfers) and their
/// expected results are hand-computed in the tests; <b>background</b> transactions (daily coffee, weekly groceries, fuel,
/// pharmacy) come from a seeded <see cref="Random"/>, so every run produces the same list and the same golden totals.
/// Identifiers: persona blocks 11000–16999, pairs 19000+ (PairId ...0101+), assigned in date order.
/// </remarks>
public static partial class SyntheticTransactionSeeder
{
    /// <summary>First day of the realistic year.</summary>
    public static readonly DateOnly RealisticFirstDay = new(2025, 10, 1);

    /// <summary>Last full day of background data.</summary>
    public static readonly DateOnly RealisticLastDay = new(2026, 10, 9);

    /// <summary>The demo moment: nothing is written at or after it (a monthly payment due on 15 October is not there yet).</summary>
    public static readonly DateTimeOffset RealisticCutoff = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Writes the realistic year; already present transactions are left as they are.</summary>
    public static Task<int> SeedRealisticAsync(BankingDbContext db, CancellationToken cancellationToken = default) =>
        SeedAsync(db, RealisticDefinitions(), cancellationToken);

    /// <summary>The realistic year in write order (date order, incomes first), identical on every call.</summary>
    public static IReadOnlyList<Transaction> RealisticDefinitions() => new RealisticYear().Build();

    private sealed class RealisticYear
    {
        private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

        // Accounts (SyntheticAccountSeeder) and the identifier block of each persona.
        private static readonly Guid Selin = Account(8), Murat = Account(9), ZeynepTry = Account(10), ZeynepUsd = Account(11),
            Hasan = Account(12), ElifEur = Account(13), ElifTry = Account(14), Can = Account(15), CanShop = Account(16);

        private const short Food = 1, Grocery = 2, Clothing = 3, Transportation = 4, Entertainment = 5, Subscription = 6,
            Travel = 7, Fuel = 8, Bills = 9, Health = 10, Other = 11;

        private readonly List<Draft> _drafts = [];

        public IReadOnlyList<Transaction> Build()
        {
            Openings();
            SelinStory();
            MuratStory();
            ZeynepStory();
            HasanStory();
            ElifStory();
            CanStory();
            Background();
            return Number();
        }

        private void Openings()
        {
            // The balance each account had on 1 October 2025 (the data begins there, the accounts are older).
            foreach (var (account, amount, currency) in new[]
                     {
                         (Selin, 5000m, "TRY"), (Murat, 30000m, "TRY"), (ZeynepTry, 15000m, "TRY"), (ZeynepUsd, 300m, "USD"),
                         (Hasan, 8000m, "TRY"), (ElifEur, 3000m, "EUR"), (Can, 5000m, "TRY"), (CanShop, 250000m, "TRY")
                     })
                Income(account, amount, currency, TransactionKind.OpeningFunding, "Devreden bakiye", At(RealisticFirstDay, 6, 0));
        }

        private void SelinStory()
        {
            foreach (var month in Months())
            {
                Income(Selin, 8000m, "TRY", TransactionKind.Salary, "Burs ödemesi", At(month, 1, 9, 0));
                Buy(Selin, 59.99m, "TRY", "Spotify öğrenci aylık abonelik ödemesi", "Spotify", Subscription, At(month, 3, 7, 0));
                Buy(Selin, 350m, "TRY", "MOBIL HAT FATURA TAHSILATI", "Mobil Hat Faturası", Bills, At(month, 10, 10, 0));
                // Monthly pocket money from her grandfather (a transfer is income, never spending).
                Transfer(Hasan, Selin, 3000m, At(month, 26, 10, 0));
            }
            ShopBuy(Selin, 320m, At(new(2025, 10, 6), 15, 0));
            ShopBuy(Selin, 210m, At(new(2026, 2, 12), 15, 0));
            ShopBuy(Selin, 480m, At(new(2026, 9, 16), 15, 0));
            // The demo moment's "today": one coffee in the morning of 10 October.
            Buy(Selin, 85m, "TRY", "POS 2201 KAMPUS KAFE", "Kampüs Kafe", Food, At(new(2026, 10, 10), 8, 15));
        }

        private void MuratStory()
        {
            // Seasonal bills, October 2025 → September 2026 (gas peaks in January).
            decimal[] gas = [650m, 1850m, 3400m, 4100m, 3700m, 2300m, 1200m, 600m, 450m, 420m, 410m, 520m];
            decimal[] power = [980m, 1050m, 1240m, 1310m, 1180m, 1020m, 940m, 990m, 1260m, 1480m, 1520m, 1090m];
            var index = 0;
            foreach (var month in Months())
            {
                Income(Murat, 85000m, "TRY", TransactionKind.Salary, "Maaş ödemesi", At(month, 15, 9, 0));
                // Home internet: 299,90 until July 2026, then 449,90 (a 50 % rise: no longer one recurring payment).
                var internet = month < new DateOnly(2026, 8, 1) ? 299.90m : 449.90m;
                Buy(Murat, internet, "TRY", "EV INTERNETI FATURA", "Ev İnterneti", Bills, At(month, 7, 9, 0));
                Buy(Murat, 229.99m, "TRY", "Netflix aylık abonelik ödemesi", "Netflix", Subscription, At(month, 12, 7, 0));
                if (index < gas.Length)
                {
                    Buy(Murat, gas[index], "TRY", "DOGALGAZ FATURA TAHSILATI", "Doğalgaz Faturası", Bills, At(month, 20, 9, 0));
                    Buy(Murat, power[index], "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(month, 20, 9, 30));
                    Buy(Murat, 380m, "TRY", "SU FATURA TAHSILATI", "Su Faturası", Bills, At(month, 22, 9, 0));
                }
                index++;
            }
            Buy(Murat, 25000m, "TRY", "OKUL TAKSIT ODEMESI", "Okul Taksidi", Bills, At(new(2026, 2, 16), 10, 0));
            Buy(Murat, 25000m, "TRY", "OKUL TAKSIT ODEMESI", "Okul Taksidi", Bills, At(new(2026, 9, 15), 10, 0));
            Buy(Murat, 45000m, "TRY", "POS 8812 TEKNOLOJI MAGAZASI BEYAZ ESYA", "Teknoloji Mağazası", Other, At(new(2026, 3, 14), 14, 0));
            ShopBuy(Murat, 640m, At(new(2025, 10, 3), 17, 0));
            ShopBuy(Murat, 380m, At(new(2026, 2, 14), 17, 0));
            ShopBuy(Murat, 1250m, At(new(2026, 9, 8), 17, 0));
        }

        private void ZeynepStory()
        {
            // Freelance work: irregular days and amounts (there is no separate income kind, so these are Salary credits).
            foreach (var (day, amount) in new (DateOnly, decimal)[]
                     {
                         (new(2025, 10, 17), 32000m), (new(2025, 11, 28), 18500m), (new(2026, 1, 9), 45000m),
                         (new(2026, 2, 20), 12000m), (new(2026, 3, 31), 27500m), (new(2026, 5, 14), 38000m),
                         (new(2026, 6, 30), 9500m), (new(2026, 8, 5), 52000m), (new(2026, 9, 22), 21000m)
                     })
                Income(ZeynepTry, amount, "TRY", TransactionKind.Salary, "Proje ödemesi", At(day, 11, 0));
            foreach (var (day, amount) in new (DateOnly, decimal)[]
                     {
                         (new(2025, 11, 3), 1800m), (new(2026, 1, 15), 2200m), (new(2026, 3, 10), 1500m),
                         (new(2026, 5, 25), 2400m), (new(2026, 7, 20), 1950m), (new(2026, 9, 12), 2100m)
                     })
                Income(ZeynepUsd, amount, "USD", TransactionKind.Salary, "Yurt dışı proje ödemesi", At(day, 16, 0));
            foreach (var month in Months())
            {
                Buy(ZeynepTry, 3500m, "TRY", "ORTAK OFIS AYLIK UYELIK", "Ortak Ofis", Bills, At(month, 2, 9, 0));
                Buy(ZeynepTry, 450m, "TRY", "MOBIL HAT FATURA TAHSILATI", "Mobil Hat Faturası", Bills, At(month, 10, 10, 0));
                // Subscriptions in dollars: recurring payments in a second currency, never added to the lira ones.
                Buy(ZeynepUsd, 22.99m, "USD", "ADOBE CREATIVE CLOUD", "Adobe", Subscription, At(month, 9, 8, 0));
                Buy(ZeynepUsd, 15m, "USD", "FIGMA PROFESSIONAL", "Figma", Subscription, At(month, 20, 8, 0));
            }
        }

        private void HasanStory()
        {
            foreach (var month in Months())
            {
                Income(Hasan, 22000m, "TRY", TransactionKind.Salary, "Emekli maaşı", At(month, 25, 9, 0));
                Buy(Hasan, 780m, "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(month, 18, 9, 0));
            }
            ShopBuy(Hasan, 180m, At(new(2026, 1, 10), 11, 0));
            ShopBuy(Hasan, 260m, At(new(2026, 5, 22), 11, 0));
        }

        private void ElifStory()
        {
            foreach (var month in Months())
            {
                Income(ElifEur, 4200m, "EUR", TransactionKind.Salary, "Gehalt", BerlinAt(month, 28, 10, 0));
                Buy(ElifEur, 1350m, "EUR", "MIETE WOHNUNG", "Kira", Bills, BerlinAt(month, 1, 8, 0));
                Buy(ElifEur, 58m, "EUR", "BVG MONATSKARTE", "BVG", Transportation, BerlinAt(month, 1, 8, 30));
            }
            Buy(ElifEur, 289m, "EUR", "FLUGTICKET BERLIN ISTANBUL", "Havayolu Bileti", Travel, BerlinAt(new(2026, 7, 25), 20, 0));
            // 31 August 23:30 in Berlin is 21:30 UTC: still August for Elif, already 1 September in Istanbul time.
            Buy(ElifEur, 64.30m, "EUR", "REWE BERLIN MITTE", "REWE", Grocery, BerlinAt(new(2026, 8, 31), 23, 30));
            // Two weeks in Türkiye (3–16 August 2026) on a lira account opened for the trip.
            Income(ElifTry, 25000m, "TRY", TransactionKind.OpeningFunding, "TRY hesabı açılış", At(new(2026, 8, 1), 10, 0));
            foreach (var (day, hour, amount, raw, merchant, category) in new (int, int, decimal, string, string, short)[]
                     {
                         (3, 19, 860m, "POS LOKANTA BEYOGLU", "Lokanta", Food),
                         (4, 11, 1240m, "POS MIGROS JET CIHANGIR", "Migros Jet", Grocery),
                         (5, 14, 420m, "TAKSI ODEMESI", "Taksi", Transportation),
                         (6, 10, 210m, "POS KAHVE EVI", "Kahve Evi", Food),
                         (8, 13, 600m, "MUZE GIRIS", "Müze", Entertainment),
                         (10, 17, 340m, "POS ECZANE", "Eczane", Health),
                         (12, 20, 1150m, "POS LOKANTA KARAKOY", "Lokanta", Food),
                         (14, 9, 120m, "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", Transportation),
                         (15, 12, 1800m, "POS CARSI HEDIYELIK", "Çarşı Hediyelik", Other),
                         (16, 8, 650m, "TAKSI ODEMESI", "Taksi", Transportation)
                     })
                Buy(ElifTry, amount, "TRY", raw, merchant, category, At(new(2026, 8, day), hour, 0));
        }

        private void CanStory()
        {
            foreach (var month in Months())
            {
                // The shop pays its owner every month (own accounts: a transfer, not spending).
                Transfer(CanShop, Can, 18000m, At(month, 1, 9, 0));
                // Netflix stops after June 2026: by 10 October it is no longer a recurring payment.
                if (month <= new DateOnly(2026, 6, 1))
                    Buy(Can, 229.99m, "TRY", "Netflix aylık abonelik ödemesi", "Netflix", Subscription, At(month, 12, 7, 0));
                // Spotify rises from 99,99 to 109,99 in July 2026: within ±10 % of the median, still one recurring payment.
                if (month <= new DateOnly(2026, 9, 1))
                    Buy(Can, month < new DateOnly(2026, 7, 1) ? 99.99m : 109.99m, "TRY", "Spotify aylık abonelik ödemesi",
                        "Spotify", Subscription, At(month, 18, 7, 0));
            }
        }

        private void Background()
        {
            BackgroundSelin(new Random(SyntheticSeedSettings.RandomSeed + 4));
            BackgroundMurat(new Random(SyntheticSeedSettings.RandomSeed + 5));
            BackgroundZeynep(new Random(SyntheticSeedSettings.RandomSeed + 6));
            BackgroundHasan(new Random(SyntheticSeedSettings.RandomSeed + 7));
            BackgroundElif(new Random(SyntheticSeedSettings.RandomSeed + 8));
            BackgroundCan(new Random(SyntheticSeedSettings.RandomSeed + 9));
        }

        private void BackgroundSelin(Random rng)
        {
            foreach (var day in Days())
            {
                // August 2026: summer school abroad, nothing but the fixed payments.
                var away = day.Year == 2026 && day.Month == 8;
                var coffee = rng.NextDouble() < 0.75;
                var starbucks = rng.NextDouble() < 0.35;
                var coffeeAmount = starbucks ? Lira(rng, 130, 210) : Lira(rng, 55, 140);
                var coffeeHour = rng.Next(10, 18);
                var weekday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
                var lunch = weekday && rng.NextDouble() < 0.6;
                var lunchAmount = Lira(rng, 45, 75);
                var ride = weekday && rng.NextDouble() < 0.5;
                var rideAmount = Lira(rng, 20, 35);
                var book = rng.NextDouble() < 0.12;
                var bookAmount = Lira(rng, 150, 450);
                if (away)
                    continue;
                if (coffee)
                    Buy(Selin, coffeeAmount, "TRY", starbucks ? "POS 4412 STARBUCKS KADIKOY" : "POS 2201 KAMPUS KAFE",
                        starbucks ? "Starbucks" : "Kampüs Kafe", Food, At(day, coffeeHour, 10));
                if (lunch)
                    Buy(Selin, lunchAmount, "TRY", "KAMPUS YEMEKHANE", "Yemekhane", Food, At(day, 12, 30));
                if (ride)
                    Buy(Selin, rideAmount, "TRY", "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", Transportation, At(day, 8, 15));
                if (book)
                    Buy(Selin, bookAmount, "TRY", "POS KITAPCI MODA", "Kitapçı", Entertainment, At(day, 16, 40));
            }
        }

        private void BackgroundMurat(Random rng)
        {
            // One or two market visits a week, at four chains; "Migros Jet" and "Migros Sanal Market" share a name.
            foreach (var week in Weeks())
            {
                var visits = rng.NextDouble() < 0.5 ? 2 : 1;
                for (var visit = 0; visit < visits; visit++)
                {
                    var day = week.AddDays(rng.Next(0, 7));
                    var pick = rng.NextDouble();
                    var amount = Lira(rng, 400, 3500);
                    var hour = rng.Next(11, 20);
                    var (raw, merchant) = pick switch
                    {
                        < 0.4 => ("POS MIGROS JET ATASEHIR", "Migros Jet"),
                        < 0.6 => ("MIGROS SANAL MARKET SIPARIS", "Migros Sanal Market"),
                        < 0.8 => ("POS A101 ATASEHIR", "A101"),
                        _ => ("POS BIM ATASEHIR", "BİM")
                    };
                    if (InRange(day))
                        Buy(Murat, amount, "TRY", raw, merchant, Grocery, At(day, hour, 20));
                }
            }
            foreach (var month in Months())
            {
                for (var fill = 0; fill < 2; fill++)
                    Monthly(rng, month, 1800, 2700, Murat, "POS AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", Fuel, 18);
                for (var dinner = 0; dinner < 2; dinner++)
                    Monthly(rng, month, 600, 1800, Murat, "POS LOKANTA ATASEHIR", "Lokanta", Food, 20);
                Monthly(rng, month, 400, 700, Murat, "SINEMA BILET", "Sinema", Entertainment, 21);
                if (month.Month is 10 or 1 or 4 or 7)
                    Monthly(rng, month, 900, 2400, Murat, "POS 0731 COCUK GIYIM", "Çocuk Giyim", Clothing, 15);
            }
        }

        private void BackgroundZeynep(Random rng)
        {
            foreach (var day in Days())
            {
                var coffee = rng.NextDouble() < 3.0 / 7;
                var coffeeAmount = Lira(rng, 90, 180);
                var dinner = rng.NextDouble() < 1.0 / 14;
                var dinnerAmount = Lira(rng, 350, 1200);
                if (coffee)
                    Buy(ZeynepTry, coffeeAmount, "TRY", "POS KAHVE EVI", "Kahve Evi", Food, At(day, 10, 45));
                if (dinner)
                    Buy(ZeynepTry, dinnerAmount, "TRY", "POS LOKANTA MODA", "Lokanta", Food, At(day, 20, 15));
            }
            foreach (var week in Weeks())
            {
                var day = week.AddDays(rng.Next(0, 7));
                var migros = rng.NextDouble() < 0.5;
                var amount = Lira(rng, 300, 1400);
                if (InRange(day))
                    Buy(ZeynepTry, amount, "TRY", migros ? "POS MIGROS JET MODA" : "POS A101 MODA",
                        migros ? "Migros Jet" : "A101", Grocery, At(day, 18, 30));
            }
        }

        private void BackgroundHasan(Random rng)
        {
            foreach (var month in Months())
                for (var visit = 0; visit < 2; visit++)
                    Monthly(rng, month, 180, 950, Hasan, "POS ECZANE", "Eczane", Health, 10);
            foreach (var week in Weeks())
            {
                var day = week.AddDays(rng.Next(0, 7));
                var bim = rng.NextDouble() < 0.6;
                var amount = Lira(rng, 250, 1200);
                if (InRange(day))
                    Buy(Hasan, amount, "TRY", bim ? "POS BIM USKUDAR" : "POS A101 USKUDAR", bim ? "BİM" : "A101", Grocery, At(day, 10, 0));
            }
        }

        private void BackgroundElif(Random rng)
        {
            foreach (var week in Weeks())
            {
                var visits = rng.NextDouble() < 0.5 ? 2 : 1;
                for (var visit = 0; visit < visits; visit++)
                {
                    var day = week.AddDays(rng.Next(0, 7));
                    var rewe = rng.NextDouble() < 0.55;
                    var amount = Euro(rng, 2500, 11000);
                    if (InRange(day) && !InTurkey(day))
                        Buy(ElifEur, amount, "EUR", rewe ? "REWE BERLIN MITTE" : "LIDL BERLIN MITTE", rewe ? "REWE" : "Lidl", Grocery,
                            BerlinAt(day, 18, 0));
                }
            }
            foreach (var month in Months())
                for (var lunch = 0; lunch < 3; lunch++)
                {
                    var day = month.AddDays(rng.Next(0, 28));
                    var amount = Euro(rng, 900, 3500);
                    if (InRange(day) && !InTurkey(day))
                        Buy(ElifEur, amount, "EUR", "DOENER IMBISS", "Döner Imbiss", Food, BerlinAt(day, 13, 0));
                }
        }

        private void BackgroundCan(Random rng)
        {
            foreach (var week in Weeks())
            {
                var meals = rng.NextDouble() < 0.5 ? 2 : 1;
                for (var meal = 0; meal < meals; meal++)
                {
                    var day = week.AddDays(rng.Next(0, 7));
                    var cafe = rng.NextDouble() < 0.5;
                    var amount = Lira(rng, 150, 900);
                    if (InRange(day))
                        Buy(Can, amount, "TRY", cafe ? "POS KAHVE EVI" : "POS LOKANTA MODA", cafe ? "Kahve Evi" : "Lokanta", Food, At(day, 13, 15));
                }
                var shops = rng.NextDouble() < 0.5 ? 2 : 1;
                for (var shop = 0; shop < shops; shop++)
                {
                    var day = week.AddDays(rng.Next(0, 7));
                    var migros = rng.NextDouble() < 0.5;
                    var amount = Lira(rng, 300, 1500);
                    if (InRange(day))
                        Buy(Can, amount, "TRY", migros ? "POS MIGROS JET MODA" : "POS A101 MODA", migros ? "Migros Jet" : "A101", Grocery,
                            At(day, 19, 0));
                }
            }
            foreach (var month in Months())
            {
                for (var fill = 0; fill < 2; fill++)
                    Monthly(rng, month, 1500, 2400, Can, "POS AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", Fuel, 8);
                // A card payment the bank cannot name: no merchant and no category, never "Diğer" (VAL-04).
                var unknown = rng.NextDouble() < 0.6;
                var terminal = rng.Next(1000, 10000);
                var day = month.AddDays(rng.Next(0, 28));
                var amount = Lira(rng, 120, 600);
                if (unknown && InRange(day))
                    Add(Can, amount, "TRY", TransactionKind.Purchase, $"POS {terminal} ODEME", null, null, At(day, 14, 0));
            }
        }

        private void Monthly(Random rng, DateOnly month, int min, int max, Guid account, string raw, string merchant, short category, int hour)
        {
            var day = month.AddDays(rng.Next(0, 28));
            var amount = Lira(rng, min, max);
            if (InRange(day))
                Buy(account, amount, "TRY", raw, merchant, category, At(day, hour, 0));
        }

        private void Income(Guid account, decimal amount, string currency, TransactionKind kind, string raw, DateTimeOffset at) =>
            Add(account, amount, currency, kind, raw, null, null, at);

        private void Buy(Guid account, decimal amount, string currency, string raw, string merchant, short category, DateTimeOffset at) =>
            Add(account, amount, currency, TransactionKind.Purchase, raw, merchant, category, at);

        private void Add(Guid account, decimal amount, string currency, TransactionKind kind, string raw, string? merchant, short? category,
            DateTimeOffset at) =>
            Keep(new Draft(account, amount, currency, kind, raw, merchant, category, at, null));

        private void ShopBuy(Guid buyer, decimal amount, DateTimeOffset at) =>
            Keep(new Draft(buyer, amount, "TRY", TransactionKind.Purchase, "POS 3301 CAN KIRTASIYE MODA", "Can Kırtasiye", Other, at, CanShop));

        private void Transfer(Guid from, Guid to, decimal amount, DateTimeOffset at) =>
            Keep(new Draft(from, amount, "TRY", TransactionKind.Transfer, "Para gönderimi", null, null, at, to));

        // Monthly patterns run into October 2026; what falls after the demo moment has not happened yet.
        private void Keep(Draft draft)
        {
            if (draft.At < RealisticCutoff)
                _drafts.Add(draft);
        }

        private List<Transaction> Number()
        {
            // Date order, incomes before spending at the same moment, then insertion order: write order never overdraws.
            var ordered = _drafts.Select((draft, index) => (draft, index))
                .OrderBy(x => x.draft.At).ThenBy(x => x.draft.Kind == TransactionKind.Purchase ? 1 : 0).ThenBy(x => x.index)
                .Select(x => x.draft).ToList();
            var next = new Dictionary<int, int>();
            var pair = 100;
            var pairOrdinal = 19000;
            var transactions = new List<Transaction>();
            foreach (var draft in ordered)
            {
                var direction = draft.Kind == TransactionKind.Purchase || draft.Kind == TransactionKind.Transfer
                    ? TransactionDirection.Debit : TransactionDirection.Credit;
                var source = draft.Category is null ? (CategorizationSource?)null : CategorizationSource.Rule;
                if (draft.Counterpart is { } counterpart)
                {
                    pair++;
                    var credit = draft.Kind == TransactionKind.Purchase
                        ? (TransactionKind.Sale, "Can Kırtasiye satış geliri")
                        : (TransactionKind.Transfer, "Gelen para transferi");
                    transactions.Add(Create(++pairOrdinal, draft.Account, draft.Amount, TransactionDirection.Debit, draft.Kind, draft.Currency,
                        draft.Raw, draft.Merchant, draft.Category, source, draft.At, PairId(pair)));
                    transactions.Add(Create(++pairOrdinal, counterpart, draft.Amount, TransactionDirection.Credit, credit.Item1, draft.Currency,
                        credit.Item2, null, null, null, draft.At, PairId(pair)));
                    continue;
                }
                var block = Block(draft.Account);
                next[block] = next.GetValueOrDefault(block, block) + 1;
                transactions.Add(Create(next[block], draft.Account, draft.Amount, direction, draft.Kind, draft.Currency, draft.Raw,
                    draft.Merchant, draft.Category, source, draft.At));
            }
            return transactions;
        }

        private static int Block(Guid account) =>
            account == Selin ? 11000
            : account == Murat ? 12000
            : account == ZeynepTry || account == ZeynepUsd ? 13000
            : account == Hasan ? 14000
            : account == ElifEur || account == ElifTry ? 15000
            : account == Can || account == CanShop ? 16000
            : throw new InvalidOperationException($"SYN-15 hesabı değil: {account}.");

        private static IEnumerable<DateOnly> Months()
        {
            for (var month = RealisticFirstDay; month <= RealisticLastDay; month = month.AddMonths(1))
                yield return month;
        }

        private static IEnumerable<DateOnly> Days()
        {
            for (var day = RealisticFirstDay; day <= RealisticLastDay; day = day.AddDays(1))
                yield return day;
        }

        // Mondays from the one on or before the first day; days outside the range are dropped by the callers.
        private static IEnumerable<DateOnly> Weeks()
        {
            var monday = RealisticFirstDay.AddDays(-(((int)RealisticFirstDay.DayOfWeek + 6) % 7));
            for (var week = monday; week <= RealisticLastDay; week = week.AddDays(7))
                yield return week;
        }

        private static bool InRange(DateOnly day) => day >= RealisticFirstDay && day <= RealisticLastDay;

        private static bool InTurkey(DateOnly day) => day >= new DateOnly(2026, 8, 3) && day <= new DateOnly(2026, 8, 16);

        private static DateTimeOffset At(DateOnly month, int day, int hour, int minute) => At(new DateOnly(month.Year, month.Month, day), hour, minute);

        // Istanbul has no daylight saving time since 2016: local time is always UTC+3.
        private static DateTimeOffset At(DateOnly day, int hour, int minute) =>
            new DateTimeOffset(day.Year, day.Month, day.Day, hour, minute, 0, Istanbul).ToUniversalTime();

        private static DateTimeOffset BerlinAt(DateOnly month, int day, int hour, int minute) =>
            BerlinAt(new DateOnly(month.Year, month.Month, day), hour, minute);

        private static DateTimeOffset BerlinAt(DateOnly day, int hour, int minute)
        {
            var local = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, Berlin.GetUtcOffset(local)).ToUniversalTime();
        }

        // Whole or half liras, the way card amounts usually look.
        private static decimal Lira(Random rng, int min, int max) => rng.Next(min * 2, max * 2 + 1) / 2m;

        private static decimal Euro(Random rng, int minCents, int maxCents) => rng.Next(minCents, maxCents + 1) / 100m;

        private static Guid Account(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:D12}");

        // Counterpart: the shop (a purchase becomes a Sale there) or the receiving account of a transfer.
        private sealed record Draft(Guid Account, decimal Amount, string Currency, TransactionKind Kind, string Raw, string? Merchant,
            short? Category, DateTimeOffset At, Guid? Counterpart);
    }
}
