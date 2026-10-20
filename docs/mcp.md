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

Hepsi `ReadOnly = true`, `Destructive = false`. Argümanlar yalnız `period` (11 ad: `Today`, `Yesterday`, `ThisWeek`,
`LastWeek`, `Last7Days`, `Last30Days`, `ThisMonth`, `LastMonth`, `Last3Months`, `Last6Months`, `ThisYear`; `Custom` ve tarih
yok) ve merchant aracında `merchant` (1–150 karakter). Müşteri, hesap ve tarih argümanı yoktur.

| Araç | Servis | Sonuç (REST karşılığı) |
| --- | --- | --- |
| `get_spending_summary` | `SpendingSummaryService` | `SpendingSummary` (`/api/analytics/spending-summary`) |
| `get_category_breakdown` | `CategoryBreakdownService` | `CategoryBreakdown` (`/api/analytics/category-breakdown`) |
| `get_merchant_spending` | `MerchantSpendingService.GetSpendingAsync` | `MerchantSpendingResult` (`/api/analytics/merchant-spending`) |

Yeni araç = `SpendingTools` benzeri bir sınıfa `[McpServerTool]` metodu + `ToolDiscoveryTests`'teki allowlist'e satır
(eklenmezse keşif testi kırmızı olur; API'deki endpoint taramasının karşılığı).

## Argüman kontrolü

SDK şemayı yayınlar ama **çalışma anında uygulamaz** (SDK belgesi: argümanlar "doğrulanmamış ve güvenilmez"); enum bağlaması
da "yesterday"i sessizce "Yesterday" yapar. Bu yüzden `ToolCallFilter` her çağrıda, araç çalışmadan önce, argümanları
**yayınlanan şemaya** göre kontrol eder: tanınmayan argüman, eksik/null zorunlu argüman, tam adla enum, metin tipi, Unicode
karakter sayısıyla uzunluk. Boş metin ve NUL gibi backend kuralları servislerde kalır (`InputRules`). Modelin gördüğü şema
ile kabul edilen tam olarak aynıdır.

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
- Araçlar yalnız 3; diğerleri MCP-03…26. Hız sınırı yok: MCP'ye yalnız aynı makinedeki AI servisi ulaşır.

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
