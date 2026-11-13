# UX-01 — Cep asistanı: görsel, kısa, iki modlu (tasarım)

Tarih: 13 Kasım 2026 · Durum: **onaylandı (13 Kasım 2026): K1-A K2-B K3-A K4-A K5-A K6-A K7-A K8-A K9-A** · todo: 14c / UX-01

Taslak ekranlar (tarayıcıda açılır, gerçek demo rakamlarıyla): `docs/superpowers/specs/ux-01-mockups/`
(`answer-styles`, `home-ideas`, `ask-flow`, `palette`, `background`, `answer-mode`).

## 1. Neden

Kullanıcı (Burak) gidişattan memnun değil: model uzun finansal metin yazıyor, uygulama bir sohbet uygulaması gibi hissettiriyor,
ana ekran "çirkin, gereksiz şeyler içeriyor ve uygulamanın fikrine uymuyor".

Uygulamanın fikri (kullanıcının sözleriyle): **bankanın backend'inden araç çağırarak kullanıcının harcama yönetimini tek bir
uygulamadan kontrol etmesini sağlayan, bunu tasarruf ve yatırım önerileriyle destekleyen** bir uygulama.

En önemli özellik **pratiklik**: kullanıcının banka uygulamasına girip tek tek toplayacağı kalemleri (markete bu ay ne gitti,
geçen aya göre fazla mı, nereye gitti) hızlıca görmesi. Asistan bir sohbet botu değil, **etkileşimli** bir şey: yazı yine olur ama
kısa; altında rakam, grafik, mağaza tablosu ve işlemler gelir.

## 2. Hedefler, hedef olmayanlar, başarısızlık sayılanlar

**Hedefler**
- Ana ekran bir bakışta "bu ay ne kadar, nereye, gelirin ne kadarı" söyler; banka uygulamasında yapılan toplamayı gereksiz kılar.
- Bir soru 1 dokunuş ya da 1 cümleyle sorulur, cevap bir kart olarak gelir: büyük rakam, karşılaştırma, dağılım, tek günlük cümle.
- Kullanıcı cevabın derinliğini seçer: **Basit** (rakam + dağılım + tek cümle) ya da **Detaylı** (istatistikler, 6 aylık seri).
- Tasarruf ve yatırım önerileri panelden ve kartlardan tek dokunuşla ulaşılır ("Nerede kısarım?", "%10 kıssam?").

**Hedef olmayanlar (bu planda yok)**
- Birden çok bankayı birleştirme (bugün veri tek sentetik bankadan; ileride ayrı konu).
- Yeni finansal hesap türleri (bu plan mevcut analizleri yeni biçimde gösterir; tek istisna K4'teki kategori detayı).
- Koyu tema varyantı (K6).

**Başarısızlık sayılanlar (çalışsa bile)**
- Cevap yine paragraf hâlinde gelir ve kullanıcı rakamı metnin içinde arar.
- Kartta görünen rakam ile cümledeki rakam farklıdır (tek kaynak: backend sonucu).
- Telefon finansal hesap yapar (toplam, yüzde, ortalama) — proje kuralı: telefon yalnız çizer.
- Mod geçişi yeni bir istek bekletir (Basit ↔ Detaylı anında olmalı).
- Ana ekran yine bilgi yığını olur (hesap kartları, işlem listesi, birbirine benzeyen kartlar).

## 3. Kısıtlar

- Proje ilkesi: LLM yorumlar ve araç seçer; .NET backend hesaplar, filtreler, yetkilendirir. MCP araçları salt okunur.
- Kartın her rakamı backend sonucundan gelir ve bugünkü sayı denetimi (AI-06) kart için de geçerli kalır.
- Yerel model `qwen3.5:4b`; ölçümler Mac'i ısıtır (aralıklı, sonra model bellekten boşaltılır).
- Mevcut testler, ölçümler (araç seçimi 113/113, cevap doğruluğu 25/25, 1278 ifade) bozulmaz; bilinçli değişen testler PR'da listelenir.
- Git akışı: her PR kendi dalında, Burak çalıştırır; AI imzası yok.
- iOS 17+, kütüphanesiz (grafikler Swift Charts), tasarım sistemi `ios/BankingAI/BankingAI/DesignSystem/` tek yer.

## 4. Kullanıcının verdiği kararlar (bu konuşmada, 13 Kasım 2026)

| Kod | Karar | Taslak |
|---|---|---|
| UX-A | Cevap biçimi **B**: kart + grafik, metin yok denecek kadar az, altta tek günlük cümle; aralıklar özenli | `answer-styles` |
| UX-B | Ana ekran **H2 — bu ay paneli**: bu ay ne kadar (geçen ayın aynı günleriyle), nereye gitti (kategori çubukları), gelir payı; soru alanı altta hep açık | `home-ideas` |
| UX-C | Soru sorulunca cevap **F1 — panelin üstünde alttan kayan sayfa**; aşağı çekince kapanır, takip soruları aynı sayfada | `ask-flow` |
| UX-D | Tema **Z3 — açık krem**: krem zemin `#f6f1e7`, beyaz kartlar, koyu kahve yazı `#2a2117`, vurgu sarı-turuncu (`#f5a700` → `#f07b0c`), bağlantı koyu turuncu `#c46a00`, artış kırmızı `#d93b2b`, azalış yeşil `#1f8a4c` | `background`, `palette` |
| UX-E | Cevap modu **Basit / Detaylı**, ikisi birden: Profil'de varsayılan + her kartın köşesinde o cevaba özel anahtar | `answer-mode` |
| UX-F | Kartları **kod kurar, iPhone yalnız çizer** (yaklaşım Y2, §5) | — |

UX-D, UI-01'in "her zaman koyu tema" kararının yerine geçer; UI-04'ün "vurgu rengi simgenin mavisi" kararı da değişir (simge
ayrı bir iş olarak K8'de).

## 5. Yaklaşımlar (seçim: Y2)

| | Ne | Artı | Eksi |
|---|---|---|---|
| Y1 | iPhone mevcut `facts` metinlerinden sayı ayıklayıp kart yapar | backend değişmez | yazıdan sayı ayıklamak kırılgan; detaylı istatistik yok; telefon hesaba başlar (kurala aykırı) — **elendi** |
| **Y2** | AI servisi araç sonucundan **yapısal kart** kurar (bugünkü `ToolOutcome.written` gibi), .NET aynen iletir, iPhone 4–5 genel şablonla çizer; Basit ve Detaylı aynı cevapta | tek kaynak, tek istek, sayı denetimi sürer, mod anında | en çok iş: sözleşmeye yeni alan (Python + .NET + OpenAPI + iPhone) |
| Y3 | Sohbet yalnız araç + dönem döner, iPhone veriyi analiz uç noktalarından kendisi çeker | iPhone analiz kodu yeniden kullanılır | iki istek, cümle ile kart farklı anda hesaplanır, "ne olurdu"/bütçe kartlarının uç noktası yok |

Neden Y2: rakamın tek kaynağı backend + kod kalır; kart ve cümle aynı sonuçtan çıkar; son PR'larda başlayan "kod yazar" yönünün
devamıdır ("kod kurar, telefon çizer").

## 6. Tasarım

### 6.1 Ekranlar ve gezinme

```text
Sekmeler (K2-B):  Ana sayfa · Analiz · Asistan · Profil   (Asistan sekmesi kalır: kayan sayfadaki oturumun tam ekranı)

Ana sayfa (H2)                         Kayan sayfa (F1)                       Profil
┌──────────────────────────────┐       ┌──────────────────────────────┐       ┌───────────────────────────┐
│ Ekim                      MA │       │            ──                │       │ Cevap biçimi [Basit|Detaylı]│
│ ┌ BU AY HARCAMA · 10 GÜN ──┐ │       │      (soru, sağda, vurgu) ▶ │       │ Hesaplarım              › │
│ │ 8.241,40 TL              │ │       │ ┌ MARKET · EYLÜL [B|D] ───┐ │       │ Tüm işlemler            › │
│ │ aynı günler 8.876,40 %7↓ │ │  ───▶ │ │ 18.016,50 TL     ▲ %51  │ │       │ Çıkış yap                 │
│ └──────────────────────────┘ │       │ │ mağaza çubukları         │ │       └───────────────────────────┘
│ ┌ NEREYE GİTTİ      Tümü › ┐ │       │ └──────────────────────────┘ │
│ │ Market    ████  5.133,00 │◀┼─ dokun│ tek günlük cümle             │
│ │ Akaryakıt ██    2.658,50 │ │       │ [Bu ay?] [%10 kıssam?] [İşlemler] │
│ └──────────────────────────┘ │       │ [ Devam et…              ↑ ] │
│ ┌ GELİR PAYI · EYLÜL %63 ──┐ │       └──────────────────────────────┘
│ └ Nerede kısarım? ›────────┘ │
│ [ Kılavuz'a sor…         ↑ ] │
└──────────────────────────────┘
```

- **Panel** üç kart, hepsi mevcut REST uç noktalarından (model yok, AI kapalıyken de çalışır):
  `period-comparison?comparison=MonthToDate`, bu ayın kategori dağılımı
  (`category-breakdown?period=ThisMonth`), geçen ayın gelir payı (`income-share?period=LastMonth`). Telefon yüzde/toplam hesaplamaz.
- **Panelin her satırı dokunulabilir** (önerim): "Market" satırı, o soru yazılmış gibi kayan sayfayı açar ("Bu ay markete ne kadar
  gitti?"); "Nerede kısarım?" öneri aracını, "Tümü ›" kategori listesini açar. Yazmadan etkileşimin yolu budur.
- **Kayan sayfa** bir sohbet oturumudur: kartlar alt alta birikir (en yenisi altta), aşağı çekince kapanır; açık kalan oturum
  sekme değişse de sürer (UI-07 kuralı), çıkışta silinir. Geri soru (seçenek kartı, CHAT-10) aynı sayfada aynen çalışır.
- **Hesaplar ve tüm işlemler** ana ekrandan kalkar, Profil'e taşınır; cevaplardaki "İşlemler" düğmesi (CHAT-07 bağlantıları)
  aynen kalır.

### 6.2 Kart (Y2): sözleşme

Cevap sözleşmesine (`/v1/chat` → `AssistantReply`) isteğe bağlı bir `cards` alanı (kart listesi, çoğu zaman tek kart) eklenir; `answer` metni kalır (erişilebilirlik,
eski istemci, ölçümler). Kartı AI servisi araç sonucundan **kodla** kurar; model kartın hiçbir alanını yazmaz.

```jsonc
"cards": [{
  "title": "Market · Eylül",                 // konu + dönem
  "headline": { "amount": 18016.50, "currency": "TRY", "text": "18.016,50 TL" },
  "change":   { "percent": 50.84, "direction": "up", "against": "Ağustos", "againstText": "11.944,50 TL" },
  "simple": [                                // Basit bölümü: sıralı bloklar
    { "kind": "bars", "rows": [ { "label": "BİM", "amount": 6216.00, "text": "6.216,00", "share": 1.0 }, … ] }
  ],
  "detailed": [                              // Detaylı bölümü: Basit'in üstüne eklenir
    { "kind": "series", "points": [ { "label": "Nis", "amount": 8654.00 }, … ], "highlight": "Eyl" },
    { "kind": "stats",  "items": [ { "label": "6 ay ortalaması", "text": "12.988,08 TL" }, … ] }
  ],
  "sentence": "Harcamanın üçte biri markete gitmiş; haftada yaklaşık 4.200 TL.",
  "actions": [ … mevcut suggestions + links … ]
}]
```

- Blok türleri (iPhone şablonları): `bars` (etiket + tutar + oran çubuğu), `series` (sütunlar), `stats` (2 sütunlu ızgara),
  `rows` (düz liste: ödemeler, işlemler), `assets` ("ne olurdu" varlık satırları). Her blokta hem sayı (çizim için) hem hazır
  yazı (`text`, backend biçimiyle) vardır: **telefon sayı biçimlendirmez ve hesaplamaz**, yalnız oranla çubuk boyu çizer.
- `share`/`percent` gibi oranları da AI servisi hesaplar (backend sonucundan); sayı denetimi kartın `text` alanlarını da tarar.
- Araç → kart eşlemesi `facts.py`'nin yanında yeni bir modülde (`cards.py`), her araç için bir kurucu (bugünkü `outcome_for` gibi).
- Kart kurulamazsa (bilinmeyen araç, eksik alan) `cards: []` ve bugünkü metin cevabı gösterilir — hiçbir zaman hata değil.
- **Birden çok para birimi** (benim kararım; ör. Elif EUR + TRY): para birimleri asla toplanmaz. Sonuçta birden çok para birimi
  varsa `cards` listesinde her para birimi için bir kart olur (Analiz sekmesinin "para birimi başına ayrı halka"
  kuralıyla aynı); tek para biriminde liste tek elemanlıdır. Panelde "Bu ay harcama" kartı da para birimi başına bir satır gösterir.

### 6.3 Cevap modu

- Profil'de `Basit | Detaylı` (varsayılan **Basit**), cihazda saklanır (yalnız arayüz tercihi; backend'e gitmez).
- Her kartın köşesinde aynı anahtar, yalnız o kart için; değişince istek atılmaz, `detailed` blokları gösterilir/gizlenir.
- Detaylı bölüm bir araçta yoksa anahtar gösterilmez.

### 6.4 Tema

- `Palette`'te Z3 değerleri; `.preferredColorScheme(.light)` her yerde (K6).
- Kontrast ve kategori renkleri **ölçülerek** (UI-05 kuralı): yazı/zemin ≥ 4,5:1, büyük yazı ≥ 3:1, kategori renkleri arası Lab ΔE ≥ 25,
  sarı yazı açık zeminde kullanılmaz (bağlantı `#9A5100`; taslaktaki `#c46a00` ölçümde krem zeminde 4,5'in altında kaldı, PR1). Bugünkü palet testleri yeni değerlerle güncellenir.
- Aralıklar `Layout` ölçüleriyle sabit: kenar ve kart içi 16, kartlar arası 12, bölümler arası 24, köşe 20.

## 7. Kararlar — kullanıcının seçmesi gerekenler

Her kararda önerim ilk sırada; gerekçe ve diğerlerinin neden seçilmediği altında.

**K1 — Kartın altındaki tek günlük cümleyi kim yazar?**
- **A (önerim): Kod, sabit kalıplardan.** Her araç için 3–6 kalıp ("Harcamanın üçte biri markete gitmiş", "haftada yaklaşık
  4.200 TL", "geçen aydan 6.072 TL fazla"); kod kalıbı seçer ve rakamı yerleştirir. Hızlı, her seferinde doğru, ölçüm gerekmez.
  Eksisi: zamanla tekdüze gelebilir.
- B: Model yazar, kod denetler (bugünkü açıklama yolu, tek cümle sınırı). Daha doğal; ama yavaş (~2 sn), bazen hatalı özet
  (bilinen sorun AI-10), cümle sayı denetiminden geçemezse düşer.
- C: Kalıp önce, uygun kalıp yoksa model. En esnek; iki yol test edilir.
- D: Cümle yok; yalnız kart. En hızlı; "günlük dil" isteği karşılanmaz.
Neden A: hedef pratiklik ve hız; rakamlı cümleyi kod zaten dört araçta yazıyor ve doğrulanabilir.

**K2 — Sekmeler**
- **A (önerim): Ana sayfa · Analiz · Profil.** Asistan sekmesi kalkar, asistan kayan sayfa olarak her sekmede altta.
- B: Dört sekme kalır; Asistan sekmesi kayan sayfadaki oturumun geçmişini tam ekran gösterir.
- **Kullanıcı kararı (13 Kasım 2026): B** — "alt barda asistan sekmesi kalsın". Asistan sekmesi ve kayan sayfa **aynı oturumu**
  gösterir (tek `ChatViewModel`, UI-07'de zaten `MainTabView`'da): panelde sorulan soru Asistan sekmesinde de görünür, sekmede
  sorulan da kayan sayfada. Sekmede balon yok, aynı B kartları alt alta.
- C: Ana sayfa · Profil; Analiz de kalkar (panel + kartlar yeterli).
Neden A: F1 seçildi, ayrı sohbet sekmesi "sohbet uygulaması" hissini geri getirir; Analiz'in grafikleri panelden "Tümü ›" ile
derinleşmek için hâlâ değerli. C'yi panel oturduktan sonra yeniden konuşabiliriz.

**K3 — İlk turda hangi araçlar kart alır?**
- **A (önerim): Altı sık araç** — harcama özeti, kategori dağılımı, kategori detayı (K4), işyeri harcaması, karşılaştırma,
  aylık seri; diğer dokuz araç ikinci turda (o zamana kadar bugünkü metin cevabı).
- B: On beş aracın hepsi tek seferde.
- C: Yalnız market örneğindeki araç (kategori detayı) + panel; geri kalanı sonra.
Neden A: kullanıcının günlük sorularının çoğu bu altı araçta; her kart kurucusu test + ölçüm ister, 15'i bir PR'a sığmaz.

**K4 — "Markete ne kadar gitti?" için kategori detayı**
Bugün market rakamı yalnız kategori dağılımının bir satırı; market içindeki mağazalar, markete özel 6 aylık seri ve en büyük
işlem hiçbir araçta yok (aylık seri aracı kategori filtresi almıyor; API alıyor).
- **A (önerim): Yeni salt okunur analiz + uç nokta + MCP aracı `get_category_spending(period, categoryCode)`**: toplam, önceki
  dönem, işyerleri, işlem sayısı, işlem başı ortalama, en büyük işlem, son 6 tam ay serisi — tek çağrı. (ANA-xx + API + MCP + OpenAPI.)
- B: Mevcut araçları birleştir: kategori dağılımı + aylık seri aracına `categoryCode` + işyeri dağılımına `categoryCode`; AI servisi
  birden çok araç çağırır (CHAT-04, bugün yok).
- C: Detaylı bölümde yalnız mevcut veriler (toplam, önceki ay); mağaza dağılımı ve seri sonra.
Neden A: tek araç = tek seçim, ölçümü kolay; çok araçlı tur (B) ayrı ve büyük bir iş; C taslaktaki kartın yarısını kaybeder.

**K5 — Panel satırına dokununca**
- **A (önerim): Kayan sayfa o soruyla açılır** (kod soruyu ve aracı kurar, model seçmez — bugünkü "önerilen soru" yolu).
- B: Analiz sekmesinde o kategoriye gider.
- C: Dokunulmaz, yalnız gösterir.
Neden A: "yazmadan etkileşim" isteğinin karşılığı; model seçmediği için hızlı ve hep doğru araç.

**K6 — Sistemin koyu modu**
- **A (önerim): Uygulama her zaman açık krem** (`.preferredColorScheme(.light)`).
- B: Koyu modda bir koyu krem/espresso varyantı (Z1) — her renk iki kez ölçülür.
Neden A: tek kimlik, yarı iş; koyu varyant istenirse sonra eklenir.

**K7 — Model ne zaman çalışır?**
- **A (önerim): Yalnız araç seçmek ve (K1-B/C seçilirse) tek cümle için.** Kart kurulan cevapta model açıklama yazmaz.
- B: Bugünkü gibi her cevapta açıklama + kart.
Neden A: hız (açıklama turu ~2 sn) ve uzun metin şikâyeti; açıklama metni `answer` alanında kodun kısa metni olarak kalır.

**K8 — Simge ve ad**
- **A (önerim): Bu planın dışında**, ayrı küçük iş (yeni palete uygun simge; "Compass/Kılavuz" adı kalır).
- B: Tema PR'ında simge de değişir.
Neden A: simge bir görsel kimlik kararı; tema PR'ını büyütmesin.

**K9 — PR sırası**
- **A (önerim):** PR1 tema (Z3, ölçülü palet, bütün ekranlar) → PR2 ana ekran paneli (H2, hesaplar
  ve işlemler Profil'e, satır dokunuşu) → PR3 kategori detayı arka uç (K4-A) → PR4 kart sözleşmesi arka uç (Python `cards.py`,
  .NET, OpenAPI; ilk altı araç) → PR5 iPhone kayan sayfa + kart şablonları + mod (Asistan sekmesi aynı oturumu kartlarla gösterir, K2-B) → PR6 kalan dokuz aracın
  kartı + ölçümler.
- B: Önce kart (PR3–5), sonra tema ve panel. Asistan önce iyileşir, ama yeni kartlar eski siyah temada yazılıp yeniden boyanır.
- C: Tek büyük PR. İncelemesi zor; kural "bir dal = bir iş".
Neden A: her PR kendi başına çalışır ve görünür bir şey getirir; tema önce gelirse sonraki her ekran bir kez yazılır.

## 8. Test ve ölçüm

- Python: her kart kurucusu için elle hesaplanmış beklenen kart (Murat Eylül market: 18.016,50; Ağustos 11.944,50; %50,84;
  BİM 6.216,00 · Migros Jet 5.317,50 · Migros Sanal Market 3.577,50 · A101 2.905,50; 6 ay Nis–Eyl 8.654,00 / 10.339,00 /
  12.896,00 / 16.078,50 / 11.944,50 / 18.016,50, ortalama 12.988,08, %38,7 üstünde; 8 işlem, ortalama 2.252,06; en büyük 3.476,00
  BİM 3 Eylül; toplam 53.388,39 içinde %33,7). Sayı denetimi kartın bütün `text` alanlarını tarar; kasıtlı bozma her PR'da.
- .NET: sözleşme testi (alan zorunlu/`null`), OpenAPI kopyası, `EndpointSweepTests` (K4-A uç noktası).
- iPhone: şablon başına görünüm modeli testleri (telefon hesap yapmıyor: yalnız `share`/`text` kullanılır), mod anahtarı, kayan sayfa
  durumu; simülatörde elle deneme (Murat).
- Ölçümler: araç seçimi (yeni araç K4 için vakalar), cevap doğruluğu (kart alanları), ifade testi; eşik: eski vakalarda bozulan yok.

## 9. Kurucuya bırakılanlar (benim kararlarım, düzeltilebilir)

- Kart blok türlerinin kesin alan adları ve Swift tarafındaki şablon bileşenlerinin adları.
- Kalıp cümlelerin metni (K1-A seçilirse; her kalıp test edilir, rakam yalnız sonuçtan).
- Panelin yükleniyor/boş/hata durumları: UI-06'daki ortak iskelet ve `StateMessage` aynen.
- Kayan sayfanın yüksekliği (yarım/tam ekran adımları) ve hareketi: `Motion` kuralları, "Hareketi azalt"ta hareket yok.
- Geri soru kartı (CHAT-10) kayan sayfada bugünkü bileşenle; yalnız renkler yeni palete uyar.

## 10. Bilinen sınırlar

- Taslak rakamları Murat'ın demo verisi; detaylı istatistiklerin (6 ay ortalaması, en büyük işlem) backend'de hesaplanması K4-A'ya bağlı.
- Birden çok banka ve yatırım ürün önerisi kapsam dışı (INV-02 havuzu ve izin konusu aynen geçerli).
- Kart sözleşmesi eklenince eski iPhone sürümü `card`'ı yok sayar ve metni gösterir (alan isteğe bağlı).
