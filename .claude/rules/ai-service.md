---
paths:
  - "ai/**"
  - "docs/local-ai.md"
---

# AI servisi ve Python (ai/)

> CLAUDE.md'den taşındı (23 Ekim 2026). Bu dosya, eşleşen bir dosya okunduğunda ya da düzenlendiğinde kendiliğinden yüklenir.

Local model ölçümü (`ai/model_check`, Python 3.14, yalnız standart kütüphane; Ollama modelleri harici SSD'de, SSD takılı
olmadan Ollama modelleri bulamaz). Thinking modunda temperature 0 sonsuz düşünmeye girer; ayrıntı `docs/local-ai.md`:

```sh
/opt/homebrew/bin/python3.14 -m venv ai/.venv && ai/.venv/bin/python -m pip install -r ai/requirements.txt   # bir kez
ai/.venv/bin/python -m pytest ai                                      # bütün Python testleri; Ollama kapalıysa gerçek-Ollama testleri atlanır
ai/.venv/bin/python -m pytest ai -k "not real"                        # modelsiz: Ollama açıkken bile modeli belleğe yüklemez (~4–13 GB)
python3 ai/model_check/run.py --model qwen3.5:4b --model qwen3.5:9b   # model ölçümü (yalnız standart kütüphane)
ai/.venv/bin/python ai/model_check/explanation_eval.py              # açıklama prompt'u değerlendirmesi (AI-05, Ollama açık)
ai/.venv/bin/python ai/model_check/answer_eval.py                   # cevap doğruluğu (SYN-15 personaları, gerçek zincir; AI servisi + MCP + .env)
ai/.venv/bin/python ai/model_check/phrasings.py                     # modelsiz ifade açıkları (CHAT-09; --typos yazım hatalarını da)
ai/.venv/bin/python ai/model_check/phrasing_eval.py --pause 1       # modelli ifade örneklemi (MCP + Ollama + .env; --typos, --refresh-tools)
ai/.venv/bin/python ai/model_check/question_eval.py --pause 1       # model yazımı seçim soruları (CHAT-09 PR3, clarify-v2; yalnız Ollama)
python3 ai/service/ollama_client.py                                  # elle kontrol (BANKINGAI_LLM_MODEL gerekir)
ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000   # AI servisi (AI-03)
```

Python bağımlılıkları (AI-03): `ai/requirements.in` doğrudan bağımlılıklar (elle), `ai/requirements.txt` `pip freeze` kilidi. Sürümler
tam sabitlenir ve seçildiği gün **en az iki haftalık** olmalıdır; pip'in seçtiği dolaylı paket daha yeniyse `requirements.in`'e eski bir
sürümle eklenir. Starlette'in `TestClient`'ı `httpx` yerine `httpx2` ister.

`ai/service/ollama_client.py` (AI-02) Ollama'yla tek konuşma yeridir: her hata dört türden birine çevrilir (`OllamaUnavailable`,
`ModelNotFound`, `OllamaTimeout`, `OllamaBadResponse`); `check_ready()` modeli yüklemez. Testler gerçek Ollama yerine sahte HTTP
sunucusu kullanır; gerçek Ollama testleri Ollama kapalıysa atlanır.

Sohbet (AI-14a, MCP-b): `POST /v1/chat` (Bearer zorunlu; gövde `{message, today, timeZone}`, ikisi de .NET'ten, CHAT-08 PR1: müşterinin günü) → `chat.ChatService`: CHAT-01 `routing.Router` (kod önce: kapsam dışı kuralı →
sabit metin, modelsiz; planlama kelimesi → `planning` grubu, harcama/ödeme kelimesi → `general` = MCP-08'in 9 aracı; takipte son çağrının
grubu; kural yoksa `LangChainIntentClassifier` `intent-v1`, ≤ 5 sn, karar yoksa `general`) → `mcp_gateway.McpToolGateway.list_tools` (her
turda, kullanıcının token'ıyla; MCP kapalıysa model çalışmaz), `narrow` ile grubun araçları (tabloda olmayan araç her grupta; yeni araç
`routing.GROUPS`'a yazılır) → `LangChainToolSelector` (o turun listesiyle `bind_tools`, `tool-selection-v1`) →
araç adı listede mi → `McpToolGateway.call` (argümanları MCP server şemaya göre kontrol eder; `invalid_arguments` → sabit geri soru) →
`facts.outcome_for` (`formatting.py` ile Türkçe metin; yok merchant modelsiz sabit cevap) → açıklama. CHAT-03: seçimden sonra
`grounding.check_arguments` (dönem/sayı mesajda yoksa araç çalışmaz, geri soru; isteğe bağlı dayanaksız argüman düşer), belirsiz merchant
`facts` → `candidates`; soru `clarification.PendingStore`'da (anahtar `sha256(token)`, 5 dk) saklanır, sonraki kısa cevap (≤ 4 kelime)
`resolve` ile kodda eşleşirse model seçimi olmadan araç tamamlanır, eşleşmezse yeni soru. Soruyu `LangChainClarificationWriter` yazar
(`clarify-v1`), `check_question` geçmezse sabit şablon; seçenekler hiçbir zaman modelden gelmez. CHAT-02: son başarılı çağrı
`conversation.ConversationStore`'da (10 dk; ortak `memory.TokenMemory`); `is_follow_up` (≤ 4 kelime ya da peki/ya…) mesajlar seçiciye
bağlamla gider (`tool-selection-v2`), `check_arguments(…, previous)` son çağrıdan aynen geleni kabul eder, söylenmeyen işyeri son
çağrınınki olur; bağlama ve açıklamaya yalnız son mesaj gider (zincirleşen soru modeli ve açıklamayı yanıltıyordu). Araç tanımı Python'da
yok (MCP'den keşif); modelin listesinden `routing.hide_dates` beş dönem aracının `from`/`to`'sunu ve `Custom` dönemini çıkarır (tarihi model seçmez, CHAT-08). Tarihi `dates.read_dates` kodla okur ("1-5 Eylül", "Eylül'de"; modelsiz): araç seçici tarih yerine "bu ay" görür, kod dönemi `Custom` + `from`/`to` ile ezer, cevabın başına `Dönem: …` satırı ekler; geçersiz/okunamayan tarih ve araç seçilemeyen harcama sorusu araçsız dönem sorusuna gider (`Pending.tool == ""`). Bütün tur tek süre; token yalnız MCP başlığına gider (testte modele gitmediği doğrulanır). Uçtan uca deneme için
.NET API ve MCP server `BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z` ile açık olmalı. Tool seçimi ve açıklama aynı `explainer.stream_message`'ı kullanır.

Önerilen sorular (CHAT-06, `ai/service/suggestions.py`): açıklanmış cevabın `suggestions`'ı sabit `follow_ups` tablosundan (araç → 2–3 öneri,
az önce çalışan çağrı önerilmez); `OfferedStore` bunları çağrılarıyla **yalnız bir sonraki mesaj** için (10 dk, `sha256(token)`) tutar ve
aynı metin (harf katlanmış, sondaki ?.! yok) gelirse araç model seçmeden çalışır (bekleyen sorudan sonra bakılır). Kapsam dışı cevapta 3 sabit
soru, geri soruda öneri yok. Başlangıçlar `GET /v1/suggestions` (`StarterQuestions`: üç modelsiz MCP kontrolü, ≤ 5 sn, sırayla ilk 3).
Yeni araç eklenince tabloya satır; öneri metni ya ölçümdeki bir takip vakası ya da ölçülmüş tam soru olmalı (`test_suggestions` denetler).

Gelir, öneri ve "ne olurdu" (INC-03 PR2, `wealth.py`, ayrıntı `docs/local-ai.md`): `routing._WEALTH_WORDS` planlama/genel kelimelerden önce bakılır
(-seydim/-saydım, maaş/gelir, "nerede tasarruf"); `wealth` grubuna model gönderemez. `_ADVICE_REQUEST` → modelsiz `ADVICE_DECLINED_TEXT`
(`Route.advice`). Gelir/öneri aracı yalnız tam ay: `needs_month_question` (model hep `LastMonth` yazar) ve `_run`'daki `has_complete_month`
→ `ask_complete_month`. `what_if_monthly`: `plan_monthly` (tutar mesajda yazılı, para birimi tutarın bitişiği `amount_currency_in`, TL dışı
`LIRA_ONLY_TEXT`); `advised_saving_call` "önerdiğin tutarı biriktirseydim"i kodla seçer (4b seçemedi). `grounding.mentions_category`: uydurma
`categoryCode` düşer. `ToolOutcome.appendix`: öneri cümleleri + yatırım uyarısı açıklamadan sonra kodla eklenir; `answer_check.has_advice` bulursa
yalnız kalemler. Cevap eval'i 23 vaka (A19–A23).

Farklı yazılışlar (CHAT-09, `ai/model_check/phrasings.py`, ayrıntı `docs/local-ai.md`): ölçümler sohbetin adımlarını **kopyalamaz**; her
ifade gerçek `ChatService`'ten geçer, `ProbeGateway` araç çağrısında `Captured` ile durur, model yerine `OracleSelector` (modelsiz) ya da gerçek
seçici. `tool_selection_eval.py` (100 vaka; Q01–Q09 PR3) ve `phrasing_eval.py` (örneklem) bunu kullanır. `test_phrasings.py` 781 ifadeyi modelsiz çalıştırır;
kırılan her ifade `phrasing_gaps.json`'da gerekçeli olmalı, düzelen açık listeden silinir (yoksa test kırılır). Yeni niyet/kalıp →
`phrasing_templates.json`; gerçek kullanıcı cümlesi → `real`. Araç tanımı değişince `phrasing_eval.py --refresh-tools` (`mcp_tools.json`).
`phrasings.py` çıktısı açıkları listeler. Test adında "real" geçmesin (`-k "not real"` modeli dışarıda bırakan süzgeçtir).
**Katlama (CHAT-09 K11-A):** `fold_turkish` altı Türkçe harfi düz harfe çevirir; her kural kalıbı/kelime tablosu `fold_rule(...)` ile
derlenir (kaynakta Türkçe yazılır). Katlanmamış Türkçe harfli kalıp `test_folding.py`'yi kırar. Katlama çakıştırırsa ("sınır"/"sinir")
kural biçimle ayrılır. Dönem: mesajdaki tek adlı dönem modelinkini ezer (`grounding._said_period`, K12-A). Zenginlik grubunda
`routing.scenario_of` → `Route.only` (K5-D); tek araçlı listede model boş dönerse `chat` aracı seçer (`wealth.chosen_scenario_arguments`,
K13-A). Aylık birikimde para birimi sorulmaz, çıplak tutar TL (K6-B). Ne olurdu cevabında açıklayana `ToolOutcome.question` gider,
`answer_check.misnamed` değer–ad karışıklığında kalemleri gösterir (K7-A, K16-A).
**Belirsizlikte geri soru (CHAT-09 PR3):** "-seydim" sorusunda senaryo kelimesi ikisi ya da hiçbiri ise `Route.undecided`; model denenmez,
işaret yoksa `wealth.scenario_by_figures` (dönem/tarih/kategori → tek seferlik, dönem sayısı olmayan tutar → aylık; "son 3 ayda"nın 3'ü
tutar değil), yine belirsizse `ask_scenario` (K17-C, K18-B). Planlamada model boş dönerse `routing.planning_of` aracı seçer, bulamazsa
`ask_topic` (K19-C; modelin listesi değişmez). Seçim soruları (`CHOICE_KINDS`, `Pending.tool == ""`) modelce kullanıcının sözleriyle
yazılır (`clarify-v2`), `check_question` seçeneği etiketiyle ya da `Option.words` kalıbıyla arar; cevap `chosen_tool` ile kodla eşleşir
(numara ya da kelime, ikisi birden → yeni soru); seçim sonrası özgün soru seçilen tek araçla yeniden işlenir, çıplak numara eklenmez
(tutar sanılırdı). Tek seferliğin kelimelerinde "harca" yok: "Dün ne harcadım?" yeni sorudur.
Yeni wealth aracı/öneri satırı: `routing.GROUPS`, `facts.outcome_for`, `suggestions._TABLE`.
**Tutar birimi ve tekrar (CHAT-10 PR1):** `amounts_in` `k`/`bin`/`milyon`'u bitişik ya da ekli okur; rakama bitişik bilinmeyen harf tutar
değildir (`unclear_amounts`; tutar cevabında `ask_amount_again` notla yeniden sorar). Hata ile biten turda bekleyen soru ve öneriler
`put_back` ile geri konur (yeni soru ezilmez); bekleyen soru yokken çıplak numara `BARE_CHOICE_TEXT` alır, modele gitmez.
**Öneriler (CHAT-10 PR2):** `follow_ups(tool, args, asked, shape)`: araç başına sıralı adaylar, sohbette sorulanlar (`AskedStore`, 30 dk, yalnız
gösterilen cevaptan sonra yazılır) ve sonuca göre anlamsızlar (`result_shape`, yalnız sayılar) elenir; aday kalmazsa en fazla 2 genel soru.
Metinler dönemli tam sorudur (`PATTERNS`); yeni kalıp → 2 model vakası + `phrasing_templates.json` "offered" niyetinde bütün dönemleri.
**Seçenekler (CHAT-10 PR3):** geri sorunun seçenekleri `ChatReply.choices` (`clarification.choices(pending)`, `Choice(label, detail)`)
ile ayrı gider; `template` ve model yazımı (`clarify-v3`) yalnız soru cümlesidir. Her `Pending` türü dokunulabilir (tutar örnekleri
`AMOUNT_EXAMPLES` 1.000/5.000/10.000 TL; tutar sorusunda ayrıca `ChatReply.input_hint` "Başka bir tutar yazın", sözleşmede
`inputHint`, PR3b D6-A); yeni tür ya da seçenek eklenince her etiketin `resolve`/`chosen_tool` ile kendi değerine
eşleştiği test genişletilir. `check_question` seçenek saymaz, ama yarım anma, liste, sunulmayan dönem, sayı ve "işyeri" demeyen işyeri
sorusunu reddeder. Prompt değişince `question_eval.py` (8 vaka, eşik %80) önce/sonra. Senaryo sorusu yok (PR3b D8-B): belirsiz "ne
olurdu" modelsiz aylık birikimdir, tutar sorusuna "tek seferlik" cevabı (`wants_one_off`) tek seferliğe geçer. İki "ne olurdu", bütçe ve
tasarruf cevaplarını kod yazar (`ToolOutcome.written`); `written` dolu bir sonuçta model, tavsiye/ad denetimi ve "Dönem:" satırı
çalışmaz. Kullanıcının rakamları takipte yalnız "peki/ya…" ile taşınır (`conversation.carried`).

MCP istemcisi (MCP-b, `ai/service/mcp_gateway.py`, `mcp` 2.3.0): `BANKINGAI_MCP_URL` (yoksa `http://127.0.0.1:5249/mcp`; yanlış biçimde
servis açılmaz). Protokol `2026-07-28`'e sabit (el sıkışma/keşif isteği yok); her çağrı kendi kısa olay döngüsünde (`anyio.run`, sohbet
senkron). **Tuzaklar:** (1) SDK `structuredContent`'i float'a çevirir (145.00 → 145.0); sonuç **metin içeriğinden** `Decimal` ile okunur.
(2) SDK 401 ve 5xx'i genel "Server returned an error response" hatasına gömer; `httpx2` cevap kancası bunları ayırır. (3) Hatalar iç içe
ExceptionGroup gelir; `_cause` ilk yaprağı alır. (4) SDK bir çağrının çevresinde kendiliğinden `tools/list` yapabilir. Süre hem
`anyio.fail_after` (toplam) hem HTTP zaman aşımıyla (istek başına) sınırlı. Hazır olma (`/health/ready`) MCP server'ın tokensız
`/health`'ine de bakar. Testlerde `FakeMcp` gerçek sunucunun 2026-07-28 cevaplarını (`resultType`, `ttlMs`, `cacheScope`) taklit eder;
gerçek .NET server açıksa (ve demo anındaysa) iki test daha çalışır. Araç seçim ölçümü:
`ai/.venv/bin/python ai/model_check/tool_selection_eval.py` (71 vaka; S27–S31 geri soru, S35–S47 bütçe/tasarruf, F01–F16 takip, R01–R08
kural dışı yazılış; varsayılan sohbet gibi yönlendirir, `--no-routing` tam liste, `--pause N` vaka arası bekleme; MCP server + Ollama açık, `.env` yüklü; MCP server yeni kodla
yeniden başlatılmış olmalı). Araç ekleyince/açıklama değişince önce ve sonra çalıştırılır (MCP-08 K2-A: eski vakalar bozulmaz, toplam
≥ %90; CHAT-01'de 4b 68/71). Sohbet cevabında araç argümanları **yazı** olarak döner (`app._as_text`: sözleşme ve .NET DTO `dict[str, str]`); araç seçici
`num_predict=200` (metin yazan model süreyi doldurmasın). Her yeni araca `facts.outcome_for`'da kalem üretici gerekir; ham
`rawDescription` asla kalem olmaz. MCP-17/18: kullanıcının kendi rakamları (bütçe tutarı, para birimi, azaltma oranı) `grounding`'de
mesajda aranır ("1.000" = 1000, "5 bin", "yarı" = 50); söylenmeyen ya da yanlış okunan sorulur. Bu sorular hep sabit metindir (model
yazmaz) ve `Pending.later` ile zincirlenir (tutar → para birimi). Araç açıklamaları kısa tutulur: 11 kategorinin listesi eski araçların
dönem seçimini bozdu ("geçen hafta" → `Last7Days`), kodlar yalnız şemada; `check_budget` listedeyken "Dün ne harcadım?" yanlış araca
gidiyordu, özet aracının açıklamasındaki "ilk tercih" bunu düzeltti (eski açıklama değişince 63 vakanın hepsi yeniden ölçülür).

AI servisi (AI-03, `ai/service/app.py`, FastAPI): `GET /health/live` (Ollama'ya sormaz), `GET /health/ready` (`check_ready`; değilse 503),
`POST /v1/explanations` (`question` + `facts`: backend'in biçimlendirdiği etiket/metin listesi; tanınmayan alan 400, `strict`). Hatalar
API'ninkiyle aynı problem+json (`title`, `status`, `code`, `traceId`; 422 yerine 400). Ollama hataları: 503 `ai_unavailable`/`model_missing`,
504 `ai_timeout`, 502 `ai_bad_response`. Uygulama `create_app()` fabrikasıyla kurulur (`--factory`; model ayarı yoksa açılmaz); açıklamayı
`Explainer` arayüzü üretir (`explainer.py`), testler `app.dependency_overrides` ile sahte explainer/readiness verir.
Servis yalnız 127.0.0.1'de dinler; token AI-07'de.

Isıtma (AI-15): açılışta lifespan arka planda `ollama_client.warm_up` çağırır (prompt yok, açılış beklemez, hata yalnız uyarı logu;
testlerde yalnız `with TestClient(app)` tetikler, `create_app(env, warm_up=...)` sahte alır). `BANKINGAI_LLM_KEEP_ALIVE` (varsayılan `30m`)
**her** Ollama isteğinde gönderilmeli: alansız istek süreyi 5 dk'ya çeker. Süresiz `-1` sayı olarak gider (`"-1"` yazısı 400 alır).

LangChain (AI-04): `LangChainExplainer` `ChatOllama` kullanır (`reasoning=False`, `temperature=0`, `num_predict=400`). **Tuzak:** ChatOllama
`invoke`'ta bile stream eder; `client_kwargs` timeout'u yalnız iki parça arası beklemeyi sınırlar. Toplam süre bu yüzden parçalar okunurken
kontrol edilir ve süre dolunca akış kapatılır (Ollama da işi bırakır). LangChain/Ollama/httpx hataları `_translate` ile dört türe çevrilir;
yalnız `json.JSONDecodeError` yakalanır (geniş `ValueError` gerçek hatayı gizlerdi). `LANGSMITH_TRACING`/`LANGCHAIN_TRACING_V2` açıksa
servis açılmaz (veri buluta gitmesin). Testler Ollama'nın satır satır JSON akışını taklit eden sahte sunucu kullanır (`test_explainer.py`).

Prompt (AI-05): `ai/service/prompts.py`, `PROMPT_VERSION` ile. Prompt metni değişince sürüm adı da değişir ve
`ai/.venv/bin/python ai/model_check/explanation_eval.py` (10 vaka, gerçek model; `pytest`'e dahil değil) önce ve sonra çalıştırılır.
Soru ve kalemlerdeki `<`/`>` `‹`/`›` olur (etiketle bloktan kaçılamaz); kural hatırlatması veri bloğundan sonra gelir (4b son talimata uyuyor).
Değerlendirmede yasak metin Türkçe harf katlanarak aranır ("TALIMAT" = "talimat"). Prompt kural koyar ama garanti vermez: kod kontrolü AI-06/10.

Çıktı doğrulama (AI-06): `answer_check.check_answer` her cevabı kalemlere karşı kontrol eder (her sayı soruda/veride geçmeli; boş cevap ve
`<veri`/"backend" yasak). `LangChainExplainer.explain` geçmeyen cevaba bir kez düzeltme ister (`prompts.build_retry_messages`), iki deneme
tek süreyi paylaşır; yine geçmezse `AnswerRejected` → 502 `ai_answer_rejected`. Sayı okuma/harf katlama yalnız `answer_check.py`'da
(`scoring.py` oradan import eder; `answer_check` standart kütüphaneyle sınırlı kalmalı). Testlerde model `_ask`, saat `clock` ile değiştirilir.
