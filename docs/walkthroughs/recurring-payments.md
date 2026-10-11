# Walkthrough: Düzenli ödemeler (REC-01–03)

Bu belge kodu satır satır Türkçe açıklar. Kodun kendisinde yalnız İngilizce `/// <summary>` ve
"neden" yorumları bulunur; buradaki Türkçe yorumlar öğrenme amaçlıdır ve koda kopyalanmaz.
🗄️ işareti veritabanına gidilen satırları gösterir.

## Okuma sırası

1. `tests/BankingAI.Application.Tests/RecurringPaymentDetectorTests.cs` — ne yapması gerektiği (ilk iki test).
2. `src/BankingAI.Application/Analytics/RecurringPaymentDetector.cs` — kuralın kendisi; DB yok.
3. `src/BankingAI.Application/Analytics/RecurringPaymentService.cs` — veriyi okuyup dedektöre veren servis.
4. `tests/BankingAI.Application.Tests/RecurringPaymentServiceTests.cs` — demo verisiyle sonuç.

İş bölümü:

```text
RecurringPaymentService  ── DB'den son 6 ayın alışverişlerini okur, merchant'a göre gruplar
          │
          └──► RecurringPaymentDetector  ── her grup için "düzenli mi?" kararını verir (saf C#, DB yok)
```

## 1. `RecurringPaymentDetector.Detect`

Genel yapı "elemeli sınav"dır: her kontrol başarısızsa metot hemen `return null` ile çıkar
(erken çıkış / early return). Dört kontrolü de geçen grup en sonda "düzenli" sonucunu alır.

```csharp
// Metot: bir ödeme listesi alır, "düzenli aylık ödeme mi?" diye bakar.
// Düzenliyse gerekçesiyle birlikte bir RecurringPattern döndürür, değilse null döndürür.
// static = nesne oluşturmadan çağrılır. "?" = sonuç null olabilir.
public static RecurringPattern? Detect(IEnumerable<PaymentPoint> payments, DateOnly today)
{
    // Liste hiç verilmemişse (null) hemen hata fırlatır. Güvenlik kontrolü.
    ArgumentNullException.ThrowIfNull(payments);

    // Ödemeleri tarihe göre eskiden yeniye sıralar ve bir listeye koyar.
    var sorted = payments.OrderBy(p => p.Date).ToList();

    // 3'ten az ödeme varsa "düzenli değil" der ve metottan çıkar.
    if (sorted.Count < MinimumOccurrences)
        return null;

    // Ardışık her iki ödeme arasındaki gün farkını hesaplar.
    // Örnek: [12 Nis, 12 May, 12 Haz] → [30, 31]
    // Zip: listeyi bir kaydırılmış haliyle yan yana koyar (1.-2., 2.-3. ...).
    // DayNumber: tarihi gün sayısına çevirir; iki tarihin farkı = aradaki gün sayısı.
    var intervals = sorted.Zip(sorted.Skip(1), (earlier, later) => later.Date.DayNumber - earlier.Date.DayNumber).ToList();

    // Aralıklardan herhangi biri 26 günden kısa veya 35 günden uzunsa "düzenli değil" der ve çıkar.
    // Ekstra alışverişi (ör. 2 gün sonra ikinci kahve) ve atlanan ayı (~61 gün) burada yakalar.
    if (intervals.Any(days => days is < MinimumIntervalDays or > MaximumIntervalDays))
        return null;

    // Tutarların ortancasını (sıralı listenin ortasındaki değer) hesaplar.
    var median = Median(sorted.Select(p => p.Amount));

    // Herhangi bir tutar ortancadan %10'dan fazla uzaksa "düzenli değil" der ve çıkar.
    // Math.Abs = mutlak değer (farkın eksi işaretini atar).
    if (sorted.Any(p => Math.Abs(p.Amount - median) > median * AmountTolerance))
        return null;

    // Listenin son elemanını (en yeni ödemeyi) alır. [^1] = sondan birinci.
    var last = sorted[^1];

    // Son ödemenin üzerinden 35 günden fazla geçmişse "artık aktif değil" der ve çıkar.
    if (today.DayNumber - last.Date.DayNumber > MaximumDaysSinceLastPayment)
        return null;

    // Buraya geldiyse bütün kontrolleri geçmiştir: "düzenli" sonucunu gerekçesiyle döndürür.
    return new RecurringPattern(
        sorted.Count,                       // kaç ödeme var
        last.Amount,                        // son ödemenin tutarı
        sorted.Min(p => p.Amount),          // en düşük tutar
        sorted.Max(p => p.Amount),          // en yüksek tutar
        sorted[0].Date,                     // ilk ödeme tarihi
        last.Date,                          // son ödeme tarihi
        intervals.Min(),                    // en kısa aralık (gün)
        intervals.Max(),                    // en uzun aralık (gün)
        NextExpected(last.Date, sorted.Max(p => p.Date.Day)));   // tahmini sonraki ödeme tarihi
}

// Yardımcı metot: bir sayı listesinin ortancasını bulur. private = sadece bu sınıf kullanır.
private static decimal Median(IEnumerable<decimal> values)
{
    // Sayıları küçükten büyüğe sıralar.
    var ordered = values.Order().ToList();

    // Ortadaki konumu bulur (tam sayı bölme: 6 / 2 = 3, 5 / 2 = 2).
    var middle = ordered.Count / 2;

    // Eleman sayısı tekse (% 2 == 1) tam ortadaki sayıyı döndürür;
    // çiftse ortadaki iki sayının ortalamasını döndürür.
    // "koşul ? A : B" = koşul doğruysa A, değilse B. 2m = decimal tipinde 2.
    return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2m;
}

// Yardımcı metot: son ödemeden bir ay sonrasını, her zamanki ödeme gününe göre tahmin eder.
private static DateOnly NextExpected(DateOnly last, int usualDay)
{
    // Son ödeme tarihine 1 ay ekler (ör. 12 Eyl → 12 Eki).
    var nextMonth = last.AddMonths(1);

    // O ayın, her zamanki ödeme gününü içeren tarihini oluşturur.
    // Ay o kadar uzun değilse ayın son gününü kullanır: ödeme günü 31, Nisan 30 çekiyor → 30 Nisan.
    // DaysInMonth = o ayın kaç gün çektiği. Math.Min = iki sayıdan küçüğü.
    return new DateOnly(nextMonth.Year, nextMonth.Month, Math.Min(usualDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
}
```

Sabitler sınıfın en üstündedir: `MinimumOccurrences = 3`, `MinimumIntervalDays = 26`,
`MaximumIntervalDays = 35`, `AmountTolerance = 0.10`, `MaximumDaysSinceLastPayment = 35`.

### Gerçek veriyle sonuç (Ada, 10 Ekim 2026)

```text
Kahve Durağı   ödeme=3  tutarlar=[110, 120, 95]       aralıklar(gün)=[17, 5]                     → DÜZENLİ DEĞİL
Netflix        ödeme=6  tutarlar=[229,99 × 6]         aralıklar(gün)=[30, 31, 30, 31, 31]        → DÜZENLİ
Spotify        ödeme=6  tutarlar=[99,99 × 6]          aralıklar(gün)=[30, 31, 30, 31, 31]        → DÜZENLİ
Starbucks      ödeme=8  tutarlar=[185 × 6, 210, 165]  aralıklar(gün)=[30, 31, 30, 31, 31, 16, 2] → DÜZENLİ DEĞİL
```

Starbucks'ın `16, 2` aralıkları 26–35 kontrolüne takılır; `median` hiç hesaplanmaz.

## 2. `RecurringPaymentService.GetAsync`

```csharp
// Ana metot: sorguyu alır, sonunda RecurringPayments döndürür. async = içinde veritabanını bekleyen işler var.
public async Task<RecurringPayments> GetAsync(RecurringPaymentQuery query, CancellationToken cancellationToken = default)
{
    // Sorgu hiç verilmemişse (null) hemen hata fırlatır.
    ArgumentNullException.ThrowIfNull(query);

    // 🗄️ DB'ye gider (müşterinin saat dilimini okur, hesap verildiyse sahibini kontrol eder).
    // Dönemi "son 6 ay" olarak sabitler ve şunları hazırlar:
    //   scoped.Period    → kesin tarih aralığı (10 Nis – 10 Eki)
    //   scoped.Purchases → bu müşterinin şahsi hesaplarındaki, bu dönemdeki alışverişler (henüz çalışmamış sorgu)
    //   scoped.TimeZone  → müşterinin saat dilimi
    // Müşteri yoksa veya hesap başkasınınsa burada hata fırlar.
    var scoped = await _scope.ResolveAsync(
        new SpendingQuery(query.CustomerId, PeriodKind.Last6Months, AccountId: query.AccountId), cancellationToken);

    // 🗄️ DB'ye gider (ToListAsync). Alışverişleri tek tek satır olarak okur.
    var rows = await (
            // Merchant'ı olmayan alışverişleri almaz; onlar gruplanamaz.
            from t in scoped.Purchases.Where(t => t.NormalizedMerchant != null)
            // Her işlemin hesabını bağlar → hesap adını almak için.
            join a in db.Accounts on t.AccountId equals a.Id
            // Her işlemin kategorisini bağlar. "into ... DefaultIfEmpty()" = LEFT JOIN:
            // kategorisi olmayan işlem de gelsin, kategori alanları boş (null) olsun.
            join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
            from c in categories.DefaultIfEmpty()
            // Her satırdan sadece gereken alanları alır: hesap, hesap adı, para birimi, merchant, tarih, tutar, kategori.
            select new
            {
                t.AccountId, a.AccountName, t.CurrencyCode, Merchant = t.NormalizedMerchant!, t.OccurredAt, t.Amount,
                CategoryCode = (string?)c!.Code, CategoryName = (string?)c!.DisplayName
            })
        .ToListAsync(cancellationToken);
    // ↑ Bu satırdan sonra "rows" bellekteki bir liste. Aşağıda veritabanına bir daha GİDİLMİYOR.

    // Müşterinin saat dilimi adını ("Europe/Istanbul") kullanılabilir bir saat dilimi nesnesine çevirir.
    var timeZone = TimeZoneInfo.FindSystemTimeZoneById(scoped.TimeZone);

    // "Bugün" = incelenen dönemin son günü (10 Ekim). Detect'e "son ödeme ne kadar eski?" kontrolü için verilecek.
    var today = scoped.Period.EndDate;

    // Metodun içinde tanımlı küçük bir fonksiyon: UTC zamanı müşterinin yerel takvim gününe çevirir.
    // Örnek: 30 Eyl 22:00 UTC → İstanbul'da 1 Eki.
    DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).DateTime);

    var payments = rows
        // Satırları (hesap, para birimi, merchant) üçlüsüne göre gruplar.
        // Örnek: Ada'nın TRY hesabındaki bütün Netflix ödemeleri tek bir grup.
        .GroupBy(r => (r.AccountId, r.CurrencyCode, r.Merchant))
        // Her grup için aşağıdaki blok çalışır:
        .Select(g =>
        {
            // Grubun ödemelerini (yerel tarih + tutar) Detect'e verir: "düzenli mi?"
            var pattern = RecurringPaymentDetector.Detect(g.Select(r => new PaymentPoint(LocalDate(r.OccurredAt), r.Amount)), today);
            // Düzenli değilse bu grup için null döndürür (listeden sonra atılacak).
            if (pattern is null)
                return null;
            // Gruptaki en yeni ödemeyi bulur; kategori ve hesap adı ondan alınacak.
            var latest = g.MaxBy(r => r.OccurredAt)!;
            // Düzenli ödeme kaydını oluşturur. Kategori adı yoksa "Sınıflandırılmamış" yazar (?? = boşsa şunu kullan).
            return new RecurringPayment(g.Key.Merchant, latest.CategoryCode, latest.CategoryName ?? AnalyticsLabels.Unclassified,
                latest.AccountName, g.Key.CurrencyCode, pattern);
        })
        // Null'ları (düzenli olmayan grupları) listeden atar; sadece RecurringPayment'lar kalır.
        .OfType<RecurringPayment>()
        // Son tutarı büyük olan önce gelecek şekilde sıralar (Netflix 229,99 → Spotify 99,99).
        .OrderByDescending(p => p.Pattern.LatestAmount)
        // Tutarlar eşitse merchant adına göre sıralar (sıra her çağrıda aynı olsun diye).
        .ThenBy(p => p.Merchant, StringComparer.Ordinal)
        // Sonucu listeye çevirir.
        .ToList();

    var totals = payments
        // Düzenli ödemeleri para birimine göre gruplar (TRY ayrı, USD ayrı).
        .GroupBy(p => p.CurrencyCode)
        // Para birimi koduna göre sıralar.
        .OrderBy(g => g.Key, StringComparer.Ordinal)
        // Her para birimi için son tutarları toplar → aylık toplam. Ada için TRY 229,99 + 99,99 = 329,98.
        .Select(g => new CurrencyRecurringTotal(g.Key, g.Sum(p => p.Pattern.LatestAmount)))
        .ToList();

    // Dönemi, düzenli ödemeleri ve aylık toplamları tek kutuda döndürür.
    return new RecurringPayments(scoped.Period, payments, totals);
}
```

Özet akış:

1. 🗄️ Kapsamı çöz: kim, hangi hesaplar, son 6 ay.
2. 🗄️ Alışverişleri satır satır oku.
3. Aynı hesap + para birimi + merchant'ı grupla.
4. Her grubu `Detect`'e sor; düzenli olanları tut.
5. Sırala, aylık toplamı hesapla, döndür.

## Bu adımda ilk kez geçen C# kavramları

| C# | Anlamı | Python karşılığı |
| --- | --- | --- |
| `RecurringPattern?` | Null olabilir | `Optional[RecurringPattern]` |
| `a.Zip(b, (x, y) => ...)` | İki listeyi eleman eleman eşleştirir | `zip(a, b)` |
| `list.Any(x => ...)` | Herhangi biri koşulu sağlıyor mu | `any(...)` |
| `days is < 26 or > 35` | Aralık dışı mı | `not 26 <= days <= 35` |
| `sorted[^1]` | Son eleman | `sorted_[-1]` |
| `koşul ? A : B` | Koşullu ifade | `A if koşul else B` |
| `2m` | `decimal` tipinde 2 | `Decimal(2)` |
| `g.MaxBy(r => r.OccurredAt)` | Anahtarı en büyük eleman | `max(g, key=lambda r: r.occurred_at)` |
| `.OfType<T>()` | Null olanları atıp tipi T olanları bırakır | `[x for x in xs if x is not None]` |
| Metot içinde `DateOnly LocalDate(...) => ...` | Yerel (iç) fonksiyon | İç içe `def` |
| `GroupBy(r => (a, b, c))` | Üçlü anahtarla gruplama | `groupby(key=lambda r: (a, b, c))` |
