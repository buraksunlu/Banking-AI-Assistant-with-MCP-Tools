# Local AI: model seçimi (AI-01)

16 Ekim 2026. Amaç: bu makinede (Apple M4, 16 GB, MacBook Air) Türkçe sorularda **doğru tool'u doğru
argümanla seçen**, **şemaya uyan JSON** üreten ve **backend'in verdiği rakamları uydurmadan açıklayan** local
Qwen modelini tahminle değil ölçümle seçmek. Ücretli API yok; her şey Ollama üzerinden yerel.

## Karar

| | |
| --- | --- |
| Varsayılan model | **`qwen3.5:4b`** (`BANKINGAI_LLM_MODEL`) |
| Yedek | `qwen3.5:9b` (indirili; tek satırla geçilir) |
| Thinking | **Kapalı** (`think: false`) |
| Sampling | `temperature: 0` (aynı soru aynı seçimi alsın) |
| Yeniden ölçüm | MCP-12/MCP-14'te gerçek tool listesiyle iki model yeniden ölçülür; 4b tool seçiminde %90'ın altına düşerse 9b'ye geçilir |

Neden 4b: iki model de geçme ölçütünü sağladı. 9b biraz daha isabetli ama bu makinede bedeli yüksek: 8–10 GB bellek
alıyor ve ölçüm sırasında swap 7,6 GB'tan 11,4 GB'a çıktı, hızı 12–20 token/sn'ye düştü. İleride aynı anda PostgreSQL
(Colima), .NET API, FastAPI, MCP server ve Xcode/Simulator çalışacak. 4b'nin hataları mimarinin zaten yakaladığı
türden (backend varsayılanıyla aynı fazladan argüman; bir yüzdeyi yazmamak); uydurma sayı iki modelde de yok.
Doğruluğu modelin büyüklüğü değil backend'in kontrolleri sağlar (AI-06, AI-09/10, MCP-12'de argüman doğrulaması).

## Ortam

- Ollama 0.40.5 (macOS uygulaması), modeller harici SSD'de (`OLLAMA_MODELS`, Ollama ayarlarındaki "Model location").
  SSD takılı değilken Ollama açılırsa modeller bulunamaz (AI-02'nin "model bulunamadı" senaryosu).
- `ollama pull qwen3.5:9b` / `qwen3.5:4b` bu Mac'te MLX biçimini indirdi (`nvfp4`; 7,6 GB ve 4,0 GB), q4_K_M değil.
- Python 3.14, yalnız standart kütüphane (Ollama REST API'sine düz HTTP). FastAPI/LangChain AI-03/04'te gelir.

## Yöntem

`ai/model_check/` (sabit vakalar + deterministik puanlama; model kendi çıktısını puanlamaz):

| Grup | Vaka | Ne ölçülür | Geçme koşulu |
| --- | --- | --- | --- |
| Tool seçimi (T01–T12) | 6 tool (analytics endpoint'lerinin sade hali), 10 analiz sorusu + transfer isteği + fıkra | Tool adı ve argümanlar birebir | Tek çağrı, doğru ad, beklenen argümanlar; **söylenmemiş argüman = hata**; kapsam dışında çağrı yok |
| Structured output (S01–S05) | Soru sınıflandırma (`intent`, `period`), Ollama `format` = JSON şema | Geçerli JSON + doğru değerler | Alanlar ve değerler birebir |
| Açıklama (E01–E03) | Hazır backend sonucu (Türkçe biçimli tutar/yüzde) → 2–3 cümle | Uydurma sayı, zorunlu metin | Metindeki her sayı verilen veride var; zorunlu metinler ("735,00 TL", "%29,12" …) aynen geçiyor |

Geçme ölçütü (plan kararı): tool seçiminde en az %90, JSON %100, uydurma sayı yok, cevap başına yaklaşık 10 sn altı.
Açıklama vakalarındaki rakamlar örnek veridir (E02: 415/1.425 = %29,12; E03: 420/735 = %57,14, 210/735 = %28,57,
105/735 = %14,29).

## Sonuçlar (thinking kapalı, temperature 0; iki koşu birebir aynı)

| | qwen3.5:4b | qwen3.5:9b |
| --- | --- | --- |
| Tool seçimi | 11/12 (T08: söylenmemiş `limit=5`) | **12/12** |
| Structured output | 5/5 | 5/5 |
| Açıklama (otomatik puan) | 2/3 (E03: `%57,14` yazılmadı) | 3/3 |
| Ortalama süre: tool / structured / açıklama | **3,1 / 1,3 / 5,0 sn** | 4,6 / 2,6 / 8,3 sn |
| Hız | 21–25 token/sn | 12–20 token/sn (bellek baskısında) |
| İlk yükleme | ~6 sn | ~11 sn |
| Yüklü bellek (`/api/ps`, 4K bağlam) | 4,2–6,9 GB | 7,7–10 GB |

Thinking açık: tool vakaları koşturulamadı. `temperature: 0` ile model "Merhaba"ya 1.500 token düşünüp cevaba hiç
geçmedi (greedy decoding'de tekrar döngüsü; Qwen bu modda greedy kullanılmamasını önerir). Önerilen sampling
(0.6 / 0.95 / 20) ile cevap geldi ama 989 düşünme token'ı, **50 sn**. Ölçütün çok dışında → thinking kapalı.
`run.py --think` artık Qwen'in önerdiği sampling'i kullanır.

## Gözlemler

- **Structured output'ta şema prompt'a da yazılmalı.** İlk koşuda iki model de her soruya `out_of_scope` dedi (1/5).
  `format` yalnız çıktıyı şemaya zorlar, modele göstermez; model prompt'ta adı geçen tek değerleri seçiyordu.
  `format` olmadan da aynı cevap geldi (kök neden prompt). Intent ve dönem değerleri anlamlarıyla prompt'a yazılınca
  ikisi de 5/5.
- **Sayı kontrolü anlam hatasını yakalamaz.** 9b'nin E02'si puanlamayı geçti ama yönü ters: "1.840,00 TL'den
  1.425,00 TL'ye yükselerek". 4b'nin E02'si de anlamca bulanık. Bu, AI-10 (sayı doğrulayıcı + şablon cevap) ve
  "nasıl hesaplandı" panelinin (AI-11) neden gerekli olduğunu gösterir; model büyütmek çözmez.
- Modeller tutarları kalın (`**735,00 TL**`) yazabiliyor; gösterim katmanı Markdown'u hesaba katmalı (AI-08/IOS).
- Kapsam dışı istekler (transfer, fıkra) iki modelde de tool'suz cevaplandı.

## Bilinen sınırlar

- Az vaka (20) ve tek koşu: temperature 0 ile tekrarlanabilir ama soru çeşitliliği dar. Gerçek tool sayısı (15–18)
  MCP'de olacak; yeniden ölçüm o yüzden karar tablosunda.
- Puanlama yazıyla yazılmış sayıları ("yedi yüz") ve anlam hatalarını görmez; Türkçe kalitesi insan gözüyle okunur
  (`run.py` cevapları tablonun altına basar).
- Ölçüm, açık uygulamalarla dolu bir makinede yapıldı (başlangıçta 9,6 GB swap); süreler yük altındaki değerlerdir.

## Çalıştırma

```sh
python3 -m unittest discover -s ai/model_check                       # puanlama testleri (Ollama gerekmez)
python3 ai/model_check/run.py --model qwen3.5:4b --model qwen3.5:9b   # iki modeli karşılaştır
python3 ai/model_check/run.py --only tool                            # BANKINGAI_LLM_MODEL ile tek grup
```

# Ollama bağlantısı (AI-02)

16 Ekim 2026. `ai/service/ollama_client.py`, AI servisinin Ollama'yla konuştuğu tek yerdir (yalnız standart kütüphane;
AI-03'ün FastAPI'si aynı klasöre gelir). Amaç: Ollama'yla ilgili her sorun adı konmuş bir hataya dönüşsün, servis çökmesin
ve ileride kullanıcıya "asistan şu an kullanılamıyor" denebilsin (AI-07: AI kapalıyken deterministik sonuç yine gösterilir).

## Ayarlar

| Değişken | Kural |
| --- | --- |
| `BANKINGAI_LLM_MODEL` | Zorunlu; boşsa açılışta hata |
| `BANKINGAI_OLLAMA_URL` | Boşsa `http://127.0.0.1:11434`; sondaki `/` atılır |
| `BANKINGAI_LLM_TIMEOUT_SECONDS` | Varsayılan 30; pozitif, sonlu sayı (`abc`, `0`, `-5`, `inf`, `nan` reddedilir) |

30 sn: ölçülen en kötü durum soğuk yükleme ~6 sn + en uzun cevap ~8 sn ≈ 14 sn; bellek baskısı için yaklaşık iki kat pay.
Sağlık kontrolü en fazla 3 sn bekler.

## Hata türleri (ölçülen Ollama davranışıyla)

| Durum | Ollama | Tür |
| --- | --- | --- |
| Sunucu kapalı | Bağlantı reddedildi, anında | `OllamaUnavailable` |
| Sunucu tarafı hata (ör. bellek yetmedi) | HTTP 5xx | `OllamaUnavailable` |
| Model indirilmemiş / harici disk takılı değil | HTTP 404 `model '…' not found` | `ModelNotFound` (ayardaki adla) |
| Süre doldu | — | `OllamaTimeout` |
| Hatalı istek (ör. boş model adı) | HTTP 400 `model is required` | `OllamaBadResponse` |
| JSON değil ya da mesaj metni yok | — | `OllamaBadResponse` |

İstemci zaman aşımıyla bağlantıyı bırakınca Ollama işi durdurur (günlükte "context canceled"): 2 sn'de kesilen uzun bir
üretimden hemen sonraki istek 0,3 sn'de cevaplandı. Yani zaman aşımı kuyrukta asılı iş bırakmaz.

## Sağlık kontrolü

`check_ready()` yalnız `/api/tags` sorar (modeli yüklemez): `READY`, `SERVER_UNAVAILABLE` (bağlantı yok, 5xx, JSON değil) ya da
`MODEL_MISSING` (model listede yok; Ollama'nın kuralıyla etiketsiz ad `:latest` sayılır). Harici disk takılmadan açılan Ollama
boş liste döner → `MODEL_MISSING`. Elle deneme:

```sh
python3 ai/service/ollama_client.py
```

Hazırsa kısa bir deneme sorusu sorar ve cevabı süreleriyle yazar; değilse ne yapılacağını Türkçe söyler (çıkış kodu 0/1/2).

## Sınırlar

- `stream: false`: cevap tek parça gelir; akan cevap (AI-08) gelirse süre sınırının anlamı değişir.
- Hata mesajları İngilizce; kullanıcıya giden Türkçe metinler AI-07/AI-08'de.
- Boş cevap metni geçerli sayılır; boş/uyuşmayan cevap AI-06'nın işi.
- Model bellekte Ollama varsayılanı kadar (5 dk) kalır; boşta kaldıktan sonraki ilk soru ~5 sn daha uzun sürer.

# AI servisi (AI-03)

19 Ekim 2026. `ai/service/app.py`: .NET API'nin ileride (AI-07) çağıracağı FastAPI servisi. Veritabanına bağlanmaz, para
hesaplamaz; backend'in hazır ve biçimlendirilmiş sonucunu modele iletir, modelin cevabını döner.

```text
.NET API ──HTTP──► FastAPI (ai/service/app.py) ──► Explainer ──► ollama_client ──► Ollama ──► qwen3.5:4b
```

## Endpoint'ler

| Endpoint | Ne yapar | Cevap |
| --- | --- | --- |
| `GET /health/live` | Süreç ayakta mı? Ollama'ya sormaz | `200 {"status":"ok","model":null}` |
| `GET /health/ready` | Ollama açık ve model kurulu mu? (`check_ready`, modeli yüklemez) | `200 {"status":"ready","model":"qwen3.5:4b"}` ya da 503 |
| `POST /v1/explanations` | Soru + hazır sonuç → Türkçe açıklama | `200 {"answer":"…","model":"qwen3.5:4b"}` |

İstek:

```json
{
  "question": "Bu ay ne kadar harcadım?",
  "facts": [
    { "label": "Dönem", "text": "1-10 Ekim 2026" },
    { "label": "Toplam harcama", "text": "735,00 TL" }
  ]
}
```

Kurallar: `question` 1–500, `facts` 1–30 kalem, `label` 1–100, `text` 1–200 karakter; yalnız boşluk olamaz. Tanınmayan alan
(ör. `customerId`) ve tip dönüşümü (`123` → `"123"`) reddedilir. `facts` backend'in AI-09 kuralıyla biçimlendirdiği metindir:
model ham sayı, GUID ya da hesap numarası görmez (AI-13).

## Hatalar

API ile aynı problem+json (`title`, `status`, `code`, `traceId`, doğrulamada `errors`):

| Durum | HTTP | `code` | `title` |
| --- | --- | --- | --- |
| Geçersiz istek (FastAPI'nin 422'si) | 400 | `validation_failed` | İstek geçersiz. (`errors`: `{"facts[0].text": ["En az 1 karakter olmalı."]}`) |
| Ollama kapalı / 5xx | 503 | `ai_unavailable` | Asistan şu an kullanılamıyor. |
| Model yok | 503 | `model_missing` | Asistan modeli bulunamadı. |
| Süre doldu | 504 | `ai_timeout` | Asistan zamanında cevap veremedi. |
| Anlamsız Ollama cevabı | 502 | `ai_bad_response` | Asistan geçersiz cevap verdi. |
| Bilinmeyen adres / yöntem | 404 / 405 | `not_found` / `method_not_allowed` | Bulunamadı. / Bu adres bu yöntemi desteklemiyor. |
| Beklenmeyen | 500 | `internal_error` | Beklenmeyen bir hata oluştu. (ayrıntı yalnız logda) |

Ollama hataları loglanır (model ve adresle; soru ve veri loglanmaz).

## Yapı

- `create_app()` fabrikası (`uvicorn --factory`): ayarlar açılışta okunur; `BANKINGAI_LLM_MODEL` yoksa servis açılmaz.
- `Explainer` arayüzü (`explainer.py`): AI-03'te `ollama_client.chat` ile **geçici** bir prompt. AI-04 LangChain'e geçer, AI-05
  asıl prompt'u yazar, AI-06 çıktıyı doğrular; endpoint ve testleri değişmez.
- Endpoint'ler düz `def`: `ollama_client` bloklayan çağrı yapar, FastAPI düz fonksiyonları ayrı iş parçacığında çalıştırır.
- Testler `app.dependency_overrides` ile sahte explainer ve hazırlık durumu verir; Ollama gerekmez.

## Bağımlılıklar

Python 3.14, `ai/.venv` (git'e girmez). `ai/requirements.in` doğrudan bağımlılıklar, `ai/requirements.txt` `pip freeze` kilidi.
Kural: tam sabit sürüm, seçildiği gün **en az iki haftalık**. 19 Ekim 2026'da pip dolaylı olarak bir günlük Starlette 1.9.0'ı
ve dört genç paketi seçti; hepsi `requirements.in`'de eski sürümlerle sabitlendi. Starlette'in `TestClient`'ı `httpx` yerine
`httpx2` ister (eskisi uyarı verir).

```sh
/opt/homebrew/bin/python3.14 -m venv ai/.venv
ai/.venv/bin/python -m pip install -r ai/requirements.txt
ai/.venv/bin/python -m pytest ai
ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000
```

## Elle doğrulama (19 Ekim 2026)

live 200; ready 200; gerçek açıklama 4,9 sn ("Bu ay (1-10 Ekim 2026) toplam **735,00 TL** harcamışsınız…"); `customerId`'li
istek 400; Ollama'ya ulaşamayan örnekte ready ve açıklama 503 `ai_unavailable` + log; model ayarı olmadan açılış `ValueError`.

## Sınırlar

- Geçici prompt kalite iddiası taşımaz (AI-05/06/10). Model cevabında Markdown (`**`) olabilir; gösterim AI-08/IOS'ta.
- Token ve müşteri bağlamı yok (AI-07); bu yüzden servis yalnız 127.0.0.1'de dinler. `/docs` açık (yalnız yerel).
- Akan cevap yok (AI-08). Tek kullanıcılı demo için senkron çağrı yeterli.

# LangChain (AI-04)

19 Ekim 2026. Açıklama artık LangChain'in `ChatOllama`'sı üzerinden üretiliyor (`explainer.py`, `LangChainExplainer`). Neden:
ileride tool seçimi, tool sonucunu modele geri verme ve structured output (MCP-12, CHAT) LangChain'in hazır parçalarıyla
yapılacak. AI-04 bu geçişin en küçük adımı: **agent yok, tool yok**, aynı açıklama LangChain üzerinden.

```text
Önce:  /v1/explanations → OllamaExplainer   → ollama_client.chat → Ollama
Sonra: /v1/explanations → LangChainExplainer → ChatOllama (stream) → Ollama
```

Endpoint, sözleşme, hata kodları ve AI-03'ün endpoint testleri değişmedi. Prompt hâlâ AI-03'ün geçici prompt'u (AI-05).

## ChatOllama ayarları

| Ayar | Değer | Neden |
| --- | --- | --- |
| `reasoning` | `False` | Ollama'ya `think: false` gider (AI-01: thinking bu makinede çok yavaş) |
| `temperature` | `0` | Aynı soru, aynı cevap |
| `num_predict` | `400` | 2–3 cümle ~150–200 token; kaçak cevap ~17 sn'de durur |
| `client_kwargs.timeout` | `BANKINGAI_LLM_TIMEOUT_SECONDS` | Yalnız hiç parça gelmezse (ör. model yüklenirken) |

## Ölçülen davranış (19 Ekim 2026)

| Konu | Sonuç |
| --- | --- |
| ChatOllama ne gönderiyor? | `/api/chat`, `think: false`, `temperature: 0`, **`stream: true`** (`invoke`'ta bile) |
| Olmayan model | `ollama.ResponseError`, `status_code=404` |
| Kapalı sunucu | `httpx.ConnectError` |
| `timeout=0,5` ile uzun cevap | **Kesilmedi, 88 sn sürdü**: timeout iki parça arası beklemeyi sınırlıyor |
| Parçaları okuyup 2 sn'de akışı kapatmak | 2,0 sn'de durdu; sonraki istek 0,5 sn (Ollama işi bıraktı) |
| `num_predict=60` | 2,2 sn, `done_reason: "length"` (cevap yarım) |
| Bozuk akış satırı | `json.JSONDecodeError` |
| LangSmith izleme | Varsayılan kapalı; açılırsa prompt'lar buluta gider |

## Toplam süre sınırı

ChatOllama cevabı parça parça aldığı için AI-02'nin "30 sn'de kes" kuralı LangChain'de kendiliğinden çalışmıyor. Bu yüzden
`LangChainExplainer` cevabı `llm.stream()` ile parça parça okur, her parçada saate bakar ve süre dolunca akışı kapatıp
`OllamaTimeout` (504) fırlatır. Akış kapanınca bağlantı kapanır, Ollama da işi bırakır. Sınır en fazla bir parça arası kadar
aşılabilir; hiç parça gelmezse `client_kwargs` timeout'u devreye girer (en kötü durumda yaklaşık iki katı).

## Hata çevirisi

| LangChain / Ollama / httpx | Servis hatası | HTTP |
| --- | --- | --- |
| `ResponseError` 404 | `ModelNotFound` | 503 |
| `ResponseError` 5xx, `httpx.TransportError`, `ConnectionError` | `OllamaUnavailable` | 503 |
| `httpx.TimeoutException`, toplam süre doldu | `OllamaTimeout` | 504 |
| `ResponseError` diğer 4xx, `json.JSONDecodeError`, `done_reason: "length"` | `OllamaBadResponse` | 502 |
| Diğer her şey | çevrilmez (gerçek hata) | 500 |

## LangSmith koruması

`create_app` ortamda `LANGSMITH_TRACING`, `LANGSMITH_TRACING_V2`, `LANGCHAIN_TRACING` ya da `LANGCHAIN_TRACING_V2` açıksa (boş,
`false`, `0`, `no` dışındaki her değer) açılmaz. Banka verisi makineden çıkmamalı; kontrol kazara yapılmış bir ayara karşıdır.

## Bağımlılıklar

`langchain-ollama 1.1.0`, `langchain-core 1.6.6` (en yenisi 3 günlüktü), `ollama 0.6.3`. LangChain yaklaşık 20 paket daha getirdi
(langsmith, httpx, requests, orjson…); iki haftadan genç olanlar (`langsmith`, `jsonpatch`, `jsonpointer`, `orjson`, `tenacity`,
`xxhash`) eski sürümlere sabitlendi. Kilitte 45 paket var.

## Elle doğrulama (19 Ekim 2026)

uvicorn + curl: cevap AI-03'tekiyle kelimesi kelimesine aynı; model sıcakken 2,1–2,3 sn (AI-03'teki 4,9 sn yüklemeyi de içeriyordu);
Ollama'ya ulaşamayan örnekte 503 `ai_unavailable`; `LANGSMITH_TRACING=true` ile servis açılmıyor.

## Yanıt süresi ve mimari kararı (19 Ekim 2026)

Soru: LangChain ve FastAPI katmanları cevabı yavaşlatıyor mu; Ollama'yı doğrudan .NET'e bağlamak daha mı mantıklı?
Ölçüm (aynı soru, model bellekte, her yoldan beşer kez):

| Yol | Ortalama | Fark |
| --- | --- | --- |
| Doğrudan Ollama (`ollama_client.chat`) | 2,03 sn | — |
| LangChain (`LangChainExplainer`) | 2,07 sn | +0,04 sn |
| FastAPI servisinin tamamı (HTTP + doğrulama + LangChain) | 2,13 sn | +0,10 sn |

Ollama'nın kendi ölçümü de 2,03 sn: sürenin neredeyse tamamı modelin kelime üretmesi. **Kullanıcı kararı: A, mevcut yapı
(.NET → FastAPI/LangChain → Ollama) korunur.** Doğrudan .NET bağlantısı (B) ya da LangChain'siz servis (C) en fazla ~0,1 sn
kazandırır, buna karşılık tool döngüsünü ve MCP istemcisini yeniden yazmayı gerektirir.

**Not (D): yanıt süresi sorun olursa başvurulacak ayarlar.** Şimdilik uygulanmadı:

- **Model bellekte daha uzun kalsın** (Ollama `keep_alive`, ör. 30 dk) ya da servis açılırken model ısıtılsın. 5 dk boşta
  kaldıktan sonraki ilk sorudaki ~5 sn yükleme kalkar; bedeli, modelin 4–7 GB belleği sürekli tutması.
- **Cevap akarak gösterilsin** (AI-08): ilk kelimeler ~0,3 sn'de görünür, bekleme hissi azalır.
- **Daha az model turu:** niyete göre tool daraltma (CHAT-01) ve tek mesajda en fazla 3 tool (CHAT-04). Her tool turu bir model turu daha demek.
- **Daha kısa cevap / daha küçük model:** süre cevap uzunluğuyla doğru orantılı (~24 token/sn); 9b aynı cevapta yaklaşık 2 kat yavaş.
