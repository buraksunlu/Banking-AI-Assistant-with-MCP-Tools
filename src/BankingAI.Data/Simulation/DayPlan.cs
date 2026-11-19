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
public static partial class DayPlan
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

    private static readonly Guid Selin = Account(8), Murat = Account(9), ZeynepTry = Account(10), ZeynepUsd = Account(11),
        Hasan = Account(12), ElifEur = Account(13), ElifTry = Account(14), Can = Account(15), CanShop = Account(16);

    private const short Food = 1, Grocery = 2, Clothing = 3, Transportation = 4, Entertainment = 5, Subscription = 6, Travel = 7, Fuel = 8,
        Bills = 9, Health = 10, Other = 11;

    // Murat's seasonal bills by calendar month, January first: the realistic year's amounts (October 2025 – September 2026).
    private static readonly decimal[] Gas = [4100m, 3700m, 2300m, 1200m, 600m, 450m, 420m, 410m, 520m, 650m, 1850m, 3400m];
    private static readonly decimal[] Power = [1310m, 1180m, 1020m, 940m, 990m, 1260m, 1480m, 1520m, 1090m, 980m, 1050m, 1240m];

    // Elif's new year in Istanbul (20 December – 2 January): 8.700 lira, so two visits fit her 17.610.
    private static readonly (int Month, int Day, int Hour, int Minute, decimal Amount, string Raw, string Merchant, short Category)[] IstanbulVisit =
    [
        (12, 21, 19, 42, 950m, "POS LOKANTA BESIKTAS", "Lokanta", Food),
        (12, 22, 11, 7, 1380m, "POS MIGROS JET BESIKTAS", "Migros Jet", Grocery),
        (12, 23, 15, 31, 460m, "TAKSI ODEMESI", "Taksi", Transportation),
        (12, 24, 13, 18, 720m, "MUZE GIRIS", "Müze", Entertainment),
        (12, 26, 20, 53, 1650m, "POS LOKANTA KARAKOY", "Lokanta", Food),
        (12, 28, 12, 26, 1400m, "POS CARSI HEDIYELIK", "Çarşı Hediyelik", Other),
        (12, 31, 22, 14, 1450m, "POS MEYHANE KUMKAPI", "Meyhane", Food),
        (1, 2, 8, 9, 690m, "TAKSI ODEMESI", "Taksi", Transportation),
    ];

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
        plan.MuratExtras();
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

    private sealed partial class Builder(DateOnly day)
    {
        private readonly List<PlannedTransaction> _plan = [];
        private readonly bool _weekday = day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        public void SelinDay()
        {
            OnDay(1, () => Income(Selin, 8000m, "TRY", "Burs ödemesi", At(9, 0), "selin-grant"));
            OnDay(3, () => Buy(Selin, 59.99m, "TRY", "Spotify öğrenci aylık abonelik ödemesi", "Spotify", Subscription, At(7, 0), "selin-spotify"));
            OnDay(10, () => Buy(Selin, 350m, "TRY", "MOBIL HAT FATURA TAHSILATI", "Mobil Hat Faturası", Bills, At(10, 0), "selin-phone"));
            // RT-05a calendar: a concert on the republic day, a new year's present, the January sales.
            OnDate(10, 29, () => Buy(Selin, 750m, "TRY", "BILETIX KONSER BILETI", "Biletix", Entertainment, At(14, 23), "selin-concert"));
            OnDate(12, 24, () => Buy(Selin, 650m, "TRY", "POS LC WAIKIKI KADIKOY", "LC Waikiki", Clothing, At(17, 48), "selin-gift"));
            OnDate(1, 10, () => Buy(Selin, 900m, "TRY", "POS LC WAIKIKI KADIKOY", "LC Waikiki", Clothing, At(15, 37), "selin-sales"));
            var rng = Rng(Selin);
            var coffee = rng.NextDouble() < 0.75;
            var starbucks = rng.NextDouble() < 0.35;
            var coffeeAmount = starbucks ? Lira(rng, 130, 210) : Lira(rng, 55, 140);
            var coffeeAt = Clock(rng, 10, 18);
            var lunch = _weekday && rng.NextDouble() < 0.6;
            var lunchAmount = Lira(rng, 45, 75);
            var lunchAt = Clock(rng, 12, 14);
            var ride = _weekday && rng.NextDouble() < 0.5;
            var rideAmount = Lira(rng, 20, 35);
            var rideAt = Clock(rng, 7, 9);
            var book = rng.NextDouble() < 0.12;
            var bookAmount = Lira(rng, 150, 450);
            var bookAt = Clock(rng, 15, 19);
            if (coffee)
                Buy(Selin, coffeeAmount, "TRY", starbucks ? "POS 4412 STARBUCKS KADIKOY" : "POS 2201 KAMPUS KAFE",
                    starbucks ? "Starbucks" : "Kampüs Kafe", Food, coffeeAt, "selin-coffee");
            if (lunch)
                Buy(Selin, lunchAmount, "TRY", "KAMPUS YEMEKHANE", "Yemekhane", Food, lunchAt, "selin-lunch");
            if (ride)
                Buy(Selin, rideAmount, "TRY", "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", Transportation, rideAt, "selin-ride");
            if (book)
                Buy(Selin, bookAmount, "TRY", "POS KITAPCI MODA", "Kitapçı", Entertainment, bookAt, "selin-book");
            MonthlyShop(rng, 2, 40, 250, Selin, 11, 18, "selin-stationery");
        }

        public void MuratDay()
        {
            OnDay(15, () => Income(Murat, 85000m, "TRY", "Maaş ödemesi", At(9, 0), "murat-salary"));
            // The 50 % rise of August 2026 stays: 449,90 a month.
            OnDay(7, () => Buy(Murat, 449.90m, "TRY", "EV INTERNETI FATURA", "Ev İnterneti", Bills, At(9, 0), "murat-internet"));
            // January 2027: 229,99 → 249,99, within the ±10 % of a recurring payment.
            OnDay(12, () => Buy(Murat, day < new DateOnly(2027, 1, 1) ? 229.99m : 249.99m, "TRY", "Netflix aylık abonelik ödemesi", "Netflix",
                Subscription, At(7, 0), "murat-netflix"));
            // A new subscription: recurring from its third payment (February 2027).
            if (day >= new DateOnly(2026, 12, 3))
                OnDay(3, () => Buy(Murat, 79.99m, "TRY", "YOUTUBE PREMIUM AYLIK", "YouTube Premium", Subscription, At(7, 0), "murat-youtube"));
            OnDay(20, () =>
            {
                Buy(Murat, Gas[day.Month - 1], "TRY", "DOGALGAZ FATURA TAHSILATI", "Doğalgaz Faturası", Bills, At(9, 0), "murat-gas");
                Buy(Murat, Power[day.Month - 1], "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(9, 30), "murat-power");
            });
            OnDay(22, () => Buy(Murat, 380m, "TRY", "SU FATURA TAHSILATI", "Su Faturası", Bills, At(9, 0), "murat-water"));
            MuratCalendar(42500m);
            MuratHabits(Rng(Murat));
        }

        // The yearly events of Murat's calendar; the bonus is the year's own.
        private void MuratCalendar(decimal bonus)
        {
            // RT-05a calendar: the republic-day holiday, the paediatrician, Black Friday, the bonus and the presents, the half-term
            // holiday booked in January (a travel month far above the usual one), school shopping at Can's shop.
            OnDate(10, 20, () => Buy(Murat, 14800m, "TRY", "OTEL REZERVASYONU", "Otel", Travel, At(21, 14), "murat-holiday"));
            OnDate(11, 10, () => Buy(Murat, 1400m, "TRY", "COCUK DOKTORU MUAYENE", "Çocuk Doktoru", Health, At(17, 42), "murat-doctor"));
            if (IsBlackFriday)
                Buy(Murat, 21999m, "TRY", "TEKNOSA ONLINE SIPARIS", "Teknosa", Other, At(10, 12), "murat-black-friday");
            OnDate(12, 15, () => Income(Murat, bonus, "TRY", "İkramiye ödemesi", At(9, 0), "murat-bonus"));
            OnDate(12, 27, () => Buy(Murat, 1850m, "TRY", "POS OYUNCAKCI ATASEHIR", "Oyuncakçı", Entertainment, At(16, 27), "murat-presents"));
            OnDate(1, 10, () => Buy(Murat, 26000m, "TRY", "OTEL REZERVASYONU", "Otel", Travel, At(20, 51), "murat-half-term"));
            OnDate(9, 8, () => ShopBuy(Murat, 2500m, At(18, 33), "murat-school"));
        }

        // Murat's everyday habits; the draws keep their order, so a day already written to the live database is planned again unchanged.
        private void MuratHabits(Random rng)
        {
            // One or two market visits a week at four chains: 1,5 a week.
            for (var visit = 0; visit < 2; visit++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var pick = rng.NextDouble();
                var amount = Lira(rng, 400, 3500);
                var at = Clock(rng, 11, 20);
                var (raw, merchant) = pick switch
                {
                    < 0.4 => ("POS MIGROS JET ATASEHIR", "Migros Jet"),
                    < 0.6 => ("MIGROS SANAL MARKET SIPARIS", "Migros Sanal Market"),
                    < 0.8 => ("POS A101 ATASEHIR", "A101"),
                    _ => ("POS BIM ATASEHIR", "BİM"),
                };
                if (goes)
                    Buy(Murat, amount, "TRY", raw, merchant, Grocery, at, $"murat-market-{visit}");
            }
            var lunch = _weekday && rng.NextDouble() < 0.6;
            var lunchAmount = Lira(rng, 180, 380);
            var lunchAt = Clock(rng, 12, 14);
            var coffee = _weekday && rng.NextDouble() < 0.4;
            var coffeeAmount = Lira(rng, 85, 160);
            var coffeeAt = Clock(rng, 8, 10);
            if (lunch)
                Buy(Murat, lunchAmount, "TRY", "YEMEKSEPETI SIPARIS", "Yemeksepeti", Food, lunchAt, "murat-lunch");
            if (coffee)
                Buy(Murat, coffeeAmount, "TRY", "POS KAHVE DUNYASI ATASEHIR", "Kahve Dünyası", Food, coffeeAt, "murat-coffee");
            Monthly(rng, 2, 1800, 2700, Murat, "POS AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", Fuel, 17, 20, "murat-fuel");
            Monthly(rng, 2, 600, 1800, Murat, "POS LOKANTA ATASEHIR", "Lokanta", Food, 19, 22, "murat-dinner");
            Monthly(rng, 1, 400, 700, Murat, "SINEMA BILET", "Sinema", Entertainment, 20, 22, "murat-cinema");
            MonthlyShop(rng, 1, 150, 600, Murat, 17, 20, "murat-stationery");
            if (day.Month is 10 or 1 or 4 or 7)
                Monthly(rng, 1, 900, 2400, Murat, "POS 0731 COCUK GIYIM", "Çocuk Giyim", Clothing, 14, 19, "murat-clothes");
        }

        public void ZeynepDay()
        {
            OnDay(2, () => Buy(ZeynepTry, 3500m, "TRY", "ORTAK OFIS AYLIK UYELIK", "Ortak Ofis", Bills, At(9, 0), "zeynep-office"));
            OnDay(10, () => Buy(ZeynepTry, 450m, "TRY", "MOBIL HAT FATURA TAHSILATI", "Mobil Hat Faturası", Bills, At(10, 0), "zeynep-phone"));
            OnDay(9, () => Buy(ZeynepUsd, 22.99m, "USD", "ADOBE CREATIVE CLOUD", "Adobe", Subscription, At(8, 0), "zeynep-adobe"));
            // Figma is cancelled after December 2026: it drops out of the upcoming payments.
            if (day < new DateOnly(2027, 1, 1))
                OnDay(20, () => Buy(ZeynepUsd, 15m, "USD", "FIGMA PROFESSIONAL", "Figma", Subscription, At(8, 0), "zeynep-figma"));
            // RT-05a calendar: a regular client abroad pays every 5 November; Black Friday, new year's eve, the sales, Valentine's day.
            OnDate(11, 5, () => Income(ZeynepUsd, 2400m, "USD", "Yurt dışı proje ödemesi", At(16, 0), "zeynep-client"));
            if (IsBlackFriday)
                Buy(ZeynepTry, 4850m, "TRY", "ZARA ONLINE SIPARIS", "Zara", Clothing, At(13, 8), "zeynep-black-friday");
            OnDate(12, 31, () => Buy(ZeynepTry, 1900m, "TRY", "POS MEYHANE KUMKAPI", "Meyhane", Food, At(23, 4), "zeynep-new-year"));
            OnDate(1, 8, () => Buy(ZeynepTry, 2600m, "TRY", "ZARA ONLINE SIPARIS", "Zara", Clothing, At(11, 46), "zeynep-sales"));
            OnDate(2, 14, () => Buy(ZeynepTry, 1600m, "TRY", "POS RESTORAN NISANTASI", "Restoran", Food, At(21, 17), "zeynep-valentine"));
            var rng = Rng(ZeynepTry);
            // Freelance work: about one project payment in lira every 40 days and one in dollars every 55, of irregular size.
            var project = rng.NextDouble() < 1.0 / 40;
            var projectAmount = rng.Next(19, 105) * 500m;
            var abroad = rng.NextDouble() < 1.0 / 55;
            var abroadAmount = rng.Next(30, 49) * 50m;
            var coffee = rng.NextDouble() < 3.0 / 7;
            var coffeeAmount = Lira(rng, 90, 180);
            var coffeeAt = Clock(rng, 9, 12);
            var dinner = rng.NextDouble() < 1.0 / 14;
            var dinnerAmount = Lira(rng, 350, 1200);
            var dinnerAt = Clock(rng, 19, 22);
            var market = rng.NextDouble() < 1.0 / 7;
            var migros = rng.NextDouble() < 0.5;
            var marketAmount = Lira(rng, 300, 1400);
            var marketAt = Clock(rng, 17, 21);
            var scooter = rng.NextDouble() < 0.3;
            var scooterAmount = Lira(rng, 25, 90);
            var scooterAt = Clock(rng, 9, 19);
            var lunch = _weekday && rng.NextDouble() < 0.35;
            var lunchAmount = Lira(rng, 160, 340);
            var lunchAt = Clock(rng, 12, 15);
            // A project payment on the client's own day would be a second one: that day belongs to the client.
            if (project)
                Income(ZeynepTry, projectAmount, "TRY", "Proje ödemesi", At(11, 0), "zeynep-project");
            if (abroad && !(day.Month == 11 && day.Day == 5))
                Income(ZeynepUsd, abroadAmount, "USD", "Yurt dışı proje ödemesi", At(16, 0), "zeynep-abroad");
            if (coffee)
                Buy(ZeynepTry, coffeeAmount, "TRY", "POS KAHVE EVI", "Kahve Evi", Food, coffeeAt, "zeynep-coffee");
            if (dinner)
                Buy(ZeynepTry, dinnerAmount, "TRY", "POS LOKANTA MODA", "Lokanta", Food, dinnerAt, "zeynep-dinner");
            if (market)
                Buy(ZeynepTry, marketAmount, "TRY", migros ? "POS MIGROS JET MODA" : "POS A101 MODA", migros ? "Migros Jet" : "A101", Grocery,
                    marketAt, "zeynep-market");
            if (scooter)
                Buy(ZeynepTry, scooterAmount, "TRY", "MARTI SCOOTER SURUS", "Martı", Transportation, scooterAt, "zeynep-scooter");
            if (lunch)
                Buy(ZeynepTry, lunchAmount, "TRY", "YEMEKSEPETI SIPARIS", "Yemeksepeti", Food, lunchAt, "zeynep-lunch");
            Monthly(rng, 1, 80, 250, ZeynepTry, "KARGO GONDERIM UCRETI", "Kargo", Other, 10, 17, "zeynep-cargo");
            MonthlyShop(rng, 1, 80, 400, ZeynepTry, 10, 18, "zeynep-stationery");
        }

        public void HasanDay()
        {
            // The January 2027 pension rise: +15 %.
            OnDay(25, () => Income(Hasan, day < new DateOnly(2027, 1, 1) ? 22000m : 25300m, "TRY", "Emekli maaşı", At(9, 0), "hasan-pension"));
            OnDay(18, () => Buy(Hasan, 780m, "TRY", "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", Bills, At(9, 0), "hasan-power"));
            // Selin's monthly pocket money: a transfer is income for her, never spending.
            OnDay(26, () => Transfer(Hasan, Selin, 3000m, At(10, 0), "hasan-to-selin"));
            OnDate(1, 14, () => Buy(Hasan, 9500m, "TRY", "DIS HEKIMI MUAYENE", "Diş Hekimi", Health, At(11, 26), "hasan-dentist"));
            OnDate(2, 6, () => Buy(Hasan, 1250m, "TRY", "NOTER HARC ODEMESI", "Noter", Other, At(10, 41), "hasan-notary"));
            var rng = Rng(Hasan);
            Monthly(rng, 2, 180, 950, Hasan, "POS ECZANE", "Eczane", Health, 9, 12, "hasan-pharmacy");
            var market = rng.NextDouble() < 1.0 / 7;
            var bim = rng.NextDouble() < 0.6;
            var amount = Lira(rng, 250, 1200);
            var marketAt = Clock(rng, 9, 12);
            var corner = rng.NextDouble() < 0.5;
            var cornerAmount = Lira(rng, 30, 140);
            var cornerAt = Clock(rng, 8, 19);
            if (market)
                Buy(Hasan, amount, "TRY", bim ? "POS BIM USKUDAR" : "POS A101 USKUDAR", bim ? "BİM" : "A101", Grocery, marketAt, "hasan-market");
            if (corner)
                Buy(Hasan, cornerAmount, "TRY", "POS BAKKAL USKUDAR", "Bakkal", Grocery, cornerAt, "hasan-corner-shop");
        }

        public void ElifDay()
        {
            OnDay(28, () => Income(ElifEur, 4200m, "EUR", "Gehalt", BerlinAt(10, 0), "elif-salary"));
            OnDay(1, () =>
            {
                Buy(ElifEur, 1350m, "EUR", "MIETE WOHNUNG", "Kira", Bills, BerlinAt(8, 0), "elif-rent");
                Buy(ElifEur, 58m, "EUR", "BVG MONATSKARTE", "BVG", Transportation, BerlinAt(8, 30), "elif-bvg");
            });
            // RT-05a: the new year in Istanbul, the flight in euros and the visit on her lira account (two visits fit its balance).
            OnDate(12, 1, () => Buy(ElifEur, 312m, "EUR", "FLUGTICKET BERLIN ISTANBUL", "Havayolu Bileti", Travel, BerlinAt(19, 36), "elif-flight"));
            foreach (var (month, dayOfMonth, hour, minute, amount, raw, merchant, category) in IstanbulVisit)
                OnDate(month, dayOfMonth, () => Buy(ElifTry, amount, "TRY", raw, merchant, category, At(hour, minute), "elif-visit"));
            var rng = Rng(ElifEur);
            for (var visit = 0; visit < 2; visit++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var rewe = rng.NextDouble() < 0.55;
                var amount = Euro(rng, 2500, 11000);
                var (hour, minute) = (rng.Next(17, 20), rng.Next(0, 60));
                if (goes)
                    Buy(ElifEur, amount, "EUR", rewe ? "REWE BERLIN MITTE" : "LIDL BERLIN MITTE", rewe ? "REWE" : "Lidl", Grocery,
                        BerlinAt(hour, minute), $"elif-market-{visit}");
            }
            var lunch = rng.NextDouble() < 3.0 / 30;
            var lunchAmount = Euro(rng, 900, 3500);
            var lunchMinute = rng.Next(0, 60);
            if (lunch)
                Buy(ElifEur, lunchAmount, "EUR", "DOENER IMBISS", "Döner Imbiss", Food, BerlinAt(13, lunchMinute), "elif-lunch");
        }

        public void CanDay()
        {
            // The shop pays its owner what the personas' purchases at the shop can carry (T3-A); Netflix and Spotify were cancelled.
            OnDay(1, () => Transfer(CanShop, Can, 3000m, At(9, 0), "shop-to-can"));
            OnDate(2, 14, () => Buy(Can, 2400m, "TRY", "POS RESTORAN NISANTASI", "Restoran", Food, At(20, 38), "can-valentine"));
            OnDate(3, 15, () => Buy(Can, 6800m, "TRY", "OTO SERVIS BAKIM", "Oto Servis", Transportation, At(10, 19), "can-car-service"));
            var rng = Rng(Can);
            for (var meal = 0; meal < 2; meal++)
            {
                var goes = rng.NextDouble() < 1.5 / 14;
                var cafe = rng.NextDouble() < 0.5;
                var amount = Lira(rng, 150, 900);
                var at = Clock(rng, 12, 15);
                if (goes)
                    Buy(Can, amount, "TRY", cafe ? "POS KAHVE EVI" : "POS LOKANTA MODA", cafe ? "Kahve Evi" : "Lokanta", Food, at,
                        $"can-meal-{meal}");
            }
            // Once a week: the shop pays him 3.000 a month now, not the fixed year's 18.000 (T3-A).
            for (var shop = 0; shop < 2; shop++)
            {
                var goes = rng.NextDouble() < 1.0 / 14;
                var migros = rng.NextDouble() < 0.5;
                var amount = Lira(rng, 300, 1500);
                var at = Clock(rng, 18, 21);
                if (goes)
                    Buy(Can, amount, "TRY", migros ? "POS MIGROS JET MODA" : "POS A101 MODA", migros ? "Migros Jet" : "A101", Grocery, at,
                        $"can-market-{shop}");
            }
            var simit = _weekday && rng.NextDouble() < 0.5;
            var simitAmount = Lira(rng, 20, 80);
            var simitAt = Clock(rng, 7, 10);
            if (simit)
                Buy(Can, simitAmount, "TRY", "POS SIMITCI MODA", "Simitçi", Food, simitAt, "can-simit");
            Monthly(rng, 2, 1500, 2400, Can, "POS AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", Fuel, 7, 10, "can-fuel");
            // A card payment the bank cannot name: no merchant and no category, never "Diğer" (VAL-04).
            var unknown = rng.NextDouble() < 0.6 / 30;
            var terminal = rng.Next(1000, 10000);
            var unknownAmount = Lira(rng, 120, 600);
            var unknownAt = Clock(rng, 11, 18);
            if (unknown)
                Add(Can, unknownAmount, "TRY", TransactionKind.Purchase, $"POS {terminal} ODEME", null, null, unknownAt, "can-unknown");
        }

        public IReadOnlyList<PlannedTransaction> Ordered() =>
            [.. _plan.Select((p, index) => (p, index)).OrderBy(x => x.p.At).ThenBy(x => x.p.Kind == TransactionKind.Purchase ? 1 : 0)
                .ThenBy(x => x.index).Select(x => x.p)];

        private void OnDay(int dayOfMonth, Action plan)
        {
            if (day.Day == dayOfMonth)
                plan();
        }

        // A calendar event: the same day of the same month every year (T5-A).
        private void OnDate(int month, int dayOfMonth, Action plan)
        {
            if (day.Month == month && day.Day == dayOfMonth)
                plan();
        }

        // The last Friday of November.
        private bool IsBlackFriday => day.Month == 11 && day.DayOfWeek == DayOfWeek.Friday && day.AddDays(7).Month == 12;

        // A habit of N times a month on random days: its daily chance, so the month averages N.
        private void Monthly(Random rng, int timesAMonth, int min, int max, Guid account, string raw, string merchant, short category,
            int fromHour, int toHour, string slot)
        {
            for (var time = 0; time < timesAMonth; time++)
            {
                var goes = rng.NextDouble() < 1.0 / 30;
                var amount = Lira(rng, min, max);
                var at = Clock(rng, fromHour, toHour);
                if (goes)
                    Buy(account, amount, "TRY", raw, merchant, category, at, $"{slot}-{time}");
            }
        }

        // The same habit at Can's shop: each purchase is a pair, the shop's sale its other leg (T3-A).
        private void MonthlyShop(Random rng, int timesAMonth, int min, int max, Guid account, int fromHour, int toHour, string slot)
        {
            for (var time = 0; time < timesAMonth; time++)
            {
                var goes = rng.NextDouble() < 1.0 / 30;
                var amount = Lira(rng, min, max);
                var at = Clock(rng, fromHour, toHour);
                if (goes)
                    ShopBuy(account, amount, at, $"{slot}-{time}");
            }
        }

        private void ShopBuy(Guid account, decimal amount, DateTimeOffset at, string slot) =>
            _plan.Add(new PlannedTransaction(Id(slot), account, amount, "TRY", TransactionKind.Purchase, "POS 3301 CAN KIRTASIYE MODA",
                "Can Kırtasiye", Other, at, CanShop, Id($"{slot}-sale"), Id($"{slot}-pair")));

        // A moment between the hours at an uneven minute, as card payments come (the live feed must not tick on the hour).
        private DateTimeOffset Clock(Random rng, int fromHour, int toHour) => At(rng.Next(fromHour, toHour), rng.Next(0, 60));

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
