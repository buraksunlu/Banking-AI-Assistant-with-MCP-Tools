using System.Security.Cryptography;
using System.Text;
using BankingAI.Data.Enums;
using BankingAI.Data.Seeding;

namespace BankingAI.Data.Simulation;

/// <summary>
/// The live bank's day (RT-02a): what the six SYN-15 personas do on a given day, after the fixed data ends. The rhythm is the realistic
/// year's (S2-A): the same salaries, bills and subscriptions on the same days of the month, and the same daily habits. The fixed seed
/// itself is never changed; this is its continuation for the live database.
/// </summary>
/// <remarks>
/// A day is planned from that day alone (S3-A): fixed payments by the day of the month, the background from a <see cref="Random"/>
/// seeded with the persona and the date, so a day planned again is the same day and nothing depends on the days before it. A weekly
/// or monthly habit of the fixed year ("one or two market visits a week") becomes the daily chance with the same average.
/// Ada, Deniz and Ege, the hand-computed demo customers, are not part of it (S1-A).
/// </remarks>
public static class DayPlan
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

    private static readonly Guid Selin = Account(8), Murat = Account(9), ZeynepTry = Account(10), ZeynepUsd = Account(11),
        Hasan = Account(12), ElifEur = Account(13), Can = Account(15), CanShop = Account(16);

    private const short Food = 1, Grocery = 2, Clothing = 3, Transportation = 4, Entertainment = 5, Subscription = 6, Fuel = 8, Bills = 9,
        Health = 10;

    // Murat's seasonal bills by calendar month, January first: the realistic year's amounts (October 2025 – September 2026).
    private static readonly decimal[] Gas = [4100m, 3700m, 2300m, 1200m, 600m, 450m, 420m, 410m, 520m, 650m, 1850m, 3400m];
    private static readonly decimal[] Power = [1310m, 1180m, 1020m, 940m, 990m, 1260m, 1480m, 1520m, 1090m, 980m, 1050m, 1240m];

    /// <summary>Every transaction of one day, in time order (incomes first at the same moment).</summary>
    public static IReadOnlyList<PlannedTransaction> For(DateOnly day)
    {
        var plan = new Builder(day);
        plan.SelinDay();
        plan.MuratDay();
        plan.ZeynepDay();
        plan.HasanDay();
        plan.ElifDay();
        plan.CanDay();
        return plan.Ordered();
    }

    /// <summary>The planned transactions after <paramref name="after"/> up to and including <paramref name="until"/>, in time order.</summary>
    public static IReadOnlyList<PlannedTransaction> Between(DateTimeOffset after, DateTimeOffset until)
    {
        var result = new List<PlannedTransaction>();
        // One day either side: Berlin's evening and Istanbul's early morning fall on another UTC date.
        for (var day = DateOnly.FromDateTime(after.UtcDateTime).AddDays(-1); day <= DateOnly.FromDateTime(until.UtcDateTime).AddDays(1);
             day = day.AddDays(1))
            result.AddRange(For(day).Where(p => p.At > after && p.At <= until));
        return [.. result.OrderBy(p => p.At).ThenBy(p => p.Kind == TransactionKind.Purchase ? 1 : 0)];
    }

    private sealed class Builder(DateOnly day)
    {
        private readonly List<PlannedTransaction> _plan = [];
        private readonly bool _weekday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public void SelinDay()
        {
            OnDay(1, () => Income(Selin, 8000m, "TRY", "Burs ödemesi", At(9, 0), "selin-grant"));
            OnDay(3, () => Buy(Selin, 59.99m, "TRY", "Spotify öğrenci aylık abonelik ödemesi", "Spotify", Subscription, At(7, 0), "selin-spotify"));
            OnDay(10, () => Buy(Selin, 350m, "TRY", "MOBIL HAT FATURA TAHSILATI", "Mobil Hat Faturası", Bills, At(10, 0), "selin-phone"));
            var rng = Rng(Selin);
            var coffee = rng.NextDouble() < 0.75;
            var starbucks = rng.NextDouble() < 0.35;
            var coffeeAmount = starbucks ? Lira(rng, 130, 210) : Lira(rng, 55, 140);
            var coffeeHour = rng.Next(10, 18);
            var lunch = _weekday && rng.NextDouble() < 0.6;
            var lunchAmount = Lira(rng, 45, 75);
            var ride = _weekday && rng.NextDouble() < 0.5;
            var rideAmount = Lira(rng, 20, 35);
            var book = rng.NextDouble() < 0.12;
            var bookAmount = Lira(rng, 150, 450);
            if (coffee)
                Buy(Selin, coffeeAmount, "TRY", starbucks ? "POS 4412 STARBUCKS KADIKOY" : "POS 2201 KAMPUS KAFE",
                    starbucks ? "Starbucks" : "Kampüs Kafe", Food, At(coffeeHour, 10), "selin-coffee");
            if (lunch)
                Buy(Selin, lunchAmount, "TRY", "KAMPUS YEMEKHANE", "Yemekhane", Food, At(12, 30), "selin-lunch");
            if (ride)
                Buy(Selin, rideAmount, "TRY", "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", Transportation, At(8, 15), "selin-ride");
            if (book)
                Buy(Selin, bookAmount, "TRY", "POS KITAPCI MODA", "Kitapçı", Entertainment, At(16, 40), "selin-book");
        }

        public void MuratDay()
        {
            OnDay(15, () => Income(Murat, 85000m, "TRY", "Maaş ödemesi", At(9, 0), "murat-salary"));
            // The 50 % rise of August 2026 stays: 449,90 a month.
            OnDay(7, () => Buy(Murat, 449.90m, "TRY", "EV INTERNETI FATURA", "Ev İnterneti", Bills, At(9, 0), "murat-internet"));
            OnDay(12, () => Buy(Murat, 229.99m, "TRY", "Netflix aylık abonelik ödemesi", "Netflix", Subscription, At(7, 0), "murat-netflix"));
            OnDay(20, () =>
            {
                Buy(Murat, Gas[day.Month - 1], "TRY", "DOGALGAZ FATURA TAHSILATI", "Doğalgaz Faturası", Bills, At(9, 0), "murat-gas");
                Buy(Murat, Power[day.Month - 1], "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(9, 30), "murat-power");
            });
            OnDay(22, () => Buy(Murat, 380m, "TRY", "SU FATURA TAHSILATI", "Su Faturası", Bills, At(9, 0), "murat-water"));
            var rng = Rng(Murat);
            // One or two market visits a week at four chains: 1,5 a week.
            for (var visit = 0; visit < 2; visit++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var pick = rng.NextDouble();
                var amount = Lira(rng, 400, 3500);
                var hour = rng.Next(11, 20);
                var (raw, merchant) = pick switch
                {
                    < 0.4 => ("POS MIGROS JET ATASEHIR", "Migros Jet"),
                    < 0.6 => ("MIGROS SANAL MARKET SIPARIS", "Migros Sanal Market"),
                    < 0.8 => ("POS A101 ATASEHIR", "A101"),
                    _ => ("POS BIM ATASEHIR", "BİM"),
                };
                if (goes)
                    Buy(Murat, amount, "TRY", raw, merchant, Grocery, At(hour, 20), $"murat-market-{visit}");
            }
            Monthly(rng, 2, 1800, 2700, Murat, "POS AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", Fuel, 18, "murat-fuel");
            Monthly(rng, 2, 600, 1800, Murat, "POS LOKANTA ATASEHIR", "Lokanta", Food, 20, "murat-dinner");
            Monthly(rng, 1, 400, 700, Murat, "SINEMA BILET", "Sinema", Entertainment, 21, "murat-cinema");
            if (day.Month is 10 or 1 or 4 or 7)
                Monthly(rng, 1, 900, 2400, Murat, "POS 0731 COCUK GIYIM", "Çocuk Giyim", Clothing, 15, "murat-clothes");
        }

        public void ZeynepDay()
        {
            OnDay(2, () => Buy(ZeynepTry, 3500m, "TRY", "ORTAK OFIS AYLIK UYELIK", "Ortak Ofis", Bills, At(9, 0), "zeynep-office"));
            OnDay(10, () => Buy(ZeynepTry, 450m, "TRY", "MOBIL HAT FATURA TAHSILATI", "Mobil Hat Faturası", Bills, At(10, 0), "zeynep-phone"));
            OnDay(9, () => Buy(ZeynepUsd, 22.99m, "USD", "ADOBE CREATIVE CLOUD", "Adobe", Subscription, At(8, 0), "zeynep-adobe"));
            OnDay(20, () => Buy(ZeynepUsd, 15m, "USD", "FIGMA PROFESSIONAL", "Figma", Subscription, At(8, 0), "zeynep-figma"));
            var rng = Rng(ZeynepTry);
            // Freelance work: about one project payment in lira every 40 days and one in dollars every 55, of irregular size.
            var project = rng.NextDouble() < 1.0 / 40;
            var projectAmount = rng.Next(19, 105) * 500m;
            var abroad = rng.NextDouble() < 1.0 / 55;
            var abroadAmount = rng.Next(30, 49) * 50m;
            var coffee = rng.NextDouble() < 3.0 / 7;
            var coffeeAmount = Lira(rng, 90, 180);
            var dinner = rng.NextDouble() < 1.0 / 14;
            var dinnerAmount = Lira(rng, 350, 1200);
            var market = rng.NextDouble() < 1.0 / 7;
            var migros = rng.NextDouble() < 0.5;
            var marketAmount = Lira(rng, 300, 1400);
            if (project)
                Income(ZeynepTry, projectAmount, "TRY", "Proje ödemesi", At(11, 0), "zeynep-project");
            if (abroad)
                Income(ZeynepUsd, abroadAmount, "USD", "Yurt dışı proje ödemesi", At(16, 0), "zeynep-abroad");
            if (coffee)
                Buy(ZeynepTry, coffeeAmount, "TRY", "POS KAHVE EVI", "Kahve Evi", Food, At(10, 45), "zeynep-coffee");
            if (dinner)
                Buy(ZeynepTry, dinnerAmount, "TRY", "POS LOKANTA MODA", "Lokanta", Food, At(20, 15), "zeynep-dinner");
            if (market)
                Buy(ZeynepTry, marketAmount, "TRY", migros ? "POS MIGROS JET MODA" : "POS A101 MODA", migros ? "Migros Jet" : "A101", Grocery,
                    At(18, 30), "zeynep-market");
        }

        public void HasanDay()
        {
            OnDay(25, () => Income(Hasan, 22000m, "TRY", "Emekli maaşı", At(9, 0), "hasan-pension"));
            OnDay(18, () => Buy(Hasan, 780m, "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(9, 0), "hasan-power"));
            // Selin's monthly pocket money: a transfer is income for her, never spending.
            OnDay(26, () => Transfer(Hasan, Selin, 3000m, At(10, 0), "hasan-to-selin"));
            var rng = Rng(Hasan);
            Monthly(rng, 2, 180, 950, Hasan, "POS ECZANE", "Eczane", Health, 10, "hasan-pharmacy");
            var market = rng.NextDouble() < 1.0 / 7;
            var bim = rng.NextDouble() < 0.6;
            var amount = Lira(rng, 250, 1200);
            if (market)
                Buy(Hasan, amount, "TRY", bim ? "POS BIM USKUDAR" : "POS A101 USKUDAR", bim ? "BİM" : "A101", Grocery, At(10, 0), "hasan-market");
        }

        public void ElifDay()
        {
            OnDay(28, () => Income(ElifEur, 4200m, "EUR", "Gehalt", BerlinAt(10, 0), "elif-salary"));
            OnDay(1, () =>
            {
                Buy(ElifEur, 1350m, "EUR", "MIETE WOHNUNG", "Kira", Bills, BerlinAt(8, 0), "elif-rent");
                Buy(ElifEur, 58m, "EUR", "BVG MONATSKARTE", "BVG", Transportation, BerlinAt(8, 30), "elif-bvg");
            });
            var rng = Rng(ElifEur);
            for (var visit = 0; visit < 2; visit++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var rewe = rng.NextDouble() < 0.55;
                var amount = Euro(rng, 2500, 11000);
                if (goes)
                    Buy(ElifEur, amount, "EUR", rewe ? "REWE BERLIN MITTE" : "LIDL BERLIN MITTE", rewe ? "REWE" : "Lidl", Grocery, BerlinAt(18, 0),
                        $"elif-market-{visit}");
            }
            var lunch = rng.NextDouble() < 3.0 / 30;
            var lunchAmount = Euro(rng, 900, 3500);
            if (lunch)
                Buy(ElifEur, lunchAmount, "EUR", "DOENER IMBISS", "Döner Imbiss", Food, BerlinAt(13, 0), "elif-lunch");
        }

        public void CanDay()
        {
            // The shop pays its owner every month (own accounts: a transfer, not spending). Netflix and Spotify were cancelled.
            OnDay(1, () => Transfer(CanShop, Can, 18000m, At(9, 0), "shop-to-can"));
            var rng = Rng(Can);
            for (var meal = 0; meal < 2; meal++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var cafe = rng.NextDouble() < 0.5;
                var amount = Lira(rng, 150, 900);
                if (goes)
                    Buy(Can, amount, "TRY", cafe ? "POS KAHVE EVI" : "POS LOKANTA MODA", cafe ? "Kahve Evi" : "Lokanta", Food, At(13, 15),
                        $"can-meal-{meal}");
            }
            for (var shop = 0; shop < 2; shop++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var migros = rng.NextDouble() < 0.5;
                var amount = Lira(rng, 300, 1500);
                if (goes)
                    Buy(Can, amount, "TRY", migros ? "POS MIGROS JET MODA" : "POS A101 MODA", migros ? "Migros Jet" : "A101", Grocery, At(19, 0),
                        $"can-market-{shop}");
            }
            Monthly(rng, 2, 1500, 2400, Can, "POS AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", Fuel, 8, "can-fuel");
            // A card payment the bank cannot name: no merchant and no category, never "Diğer" (VAL-04).
            var unknown = rng.NextDouble() < 0.6 / 30;
            var terminal = rng.Next(1000, 10000);
            var unknownAmount = Lira(rng, 120, 600);
            if (unknown)
                Add(Can, unknownAmount, "TRY", TransactionKind.Purchase, $"POS {terminal} ODEME", null, null, At(14, 0), "can-unknown");
        }

        public IReadOnlyList<PlannedTransaction> Ordered() =>
            [.. _plan.Select((p, index) => (p, index)).OrderBy(x => x.p.At).ThenBy(x => x.p.Kind == TransactionKind.Purchase ? 1 : 0)
                .ThenBy(x => x.index).Select(x => x.p)];

        private void OnDay(int dayOfMonth, Action plan)
        {
            if (day.Day == dayOfMonth)
                plan();
        }

        // A habit of N times a month on random days: its daily chance, so the month averages N.
        private void Monthly(Random rng, int timesAMonth, int min, int max, Guid account, string raw, string merchant, short category,
            int hour, string slot)
        {
            for (var time = 0; time < timesAMonth; time++)
            {
                var goes = rng.NextDouble() < 1.0 / 30;
                var amount = Lira(rng, min, max);
                if (goes)
                    Buy(account, amount, "TRY", raw, merchant, category, At(hour, 0), $"{slot}-{time}");
            }
        }

        private void Income(Guid account, decimal amount, string currency, string raw, DateTimeOffset at, string slot) =>
            Add(account, amount, currency, TransactionKind.Salary, raw, null, null, at, slot);

        private void Buy(Guid account, decimal amount, string currency, string raw, string merchant, short category, DateTimeOffset at,
            string slot) =>
            Add(account, amount, currency, TransactionKind.Purchase, raw, merchant, category, at, slot);

        private void Add(Guid account, decimal amount, string currency, TransactionKind kind, string raw, string? merchant, short? category,
            DateTimeOffset at, string slot) =>
            _plan.Add(new PlannedTransaction(Id(slot), account, amount, currency, kind, raw, merchant, category, at));

        private void Transfer(Guid from, Guid to, decimal amount, DateTimeOffset at, string slot) =>
            _plan.Add(new PlannedTransaction(Id(slot), from, amount, "TRY", TransactionKind.Transfer, "Para gönderimi", null, null, at, to,
                Id($"{slot}-credit"), Id($"{slot}-pair")));

        // S3-A: the identifier is the plan's own name; the same day and slot is always the same row.
        private Guid Id(string slot) => NameId($"simulator:{day:yyyy-MM-dd}:{slot}");

        // The persona and the date seed the day; another day or another persona never shares its numbers.
        private Random Rng(Guid account) => new(SyntheticSeedSettings.RandomSeed + day.DayNumber * 1000 + AccountNumber(account));

        // The account's number in its fixed identifier (8–16): small, stable and different for every persona.
        private static int AccountNumber(Guid account) =>
            (int)(ulong.Parse(account.ToString("N")[20..], System.Globalization.NumberStyles.HexNumber) % 1000);

        private DateTimeOffset At(int hour, int minute) =>
            new DateTimeOffset(day.Year, day.Month, day.Day, hour, minute, 0, Istanbul).ToUniversalTime();

        private DateTimeOffset BerlinAt(int hour, int minute)
        {
            var local = new DateTime(day.Year, day.Month, day.Day, hour, minute, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, Berlin.GetUtcOffset(local)).ToUniversalTime();
        }
    }

    // Whole or half liras, the way card amounts usually look.
    private static decimal Lira(Random rng, int min, int max) => rng.Next(min * 2, max * 2 + 1) / 2m;

    private static decimal Euro(Random rng, int minCents, int maxCents) => rng.Next(minCents, maxCents + 1) / 100m;

    /// <summary>A name-based identifier: the first 16 bytes of the name's SHA-256, marked as a version 8 (custom) UUID.</summary>
    public static Guid NameId(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static Guid Account(int number) => Guid.Parse($"20000000-0000-0000-0000-{number:D12}");
}
