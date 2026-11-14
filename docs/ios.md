# iOS uygulaması (SwiftUI)

`ios/BankingAI/` (Xcode 26.5, Swift 6.3, en düşük iOS 17). Kurallar RULE.md: yalnız Swift + SwiftUI, MVVM, URLSession, Codable,
NavigationStack, üçüncü parti kütüphane yok. Uygulama yalnız .NET API'ye konuşur; finansal hesap mobilde yapılmaz.

## iOS-a: proje temeli ve giriş (IOS-01, IOS-03 çekirdeği, IOS-13; 20 Ekim 2026)

```text
ios/BankingAI/
  BankingAI.xcodeproj          (eşitlenen klasörler: klasöre eklenen dosya projeye kendiliğinden girer)
  BankingAI/
    App/         BankingAIApp (oturum + istemci bir kez kurulur), RootView (giriş mi ana ekran mı), AppConfig (API adresi)
    Networking/  APIClient (tek ağ kapısı), APIError (+ Türkçe mesaj), APIModels (Codable)
    Session/     SessionStorage (Keychain), SessionStore (@Observable oturum)
    Login/       LoginView, LoginViewModel
    Home/        HomeView, HomeViewModel (ana ekran: "Merhaba, Ada!", "Asistana sor", Çıkış)
    Chat/        ChatView, ChatViewModel, MessageBubble, ChatMessage (AI-08)
    Info.plist   yalnız yerel ağa düz HTTP (NSAllowsLocalNetworking)
  BankingAITests/  Swift Testing; sahte ağ (MockURLProtocol, test başına ayrı sunucu adı), bellekte oturum
```

Akış: `View → ViewModel (@Observable) → APIClient → URLSession → .NET API`. View'larda ağ kodu yok. Xcode 26 yeni projelerde
bütün kodu varsayılan olarak `MainActor`'a bağlar (`SWIFT_DEFAULT_ACTOR_ISOLATION = MainActor`); ağ beklemeleri `await` ile
arayüzü dondurmaz.

### Kurallar

- **Token yalnız Keychain'de** (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`; yedeğe ve başka cihaza gitmez), UserDefaults'ta
  değil. Şifre hiçbir yerde saklanmaz; her denemeden sonra alandan silinir.
- **401:** token'lı bir çağrıda 401 → oturum kapanır, giriş ekranı gelir (`APIClient.onUnauthorized`). Giriş çağrısındaki 401
  yalnız "Kullanıcı adı veya şifre hatalı." demektir; oturuma dokunmaz.
- **Süre:** token'ın bitişi (`expiresAt`, 60 dk) saklanır; uygulama açılırken ya da çağrı öncesi süresi geçmişse oturum kapanır.
  Token yoksa çağrı ağa hiç çıkmaz.
- **Hatalar** (`APIError`): API'nin problem gövdesi (`status`, `code`, Türkçe `title`, 429'da `Retry-After`), `unreachable`,
  `cancelled`, `invalidResponse`. Ekrandaki metin `APIError.message`: API başlıkları zaten Türkçe; 429 →
  "Çok fazla deneme; N sn sonra tekrar deneyin."

### Çalıştırma

Simülatör Mac'in ağını kullanır; `http://localhost:5248` Mac'teki API'dir.

```sh
dotnet run --project src/BankingAI.Api --launch-profile http
open ios/BankingAI/BankingAI.xcodeproj     # Xcode'da iPhone 17 Pro seç, ▶ Run
xcodebuild -project ios/BankingAI/BankingAI.xcodeproj -scheme BankingAI \
  -destination 'platform=iOS Simulator,name=iPhone 17 Pro,OS=26.5' test
```

Demo kullanıcıları `ada`, `deniz`, `ege`; şifreler yalnız yerel `.env`'de (`BANKINGAI_DEMO_PASSWORD_*`).

### Test ve doğrulama

34 test durumu (APIClient 18, Login 7, Home 4, SessionStore 4, Keychain 1). 7 kasıtlı bozma yakalandı: token gönderilmez, giriş
401'i oturumu kapatır, `Retry-After` okunmaz, süresi geçmiş oturum açılışta kalır, şifre alanda kalır, boşluklu kullanıcı adı
kabul edilir, 401 oturumu kapatmaz. Elle (simülatör, gerçek API): yanlış şifre mesajı, doğru girişte "Merhaba, Ada!", uygulama
kapanıp açılınca oturum sürüyor, Çıkış giriş ekranına döndürüyor.

### Sınırlar

- Yalnız simülatör: gerçek iPhone için API'nin ağa açılması ve HTTPS gerekir (OPS-04).
- Hesap ekranları IOS-04… (ana ekrana eklenecek).
- Sohbet çağrısı dışındaki istekler URLSession'ın varsayılan bekleme süresini (60 sn) kullanır.

## iOS-b: sohbet ekranı (AI-08; 20 Ekim 2026)

Backend değişmedi; iPhone mevcut iki uç noktayı kullanır: `GET /api/assistant/status` ve `POST /api/assistant/messages`.

```text
HomeView ──"Asistana sor"──► ChatView ──► ChatViewModel ──► APIClient ──► .NET API ──► AI servisi ──► MCP + Ollama
 (@State chat: ChatViewModel; ana ekranla yaşar, çıkışta biter)
```

- **Yer (K11-B):** ana ekranda "Asistana sor" düğmesi, sohbet NavigationStack ile itilir. Hesap ekranları ileride ana ekrana gelir.
- **Geçmiş (K4-D):** yalnız bellekte. `ChatViewModel` `HomeView`'un `@State`'inde durur: geri dönüp girince konuşma yerindedir; çıkış
  yapınca ana ekranla birlikte silinir. Diske yazılmaz.
- **Gönderme:** tek seferde bir soru (tek yerel model sırayla cevaplar; API dakikada 10 mesaj). Metin kırpılır; boş ya da 500 karakteri
  aşan mesaj gönderilmez (API gibi Unicode karakter sayılır).
- **Durdur (K5-C):** bekleyen isteği iptal eder; ekrandan çıkınca ya da uygulama arka plana geçince de iptal. Soru "Durduruldu" +
  "Tekrar gönder" olur, hata gösterilmez. Model arka planda turu yine bitirir (sınır).
- **Hatalar (K6-A, `ChatViewModel.failed`):** 503 → turuncu "Asistan şu an kullanılamıyor" bandı, yazı alanı kapanır, "Yeniden kontrol et"
  durumu yeniden sorar; 504/502 → balonun altında API'nin Türkçe başlığı + "Tekrar dene"; 429 → "N sn sonra"; 401 → oturum kapanır,
  giriş ekranı gelir (APIClient). "Tekrar dene" aynı balonu yeniden gönderir, sorunun ikinci kopyası oluşmaz.
- **Durum (K7-A):** ekran açılınca bir kez sorulur; başarısız kontrol durumu değiştirmez.
- **Bekleme süresi (K12-A):** yalnız sohbet isteğinde 40 sn (`APIRequest.timeout`). Zincir AI servisi 30 → .NET 35 → iPhone 40: API'nin
  açıklayıcı 504'ü her zaman önce gelir.
- **Cevap:** satır içi Markdown (K8-A, `inlineOnlyPreservingWhitespace`: kalın/italik/bağlantı, satır sonları korunur); bozuk Markdown
  düz metin olarak gösterilir. `Answer`, `Clarification`, `OutOfScope` aynı balonda. API'nin gönderdiği araç ve rakam kalemleri çözülmez
  (K13-A; "nasıl hesaplandı" paneli IOS-11).
- **Boş sohbet (K9-A):** üç örnek soru, dokununca gönderilir (kapsam dışı cevabının önerdiği sorularla aynı). CHAT-06'dan beri bunlar
  yalnız yedek: önce sunucunun başlangıç soruları istenir (aşağıda "Önerilen sorular").

### Test ve doğrulama (AI-08)

54 test durumu (önceki 34 + `ChatViewModelTests` 20: kırpma ve 500 sınırı, boş/uzun mesaj, beklerken ikinci mesaj, Durdur, 503 bandı ve
geri gelme, zaman aşımı, 429, 401, aynı balonda yeniden deneme, açılıştaki durum, cevap türleri, Markdown kalın/bozuk, örnek sorular,
yalnız sohbet çağrısının 40 sn beklemesi). 8 kasıtlı bozma yakalandı (aralarında: Durdur iptal etmez, 503 bandı açmaz, "Tekrar dene" yeni
balon ekler, beklerken ikinci gönderim engellenmez, mesaj kırpılmaz, bozuk Markdown metni yutar).

Elle (simülatör; API + MCP server + AI servisi + Ollama açık, `BANKINGAI_DEMO_NOW=2026-10-10T12:00:00Z`): örnek soruya kalın rakamlı
cevap; Durdur → "Durduruldu", "Tekrar gönder" aynı balonda cevap alır; geri dönüp girince geçmiş duruyor, beklerken çıkınca soru
durduruluyor; AI servisi kapalıyken bant + kapalı yazı alanı + pasif "Tekrar dene", servis açılınca "Yeniden kontrol et" bandı kaldırıyor ve
"Tekrar dene" cevap alıyor; çıkış ve yeniden girişte sohbet boş.

### Sınırlar (AI-08)

- Durdur yalnız iPhone'un beklemesini bırakır; .NET ve AI servisi turu bitirir (iptal zincire taşınmıyor).
- Takip sorusu yok ("peki geçen ay?", CHAT-02); her mesaj bağımsız.
- Kaynak ve rakam paneli yok (IOS-11).
- Soğuk model: model 5 dk kullanılmayınca bellekten çıkar; harici SSD'den yüklenirken ilk soru 504 alabilir ("Tekrar dene" çalışır).
- Akarak gösterme (stream) yok: cevap tek seferde gelir.

## iOS-c: önerilen sorular (CHAT-06 PR2; 22 Ekim 2026)

Arka uç (PR1, docs/local-ai.md "Önerilen sorular") her açıklanmış cevapla `suggestions` ve sohbet açılışı için
`GET /api/assistant/suggestions` veriyor; iPhone bunları yalnız gösterir ve dokunulanı **metni aynen** gönderir (sunucu o metni tanıyıp
aracı model seçmeden çalıştırır). Hangi sorunun geleceğine iPhone karar vermez.

```text
ChatView ──dokunma──► ChatViewModel.send(metin) ──► APIClient ──► POST /api/assistant/messages
   ▲ suggestions (son cevabın altında) / starters (boş sohbet)       ◄── reply.suggestions
   └── .task { open() } ──► GET /api/assistant/status  +  (sohbet boşsa) GET /api/assistant/suggestions
```

Kullanıcı kararları:

- **K1-B — tek liste:** `ChatViewModel.suggestions` her cevapta yenilenir, bir şey gönderilir gönderilmez boşalır (sunucu da teklifini
  yalnız bir sonraki mesajda tanır). Hata, Durdur ve geri soruda liste boş.
- **K2-A — son balonun altında:** alt alta küçük düğmeler (`.bordered`, `.small`), yalnız son mesaj asistanınsa; beklerken pasif.
- **K3-B — başlangıçlar yalnız boş sohbette**, durum kontrolüyle yan yana (`open()`); geçmiş varsa istek gitmez.
- **K4-B — yüklenirken gösterge, olmazsa yedek:** `starters` boşken `ProgressView`; hata (503/504/bozuk) ya da boş liste → sabit üç örnek.
  Başlangıç hatası bandı açmaz; bandı yalnız durum kontrolü açar.
- **K5-A — yedek örnekler** arka ucun kapsam dışı cevabındaki üç soru (her müşteri cevap alır; eski listedeki Starbucks sorusu çıktı).
- **K6-A — `suggestions` zorunlu:** alanı olmayan cevap "Beklenmeyen bir cevap alındı" + "Tekrar dene" (sessizce "öneri yok" sayılmaz).

### Test ve doğrulama (CHAT-06)

66 test durumu (önceki 54 + `ChatSuggestionTests` 12: cevabın önerileri, dokunulan metnin aynen gitmesi, gönderince boşalması,
hata ve geri soruda boş liste, alanı olmayan cevap, boş sohbette başlangıç + durum, dört yedek durumu, geçmişte başlangıç isteği yok).
6 kasıtlı bozma yakalandı (gönderince temizlememe, öneri koymama, geçmişte de isteme, yedeksiz bırakma, alanı isteğe bağlı yapma,
Durdur'un iptal etmemesi).

**Bulgu (kararsız test):** `stoppingCancelsTheRequestAndShowsNoError` yeni testlerle birlikte yaklaşık her iki koşuda bir kırmızıydı.
URLSession iptali uydurma sunucuya kendi kuyruğunda, görev hata verdikten biraz sonra bildiriyor; test sayacı hemen okuyordu. Test artık
isteğin sunucuya ulaşmasını ve iptal sayacını kısa süre (`waitUntil`, ≤ 2 sn) bekliyor; düzeltmeden sonra 10/10 ve bozmayı hâlâ yakalıyor.
Ürün kodu değişmedi.

### Sınırlar (CHAT-06)

- Öneriler yalnız son cevabın altında; eski cevaplarınkiler kaybolur.
- Başlangıç soruları ekran her boş açıldığında yeniden istenir.
- Öneri düğmeleri erişilebilirlikte normal düğme olarak okunur.

## iOS-d: hesaplar (IOS-04; 22 Ekim 2026)

Ana ekranın gövdesi artık müşterinin hesap listesi: selamlamanın altında "Hesaplarım", en altta her zaman erişilebilen "Asistana sor".
Backend değişmedi; liste `GET /api/accounts`'tan (API-05) gelir, sırası sunucunundur (en eski önce, eşitse hesap numarası).

```text
HomeView ── AccountsSection ── .task (yalnız liste hiç gelmediyse) ──► AccountsViewModel.load() ──► APIClient ──► GET /api/accounts
   │            └─ state: loading → ProgressView | empty → "Henüz hesabınız yok" | failed → "Tekrar dene" | loaded → AccountRow…
   └─ .refreshable (aşağı çek) ──► AccountsViewModel.load()
```

- `Accounts/AccountsViewModel`: dört durum (`loading`, `loaded`, `empty`, `failed`) + `refreshError`. 401 ve iptal hiçbir şey göstermez
  (401 oturumu `APIClient`'ta zaten kapatır).
- `Accounts/AccountRow`: tek hesap kartı (simge, ad, numara, bakiye, işletme hesabında "İşletme" etiketi).
- `Formatting/Money`: tutar yazımı AI servisindeki `formatting.money` ile aynı (`577.490,18 TL`, `500,00 USD`). Yalnız gösterim;
  mobilde hesap yapılmaz (IOS-10).
- `Account` modeli `createdAt`'i çözmez: ekranda yok, sıra zaten sunucudan geliyor, mikro saniyeli değeri eski sistemlerin `.iso8601`'i
  reddedebilir. `balance` `Decimal`'e kayıpsız çözülür; bilinmeyen `accountType` sözleşme hatasıdır ("Beklenmeyen bir cevap alındı").
- Vurgu rengi (`AccentColor`): açık modda koyu lacivert, koyu modda açık mavi. Kartlar `secondarySystemGroupedBackground` üstünde
  (düz `.background` koyu modda ekranla aynı siyah).

Kullanıcı kararları:

- **K1-D — ana ekranın gövdesi, ayrı parçalar:** liste kendi ViewModel'i ve görünümüyle; IOS-07 son işlemleri yanına ekler.
- **K2-A — tam hesap numarası** (sentetik ve müşterinin kendi numarası; maskeleme başkasının adı içindir).
- **K3-A — sohbetle aynı tutar yazımı** (TRY → "TL", diğerleri kodla).
- **K4-A — düz liste, sunucu sırası**; işletme hesabı etiketle. Para birimi başına toplam yok.
- **K5-A — aşağı çekip yenileme + "Tekrar dene"**; sohbetten dönünce yeniden istenmez (sohbet bakiyeyi değiştiremez).
- **K6-A — yenileme hatası listeyi silmez:** eski liste kalır, üstte turuncu bant; başarılı yenileme bandı kaldırır. Tam ekran hata
  yalnız liste hiç gelmediyse.
- **K7-B — küçük cila:** vurgu rengi, kart görünümü, kalın ve eşit genişlikli rakamlar.
- **K8-A — hesaba dokunulmaz** (hesap detayı ve işlem listesi IOS-05).

### Test ve doğrulama (IOS-04)

85 test durumu (önceki 66 + `AccountsViewModelTests` 12: Ada'nın gerçek cevabı sırası ve bakiyeleri `Decimal` olarak birebir, istek ve
token, işletme hesabı, bilinmeyen tür, boş liste, sunucu hatası, ulaşılamayan sunucu, 401, iptal, yenileme hatasında liste kalır, başarılı
yenileme bandı kaldırır, hatadan sonra "Tekrar dene"; `MoneyTests` 7 elle hesaplanmış tutar). 7 kasıtlı bozma yakalandı (sıra ters → 3 test, 401 hata gösterir → 1, iptal hata gösterir → 1, yenileme listeyi siler → 1, token gönderilmez → 2, İngilizce sayı biçimi → 7 durum, TRY "TL" olmaz → 4 durum).
Simülatörde beklenen: Ada → Ada boş TRY 0,00 TL, Ada günlük TRY 577.490,18 TL, Ada USD 500,00 USD; Deniz → Deniz aile TRY 522.900,00 TL,
Kahve Durağı – Kadıköy 2.595,00 TL "İşletme"; Ege → Ege seyahat EUR 1.000,00 EUR, Ege günlük TRY 273.380,00 TL.

### Sınırlar (IOS-04)

- Hesaba dokunulamaz (IOS-05); para birimi başına toplam yok.
- Bakiye yalnız ilk açılışta ve aşağı çekince güncellenir; sohbetten dönünce istenmez.
- Kesirli saniye: `createdAt` gerekirse (ör. IOS-05) tarih çözücüsü kesirli saniyeyi de kabul edecek biçimde genişletilmeli.

## iOS-e: son işlemler ve tüm işlemler (IOS-07; 22 Ekim 2026)

Hesap kartlarının altında "Son işlemler": bütün hesapların en yeni 5 işlemi; daha fazlası varsa "Tüm işlemleri gör" → "Tüm işlemler"
ekranı, sona yaklaştıkça 20'şer işlem daha yükler. Backend değişmedi; ikisi de `GET /api/transactions` (ANA-09) kullanır: sıra, maskeli
karşı taraf ve müşterinin yerel günü (`localDate`) backend'den gelir.

```text
HomeView (.refreshable → hesaplar + son işlemler paralel)
 ├─ AccountsSection
 └─ RecentTransactionsSection ── .task ──► RecentTransactionsViewModel.load() ──► GET /api/transactions?limit=5
        └─ hasMore ise "Tüm işlemleri gör" ──NavigationLink(value:)──► TransactionHistoryView
                                                    └─ son satırdaki gösterge görününce ──► TransactionHistoryViewModel.loadMore()
                                                                                             ──► GET /api/transactions?period=AllTime&limit=20&cursor=… (API-17)
TransactionRow ◄── TransactionDisplay (saf kurallar: başlık, "etiket · gün", işaretli tutar, simge)
```

- `APIRequest.query`: sorgu parametreleri ayrı taşınır (`appending(path:)` yola yazılmış "?"i kaçışlı yazardı); test kaydı `query`'yi de tutar.
- `TransactionItem` `occurredAtUtc`'yi çözmez (gün `localDate`'ten; saat IOS-06). Bilinmeyen `kind`/`direction` sözleşme hatasıdır.
- `TransactionDisplay`: başlık harcamada işyeri ya da ham açıklama, transferde maskeli karşı taraf (kendi hesabına transferde hesap adı),
  maaşta "Maaş", satışta alıcı ya da "Satış", açılışta "Açılış bakiyesi"; etiket kategori adı / "Sınıflandırılmamış" ("Diğer" değil) /
  "Gelen transfer" / "Giden transfer" / "Gelir" / "Satış geliri" / "Açılış". Tutar `direction`'dan "+" ya da "−" (U+2212) + `Money`.
  Gün elle yazılır ("2026-10-08" → "8 Ekim 2026"): takvim/saat dilimi kullanılmaz, okunamayan değer olduğu gibi gösterilir.
- `TransactionHistoryViewModel`: (API-17'den beri, iOS-f) bütün geçmiş, sonraki sayfa önceki sayfanın `nextCursor`'ıyla; aynı anda ikinci
  sayfa istenmez; sayfa hatasında liste kalır ve aynı sayfa yeniden istenir; aşağı çekince ve ekran her
  açıldığında en yeni sayfadan başlar (eski liste yeni sayfa gelene kadar durur). Başlıkta API'nin çözdüğü tarih aralığı.

Kullanıcı kararları: K1-A (tek arama isteği), K2-B (5 işlem + "Tüm işlemleri gör"), K3-A (işyeri, yoksa ham açıklama), K4-A (giden "−"
normal renk, gelen "+" yeşil), K5-A (yalnız gün, `localDate`), K6-A (kategori/tür simgesi), K7-B (tüm işlemler ekranı, kaydırdıkça 20'şer),
K8-A (işletme hesabı dahil), K9-A (ikinci satır "etiket · gün", hesap adı yok), K10-C (ViewModel kalıbı şimdilik kopya, IOS-05'te
ortaklaşır), **K11-D (plan sırasında çıktı):** API dönem verilmezse son 3 ayı arıyor, tarih aralığı en fazla 24 ay ve bitişi iş gününden
sonra olamıyor (telefon iş gününü bilmez: demo saati 10 Ekim, telefon 22 Ekim) → "Tüm işlemler" bugün son 3 ay; bütün geçmiş API-17'de
(imleçli sayfalama + dönemsiz geçmiş).

### Test ve doğrulama (IOS-07)

125 test durumu (+40: `RecentTransactionsViewModelTests` 9, `TransactionHistoryViewModelTests` 12, `TransactionDisplayTests` 19): Ada'nın 5 işlemlik cevabı (sıra, `Decimal`, tür/yön), `limit=5` ve dönemsiz istek, `hasMore`, bilinmeyen
tür/yön, boş, hata, 401, yenileme hatası; tüm işlemler: ilk sayfa + tarih aralığı, `offset` 20/40, son sayfadan sonra istek yok, sayfa
kenarında tekrar, sayfa hatası + aynı sayfa yeniden, ilk sayfa hatası, boş, aşağı çekme, yenileme hatası, aynı anda tek istek, 401, iptal;
satır kuralları her tür için, 6 simge, 6 tarih. 11 kasıtlı bozma yakalandı (sıra ters → 1, `hasMore` hep açık → 1, `limit` gitmez → 5, `offset` gitmez → 2, tekrar ayıklanmaz → 1, yenileme listeyi siler → 1, eşzamanlılık kontrolü yok → 1, işaret ters → 6, ham açıklama işyerinin önünde → 1, kategorisiz "Diğer" → 1, ay bir kayık → 9).
Bulgu: boş listede `shown[0]` okuyan bir test kırmızı yerine bütün test sürecini çökertiyordu (92 test birden "crashed"); `#require` ile
düzeltildi.

Simülatörde beklenen (veritabanından; karşı taraf adları docs/api.md maskeleme örneğinden):

- Ada: POS 6604 ONLINE ODEME 4471 · Sınıflandırılmamış · 9 Ekim 2026 · −145,00 TL; Ege Y*** · Gelen transfer · 8 Ekim 2026 · +300,00 TL;
  Starbucks · Yeme & İçme · 8 Ekim 2026 · −165,00 TL; Kahve Durağı · 7 Ekim 2026 · −95,00 TL; Starbucks · 6 Ekim 2026 · −210,00 TL.
- Deniz: Ada K*** · Satış geliri · 7 Ekim 2026 · +95,00 TL; Elektrik Faturası · Faturalar · −1.850,00 TL; Maaş · Gelir · 5 Ekim 2026 ·
  +55.000,00 TL; Aile Marketi · Market · 4 Ekim 2026 · −9.500,00 TL; Ege Y*** · Satış geliri · 4 Ekim 2026 · +140,00 TL.

### Sınırlar (IOS-07)

- ~~"Tüm işlemler" yalnız son 3 ay; offset kayması~~ → API-17 ile kapandı (iOS-f).
- Saat yok (IOS-06), işleme dokunulmaz (IOS-06), hesap adı satırda yok.
- ViewModel kalıbı iki kopya (K10-C); IOS-05'te ortaklaşır.

## iOS-f: bütün geçmiş ve imleçli sayfalama (API-17; 23 Ekim 2026)

"Tüm işlemler" artık müşterinin bütün geçmişini gösterir (önce son 3 ay). Backend'de `period=AllTime` ve imleç (docs/api.md);
telefonda tarih mantığı yok: dönemi ve "bugün"ü backend çözer, başlıkta cevabın aralığı yazılır ("1 Nisan 2025 – 10 Ekim 2026").

- `APIRequest.history(limit:cursor:)`: `GET /api/transactions?period=AllTime&limit=20[&cursor=…]`. Son işlemler kartı
  (`APIRequest.transactions(limit:)`, 5 işlem) dönemsiz kaldı (API'nin varsayılanı, son 3 ay).
- `TransactionSearchResult.nextCursor`: son sayfada nil. `TransactionHistoryViewModel` imleci saklar; `hasMore` = imleç var mı. Yenileme
  imleçsiz başa döner, sayfa hatasında aynı imleç yeniden istenir.
- Kimliğe göre tekrar ayıklaması kalktı: imleçle sayfa kenarında tekrar sunucuda imkânsız (karar K6-A).
- Metinler: boş liste "Henüz işlem yok", son sayfa "Bütün işlemler gösterildi.".

Kararlar (API-17): K1-B (imleç offset'in yerini aldı, iki liste endpoint'inde), K2-A (açık ad `AllTime`, varsayılan son 3 ay kaldı),
K3-A (dönem en eski işlemin günü – iş günü), K4-A (opak imleç), K5-A (`totalCount` + `hasMore` kaldı, `nextCursor` eklendi), K6-A (tek PR).

Test: 125 test durumu (sayı değişmedi): istek sırası `period=AllTime&limit=20` → `…&cursor=c20` → `…&cursor=c40`, kısa sayfanın kendi
imleciyle devam (`theNextPageFollowsTheCursorOfTheLastPage`, tekrar ayıklama testinin yerine), hata sonrası aynı imleç, yenilemede
imleçsiz istek, başlıkta bütün geçmişin aralığı. Kasıtlı bozma: sonraki sayfa imleçsiz istenir → 3 test kırmızı.

Sınır: son işlemler kartı son 3 aya bakar (o aralıkta işlem yoksa kart boş görünür).


## iOS-g: hesap detayı ve tarih filtresi (IOS-05; 23 Ekim 2026)

Kararlar: **K1-A, K2-C, K3-A, K4-B, K5-B, K6-B, K7-A, K8-B, K9-B, K10-B**.

- Hesap kartı → `AccountDetailView`: üstte hesap adı/numarası, kişisel/işletme türü ve **güncel bakiye**; altta o hesabın hareketleri.
  Kartın verisi hemen görünür, `GET /api/accounts/{id}` ayrıca yeniler. Bakiye seçilen dönemden hesaplanmaz; iki okuma tek atomik
  snapshot değildir. Hesap bilgisi yenilenemezse son bilgi korunur ve tekrar deneme gösterilir.
- `TransactionHistoryViewModel(api:accountID:)` aynı imleçli listeyi hesap veya bütün müşteri kapsamında çalıştırır.
  `GET /api/accounts/{id}/transactions?period=AllTime&limit=20`, sonra aynı filtre + önceki `nextCursor`.
  İlk seçim **Tüm geçmiş**; liste en yeni önce, backend sırasındadır. `totalCount` API'den gösterilir.
- **Dönem paneli:** Tüm geçmiş / Bu ay / Geçen ay / Son 3 ay / Son 6 ay / Özel tarih aralığı. Taslak seçim ayrıdır;
  `Uygula` ilk sayfayı getirir, başarıda satırlar/dönem/imleç birlikte değişir ve panel kapanır. Hata veya Vazgeç eski sonucu korur.
  Başarısız filtre hatası panelde, başarısız sayfa/yenileme hatası listede gösterilir. Filtre yalnız hesap detayındadır;
  mevcut "Tüm işlemler" ekranına filtre eklenmedi.
- Hazır dönemleri **backend iş saatiyle** çözer. Özel aralık günleri sabit Gregorian/UTC takvim taşıyıcısıyla `YYYY-MM-DD`
  yazılır (saat anı olarak dönüştürülmez); iki uç gün dahildir. Başlangıç > bitiş ve >24 ay yerelde de reddedilir.
  Gelecek tarih kararı backend'indir; telefon bugünü demo günü kabul edilmez. `ProblemBody.errors` isteğe bağlı çözülür,
  `APIError.validation` alan mesajını gösterir. Backend sözleşmesi/migration değişmedi.
- Filtre değişince eski istek iptal edilir, sorgu nesliyle geç gelen sonuç düşürülür; eski imleç yeni filtreye taşınmaz.
  `retry()` başarısız işlemin imlecini saklar: yenileme hatasında ilk sayfayı, sayfa hatasında aynı sonraki sayfayı tekrar ister.
- Aşağı çekme hesap başlığını ve seçili dönemin ilk sayfasını yeniler. Aynı detayın `open()` çağrısı tekrarlanırsa filtre/liste
  korunur; hesaba yeni giriş yeni model ve AllTime oluşturur. Hesap veya hareket isteğinde 404 bütün detayı erişilemez yapar;
  401 mevcut oturum mekanizmasına gider. Boş hesap ve boş filtre sonucu farklı metin alır.
- `Shared/ListLoader` + `ListState`: Accounts/RecentTransactions'ın dört durum ve yenileme davranışı ortaklaştırıldı;
  ekranların public model yüzeyi ve mevcut testleri korundu. Sayfalı liste bundan ayrıdır. `TransactionRows` iki ekranda aynı
  satır/yükleme/hata/sayfalama görünümünü sağlar. View'da ağ kodu ve mobil finansal hesap yoktur.

```text
AccountsSection → AccountDetailView (@State model)
                   ├─ AccountDetailViewModel → APIClient.account(id)
                   └─ TransactionHistoryViewModel(accountID)
                        ├─ TransactionRows
                        └─ TransactionFilterSheet → TransactionFilter → APIClient.history(...)
AccountsViewModel / RecentTransactionsViewModel → ListLoader
```

Doğrulama: 151 Swift Testing test durumu (önceki 125 + yeni 26). Önce eksik hesap detayı/filtre API'leri nedeniyle derleme
kırmızı görüldü; uygulama sonrası bütün paket yeşil. Yeni testler hesap kapsamı/token, karttan güncelleme, bağımsız başlık hatası,
404/401, filtre/ilk sayfa/imleç, başarısız filtrede eski sonuç, özel günler, API tarih hatası, geç gelen istek, iptal, yenileme
tekrarı, filtreli boş sonuç, yeni detay ömrü ve 24 ay/artık gün sınırlarını kapsar.

Görsel kontrol: geçici simülatör test düzeneğinde sahte ağla hesap detayı, özel tarih paneli ve büyük erişilebilirlik yazı boyutunda koyu görünüm render edilip incelendi. Geçici düzenek kaldırıldı.

**Düzeltme (23 Ekim 2026, dal `fix/ios-custom-period`):** özel aralık `period=Custom&from&to` gönderiyordu, API dönem adı ile tarihleri birlikte
reddeder (400); sahte ağ testi bu yanlış sorguyu bekleyerek hatayı gizlemişti. Artık yalnız `from` ve `to` gider (`TransactionFilter.query`);
API'de `period=Custom` (tarihli/tarihsiz) 400'ü testle sabitlendi. Gerçek API'ye karşı simülatörde (Murat) özel aralık hatasız uygulandı.
Ders: istek biçimini yalnız sahte ağla doğrulamak yetmez, her yeni istek biçimi bir kez gerçek API'ye karşı denenir.

Sınırlar: işlem satırı henüz açılmaz (IOS-06), detayda işlem türü/merchant/tutar filtresi yok, filtre tüm müşteri listesinde yok.
Gerçek API kapalıyken doğrulama sahte ağla yapılmıştır; demo verisiyle canlı uçtan uca kontrol ayrıca yapılmalıdır.

## iOS-h: cevaptan işlemlere bağlantı (CHAT-07 PR2; 23 Ekim 2026)

Kararlar: **K1-A … K8-A**. Backend tarafı PR1'de (docs/api.md: `links`, `accountType`).

- `AssistantReply.links` zorunlu bir listedir (eksikse sözleşme uyuşmazlığı, CHAT-06'daki `suggestions` gibi). Tek bir bağlantı okunamazsa
  (bilinmeyen dönem/tür, `Custom` için eksik gün, isimli dönemde gün) **o bağlantı düşer, cevap kalır** (K5-A); `AssistantLink` kendi
  `TransactionFilter`'ını okunurken bir kez çözer.
- Düğme cevabın **kendi balonunun altında kalıcıdır** (K4-A; öneriler gibi silinmez: liste o cevabın rakamının kanıtıdır).
- `TransactionFilter` yalnız bağlantıların kullandığı 7 isimli dönem aldı (Bugün, Dün, Bu hafta, Geçen hafta, Son 7/30 gün, Bu yıl); filtre
  panelinin `presets` listesi aynı. `TransactionCriteria` (tür, hesap türü, kategori, işyeri) sorguya dönemden sonra sabit sırayla eklenir.
  Özel aralık yalnız `from`/`to` gider.
- `LinkedTransactionsView`: üstte tek satır "Bu ay · Harcamalar · Kişisel hesaplar" (özel aralıkta "Özel tarih aralığı"), altında backend'in çözdüğü
  günler ve "N işlem"; satırlar mevcut `TransactionRows`. Toplam yok, telefon hesap yapmaz.
- **Navigasyon tuzağı:** düğme `NavigationLink { LinkedTransactionsView … }` (hedefli). Aynı adsız `NavigationStack`'te sohbet hedefli linkle
  açıldıktan sonra **değer-tabanlı** link itildiğinde yığın kendini geri sarıyordu (liste açılıp hemen kapanıyor, istek yine de gidiyordu).
  Birim testleri navigasyonu görmez; sorun yalnız gerçek zincirde simülatörde bulundu.

```text
ChatView ─ MessageBubble ─ NavigationLink ─► LinkedTransactionsView(@State TransactionHistoryViewModel(filter, criteria))
ChatViewModel.transactions(for: link) ─► TransactionHistoryViewModel ─► APIRequest.history(filter, criteria) ─► GET /api/transactions
```

Doğrulama: 185 Swift Testing test durumu (önceki 151). Eski chat testlerinin sahte cevaplarına `links` eklendi. Dört kasıtlı bozma yakalandı (işyeri
sorgudan düşer; bağlantı yeni cevapla silinir; tek bozuk bağlantı cevabı düşürür; ölçütler dönemden önce yazılır). Gerçek zincirde (Murat, demo anı,
yeni API): "Bu ay ne kadar harcadım?" → 8.241,40 TL / 4 işlem, liste 2.658,50 + 1.842,50 + 449,90 + 3.290,50 = 8.241,40; "1 Eylül ile 5 Eylül
arası ne kadar harcadım" → 5.116,50 TL / 2 işlem, liste 3.476,00 + 1.640,50 = 5.116,50. Geri soru cevabında düğme çıkmadı.

Sınırlar: kategori adı başlıkta yok (bütçe + kategori bağlantısı kategoriyi söylemeden süzer), liste toplamı yok, karşılaştırma/aylık seri/düzenli ve
yaklaşan ödeme/tasarruf cevaplarında düğme yok (PR1 K3-B), satıra dokununca detay açılmaz (IOS-06).

## iOS-i: kategori grafiği (IOS-08 PR1; 23 Ekim 2026)

Ana ekranda "Harcama analizi" kartı, dokununca `AnalyticsView`; ilk parça "Kategorilere göre" halka grafiğidir. Backend ve migration değişmedi:
`GET /api/analytics/category-breakdown?period=…` hazırdı. Telefon çizer, hesap yapmaz: toplam, tutar ve yüzde backend'indir. Aylık grafik IOS-08
PR2'dir (aynı ekrana eklenir).

Kararlar: K1-B (ayrı ekran, ana ekranda tek kart), K2-A (halka + altında liste), K3-A (aylık çubuk, PR2), K4-B (dört adlı dönem), K5-A (para birimi
başına ayrı halka), K6-A (Sınıflandırılmamış ayrı gri dilim), K7-A (aylık: bütün harcama, 6 ay; PR2), K8-A (dokununca özet satırı), K9-B (kategori
koduna sabit renk), K10-B (iki PR).

```text
HomeView ─ "Harcama analizi" ─► AnalyticsView ─ CategoryBreakdownSection(@Bindable CategoryBreakdownViewModel)
CategoryBreakdownViewModel ─► APIRequest.categoryBreakdown(period:) ─► GET /api/analytics/category-breakdown?period=ThisMonth
AnalyticsDisplay (saf kurallar: yüzde yazımı, renk, özet cümlesi, halkadaki noktadan dilim)
```

- `Analytics/CategoryBreakdownViewModel`: `loading`/`loaded`/`empty`/`failed`, seçili dönem, `refreshError`, dilim seçimi (para birimi başına).
  **Dönem seçimi** `AnalyticsPeriod` (Bu ay / Geçen ay / Son 3 ay / Bu yıl; API adları): telefon tarih hesaplamaz. Yenileme hata verirse eski halka
  silinmez, dönem seçimi halkanın gösterdiği döneme geri döner ve üstte bant çıkar. Her istek bir sıra numarası taşır: **geç gelen eski cevap
  yeni seçimin üstüne yazılmaz** (`generation`). 401 ve iptal ekranda hata göstermez. Dönem değişince seçim temizlenir.
- `Analytics/AnalyticsDisplay`: `percentText` (`%80,27`), `summary` ("Yeme & İçme, 590,00 TL, %80,27"), `accessibilityLabel` (yüzde ve işlem sayısı
  ile), `tone(forCategory:)` (11 bilinen kod için ayrı renk, `nil` her zaman gri, bilinmeyen kod sarı; renk tek başına bilgi taşımaz, liste adı yazar),
  `slice(atAmount:in:)` (halkada dokunulan noktanın dilimi; kenar, biten dilime aittir).
- `CategoryBreakdownSection`: `Chart` + `SectorMark` (iç yarıçap oranı 0,62), `chartAngleSelection` halkadaki dokunuşu dilime çevirir, altındaki
  liste satırı da dilimi seçer/bırakır; seçilmeyen dilimler soluk. Kart arka planı `secondarySystemGroupedBackground` (koyu modda ekrandan ayrışır).
  Para birimleri **asla toplanmaz**: her biri kendi başlığı, toplamı ve halkasıyla.
- Kategorisiz grup `categoryCode: nil` → `id` "UNCATEGORIZED"; `OTHER` ("Diğer") ile birleşmez.

Doğrulama: 212 Swift Testing test durumu (önceki 185; +27: `CategoryBreakdownViewModelTests` 17, `AnalyticsDisplayTests` 10). Dokuz kasıtlı bozmanın
hepsi yakalandı (dönem gitmez → 17 test, Sınıflandırılmamış gri değil, kenar sonraki dilime gider, dönem geri dönmez, geç cevap kabul edilir,
dönem değişince seçim kalır, dokunuş seçimi bırakmaz, yüzde tek hane, yenileme hatası halkayı siler).

Canlı kontrol (23 Ekim 2026, simülatör + gerçek API, Ada): kart, dört dönem, dilim seçimi ve koyu mod elle doğrulandı. Kart ilk sürümde "Son işlemler"in altındaydı ve görünmedi; selamlamanın hemen altına taşındı.

Sınırlar: özel tarih aralığı yok (yalnız dört adlı dönem); dilime dokununca işlem listesi açılmaz (bağlantı için kategori filtresi doğrulanmalı);
halka yalnız açılışta ve aşağı çekince yenilenir; aylık grafik PR2'dedir.

## iOS-j: aylık çubuk grafik (IOS-08 PR2; 23 Ekim 2026)

Analiz ekranında kategori halkasının altına "Aylık harcama" eklendi: `GET /api/analytics/monthly-trend?months=6`, son 6 tam ay + devam eden ay =
7 çubuk, para birimi başına ayrı grafik. Backend ve migration değişmedi; telefon toplam hesaplamaz.

Kararlar (PR1'den K3-A, K5-A, K7-A geçerli): K11-A (değer ekseni yok, tutar dokununca tam yazılır, yalnız ızgara çizgisi), K12-A (devam eden ay %45
soluk + etiketinin altında nokta, seçilince "devam ediyor" yazar), K13-A (harcamasız ay sıfır çubuk olarak kalır), K14-A (kategori halkasının altında).

```text
AnalyticsView ─ CategoryBreakdownSection                 (PR1)
              └ MonthlyTrendSection(MonthlyTrendViewModel) ─► APIRequest.monthlyTrend(months: 6) ─► GET /api/analytics/monthly-trend
AnalyticsDisplay: shortMonth, monthLabel, monthSummary, monthAccessibilityLabel
```

- `MonthlyTrendViewModel`: `loading`/`loaded`/`empty`/`failed`, `refreshError`, çubuk seçimi (para birimi başına). Geç gelen eski cevap yok sayılır
  (`generation`); yenileme hatası eski grafiği silmez; yeni cevapta artık olmayan ayın seçimi düşer; 401 ve iptal hata göstermez.
- `MonthlySpending.id` "2026-10" (yıl-ay): grafiğin x değeri ve seçim anahtarı; etiket yalnız ay adıdır ("Eki"), yıl seçilince özet satırında çıkar.
- `MonthlyTrendSection`: `BarMark` + `chartXSelection`; `chartYAxis` yalnız `AxisGridLine`; `accessibilityLabel` ay, tutar ve işlem sayısını söyler.
- Cevaptaki `months` pencere listesi ve `merchantMatch` okunmaz; çizim `currencies[].months`'tan yapılır.

Doğrulama: 232 Swift Testing test durumu (önceki 212; +20: `MonthlyTrendViewModelTests` 16, `AnalyticsDisplayTests` +4). Dokuz kasıtlı bozmanın hepsi
yakalandı (`months` gitmez, kısmi ay tam sayılır, Aralık ertesi yıl yazılır, harcamasız ay düşer, geç cevap kabul edilir, dokunuş seçimi bırakmaz,
kaybolan ayın seçimi kalır, yenileme hatası grafiği siler, ay adları kayar). Görünüm (soluk renk, nokta, koyu mod) testle değil elle doğrulandı: simülatör + gerçek API, Deniz, demo anı (23 Ekim 2026).

Sınırlar: kategoriye göre süzme yok, 12 ay yok; çubuğa dokununca o ayın işlem listesi açılmaz; kategori halkası seçilen aydan etkilenmez; grafik yalnız
açılışta ve aşağı çekince yenilenir; eksende tutar etiketi yok.


## iOS-k: tasarım sistemi ve siyah tema (UI-01; 26 Ekim 2026)

Uygulama artık her zaman siyah ve tek vurgu rengi şampanya altını. Yalnız görünüm değişti: ViewModel, ağ ve rakam mantığı aynı, ekran düzenleri
eski (UI-02'den itibaren yeniden tasarlanır). Backend değişmedi.

Kararlar: K1-A her zaman siyah (açık tema yok), K2-A şampanya altını `#C9A96E` az kullanılır, K3-A…K6-A (UI-02+ için geçerli), K7-B zemin `#0A0A0B`
(saf siyah değil: kartlar zeminden bir basamak ayrılsın), K8-A altının üstünde beyaz yazı kalan iki yer (ana ekran düğmesi, kullanıcı balonu) bu adımda düzeltildi,
K9-A mantık içeren iki kural test edildi.

```text
DesignSystem/
  Palette.swift     Swatch (hex + WCAG kontrast) ve Palette: background, surface, raised, hairline, primary/secondaryText, accent, onAccent, incoming, failure
  Typography.swift  yazı ölçeği + label(_:) (Türkçe büyük harf)
  Layout.swift      boşluk ve köşe değerleri
  Components.swift  cardSurface, SectionTitle, PrimaryButtonStyle, SecondaryButtonStyle, ChipButtonStyle, StatusBand
```

- Ekranlar renk yazmaz, `Palette`'ten alır. `BankingAIApp` kökte `.preferredColorScheme(.dark)` ve `.tint(altın)` verir; `Info.plist` `UIUserInterfaceStyle = Dark`
  (açılış anı da siyah), `AccentColor` iki görünümde altın.
- Bu adımda bağlananlar: ana ekran/analiz/sohbet zemini `Palette.background`, kartlar `Palette.surface`, "Asistana sor" `PrimaryButtonStyle`, kullanıcı balonu `Palette.raised`.
  Turuncu bantlar ve kategori renkleri aynı kaldı (UI-05/UI-06).
- Kontrast (hesaplanmış): ikincil metin/yüzey 5,64; altın/zemin 8,84; siyah yazı/altın düğme 8,84; birincil/yükseltilmiş yüzey 14,54; gelen para/yüzey 9,11; hata/yüzey 6,63; hepsi ≥ 4,5.

Doğrulama: 249 test durumu geçti (main'de 231, +18 yeni `DesignSystemTests`: formül 21,0 ve 4,54, sıra bağımsızlığı, 11 metin/yüzey çifti ≥ 4,5, zemin saf siyah değil, Türkçe büyük harf, katalogdaki vurgu rengi `Palette.accent` ile aynı (açık ve koyu)).
Dört kasıtlı bozma yakalandı (dördüncüsü: katalogdaki rengi tek başına değiştirmek): ikincil metin `#5A5A5E` (kontrast testi), `uppercased()` Türkçesiz ("SON IŞLEMLER"), zemin saf siyah. Simülatörde (Murat, gerçek API) ana ekran ve sohbet önce/sonra görüldü.
Sınırlar: açık tema yok; alt çubuk hâlâ sistem materyali (hafif gri bant, UI-03); giriş ekranı `Form` olarak kaldı (UI-04); `StatusBand` ve diğer ortak parçalar henüz ekranlarda kullanılmıyor.


## iOS-l: sohbet ekranı (UI-02; 26 Ekim 2026)

Sohbet ekranı siyah premium dile geçti. Yalnız görünüm değişti: Durdur, Tekrar dene, bağlantılar, öneriler ve kaydırma aynı; backend değişmedi.

Kararlar: K1-B cevaptaki rakamlar kalın; K2-B asistan işareti ince altın halkada "A" (`AssistantMark`, klişe yıldız yok); K3-A (14b) asistan cevabı balonsuz, kullanıcı
mesajı koyu kapsül; K3-D öneriler yatay kayan çipler, boş sohbette başlangıç soruları tam genişlik satır; K4-A "Asistan yazıyor…" yerine üç nokta; K5-C yüzen giriş kapsülü,
sayaç yalnız 450 karakterden sonra; K6-C boş sohbette işaret + başlık + başlangıç soruları; K8-A `StatusBand` isteğe bağlı eylem (altın).

```text
Chat/
  ChatView.swift         düzen: boş sohbet, öneri çipleri, yüzen giriş kapsülü, sayaç
  MessageBubble.swift    kullanıcı kapsülü / balonsuz cevap, hata ve "Tekrar dene"
  AnswerHighlight.swift  cevaptaki rakamları kalın yazar (sayı okunmaz, yalnız biçim)
  TypingDots.swift       sırayla yanan üç nokta; "Hareketi azalt"ta duruk
DesignSystem/Components.swift  StatusBand eylemi, AssistantMark
```

- Rakam vurgusu yalnız görünüm: metin sayı olarak okunmaz; daha uzun bir sayının ya da kelimenin parçası kalın yapılmaz; asistanın zaten kalın yazdığı rakam tek kalın kalır; kullanıcının metni olduğu gibi gösterilir.
- Sayaç API'nin saydığı gibi sayar (Unicode karakter), sınır kontrolüyle aynı sayı.

Doğrulama: 261 test durumu geçti (main'de 249, +12 `AnswerHighlightTests`).
Sınırlar: ana ekran UI-03, giriş UI-04, kalan ekranlar UI-05.

## iOS-m: ana ekran (UI-03; 26 Ekim 2026)

Ana ekran siyah premium dile geçti. Yalnız görünüm ve bir yeni okuma: ViewModel, ağ ve rakam mantığı aynı, backend değişmedi.

Kararlar: K1-A selam + backend günü + profil dairesi; K2-B kartta "•••• 001"; K3-A alt alta tam genişlik hesap kartları; K4-A "Bu ay harcama" kartı; K5-A yüzen "Asistana sor" kapsülü;
K6-A `TransactionRow` her yerde Palette'te; K7-B sıra: üst satır → harcama kartı → hesaplar → son işlemler; K8-A yalnız renkler (iskeletler UI-06).

```text
Home/
  HomeView.swift            düzen: üst satır, kart, bölümler, yüzen düğme (navigasyon çubuğu gizli)
  HomeHeader.swift          selam, gün satırı, baş harfli profil menüsü (Çıkış)
  HomeDisplay.swift         initials (Türkçe büyük harf), dayLine (okunamazsa nil)
Analytics/
  SpendingSummaryViewModel.swift  GET /api/analytics/spending-summary?period=ThisMonth; ListLoader + endDate
  SpendingSummaryCard.swift       kart: para birimi başına büyük tutar, alışveriş sayısı, "Harcama analizi"
Accounts/
  AccountRow.swift, AccountDisplay.swift  kart; shortNumber, spokenSummary (VoiceOver tek cümle)
```

- Gün satırı telefon takviminden değil, `spending-summary` cevabındaki `period.endDate`'ten gelir (demo saati telefon tarihinden farklı olabilir); cevap gelene kadar satır boş yer tutar, okunamayan gün hiç gösterilmez.
- Para birimleri toplanmaz: iki para biriminde iki satır. Harcama yoksa "Bu ay henüz harcama yok"; ilk yükleme hatasında mesaj + "Tekrar dene"; yenileme hatasında eski rakam kalır, üstte bant çıkar; 401 ve iptal sessiz.
- Aşağı çekip yenileme hesapları, son işlemleri ve özeti birlikte yeniler. Çıkış sağ üstteki profil menüsündedir (UI-07'de Profil ekranına taşınır).

Doğrulama: 280 test durumu geçti (+19 `HomeRedesignTests`: istek 1, ViewModel 6, numara kısaltma 5, baş harfler 4, gün satırı 2, VoiceOver cümlesi 1). Altı kasıtlı bozma yakalandı: dönem adı küçük harf, boş liste `loaded`, yenileme hatasında gün silinir, ay kayar, numara son 4 karakter, Türkçesiz `uppercased()` ("II").
Sınırlar: kart yalnız "bu ay"ı gösterir; iskeletler UI-06; sekme çubuğu ve Profil UI-07; hesap detayı, tüm işlemler ve işlem detayı ekranlarının düzeni UI-05 (yalnız satır rengi şimdi değişti).

## iOS-n: giriş ekranı ve kimlik (UI-04; 26 Ekim 2026)

Giriş ekranı, uygulama simgesi, açılış rengi ve adlar siyah premium dile geçti. `LoginViewModel`, ağ ve hata metinleri aynı; backend'de yalnız üç sohbet hata metni değişti (K9-B).

Kararlar: K1 uygulama adı **Compass**; K10-C simge kullanıcının verdiği görselden kırpıldı (temiz sürüm sonra gelir); K11-B vurgu rengi simgenin mavisi `#3D8BFF` (UI-01 K2-A'nın altını kalktı; yeşil yalnız gelen parada); K12-A simge betiği silindi; K2 asistan adı **Kılavuz** (monogram "K"); K3-A ortalı düzen; K4-B koyu dolu alan, etiket üstte, odakta altın çizgi; K5 a+b+d (İleri/Git akışı, ikonlu hata satırı, "Giriş yapılıyor…"); K6-A/K7-A (betikle çizilen halkalı "C") K10–K12 ile yerini görsele bıraktı; K8-A açılış ekranı yalnız renk; K9-B backend hata metinleri de "Kılavuz".

```text
DesignSystem/
  Brand.swift                    appName, tagline, assistantName, assistantInitial, askAssistant ("Kılavuz'a sor")
  Components.swift               + BrandMark, inputField(focused:)
Login/LoginView.swift            ScrollView, iki alan (@FocusState), hata satırı, altın düğme
Assets.xcassets/
  LaunchBackground.colorset      #0A0A0B (Palette.background ile aynı; testle bağlı)
  AppIcon.appiconset/icon.png    tek yuva (1024², alfasız); koyu ve renklendirilmiş türleri iOS türetir
Info.plist                       CFBundleDisplayName = Compass, UILaunchScreen.UIColorName
```

- Simge: kaynak görsel (kullanıcının, repoda değil) çerçevesi, gölgesi ve gri zemini dışarıda kalacak biçimde iç kareden kırpılıp 1024²'ye küçültüldü (`sips -c 1215 1215 --cropOffset 400 420`, sonra `-z 1024 1024 -s format png`). iOS köşeyi kendisi yuvarlar. Daha az ayrıntılı, kenarlara kadar dolu bir sürüm gelince yalnız `icon.png` değişir.
- Vurgu rengi iki yerde durur (`Palette.accent` ve `AccentColor` asset'i); `DesignSystemTests` ikisini, `IdentityTests` değeri ve gelen para yeşilinden farklı olduğunu bağlar. Kontrast: zeminde ≈ 6,0, kartta ≈ 5,6, düğmede siyah yazı ≈ 6,0.
- Ad iki yerde durur (kod ve Info.plist); `IdentityTests` ikisinin eşit olduğunu denetler. Hedef adı, bundle kimliği ve repo "BankingAI" kalır.
- "Asistan" yazan her ekran metni `Brand`'den okunur. Backend'de `ApiExceptionHandler` üç hata başlığı "Kılavuz …" oldu (docs/api.md, `AssistantEndpointTests`); `ai/service/app.py` metinleri telefona ulaşmadığı için (.NET üç koda çevirir) "Asistan" kaldı.

Doğrulama: iOS 288 test durumu (+8 `IdentityTests`), Api AssistantEndpointTests 72/72. Yedi kasıtlı bozmanın hepsi yakalandı (ad, açılış rengi, Türkçesiz büyük harf, soluk hata rengi, şifrenin silinmemesi, ad kayması, eski hata metni). Bulgular: `UILaunchScreen_Generation = YES` ayarı Info.plist anahtarını ezmedi (derlenmiş pakette test okuyor); bozma 3'ü `IdentityTests`'teki "KILAVUZ" testi değil mevcut "SON İŞLEMLER" testi yakalıyor ("kılavuz"da noktalı İ yok).
Review düzeltmeleri: yanlış giriş hatası VoiceOver'da duyuruluyor (`AccessibilityNotification.Announcement`); alan kutusunun dolgusuna dokununca da odak geliyor; görünen alan etiketi VoiceOver'da ikinci kez okunmuyor ve başlık sayılmıyor; simge betiğinin çalıştırma yorumu düzeltildi (betik sonra K12-A ile silindi). Bilinçli bırakılanlar: açılış ayarı derlenmiş pakette testle doğrulanıyor (`project.pbxproj`'a dokunulmadı); odak `.task` içinde atanıyor (cihazda UI-06 taramasında bakılacak).
Sınırlar: şifre göster/gizle ve yüzen etiket yok (UI-06); Liquid Glass simge yok; simge kırpılmış görsel, küçük boyutta ayrıntılar (devre çizgileri, çubuklar) seçilmiyor, temiz sürüm bekliyor; backend `ai/service` metinleri "Asistan"; Profil ekranında ad ve sürüm UI-07.

## iOS-o: işlem ekranları (UI-05 PR1; 26 Ekim 2026)

Tüm işlemler, bağlantılı işlemler, işlem detayı, hesap detayı ve tarih filtresi paneli siyah premium dile geçti. ViewModel, ağ, sayfalama ve rakam mantığı aynı; yalnız görünüm ve iki saf metin yeri değişti.

Kararlar: K1-B uzun listeler tek kart (`ScrollView` + `LazyVStack`), sistem `List` yok; K2-A hesap detayı üstü kartsız, çok büyük ve ince bakiye; K3-A filtre bölüm başlığının sağında çip (dönem "Tüm geçmiş"ten farklıysa vurgulu); K4-A filtre paneli sistem `Form` kaldı, yalnız renkler Palette'ten (`.medium`/`.large` yükseklik); K5-A işlem detayı üç parça (başlık+tutar, bilgi kartı, "Banka kaydı" kartı); K9-A boş/hata durumları yalnız renk (ortak görünüm UI-06); K10-B iki PR (bu PR işlem ekranları, ikincisi harcama analizi).

```text
DesignSystem/Components.swift   + HairlineDivider, DetailRow, BusinessTag, blackScreen(); ChipButtonStyle(isSelected:)
Transactions/TransactionRows    tek kart; sayfa dönen çarkı kartın içinde ve tembel
Transactions/TransactionDisplay + kindText      Accounts/AccountDisplay + typeText
```

- Sayfalama tetikleyicisi (`.task(id: items.count)`) kartın `LazyVStack`'i içinde kalmalı: dışarıda olsaydı bütün sayfalar kaydırmadan yüklenirdi.
- `blackScreen()` içerik ve gezinme çubuğu zeminini aynı yapar; çubuk kaydırınca sistem grisine dönmez.

Doğrulama: iOS paketi yeşil, `DetailTextTests` +7 test durumu (log sayımı 294, beklenen 295; fark doğrulanmadı). İki kasıtlı bozma yakalandı (transfer yönü metni, hesap türü metni). Simülatörde (kullanıcı) tüm ekranlar denendi, sorun yok.
Sınırlar: boş/hata durumları sistem görünümünde (UI-06); `LazyVStack` yüklenen satırları atmaz (binlerce satırda bellek); gün başlıklı liste ve dönem çipleriyle anında filtre yok (kapsam dışı bırakıldı).

## iOS-p: harcama analizi (UI-05 PR2; 26 Ekim 2026)

Analiz ekranı (kategori halkası ve aylık çubuklar) siyah dile geçti. ViewModel'ler, dönemler, seçim ve "geç gelen eski cevap" mantığı aynı.

Kararlar: K6-A ölçerek seçilen 13 renk; K11-A renkler `Palette.swift`'te `CategoryTone.swatch`; K12-B ton adları `green`→`lime`, `red`→`magenta`, `blue`→`slate` (kod→ton eşlemesi `AnalyticsDisplay.tone`'da kalır); K13-A renk farkı CIE76 Lab ΔE (`Swatch.distance`), eşik 25; K7-A çubuklar vurgu mavisi; K8-A sistem bölümlü dönem seçicisi kaldı; K14-B halka ortasında backend'in toplamı; K15-A seçim görünümü aynı; K9-A boş/hata yalnız renk.

```text
DesignSystem/Palette.swift      + Swatch.distance(to:), Swatch.over(_:opacity:), CategoryTone.swatch
Analytics/AnalyticsDisplay.swift  ton adları, barOpacity (görünümden taşındı)
Analytics/CategoryBreakdownSection, MonthlyTrendSection, AnalyticsView   Palette, StatusBand, cardSurface, blackScreen
BankingAITests/CategoryPaletteTests.swift (yeni)
```

- Ölçüm: eski sistem renklerinde "Diğer" mavisi ile vurgu ΔE 5,4 (pratikte aynı), ulaşım–seyahat 14; ilk plandaki gözle seçilen palet giyim–abonelik 8,3. Yeni palet: en yakın çift 28,7, anlamlı renklere en yakın 29,1, kartta kontrast en düşük 3,63 (gri), diğerleri ≥ 4,5.
- Devam eden ay: IOS-08 K12-A'nın %45 saydamlığı kartta 2,07:1 veriyordu (grafik eşiği 3); %70 (≈ 3:1) oldu, nokta ikinci işaret olarak kaldı.
- Review düzeltmeleri (PR1 ekranlarında da): boş liste + başarısız yenileme artık hata bandı gösterir (PR1'de kayboluyordu); hesap detayında bakiye VoiceOver'da "Güncel bakiye, …"; `over` saydamlığı 0–1'e sıkıştırır.

Doğrulama: iOS 303 test durumu (+8 `CategoryPaletteTests`). Beş kasıtlı bozmanın hepsi yakalandı (Diğer = vurgu, abonelik = giyim, koyu gri, devam eden ay opak, karekök yok). Simülatörde (kullanıcı) renkler, ortadaki toplam, VoiceOver ve boş liste + hata denendi.
Sınırlar: renk körlüğü için ayrıca denenmedi (renk tek bilgi taşıyıcısı değil: liste adı ve VoiceOver cümlesi var); ΔE eşiği 25 bizim seçimimiz; animasyonlar UI-06.

## iOS-q: yükleniyor ve boş/hata durumları (UI-06 PR1; 26 Ekim 2026)

Dönen gri çark ve sistemin gri boş/hata görünümü (`ContentUnavailableView`) uygulamanın kendi diline geçti. ViewModel, ağ, sayfalama ve metinler aynı; yalnız durumların görünümü değişti.

Kararlar: K1-C liste ve kartlarda gerçek satır (`AccountRow`, `TransactionRow`) nötr sahte veriyle çizilip `redacted` ile griye çevrilir, grafiklerde gri halka ve 7 sabit boy çubuk (Swift Charts işaretleri `redacted` ile grileşmiyor); K2-B 1 sn'lik yumuşak nabız (opaklık 1 ↔ 0,5), "Hareketi azalt" açıkken duruk; K3-A gecikme yok (hızlı cevapta iskelet bir an görünür); K4-C tek `StateMessage`, iki boy: `.inline` bölümde (çağıran karta koyar), `.screen` tam ekran; K5-A metinler aynen; K10-B iki PR.

```text
DesignSystem/Skeleton.swift      (yeni) sahte satırlar, iskelet grisi, nabız, .skeleton(spoken:)
DesignSystem/StateMessage.swift  (yeni) ortak boş/hata görünümü
Accounts/AccountsSection, Transactions/RecentTransactionsSection, Analytics/CategoryBreakdownSection,
Analytics/MonthlyTrendSection    çark → iskelet, ContentUnavailableView → StateMessage
Transactions/TransactionRows     ilk sayfadan önce 6, sonraki sayfa için 2 iskelet satırı (+ TransactionSkeletonRows)
Analytics/SpendingSummaryCard, Home/HomeHeader   kendi redacted çözümleri ortak .skeleton()'a geçti
Accounts/AccountDetailView       "Hesap bulunamadı" StateMessage(.screen)
BankingAITests/LoadingStateTests.swift (yeni)
```

- İskelet satır sayıları gerçek listeyle aynı: hesap 2, son işlem `RecentTransactionsViewModel.count` (5), kategori 4, çubuk `MonthlyTrendViewModel.months + 1` (7), tüm işlemler ilk sayfa 6, sonraki 2. Sahte satırların kimliği sabit ve tekrarsız (nabız sırasında yeniden kurulmasınlar).
- İskelet grisi metin renginin %16'sı: zeminde ΔE 16,2, kartta 16,7 (görünür), kontrast 1,46/1,55 (3:1'in altında, içerik ya da düğme gibi okunmaz).
- VoiceOver: iskelet tek "Yükleniyor" okunur, altındaki sahte metin hiç okunmaz; üst satırdaki ad iskeleti hiç okunmaz (`spoken: false`), aşağıdaki kartlar zaten "Yükleniyor" diyor.
- `redacted` şekilleri griye çevirmez: iskelette şekiller (`Circle`, çubuklar) `Skeleton.fill` ile boyanır.
- Kalan çarklar bilinçli: giriş düğmesi, filtre paneli ve hesap detayındaki "Hesap bilgileri yenileniyor…" (bir işlemin sürdüğünü söylüyor; ekrandaki geçerli bakiyeyi iskeletle örtmek yanlış olurdu).

Doğrulama: iOS 309 test durumu (+7 `LoadingStateTests`), 0 kırmızı. Üç kasıtlı bozmanın hepsi yakalandı (son işlem iskeleti 4 satır, aynı kimlik, sahte veride persona adı). İskeletler ve `StateMessage` geçici bir `ImageRenderer` testiyle resme çizilip gözle kontrol edildi (dosya silindi); bulunan tek kusur (kategori noktaları parlak) düzeltildi. Simülatörde dolu ana ekran görüldü; yerel API hızlı olduğu için iskelet canlıda gözle yakalanmadı.
Sınırlar: geçiş animasyonları, titreşim, şifre gözü ve erişilebilirlik taraması PR2'de; iskelet sabit "TRY" ve tek para birimi kartı gösterir (birden çok para birimli müşteride veri gelince kart sayısı artar).

## iOS-r: geçişler, titreşim, şifre gözü ve erişilebilirlik taraması (UI-06 PR2; 26 Ekim 2026)

Uygulama dokununca ve değişince "hissediliyor"; en büyük yazı boyutunda (AX5) bulunan bozukluklar düzeltildi. ViewModel, ağ ve metinler aynı.

Kararlar: K6-C durum değişiminde solma + rakam dönmesi (`contentTransition(.numericText())`, yalnız görünüm) + sohbette mesaj ve önerilerin alttan belirmesi; ekran geçişleri sistemin (UI-07 sekmeleriyle çakışmasın); K7-A titreşim yalnız gönderimde (hafif darbe; yazılan, öneri ya da tekrar) ve yanlış girişte (hata); yenilemede iOS'un kendi eşik titreşimi yeter (ikincisi çift olurdu); K8-A şifre gözü; K9-A elle tarama + aynı PR'da düzeltme.

```text
DesignSystem/Motion.swift      (yeni) Motion.animation(_:reduceMotion:), .motion(value:)
DesignSystem/Components.swift  StatusBand AX'te dikey, BusinessTag bölünmez, BrandMark ölçeklenir
DesignSystem/StateMessage.swift metinler kesilmez, sarılır
Bölümler + SpendingSummaryCard  .motion(value: state); AccountRow/SpendingSummaryCard rakam dönmesi
Chat/ChatView.swift            mesaj/öneri geçişi, gönderim titreşimi, kaydırma Motion'a bağlı, Gönder/Durdur 44 pt
Login/LoginView.swift          şifre gözü, hata titreşimi
Transactions/TransactionRow    AX boyutlarında tek sütun (simge yok)
Home/HomeHeader                profil dairesi 44 pt, baş harfler xxLarge'da sabit + büyük içerik görüntüleyici
BankingAITests/MotionTests.swift (yeni)
```

- "Hareketi azalt" açıkken `Motion.animation` nil verir: solma, rakam dönmesi, mesaj kayması ve sohbet kaydırması olmaz, değişiklik anında olur. İskelet nabzı PR1'den beri aynı kuralda.
- Şifre gözü görünümün durumudur (`LoginViewModel` değişmedi); "Giriş yap"a basılınca şifre gizlenir ve eskisi gibi silinir. VoiceOver "Şifreyi göster/gizle".
- AX5 taraması (simülatör + geçici `ImageRenderer` testi, dosya silindi): işlem satırında başlık "Akar…" ve tutar üç satırdı; "İŞLETME" kapsülde ikiye bölünüyordu; `StateMessage` ve hesap adı "…" ile kesiliyordu; `StatusBand` metni heceleniyordu; profil dairesinde "MA" yerine "…"; Gönder/Durdur 34 pt (en az 44 olmalı); marka yazısı sabit 44 pt. Hepsi düzeltildi, normal boyut görünümü aynı kaldı.
- Ders: `ImageRenderer` sayfaya sınırlı yükseklik verince metinleri kesiyor; kaydırma görünümünü taklit etmek için sayfa `fixedSize(vertical:)` ile çizilmeli, yoksa yanlış bulgu çıkar.

Doğrulama: iOS 312 test durumu (+3 `MotionTests`), 0 kırmızı; kasıtlı bozma (`Motion` ayarı yok sayıyor) yakalandı. Simülatörde AX5 ana ekran önce/sonra görüldü.
Sınırlar: titreşim simülatörde hissedilmez (kodda doğrulandı); giriş, sohbet ve işlem detayı ekranları AX5'te ve VoiceOver ile kullanıcı tarafından elle denenecek; Accessibility Inspector "Audit" kullanıcı tarafından çalıştırılacak; arayüz testi hedefi yok (koruma elle tarama).

## iOS-s: gelir payı kartı (INC-04; 10 Kasım 2026)

Ana ekranda "Bu ay harcama" kartının altında "Gelire göre harcama": geçen tam ayın harcaması gelirin yüzdesi olarak, Temel / İsteğe
bağlı / Bilinmiyor gruplarına bölünmüş bir çubuk ve kurallar tuttuysa en fazla 2 tasarruf önerisi. Rakam, pay ve öneri metni
backend'in (`GET /api/analytics/spending-advice?period=LastMonth`, docs/api.md INC-02); telefon toplamaz, çıkarmaz, yalnız çizer ve yazar.
Model yok: öneri cümlesi .NET'in sabit şablonudur, kart AI servisi kapalıyken de aynıdır. Backend değişmedi.

Kararlar: K1-A tek istek; K2-B üç gruplu çubuk + açıklama (isteğe bağlı vurgu mavisi, Temel `slate`, Bilinmiyor gri; yeşil/kırmızı
yok, renk farkı ve kontrast testle); K3-A %100'ü aşan pay çubuğu doldurur ve kırpılır, başlık tam rakamı yazar, altında "Harcama
gelirin üzerinde"; K4-B öneri kısa satır ("Eğlence · ayda 236,18 TL, yılda 2.834,20 TL"), dokununca backend'in cümlesi açılır; K5-B
öneri yoksa sabit sebep ("Öneri yok; harcamalarınız kuralların içinde kalıyor."), maaş yoksa başlık sebebi söyler ve çubuk çizilmez;
K6-A karttan başka ekrana bağlantı yok; K7-A bağımsız kart, IOS-12 içgörü bölümü gelince oraya taşınır.

```text
Networking/APIModels.swift      SpendingAdvice, CurrencyAdvice, GroupSpending, AdviceItem, SpendingGroup/AdviceRule/NoAdviceReason
Networking/APIClient.swift      APIRequest.spendingAdvice(period:)
Insights/IncomeShareViewModel   (yeni) ListLoader kalıbı; ay adı backend'in ilk gününden
Insights/IncomeShareDisplay     (yeni) saf: ay, başlık, çubuk parçaları (Decimal'de kırpma), açıklama, öneri satırı, sebep, renk
Insights/IncomeShareCard        (yeni) görünüm; yükleniyor iskelet, boş/hata StateMessage, yenileme hatası StatusBand
DesignSystem/Skeleton.swift     Skeleton.incomeShare (nötr yer tutucu)
Home/HomeView, App/BankingAIApp kartın yeri, aşağı çekip yenilemeye katıldı
BankingAITests/IncomeShareTests (yeni) 25 test durumu
```

- Bilinmeyen grup adı çözümleme hatasıdır ("Tekrar dene"); başka bir grup gibi çizilmez.
- Para birimleri ayrı blok (Zeynep TRY %43,11 ve USD %2,81); birden fazla para biriminde blokta kod yazılır.
- "kadarı" ile yazılır ("Gelirinizin %90,36 kadarı"): yüzdeden sonra sesli uyuma göre değişen ek gerekmez.
- Çubuk parçaları Decimal'de kırpılır (1 − 0,8 = 0,2; Double'da 0,19999… çıkıyordu, test yakaladı).

Doğrulama: iOS 313 → 338 test durumu, 0 kırmızı. 10 kasıtlı bozma (dönem ThisMonth, kırpma yok, ay kayması, 3 öneri, sebeplerin yer
değiştirmesi, yeşil grup rengi, başlık yanlış alandan, yenilemede ay silinmesi, %100'de aşım, gevşek grup çözümleme) hepsi yakalandı.
Sınırlar: yalnız geçen tam ay (backend kuralı); demoda yalnız Selin öneri alır; simülatörde elle deneme kullanıcıda (API demo anıyla,
`BANKINGAI_DEMO_NOW`, açılmazsa "geçen ay" Ekim 2026 olur ve rakamlar tablodan farklıdır); AX5 ve VoiceOver elle denenmedi.

## iOS-t: dokunulabilir seçenekler (CHAT-10 PR3b; 12 Kasım 2026)

PR3a'dan beri asistan geri sorunun metninde seçenekleri saymıyor ("Hangi dönem için?"); seçenekler cevabın `choices` alanında
(`[{label, detail}]`). Telefon bunları son cevabın altında alt alta, tam genişlik satırlar olarak gösterir; dokununca yalnız etiket
gönderilir ve AI servisi onu bekleyen soruyla eşleştirir (yazarak cevap da çalışır).

Kararlar: D1-A seçenekler `ChatViewModel.choices`'ta, önerilerin ikizi (yalnız son soru dokunulabilir, K8-A; eski sorularda satır
yok); D2-A alan zorunlu, bozuk tek seçenek bütün cevabı "Tekrar dene"ye düşürür (seçenek sorunun kendisi, link gibi ek değil); D3
simülatör denemesinden sonra değişti (kullanıcı: "düzensiz duruyor"): ayrı çerçeveli kutular yerine tek kart (`Chat/ChoiceList`),
satırlar ince çizgiyle ayrılır, etiket Türkçe büyük harfle başlar (`ChoiceDisplay.title`; gönderilen etiket değişmez), `detail` altında
küçük gri satır, sağda vurgu renginde ok, basılıyken satır aydınlanır, en az 44 pt; D4-A seçenekli cevapta öneri
çipi gösterilmez; D5-A satır ekran okuyucuda tek öğe (etiket + açıklama). Önceki kararlar: P4-A yalnız etiket, P6-A hatadan sonra
satırlar gizli, "Tekrar dene" kullanıcının balonundaki etiketi yeniden gönderir.

```text
Networking/APIModels.swift      AssistantChoice (yeni), AssistantReply.choices (zorunlu)
Chat/ChatViewModel.swift        choices: cevaptan gelir, gönderimde boşalır; seçenek varsa suggestions boş
Chat/ChoiceList.swift           (yeni) ChoiceList kartı + ChoiceDisplay.title (Türkçe baş harf)
Chat/ChatView.swift             kart son cevabın altında, önerilerin üstünde; liste soru gelince kartın sonuna kayar
BankingAITests/ChatChoiceTests  (yeni) 10 test, 18 test durumu
```

Simülatör denemesinde (Murat) iki hata bulundu ve düzeltildi: (1) liste soruya kayıyordu, kart girişin altında gizli kalıyordu →
liste kartın sonuna kayar; (2) kart her soruda aynı sabit kimliği taşıyordu, SwiftUI ikinci sorunun altında ilk sorunun satırlarını
tuttu ("Ayda kaç TL" altında senaryo satırları) → kimlik soruya bağlı (`choices-<mesaj id>`). İkincisi için ViewModel testi eklendi
(ViewModel zaten doğruydu; hata yalnız görünümdeydi, birim testi görünümü göremez).

İkinci tur (kullanıcının denemesi, D6-A): tutar sorularında kartın son satırı `inputHint` ("Başka bir tutar yazın"); ikincil renk ve
kalem simgesiyle seçeneklerden ayrışır, dokununca mesaj göndermez, yazı alanına odaklanır (`@FocusState`). `inputHint` zorunlu alan
(eksik/`null` → "Tekrar dene"), gönderimde seçeneklerle birlikte boşalır.

Doğrulama: iOS 338 → 360 test durumu (xcodebuild sayımı), 0 kırmızı. 6 kasıtlı bozma (seçenek okunmaz, eksik alan boş sayılır, bozuk seçenek
atlanır, gönderimde boşalmaz, hatada geri gelir, seçenekle öneri birlikte) hepsi yakalandı. Görünüme ait iki şey (dokununca etiket
yerine başka metin, satırın yeri) birim testinde değil, simülatörde denendi.


## iOS-u: alt sekme çubuğu ve Profil (UI-07; 12 Kasım 2026)

Giriş yapılınca uygulama dört sekmeli: Ana sayfa · Analiz · Asistan · Profil. Her sekmenin kendi gezinme yığını var; ekran
modellerinin hepsi `App/MainTabView`'da yaşar, sekme değişince sohbet ve grafikler olduğu gibi kalır, çıkışta hepsi silinir
(AI-08 K4-D). Backend değişmedi.

```text
RootView ─► MainTabView (ViewModel'ler, seçili sekme, müşterinin saat dilimi)
             ├─ Ana sayfa  ─ HomeView (kendi NavigationStack'i; yüzen "Asistana sor" kapsülü kalktı)
             ├─ Analiz     ─ NavigationStack ─ AnalyticsView
             ├─ Asistan    ─ NavigationStack ─ ChatView
             └─ Profil     ─ NavigationStack ─ ProfileView
```

Kararlar: K1-A sistem `TabView`'i (iOS 26'da cam çubuk, iOS 17–18'de düz koyu; erişilebilirlik sistemden); K2-A sıra Ana sayfa ·
Analiz · Asistan · Profil; K3-A sekme adı "Asistan" (ekran başlığı `Brand.assistantName`), simgeler `house` · `chart.pie` ·
`bubble.left.and.text.bubble.right` · `person.crop.circle`; K4-A "Bu ay harcama" kartı Analiz sekmesine geçirir (ekran iki yerde
açılmaz, `AnalyticsRoute` silindi); K5-B sekme değişince bekleyen soru durmaz (yalnız Durdur ve uygulamanın arka plana geçmesi;
AI-08 K5-C daraldı); K6-B Profil: ad soyad, kullanıcı adı, müşteri numarası, para birimi, saat dilimi, uygulama sürümü (hepsi
`GET /api/me`'den; ikinci istek yok, `HomeViewModel.profile`); K7-B Çıkış onaylı ("Sohbet geçmişi silinir."); K8-B başlıktaki baş
harf dairesi Profil sekmesine geçirir (menü kalktı); K9-C saat dilimi `Europe/Istanbul · GMT+03:00` (fark o günün, yaz saatine göre);
K10-A tek PR.

```text
App/AppTab.swift                (yeni) sekmelerin sırası, adı, simgesi
App/MainTabView.swift           (yeni) TabView, ViewModel'ler, sekme geçişi, saat dilimi ortamı bütün sekmelere
App/BankingAIApp.swift          RootView → MainTabView
Profile/ProfileDisplay.swift    (yeni) saf: ad soyad, sürüm ("1.0 (1)", eksikse satır yok), saat dilimi
Profile/ProfileView.swift       (yeni) görünüm; yükleniyor iskelet, hata StateMessage + Tekrar dene, onaylı Çıkış
Home/HomeView, HomeHeader       kapsül ve Analiz yolu kalktı; kart ve daire sekmeye geçirir
Home/HomeViewModel              /api/me cevabını `profile` olarak tutar
Chat/ChatView                   onDisappear'daki durdurma kalktı (K5-B)
DesignSystem/Skeleton.swift     Skeleton.profile (nötr yer tutucu)
BankingAITests/NavigationProfileTests (yeni) 11 test durumu
```

- Saat dilimi ortamı (`customerTimeZone`) eskiden yalnız Ana sayfa yığınındaydı; sohbet artık ayrı yığında olduğu için `MainTabView`'a
  taşındı (yoksa sohbetten açılan işlem detayında saat yanlış dilimde görünürdü).
- Bilinmeyen saat dilimi adı farksız, olduğu gibi yazılır; sürüm anahtarlarından biri eksikse satır gizlenir (uydurma değer yok).

Doğrulama: iOS 360 → 371 test durumu, 0 kırmızı. 5 kasıtlı bozma (sekme sırası, sürüm parçalarının yer değiştirmesi, farkın işareti,
yaz saatinin yok sayılması, profilin saklanmaması) hepsi yakalandı. Sekme geçişi ve K5-B görünüm katmanında; birim testi değil
simülatör.
Simülatör (Murat, API demo anıyla; AI servisi kapalı): dört sekme cam çubukta; kart Analiz'e, daire Profil'e geçirir; Profil satırları
`CUST-6722`, `Europe/Istanbul · GMT+03:00`, `1.0 (1)`; Asistan sekmesinde kapalı bandı ve giriş kapsülü çubuğun üstünde. Üç görünüm
hatası bulundu ve düzeltildi: (1) Çıkış beyazdı (düğme stili yazı rengini eziyordu; renk yazının kendisinde); (2) onay iOS 26'da
sayfanın tepesinde balon olarak çıkıyordu (onay sayfaya bağlıydı; düğmeye bağlandı, balon düğmeyi gösterir; "Vazgeç" bu biçimde
dışarı dokunmaktır); (3) en büyük yazıda sürüm kartı daralıyor, Çıkış çubuğa yapışıyordu (kartlar tam genişlik, altta boşluk).
K5-B'nin "cevap beklerken sekme değiş" davranışı modelle denenmedi (AI servisi kapalıydı).
Sınırlar: açık sekmeye yeniden dokununca yığının başına dönme yok; cevap gelince sekme rozeti yok (K5-C seçilmedi); Profil yalnız
gösterir.

## iOS-v: açık krem tema (UX-01 PR1; 13 Kasım 2026)

Uygulama artık her zaman açık temada: krem zemin, beyaz kart, koyu kehribar vurgu. UI-01'in "her zaman siyah" ve UI-04'ün mavi vurgu
kararlarının yerine geçer (UX-01 K6-A, Z3). Yalnız görünüm değişti: ekranlar, ViewModel, ağ ve rakam mantığı aynı; ekranlar renkleri
yine yalnız `Palette`'ten aldığı için hiçbir ekran dosyasında renk yazılmadı (yalnız iki düğme dolgusu yeni tokena geçti).

| Token | Değer | Ölçüm (WCAG) |
|---|---|---|
| `background` | `#F6F1E7` krem | — |
| `surface` / `raised` / `hairline` | `#FFFFFF` / `#EFE7D8` / `#E2D8C4` | — |
| `primaryText` | `#2A2117` | 14,04 zemin · 15,81 kart · 12,87 raised |
| `secondaryText` | `#6E604D` | 5,42 · 6,10 · 4,96 |
| `accent` (yazı, bağlantı, seçili, grafik çubuğu) | `#9A5100` | 5,25 · 5,91 · 4,81; soluk ay çubuğu (0,7) 3,23 |
| `accentFill` (yeni; yalnız düğme dolgusu) | `#F5A700` | kartta 2,02 → yazı olarak kullanılmaz (testle sabit) |
| `onAccent` | `#2A1A00` | 8,35 `accentFill` üstünde |
| `incoming` / `failure` | `#1E7F45` / `#C8321F` | 5,03 kart / 5,34 kart · 4,74 zemin |

Kararlar: **R1-A** kategori renkleri grafiktir (adları yanında ana yazı renginde), bu yüzden kartta ≥ 3:1 (WCAG 1.4.11); kendi aralarında
ΔE ≥ 25, vurgu/düğme sarısı/gelen para/hata ve sınıflandırılmamış griye ΔE ≥ 20 (4,5 yazı eşiği açık temada yalnız cırtlak renk
bırakıyordu). **R2-A** grafik çubukları koyu kehribar (`#9A5100`); mockup'taki sarı çubuk kartta 2,0:1 kalıyordu.
Kategori seti: orange `#E0701A` · lime `#6E9A1F` · brown `#7A4E2E` · pink `#D6568C` · purple `#9B4FD0` · indigo `#3E57C9` · teal `#0B6F86` ·
cyan `#2E9BD6` · magenta `#B8389E` · mint `#2F9E86` · slate `#8E6A8A` · yellow `#A88C00` · gray `#8A7F70`. Planın ilk mint/slate değerleri
kaydırıldı (gelen paraya 20,0 sınırdaydı; gelir kartında griye 22,9 ile oradaki ≥ 25 testini geçmiyordu). İskelet dolgusu 0,16 → 0,18
(0,16'da krem zeminde ΔE 11,7 < 12). `AccentColor` ve `LaunchBackground` asset'leri iki görünümde yeni değerde; `.preferredColorScheme(.light)`.

Doğrulama: 374 test durumu geçti (371 + 3 yeni: vurgu/raised kontrastı, düğme sarısı yazı değil, kategoriler griden uzak; diğerleri yeni
değerlere çevrildi ya da yeniden adlandırıldı). Dokuz kasıtlı bozmanın hepsi yakalandı: ikincil yazı `#9A8C78`, vurgu `#C46A00`,
düğme yazısı beyaz, zemin beyaz, açılış rengi beyaz, teal = cyan, eski gri, iskelet 0,10, düğme sarısı = vurgu. Simülatörde (Murat, gerçek
API ve model): giriş, Ana sayfa, Analiz (halka + aylık çubuk), Asistan (boş durum, cevap, bağlantı, öneri çipleri, dolu gönder düğmesi),
Profil; Ana sayfa en büyük yazı boyutunda. Düzeltme gereken ekran çıkmadı.
Sınır: Elif (iki para birimi) ve hata bandı/seçenek kartı simülatörde görülmedi (renkleri aynı tokenlardan geliyor ve testle korunuyor).

```text
DesignSystem/Palette.swift      açık değerler, accentFill, kategori tonları
DesignSystem/Skeleton.swift     fillOpacity 0,18
DesignSystem/Components.swift   birincil düğme dolgusu accentFill
Chat/ChatView.swift             gönder düğmesi dolgusu accentFill
App/BankingAIApp.swift          .preferredColorScheme(.light)
Assets.xcassets                 AccentColor #9A5100, LaunchBackground #F6F1E7
BankingAITests/                 DesignSystem, CategoryPalette, Identity testleri
```
