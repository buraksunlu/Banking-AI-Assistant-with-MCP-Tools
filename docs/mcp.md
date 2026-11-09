# Read-only MCP server (MCP-01, MCP-02; 19 Ekim 2026)

Modelin kullandığı araçlar .NET'te ayrı bir programda, **`BankingAI.McpServer`**'da yayınlanır. AI servisi (FastAPI) araç
listesini buradan öğrenir (`tools/list`) ve araçları buradan çalıştırır (`tools/call`). iPhone'un kapısı (`BankingAI.Api`)
ile modelin kapısı ayrı süreçlerdir; MCP server yalnız `127.0.0.1:5249`'da dinler.

MCP-a (server tarafı) ve MCP-b (AI servisinin istemcisi, 20 Ekim 2026) birlikte tamam; geçici REST köprüsü silindi.

## Sınır: kim neyi bilir

```text
iPhone ──Bearer──► BankingAI.Api ──aynı token──► AI servisi ──aynı token──► BankingAI.McpServer ──► Application servisleri ──► PostgreSQL
                    (giriş, hesaplar,              (model seçer,             (araç listesi, argüman
                     yazma, sohbet kapısı)          kod biçimlendirir)        kontrolü, hesap: okuma)
```

| Parça | Bilir | Bilmez |
| --- | --- | --- |
| Model | Araç adları, açıklamaları, argüman şeması | Müşteri kimliği, token, SQL, yazma işlemi |
| AI servisi | Kullanıcının token'ı (yalnız başlıkta taşır), araç sonuçları | Veritabanı, hesap kuralları |
| MCP server | Token'daki müşteri, Application servisleri (yalnız okuma) | Yazma servisleri (DI'da kayıtlı bile değil) |

## Kararlar (kullanıcı, 19 Ekim 2026)

| Karar | Seçim |
| --- | --- |
| K1 | Ayrı proje ve süreç, yalnız `127.0.0.1`, Streamable HTTP (durumsuz) |
| K2 | Kullanıcının JWT'si aynı imza anahtarıyla doğrulanır, müşteri `sub`'dan; araçlarda müşteri argümanı yok |
| K3 | Araç adı/açıklaması/şeması .NET'te; AI-14a'nın ölçülen metinleri birebir (dönem açıklaması dahil) |
| K4 | Bugünkü üç araç: `get_spending_summary` (MCP-05), `get_category_breakdown` (MCP-07), `get_merchant_spending` (MCP-25) |
| K5 | İki adım: MCP-a (server) → MCP-b (Python istemcisi, köprü silinir) |
| K6 | Araçlar Application servislerini doğrudan çağırır (HTTP yok) |
| K7 | MCP-b'de Python: resmi `mcp` SDK'sı + kendi `McpToolGateway`'imiz |
| K8 | Ortak kurulum `BankingAI.Hosting`: JWT, iş saati, JSON kuralları, `AddBankingCore()` (yalnız okuma servisleri) |
| K9 | Araç sonucu REST cevabıyla aynı JSON (`structuredContent` + çıktı şeması) |
| K10 | Araç hatası: `isError` + `{"code", "message"}` |
| K11 | Testler gerçek protokolle: `BankingAI.McpServer.Tests` + resmi C# MCP istemcisi |

## Araçlar

Hepsi `ReadOnly = true`, `Destructive = false`. Argümanlar: `period` (11 ad: `Today`, `Yesterday`, `ThisWeek`,
`LastWeek`, `Last7Days`, `Last30Days`, `ThisMonth`, `LastMonth`, `Last3Months`, `Last6Months`, `ThisYear`; CHAT-08'den beri
bir de `Custom`, aşağıda), merchant aracında `merchant` (1–150 karakter), `compare_spending`'de `comparison` (`PreviousFullMonth` | `MonthToDate`;
`Custom` yok), `get_monthly_trend`'de zorunlu `months` (tam sayı 1–12), `get_upcoming_payments`'ta isteğe bağlı `days` (tam sayı
1–31, verilmezse 30), `check_budget`'te zorunlu `amount` (JSON sayısı) ve `currencyCode` (`TRY` | `USD` | `EUR` | `GBP`),
`simulate_savings`'te zorunlu `reductionPercent` (tam sayı 1–100) ve isteğe bağlı `months` (1–12, verilmezse 6); ikisinde de isteğe
bağlı `categoryCode` (11 kodluk kapalı küme, `ToolCategory`) ya da `merchant` (MCP-17/18). Müşteri ve hesap argümanı yoktur.

**Tarih aralığı (CHAT-08 K2-B).** Dönem alan beş araçta (özet, kategori, tek işyeri, işyeri dağılımı, en büyükler) isteğe bağlı
`from` ve `to` vardır (`["string", "null"]`) ve `period` `Custom` olabilir. Tarihi model seçmez (ANA-20): AI servisi bu üç değeri
modelin gördüğü listeden gizler (`routing.hide_dates`), kullanıcının sözünden **kodla** okuduğu tarihi kendisi yazar. Sunucu yine
de her çağrıyı denetler: `ToolCallFilter` metin tipini, `ToolDate` yalnız `YYYY-MM-DD` biçimindeki gerçek bir günü kabul eder
(`2026-9-1`, `01.09.2026`, `2026-02-31`, saatli biçim reddedilir); aralık kuralları API'ninkidir (`PeriodResolver.ValidateCustom`:
iki uç da zorunlu, başlangıç ≤ bitiş, bitiş gelecekte değil, en çok 24 ay; tarih yalnız `Custom` ile verilir) ve ihlal
`invalid_arguments` olur. Karşılaştırma, aylık seri, ödemeler, bütçe ve tasarruf araçlarında tarih yoktur.

| Araç | Servis | Sonuç (REST karşılığı) |
| --- | --- | --- |
| `get_spending_summary` | `SpendingSummaryService` | `SpendingSummary` (`/api/analytics/spending-summary`) |
| `get_category_breakdown` | `CategoryBreakdownService` | `CategoryBreakdown` (`/api/analytics/category-breakdown`) |
| `get_merchant_spending` | `MerchantSpendingService.GetSpendingAsync` | `MerchantSpendingResult` (`/api/analytics/merchant-spending`) |
| `get_merchant_breakdown` (MCP-08) | `MerchantSpendingService.GetBreakdownAsync` | `MerchantBreakdown` (`/api/analytics/merchant-breakdown`) |
| `get_highest_transactions` (MCP-10) | `HighestTransactionsService` (yalnız harcama, 5 kalem) | `HighestTransactions` (`/api/analytics/highest-transactions`) |
| `compare_spending` (MCP-09) | `SpendingComparisonService.CompareAsync` | `SpendingComparison` (`/api/analytics/period-comparison`) |
| `get_monthly_trend` (MCP-24) | `SpendingComparisonService.GetMonthlyTrendAsync` (filtresiz) | `SpendingTrend` (`/api/analytics/monthly-trend`) |
| `get_recurring_payments` (MCP-27) | `RecurringPaymentService` (`PaymentTools`) | `RecurringPayments` (`/api/analytics/recurring-payments`) |
| `get_upcoming_payments` (MCP-16) | `UpcomingPaymentService` (`PaymentTools`) | `UpcomingPayments` (`/api/analytics/upcoming-payments`) |
| `check_budget` (MCP-17) | `BudgetCheckService` (`PlanningTools`) | `BudgetCheck` (`/api/analytics/budget-check`) |
| `simulate_savings` (MCP-18) | `SavingsSimulationService` (`PlanningTools`) | `SavingsSimulation` (`/api/analytics/savings-simulation`) |
| `get_income_share` (INC-03) | `IncomeShareService` (`IncomeTools`) | `IncomeShare` (`/api/analytics/income-share`) |
| `get_spending_advice` (INC-03) | `SpendingAdviceService` (`IncomeTools`) | `SpendingAdvice` (`/api/analytics/spending-advice`) |
| `what_if_one_off` (INC-03) | `WhatIfService.OneOffAsync` (`InvestmentTools`) | `OneOffWhatIf` (`/api/analytics/what-if/one-off`) |
| `what_if_monthly` (INC-03) | `WhatIfService.MonthlyAsync` (`InvestmentTools`) | `MonthlyWhatIf` (`/api/analytics/what-if/monthly`) |

Yeni araç = `SpendingTools` benzeri bir sınıfa `[McpServerTool]` metodu + `ToolDiscoveryTests`'teki allowlist'e satır
(eklenmezse keşif testi kırmızı olur; API'deki endpoint taramasının karşılığı).

## Argüman kontrolü

SDK şemayı yayınlar ama **çalışma anında uygulamaz** (SDK belgesi: argümanlar "doğrulanmamış ve güvenilmez"); enum bağlaması
da "yesterday"i sessizce "Yesterday" yapar. Bu yüzden `ToolCallFilter` her çağrıda, araç çalışmadan önce, argümanları
**yayınlanan şemaya** göre kontrol eder: tanınmayan argüman, eksik/null zorunlu argüman, tam adla enum, metin tipi, Unicode
karakter sayısıyla uzunluk, tam sayı (`integer`: JSON tam sayısı, `minimum`/`maximum`; SDK bağlaması `"3"` ve `3.5`'i sessizce 3
yapardı), tutar (`number`: JSON sayısı ve `decimal`'a sığmalı; `"1000"`, `true` ve `1e40` reddedilir, kuruş ve üst sınır
`InputRules.Money`'de). İsteğe bağlı argüman şemada `["string", "null"]` olarak yayınlanır: `null` "verilmedi" demektir ve kabul
edilir (MCP-17/18). SDK `[Range]`'i şemaya `minimum`/`maximum` olarak, varsayılan değerli parametreyi zorunlu olmayan olarak yazar. Boş metin
ve NUL gibi backend kuralları servislerde kalır (`InputRules`). Modelin gördüğü şema ile kabul edilen tam olarak aynıdır.

## Hatalar (K10-A)

| Durum | Cevap |
| --- | --- |
| Token yok, imzası yanlış, süresi dolmuş | HTTP 401 (araç çalışmaz) |
| Token geçerli ama müşteri silinmiş | araç hatası `session_invalid` |
| Argüman şemaya ya da backend kuralına uymuyor | araç hatası `invalid_arguments` (mesaj alanı söyler) |
| Beklenmeyen hata | araç hatası `internal_error`, "Beklenmeyen bir hata oluştu." (ayrıntı yalnız logda) |
| Olmayan araç adı | JSON-RPC protokol hatası (araç sonucu değil) |

İstisnalar tek yerde sözleşmeye çevrilir: `ToolErrors.RunAsync` (araç gövdesi) ve `ToolCallFilter` (sonuç). SDK'ya
bırakılırsa her istisna "An error occurred invoking 'x': …" cümlesine dönüşür; AI servisi bunu ayrıştırmak zorunda kalırdı.

## SDK bulguları

- Paket `ModelContextProtocol.AspNetCore` 2.2.0 (13 Ağustos 2026); 2026-07-28 protokol sürümünden beri durumsuz çalışma
  varsayılan. Python tarafında `mcp` 2.x aynı protokolü destekliyor (MCP-b).
- SDK hangi **metot parametresinin** DI servisi olduğunu tahmin ediyor; testlerde ara sıra `CategoryBreakdownService`'i
  argüman sanıp şemaya "zorunlu `categories`" yazdı. Servisler bu yüzden araç sınıfının **kurucusundan** alınır.
- Projenin katı enum çeviricisi şema üreticisine tanıtılmamış (şema `true` çıkıyor); araç JSON'u standart string-enum
  çeviricisini kullanır (`BankingJson.ConfigureForTools`), katılık filtrededir. Çıktı biçimi API'ninkiyle aynı.
- SDK, verilen JSON ayarlarında açık bir `TypeInfoResolver` ister.

## Çalıştırma

```sh
BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z dotnet run --project src/BankingAI.McpServer --launch-profile http   # 127.0.0.1:5249/mcp
```

Ayarlar API'ninkiyle aynı (`BANKINGAI_CONNECTION_STRING`, `BANKINGAI_JWT_SIGNING_KEY`, isteğe bağlı `BANKINGAI_DEMO_NOW`);
eksikse açılmaz. Elle deneme (19 Ekim 2026, Ada'nın token'ı): tokensız 401; `tools/list` üç araç; dün 145.00; son 3 ay
Starbucks 930.00 (`Matched`); `"yesterday"` → `invalid_arguments`; logda token yok.

## Sınırlar

- Kullanıcının tam yetkili token'ı MCP'ye kadar gidiyor (K2-A); kısa ömürlü "yalnız MCP" token'ı SEC-07'de.
- Araçlar 9 (MCP-08 ile; aşağıda); bütçe, tasarruf, arama, hesaplar ve kavramsal merchant MCP-03/06/15/17/18'de. Hız sınırı yok:
  MCP'ye yalnız aynı makinedeki AI servisi ulaşır.

# MCP-b: AI servisinin istemcisi (MCP-12/13/14, 20 Ekim 2026)

`ai/service/mcp_gateway.py` (`McpToolGateway`, resmi Python `mcp` 2.3.0). Her sohbet turu:

```text
list_tools(token) ─► model seçer (o turun listesi) ─► ad listede mi ─► call(tool, args, token) ─► sonuç metni Decimal ile ─► facts.py
```

| Karar | Seçim |
| --- | --- |
| K1 | Araç listesi her turda kullanıcının token'ıyla (önbellek yok; sunucu da `ttlMs: 0` diyor) |
| K2 | Her MCP çağrısı kendi kısa olay döngüsünde (`anyio.run`); sohbet senkron kaldı |
| K3 | Python argüman doğrulamaz; `invalid_arguments` → bugünkü sabit geri soru |
| K4 | Testler: gerçek sunucunun cevap biçimini taklit eden sahte MCP + gerçek sunucu açıksa atlanabilir testler |
| K5 | `BANKINGAI_MCP_URL`, yoksa `http://127.0.0.1:5249/mcp`; yanlış biçimde servis açılmaz; açılışta logda |
| K6 | MCP server'a tokensız `GET /health` (veritabanı); AI servisinin `/health/ready`'si ona da bakar |
| K7 | `ai/model_check/tool_selection_eval.py` (12 vaka): önce 12/12 (Python tanımları), sonra 12/12 (MCP tanımları), seçimler birebir aynı |

Hata eşlemesi (AI servisinin cevabı değişmedi): HTTP 401 ve `session_invalid` → 401 `session_invalid`; bağlantı yok, 5xx,
`internal_error` ve sözleşme dışı cevap → 503 `backend_unavailable`; süre → 504 `ai_timeout`; protokol hatası (olmayan araç) →
502 `ai_bad_tool_call`; `invalid_arguments` → 200 sabit geri soru.

SDK bulguları (2026-07-28 protokolü): sonuçlar `resultType: "complete"` ve sunucu bilgisi taşır, listeler `ttlMs`/`cacheScope` taşır
(sahte sunucu bunlarsız cevap verince SDK reddetti); `structuredContent` float'a çevrilir, para metinden okunur; 401/5xx genel bir
hataya gömülür, HTTP kancasıyla ayrılır; hatalar iç içe ExceptionGroup gelir; SDK çağrı çevresinde kendiliğinden `tools/list` yapabilir.

Ölçüm (20 Ekim 2026, yerel): `tools/list` ortanca 2 ms, `tools/call` ortanca 8 ms; tur başına ~10 ms (model 2–5 sn). Uçtan uca (API +
MCP + AI servisi + `qwen3.5:4b`, Ada, demo anı): dün 145,00 TL, bu hafta 615,00 TL, kategoriler 590,00 (%80,27) + 145,00, son 3 ay
Starbucks 930,00 TL, Migros bulunamadı, transfer `OutOfScope`; MCP kapalıyken hazır olma 503, .NET `available: false`, mesaj anında 503
(model çalışmadan); logda token yok.

Sınırlar: tur başına iki kısa MCP bağlantısı; sohbet senkron (istemci ayrılınca model turu bitirir); kullanıcının tam token'ı MCP'ye
kadar gider (SEC-07); 3 araç.

# MCP-08: yeni analiz araçları (MCP-08/09/10/16/24/27, 20 Ekim 2026)

Hazır analizlerden altısı modele açıldı (3 → 9 araç); backend ve API değişmedi. Kullanıcı kararları: K1-B (metin/enum argümanlı dört
araç + sayı argümanlı iki araç), K2-A (eşikli ölçüm: eski 12 vaka 12/12, toplam ≥ %90), K3-A (`months` 1–12, filtre yok), K4-A (en
büyük harcamalarda yalnız harcama, sabit 5, ham açıklama modele gitmez), K5-A (tahmin olan sonuçlarda "tahmini" etiket + kural notu),
K6-B (uzun listede ilk 5 kalem + "N … daha var" notu; Python tutar toplamaz), K7-A (sohbet cevabında argümanlar yazı: `"3"`), K8-A
(araç seçicide 200 token sınırı).

- **.NET:** `SpendingTools`'a dört araç, yeni `PaymentTools` (düzenli/yaklaşan ödemeler), `ToolComparison` enum'u, `ToolCallFilter`'a
  tam sayı kuralı. `get_spending_summary` ve `get_highest_transactions` açıklamalarına ölçümle ayrıştırıcı ipucu eklendi.
- **Python:** `facts.py`'ye altı kalem üretici (`formatting.month`); ham işlem açıklaması (`rawDescription`) hiçbir zaman kalem olmaz
  (AI-13). `app.py` cevapta argüman değerlerini yazıya çevirir (`_as_text`): sözleşme (`ToolCallOut`, .NET `AssistantToolCall`)
  `dict[str, str]`'dir ve sayı argümanı canlı serviste 500 veriyordu (sahte sohbet servisli testler yakalamamıştı). Seçici
  `num_predict=200` (`MAX_TOOL_CALL_TOKENS`): fıkra yazan model 30 sn'ye kadar gidip 504 alabiliyordu; kesilen metin "araç yok" sayılır.
- **Ölçüm** (`tool_selection_eval.py`, 26 vaka; vaka listesi bir vakada eşdeğer doğru cevapları kabul eder): 3 araçta 12/12 → 9 araçta
  ilk koşu **25/26** ("Dün ne harcadım?" → `get_highest_transactions`). Önce yalnız en büyük harcamalar açıklaması daraltıldı (yine
  S01 hatalı, ayrıca S12 iki kez 30 sn zaman aşımı); toplam harcama açıklamasına "ne harcadım / ne kadar harcadım" eklenince dört
  koşuda **26/26** (ikisi K8-A'dan sonra). Vaka başına ortalama 1,6 sn.
- **Uçtan uca** (.NET API → AI servisi → MCP → model, Ada, demo anı): bu ay en çok Starbucks 375,00 TL (%51,02); geçen ayın en büyük
  harcaması Netflix 229,99 TL; geçen aya göre +110,00 TL (%21,36, yeme-içme); abonelikler Netflix 229,99 + Spotify 99,99 = aylık
  329,98 TL; 7 günde Netflix 229,99 TL (tahmini); son 3 ay 514,98 / 514,98 / 624,98 / 735,00 (devam eden); fıkra → kapsam dışı.
- **Bulgu:** karşılaştırmada boş taraf JSON'da `0.0` yazılır (SQL toplamının ölçeği); değer doğru, Python "0,00 TL" yazar.

Sınırlar: bütçe/tasarruf (para birimi tahmini riski, CHAT-03 sonrası), işlem araması, hesaplar ve kavramsal merchant (iki adım,
CHAT-04) yok; aylık seride kategori/işyeri filtresi yok; takip sorusu yok (CHAT-02); araç sayısı arttıkça isabet yeniden ölçülmeli
(CHAT-01 niyete göre daraltma).

# MCP-17/18: bütçe ve tasarruf araçları (21 Ekim 2026)

Hazır iki analiz modele açıldı (9 → 11 araç): bütçe kontrolü (ANA-13) ve tasarruf simülasyonu (ANA-14). İlk kez argüman
**kullanıcının kendi rakamı** (bütçe tutarı, azaltma oranı); asıl iş, modelin bu rakamı uydurmasını ya da yanlış okumasını
("1.000 TL" → `1.0`) engellemek. Kullanıcı kararları: K0-A (iki araç tek PR), K1-A (`check_budget`: tutar + para birimi zorunlu,
kategori ya da işyeri isteğe bağlı), K2-A (para birimi söylenmezse geri soru), K3-A (tutar rakam ya da "bin" kalıbıyla okunur, yoksa
geri soru), K4-A (oran rakam, "yüzde yirmi", "yarı" = 50, "çeyrek" = 25, "tamamen" = 100; yoksa geri soru), K5-A (`months` isteğe
bağlı 1–12, verilmezse 6), K6-A (kategori kapalı küme, Türkçe adı backend'den), K7-A (tam kalemler + notlar), K8-A (seçim ölçümüne 15
vaka; eski 48 bozulmaz, toplam ≥ %90), K9-A (cevap doğruluğuna 5 persona vakası).

- **.NET:** `PlanningTools` (`check_budget`, `simulate_savings`), `ToolCategory` (11 kod; keşif testi kategori tablosuyla birebir
  karşılaştırır) ve `ToolCurrency` enum'ları; `ToolCallFilter`'a `number` kuralı ve `["string", "null"]` tipi. `SavingsSimulation`'a
  `CategoryDisplayName` eklendi (API sözleşmesine bir alan; `docs/openapi/v1.json` yenilendi). Testler McpServer 85 → 131.
- **Python:** `grounding.py` tutarı (`amounts_in`; "1.000" = 1000, "5 bin" = 5000), para birimini (`currency_in`; TL/lira/₺, euro/€,
  dolar/$, sterlin/£; iki para birimi geçerse hiçbiri) ve oranı (`percent_in`) mesajda arar; model zorunlu bir rakamı hiç yazmadıysa
  o da sorulur; boş metinli isteğe bağlı argüman düşer. `clarification.py`'ye üç soru türü (`amount`, `currency`, `percent`): bunlar
  **hep sabit metin** (model yazarsa bir tutar önerebilirdi) ve `later` ile zincirlenir: "Bütçemi aşar mıyım?" → tutar sorulur →
  "3000" → para birimi sorulur → "1" → araç çalışır; "3.000 TL" cevabı ikisini birden tamamlar. `facts.py`'ye iki kalem üretici:
  bütçede dönem (ayın kaçıncı günü), hedef, bütçe, harcanan, kalan ya da aşım, günlük ortalama, ay sonu tahmini, tahmini aşım ya da
  "bütçe içinde kalınır" ve doğrusal tahmin notu; tasarrufta ay ay tutarlar (boş ay 0,00), toplam, ortalama, aylık/yıllık tahmini
  tasarruf ve "yatırım tavsiyesi değildir" notu.
- **SDK bulguları:** isteğe bağlı parametre `["string", "null"]` ve enum'a `null` ile yayınlanıyor; filtre `type`'ı tek yazı sandığı
  için bu tiple çöküyor ve yayınlanan `null`'u reddediyordu. `decimal` `number` yayınlanıyor ama filtrede kuralı yoktu (`"1000"`
  denetimsiz geçerdi).
- **Araç seçim ölçümü** (`tool_selection_eval.py`, 63 vaka): 9 araçla önce **48/48**. İlk taslakla (açıklamalarda 11 kategorinin
  Türkçe karşılıkları) 11 araçta **53/63**; eski 5 vaka bozuldu ("geçen hafta" → `Last7Days`, "geçen ay" → `Last30Days`, "Dün ne
  harcadım?" → en büyük harcamalar). Bozulma her koşuda aynıydı ve yeni araçlar listeden çıkarılınca 5'i de geçiyordu. Deneyler (vaka
  başına tek çağrı, aralarda bekleme): kategori listesi çıkınca dönem kaymaları düzeldi (kodlar yalnız şemada); `check_budget`'ın
  "Yalnız kullanıcı bir bütçe tutarı söylediyse" diye başlaması "Geçen hafta Starbucks'a"yı düzeltti; S01 ise bütçe aracının açıklaması
  tek cümleye inse bile bozuk kaldı, en büyük harcamalar açıklamasını daraltmak da düzeltmedi; `get_spending_summary` açıklamasına
  "gibi genel harcama soruları için ilk tercih" eklenince düzeldi (eski bir açıklama değişti, bu yüzden 63 vaka yeniden ölçüldü). Son
  durum **60/63**, eski 48'in hepsi geçiyor (eşik ≥ %90 sağlandı). Kalan üçü: S40 ve S46'da (bütçe/oran söylenmemiş) model hiç araç
  seçmiyor, kullanıcı geri soru yerine kapsam dışı metnini görür (ve model 200 token sınırına kadar yazdığı için ~15 sn sürüyor); S43'te
  model "yılda" için `months: 12` yazıyor, ölçüm modelin ham seçimini puanladığı için FAIL; sohbette bu değer `grounding`'de düşer ve 6
  ay kullanılır. Vaka başına ortalama 3,9 sn (MCP-08'de 1,6). Ölçüm sırasında iki hata kodda düzeltildi: "yılda ne kadar
  biriktiririm" 12 ay sayılıyordu (artık yalnız "son/geçen/bir yıl"), model boş `categoryCode: ""` yazıyordu (isteğe bağlı boş metin
  artık düşer).
- **Cevap doğruluğu** (`answer_eval.py`, gerçek zincir, bağımsız SQL rakamları): **14/15**; eski 10 vaka geçiyor. A11 (Selin yeme-içme
  bütçesi), A13 (Elif'in euro bütçesi aşılmış: 55,03 EUR), A14 (Selin %20, Ağustos 0,00), A15 (Zeynep TRY ve USD ayrı) geçti. A12'de
  (Murat "market bütçem 8.000 TL, bu hızla aşar mıyım?") kalemler doğru ve cevap soruyu doğru yanıtlıyordu (ay sonu 15.912,30 TL,
  7.912,30 TL aşım), ama vakanın cevap şartı harcanan tutardı (5.133) ve o cevapta yoktu. Şart soruyla uyumsuzdu (kullanıcı kararı B,
  21 Ekim 2026): artık sorunun cevabı olan aşımı (7.912,30) ister, harcanan tutar kalem şartında kontrol edilmeye devam eder. Bu koşunun
  A12 cevabı yeni şartı karşılıyor; ölçüm yeniden koşulmadı (bir sonraki koşuda 15/15 beklenir). A15'te model iki para birimini "veya" ile bağladı
  (anlam kontrolü kodla yapılmıyor, AI-10).
- **Bellek:** ölçüm sırasında model 12,7 GB'a kadar büyüdü (16 GB'lık makinede swap); ölçümler tek oturumda, aralarda beklemeyle
  çalıştırıldı ve sonra model boşaltıldı. Model gerektirmeyen Python testleri: `pytest ai -k "not real"`.

Sınırlar: bütçe yalnız bu ay, kaydedilmez (BUD-01..04); "geçen ay bütçemi aştım mı?" desteklenmez; yazıyla tutar ("bin beş yüz")
geri soruya düşer; kategori ile işyeri birlikte verilemez; araç sayısı 11 (CHAT-01 niyete göre daraltma).

## INC-03 PR1 — gelir, öneri ve "ne olurdu" araçları (9 Kasım 2026)

Bugünün dört analizi salt okunur araç oldu (11 → 15 araç); sohbet onları PR2'ye kadar **kullanmaz**: AI servisinde yeni `wealth`
grubu tabloda var ama ona giden kural yok (tabloda olmayan araç her grupta görüneceği için satır şart). Kararlar (INC-03 planı):
K1-A (dört ayrı araç), K2-C (gelir ve öneri yalnız tam ay: `period` `LastMonth` | `Custom`, `ToolCompleteMonth`; "bu ay" seçilemez),
K15-B (birikimde `currencyCode` yalnız `TRY`, `ToolLiraCurrency`).

- **`IncomeTools`:** `get_income_share`, `get_spending_advice` — tam ay ya da `Custom` + ayın ilk–son günü (kuralı servis uygular:
  `2026-09-02`–`2026-09-30` reddedilir). Yeni argüman yok; müşteri yalnız token'dan.
- **`InvestmentTools`:** `what_if_one_off` (beş dönem aracının `period`/`from`/`to`'su + isteğe bağlı `categoryCode`; tarih aralığı
  testleri artık altı aracı tarar), `what_if_monthly` (isteğe bağlı `amount` `["number","null"]`, `currencyCode`, `fromAdvice`
  `boolean`; kaynak kuralları servisin: tutar ya da öneri, ikisi birden/hiçbiri, TRY dışı, 1.000.000 üstü `invalid_arguments`). Her
  sonuçta backend'in sabit `disclaimer`'ı.
- **Bulgu (filtre):** `boolean` tipi denetlenmiyordu; SDK `"fromAdvice":"true"` metnini sessizce `true` yapardı. Filtre artık yalnız
  JSON `true`/`false` kabul eder.
- **Testler:** McpServer 156 → 195 (+39: keşif allowlist'i ve şemalar, 20 argüman reddi, 3 sınır kabulü, 6 rakam testi — Selin pay
  %90,36, Murat Eylül %62,81, Selin öneri 236,18 / Ada `NoRuleMatched`, Eylül eğlencesi 2.253,50 → 2.297,38 / 2.222,62 / 2.194,89,
  birikim öneriyle altın 3.000,60 ve 1.000 TL ile 12.704,72, öneri yoksa `NoAdvice` — ve 5 silinmiş müşteri satırı). Python
  781 → 782 modelsiz. 8 kasıtlı bozmanın hepsi yakalandı. Araç seçimi ve cevap ölçümü PR2'de (açıklamalar orada ölçülüp gerekirse
  değişir; değişirse keşif testi de güncellenir).

