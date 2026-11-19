# Murat demo verisi: 2,5 yıl, zengin, yalnız canlı veritabanı (DEMO-01)

Tarih: 19 Kasım 2026. Durum: tasarım, kullanıcı onayı bekliyor.

## Neden

Sunumun demo kişisi Murat (kullanıcı kararı, 19 Kasım 2026). Her araç ve her senaryo Murat'ın verisinde net, çeşitli ve zengin
görünmeli; sonraki iki iş (rutin / rutin olmayan ayrımı + öneri; ana ekranda "yaklaşan rutin ödemeler" kartı) bu veriye dayanıyor.
Bugün Murat'ın 1 Ekim 2025 – 16 Kasım 2026 arası 277 işlemi var; taksit kavramı yok, düzenli ödemesi yalnız Netflix + Su, tasarruf
önerisi almıyor, döviz/altın fiyatı 6 Kasım'da bitiyor (bugün "ne olurdu" rakam vermez).

Bu iş üç parçalı serinin ilkidir:
1. **DEMO-01 (bu belge):** Murat'ın verisi.
2. Rutin ve temel / rutin olmayan ayrımı, işlem listesinde ve detayda "altın olsaydı" notu, rutin olmayanlara öneri (ayrı tasarım).
3. Ana ekranda gelir payı kartının yerine "yaklaşan rutin ödemeler" (kullanıcı kararı B; ayrı tasarım).

## Hedefler

- Murat'ın verisi **Mayıs 2024 – bugün** (≈ 30 ay), ayda ≈ 70–90 işlem; toplam ≈ 2.000+ işlem.
- Her araç Murat'la anlamlı ve çeşitli cevap verir (tablo aşağıda).
- Döviz ve altın fiyatları **1 Mayıs 2024 – 19 Kasım 2026** aralığında, yalnız canlı veritabanında.

## Hedef olmayanlar / kaçınılacaklar

- Sabit demo veritabanı (`bankingai_foundation`), seed'ler, testlerin fixture'ları ve model ölçümleri **değişmez** (yaklaşım A).
- Yeni tablo, kolon ya da migration yok. Taksit bilgisi yalnız işlem açıklamasında ve düzende durur.
- Diğer personaların hikâyesi değişmez; yalnız Murat'ın transferlerinin karşı bacakları onların canlı hesabına düşer.
- Kaçınılacak: bakiyenin herhangi bir anda eksiye düşmesi, aynı satırın iki kez yazılması, sabit veritabanına yazılması.

## Yaklaşımlar (seçilen: A)

- **A. Yalnız canlı veri (seçildi).** Murat'ın gün planı zenginleşir + yalnız canlıya yazılan "geçmiş ek". Sabit veritabanı ve
  ölçümler bozulmaz; yedek, sunumdan önce canlı veritabanının dökümüdür (`pg_dump`).
- B. Sabit seed de değişir: en tutarlı, ama Murat'ın elle hesaplanmış rakamları, cevap doğruluğu ölçümü ve belgeler yeniden;
  uygulanmış demo kaydı değişir (kural buna karşı). Sunuma katkısı yok.
- C. Yeni persona: kullanıcı Murat'ı seçti, hikâyesi kurulu.

## Tasarım

### Nereye yazılır

- Yeni `src/BankingAI.Data/Simulation/MuratHistory.cs`: Murat'ın **sabit verinin bittiği ana (`RealisticCutoff`) kadarki** ek satırları
  (Mayıs 2024 – Eylül 2025 tamamı + Ekim 2025 – 10 Ekim 2026 arasına eklenen taksit/abonelik/rutin dışı satırlar). `PlannedTransaction`
  kullanır; kimlikler bugünkü gibi anahtardan türetilir (sabit, tekrar çalıştırmada aynı).
- `DayPlan.MuratDay()` zenginleşir: 11 Ekim 2026'dan sonrası (taksitlerin devamı, yeni abonelikler, rutin dışı çeşitlilik, transferler).
- `LiveSimulator.CatchUpAsync` plana `MuratHistory`'yi de katar. Aynı yazıcılar, aynı "yalnız canlı" kontrolü, aynı "zaten var" sayımı.
  Yeni komut yok: `simulate catch-up` hepsini yazar.

### Murat'ın hikâyesi (çerçeve)

| Konu | İçerik | Beslediği araç / senaryo |
|---|---|---|
| Maaş | Mayıs–Aralık 2024 68.000; 2025 Ocak zam → 76.000; Ekim 2025'ten 85.000 (mevcut). Aralık 2024 ve 2025'te ikramiye | gelir, "maaşım yattı mı", gelir payı, zam görünür |
| Taksitler | Laptop 6 × 3.000 (Haz–Kas 2024, **bitmiş**); telefon 12 × 4.250 (Haz 2026 – May 2027); mobilya 9 × 2.800 (Ağu 2026 – Nis 2027). Açıklama `TAKSIT 03/12` biçiminde | rutin ödemeler (taksit), yaklaşan ödemeler, bitmiş taksit |
| Abonelikler | Netflix (mevcut), Spotify Aile, iCloud, spor salonu; Disney+ Mart 2025'te **iptal** | düzenli ödemeler, iptal edilen artık rutin değil |
| Faturalar | Su, internet (Ağustos zammı mevcut), doğalgaz ve elektrik mevsimlik, cep telefonu faturası; 2024–2025 aynı düzen | rutin fatura, mevsimsellik, karşılaştırma |
| Temel | Market (Migros Jet / Migros Sanal Market / A101 / BİM), akaryakıt, lokanta, kahve, çocuk doktoru, eczane | kategori, işyeri, "Migros" geri sorusu, bütçe |
| Rutin olmayan, isteğe bağlı | Trendyol, Hepsiburada, Zara, çocuk giyim, oyuncakçı, D&R, konser (Biletix), sinema; her yaz otel tatili, Kasım indirimleri | en büyük harcamalar, tasarruf önerisi, "harcamasaydım", "altın olsaydı" notu |
| Öneri tetiklenir | Ekim 2026'da (bugünün "geçen ayı") isteğe bağlı harcamada belirgin artış (konser + Trendyol + giyim) → en az bir, tercihen iki tasarruf önerisi | gelir payı ve öneri, "önerdiğin tutarı biriktirseydim" |
| Transferler | Yeğeni Can'a ayda 2.500 harçlık (giden); Selin'den bir kez 3.000 borç geri ödemesi (gelen); Hasan'ın dükkânından alışveriş (mevcut) | transfer aracı, yön ve kişi |
| Belirli günler | 5 Eylül 2026 (okul açılışı: kırtasiye, çocuk giyim, market), bazı cumartesiler birden çok işlem | tarihli sorular ("5 Eylül'de ne harcadım?") |
| Bütçe | Kasım 2026 market harcaması aylık ortalamaya yakın | "market bütçem 8.000" senaryosu |

Tutarlar sabit tohumlu üreticiyle (bugünkü `Rng(Murat)` kalıbı) dağıtılır; hikâye satırları elle yazılır.

### Market verisi

- `market download tcmb` ve `market download goldapi` ile **1 Mayıs 2024 – 19 Kasım 2026** indirilir (dış kaynak: TCMB, gold-api;
  anahtar `.env`'de). `market import` **yalnız canlı veritabanına** yapılır; sabit veritabanının 1.485 satırı değişmez.
- Altın, bugünkü gibi ons × USDTRY ÷ 31,1; sıçrama kontrolleri aynı.

### Doğrulama

- Veritabanısız testler (`Data.Tests`, `DayPlan`/`MuratHistory`): kimlikler tekrarsız ve sabit; Murat'ın sıralı bakiyesi hiçbir anda
  eksiye düşmüyor (sabit veri + ek birlikte); taksitlerin sayısı ve numaraları doğru; 19 Kasım 2026 itibarıyla düzenli ödeme kuralı telefon
  ve mobilya taksitlerini, Spotify/iCloud/spor salonunu tanıyor, laptop ve Disney+'ı tanımıyor; Ekim 2026'da öneri kuralı tetikleniyor;
  bütün satırlar sabit verinin kimlik aralığıyla çakışmıyor.
- Kasıtlı bozma: bakiye kontrolü, taksit sayısı, kimlik türetme.
- **Canlıya yazmadan önce:** kuru çalıştırma sayıları kullanıcıya gösterilir, onay alınır (kalıcı demo verisi). Yazımdan sonra tekrar
  çalıştırma 0 yeni satır yazmalı; sabit veritabanının satır sayıları önce/sonra aynı.
- Canlı zincirde (`scripts/run-live.sh`) her araç için bir Murat sorusu çalıştırılıp rakamlar SQL ile karşılaştırılır; sonuç tablosu
  PR'a.

### Sunum yedeği

Yazımdan ve kontrolden sonra `pg_dump` ile canlı veritabanının dökümü (repo dışında, yerel). Sunum sırasında sorun olursa geri yüklenir.

## Yapımcıya bırakılanlar (benim kararlarım; kullanıcı düzeltebilir)

- Ekim 2025'teki "devreden bakiye" (OpeningFunding) satırı değişmez (sabit veri); geçmiş ek o tarihe kadar neredeyse sıfırlanacak biçimde
  kurulur (maaşın çoğu harcanır), böylece bakiye hikâyesi bozulmaz ve bugünkü bakiye makul kalır.
- Taksit kategorisi ürüne göre (telefon/laptop/mobilya → `OTHER`); rutin sayılması kategoriden değil düzenden gelir.
- Can ve Selin'in canlı bakiyeleri transferlerle değişir (yalnız canlı).
- Tam tutar listeleri ve tohum değerleri.
