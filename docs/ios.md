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
