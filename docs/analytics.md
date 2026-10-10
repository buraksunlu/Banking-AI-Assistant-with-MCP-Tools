# Deterministik analytics sözleşmesi

Bu belge `BankingAI.Application/Analytics` altındaki servislerin girdi, çıktı ve
hesap kurallarını tanımlar. LLM bu servisleri seçer ve sonucu açıklar; hesap,
filtreleme, yetki ve dönem çözümü backend'de yapılır (RULE.md §2, VAL-05).

## Ortak kurallar

- **Dönem:** `PeriodResolver` (ANA-01). Sembolik dönem (ThisMonth, LastMonth,
  Last3Months, Last6Months, Custom) müşterinin `Customers.TimeZone` değerine göre
  `[StartUtc, EndUtc)` aralığına çevrilir. Sonuçta yerel `StartDate`/`EndDate` döner.
- **Saat:** `TimeProvider` ile verilir; testler ve demo sabit saat kullanabilir.
- **Müşteri kimliği** güvenilir çağıran bağlamından gelir (API oturumu), model
  argümanından değil (SEC-01).
- **Harcama** = `Kind = Purchase`. Transfer, Sale, Salary ve OpeningFunding sayılmaz.
- **Para birimleri** ayrı satırlarda döner; dönüştürülmez ve toplanmaz.
- **Hata sözleşmesi:** olmayan müşteri veya başka müşterinin hesabı
  `KeyNotFoundException` (API'de 404; varlık sızdırılmaz). Geçersiz girdi
  `ArgumentException` (API'de 400).

## Ortak kapsam (`PurchaseScope`, ANA-03 refactor'ü)

Bütün harcama servisleri aynı girdiyi (`SpendingQuery(CustomerId, Period, From?, To?,
AccountId?)`) ve aynı kapsamı kullanır: müşteri ve saat dilimi, hesap sahipliği ve
Personal kontrolü, dönem çözümü ve `Kind = Purchase` temel sorgusu. Kapsam `internal`
bir sınıfta tek yerde durur; yetki kuralı bir serviste düzeltilip diğerinde unutulamaz.
Sorgu çalıştırılmadan döner, her servis kendi tek toplamasını ekler.

## ANA-02 — Harcama özeti (`SpendingSummaryService`)

Girdi: `SpendingQuery`.
Çıktı: `SpendingSummary(Period, Currencies)`; her para birimi için
`TotalAmount`, `TransactionCount`, `UncategorizedAmount`, `UncategorizedCount`.

- Varsayılan kapsam müşterinin **Personal** hesaplarıdır. `AccountId` verilirse
  müşteriye ait şahsi hesap olmalıdır; Business hesap `ArgumentException` verir.
- Kategorisiz Purchase toplamdan düşmez; ayrıca gösterilir.
- Dönemde harcama yoksa `Currencies` boş listedir; "0" satırı uydurulmaz.
- SQL: müşterinin saat dilimini okuyan bir sorgu ve hesap filtresini alt sorgu
  olarak içeren tek bir `GROUP BY "CurrencyCode"` sorgusu. Satırlar belleğe çekilmez;
  değerler parametre olarak gönderilir.

Kabul örnekleri (10 Ekim 2026 15:00 Istanbul, demo verisi):

| İstek | Sonuç |
| --- | --- |
| Ada, bu ay | TRY 735,00, 5 işlem, kategorisiz 145,00 (1). Bütün Debit toplamı 2.235 olurdu. |
| Deniz, bu ay | TRY 11.350, 2 işlem; dükkân satışı (355) ve transferler hariç |
| Ege, bu ay | TRY 840, 2 işlem |
| Deniz, geçen ay | TRY 31.000, 1 işlem; Eylül transferleri (2.750) hariç |
| Ada, son 3 ay | TRY 2.389,94, 15 işlem |
| Ada, Ocak 2026 | Boş liste |
| Ada + geçici 10 USD harcama | Ayrı satırlar: TRY 735, USD 10 |
| Ada, Deniz'in hesabı | `KeyNotFoundException` |
| Deniz, dükkân hesabı | `ArgumentException` |

Para biçimlendirme ("735,00 TL") servis sorumluluğu değildir; API/AI katmanında yapılır.

## ANA-03 — Kategori dağılımı (`CategoryBreakdownService`)

Girdi: `SpendingQuery`. Çıktı: para birimi başına `TotalAmount` ve `Categories`;
her satırda `CategoryCode`, `DisplayName`, `Amount`, `TransactionCount`, `Percentage`.

- Kapsam ANA-02 ile aynıdır (`PurchaseScope`); kategori tutarları toplamı ve işlem
  sayısı her zaman harcama özetine eşittir (tutarlılık testi).
- Kategorisiz Purchase ayrı grupta döner: `CategoryCode = null`, kullanıcı kararıyla
  `DisplayName = "Sınıflandırılmamış"`. `OTHER` ("Diğer") ayrı bir kategoridir; birleştirilmez.
- Sıra: tutar büyükten küçüğe; eşitlikte kod, Sınıflandırılmamış en sonda. "En çok
  harcanan kategori" listenin ilk elemanıdır. Sıfır tutarlı kategori listelenmez.
- Yüzde backend'de `tutar / para birimi toplamı × 100` olarak hesaplanır ve 2 ondalığa
  yuvarlanır (AwayFromZero). Tutarlar kesindir; yuvarlanmış yüzdelerin toplamı 100'den
  birkaç yüzde birlik sapabilir ve zorla 100'e tamamlanmaz.
- SQL: saat dilimi sorgusu ve kategorilere `LEFT JOIN` yapan tek bir
  `GROUP BY CurrencyCode, Code, DisplayName` sorgusu. Yüzde ve sıralama birkaç satırlık
  sonuçta C# içinde yapılır.

| İstek | Sonuç |
| --- | --- |
| Ada, bu ay | FOOD 590 (4, %80,27); Sınıflandırılmamış 145 (1, %19,73) |
| Ada, son 3 ay | FOOD 1.255 (8, %52,51); SUBSCRIPTION 989,94 (6, %41,42); Sınıflandırılmamış 145 (1, %6,07) |
| Deniz, bu ay | GROCERY 9.500 (%83,70); BILLS 1.850 (%16,30); dükkân geliri yok |
| Ege, son 6 ay | TRANSPORTATION 5.600 (13, %79,21); TRAVEL 1.200 (%16,97); FOOD 270 (2, %3,82) |
| Ada, Haziran 2025 | OTHER 780 (%65,00) ve Sınıflandırılmamış 420 (%35,00) ayrı satırlar |
