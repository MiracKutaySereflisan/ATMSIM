# ATMSIM

ATM simülatörü — üç işlem, enjekte edilen arızalar, makineyle denetlenen para korunumu.

---

ATM simülatörü. Üç işlem: bakiye sorgu, para çekme, para yatırma (geri dönüşümlü).
Tarayıcıda gerçek bir ATM gibi görünen ekran → terminal uygulaması → kalıcı TCP soketle
bağlı host sunucu. Asıl konu mutlu yol değil, arıza anları: hat kopması, zaman aşımı,
çift istek, kısmi dağıtım — ve her birinde paranın korunup korunmadığı.

## Ölçülen sonuç

Otuz arıza senaryosu veriden okunuyor ve aynı tohumla birebir yeniden üretiliyor. Aynı
senaryolar, aynı adımlar, aynı tohumlarla iki kez koşuluyor:

| | naif akış | sertleştirilmiş akış |
|---|---|---|
| açıklanamayan farkla biten senaryo | **21 / 30** | **0 / 30** |
| bunlardan ancak gün sonunda görülen | 10 | 0 |
| hiç görülmeyen | 0 | 0 |

"Naif akış" ikinci bir kod kopyası değildir: aynı program, alanın yedi kuralı tek tek
kapatılarak koşulur (`src/Atm.Protocol/Hardening.cs`). Kural kural dağılım ve koşulmayan
hücreler `./scripts/run-scenarios.sh` çıktısındadır.

Kapsama: `işlem × arıza × an` tablosunun **28 / 61** anlamlı hücresi koşuldu; koşulmayan
33 hücre raporda adıyla listelenir. Kapsama, nereye baktığımızı söyler; ne kadar iyi
baktığımızı söylemez.

## Nasıl çalıştırılır

```
./scripts/demo.sh              # host + terminal + tarayıcı, tek komut
./scripts/demo.sh --yeni-gun   # önceki günün kayıtlarını arşivler, temiz günle başlar
./scripts/demo.sh --kapat      # açık kalmış süreçleri kapatır

./scripts/build.sh             # bütün projeleri derler
./scripts/test.sh              # 373 testi koşar; yeşilse 0, kırmızıysa 1 döner
./scripts/run-scenarios.sh     # 30 arıza senaryosu + naif karşılaştırma
./scripts/check.sh [tohum]     # bir günü koşar, para korunumunu ve gün sonunu denetler
```

Demo değerleri: kart `4111 1111 1111 1111`, PIN `1234`, bakiye 2.500,00 TL. Diğer demo
kartları `docs/kurulum.md` içinde.

Testler `dotnet test` ile koşmaz: test projesi kendi koşucusunu taşır (sıfır dış
bağımlılık kararı). Doğru komut `./scripts/test.sh`.

## Mimari

```
[Tarayıcı: ATM ekranı]   HTML/CSS/JS, tek dosya, dış kaynak çağırmaz.
        ↕ WebSocket      Sadece gösterir ve tuşu bildirir; karar vermez.
[Atm.Terminal]           Ekran akışı, çekim ve yatırma akışları, kaset ve kupür
        ↕                mantığı, sahte dispenser, işlem günlüğü, mesaj kuyruğu.
        ↕ kalıcı TCP soket, uzunluk önekli mesaj
[Atm.Host]               Hesaplar, PIN doğrulama, yetkilendirme, defter, kalıcılık.

[Atm.Audit]              İkisini de görür, ikisi de onu görmez: para korunumu
                         denetleyicisi, senaryo koşucusu, kapsama matrisi.
```

Kalıcı soket seçilme sebebi: projenin konusu hattın koptuğu, cevabın geciktiği anlardır.
HTTP bu anları kütüphanenin içinde saklar, soket gösterir.

## Hangi dosya nerede

| Klasör | İçinde ne var |
|---|---|
| `src/Atm.Protocol/` | Mesaj tipleri, zarf, çerçeveleme, saat ve hat arayüzleri, `Hardening.cs` |
| `src/Atm.Host/` | Hesaplar, PIN, yetkilendirme, defter, kalıcılık, açılış denetimi |
| `src/Atm.Terminal/` | Ekran akışı, çekim ve yatırma akışları, kaset, dispenser, WebSocket sunucu |
| `src/Atm.Terminal/wwwroot/` | ATM ekranı (tek HTML, gömülü CSS/JS) |
| `src/Atm.Audit/` | Para korunumu denetleyicisi, senaryo koşucusu, kapsama matrisi |
| `tests/Atm.Tests/` | 373 test ve kendi koşucusu |
| `scenarios/` | 30 arıza senaryosu (JSON) |
| `docs/` | Protokol sözleşmesi, model, senaryo biçimi, sözlükler, kurulum |
| `scripts/` | Derleme, test, senaryo, denetim ve demo betikleri |

Okumaya nereden başlamalı: `docs/protocol.md` (iki makinenin sözleşmesi) →
`src/Atm.Terminal/WithdrawalFlow.cs` (bir çekimin dört adımı, özellikle 204–236) →
`src/Atm.Audit/ConservationChecker.cs` (iddianın koşulabilir hâli).

## Sınırlar

Bu depoda gerçek banka verisi, gerçek kart/PIN, kurum logosu, kurum ekran metni ve kurum
içi doküman yoktur ve olmayacaktır. Ekranda uydurma bir banka adı kullanılır. Kart
numaraları üretilmiştir, Luhn-geçerlidir ve hiçbir gerçek karta karşılık gelmez.

Kapsam dışı bırakılanlar bilinçlidir: veritabanı (dosya ve bellek yeterli) · şifreleme ve
anahtar yönetimi · gerçek donanım (XFS) katmanı · dördüncü bir işlem (fatura, transfer,
döviz) · kartsız/karekod akışı · çok ATM · çok bankalı yönlendirme · eşzamanlı müşteri.
Mesaj biçimi ISO 8583 değildir; kamuya açık standartların genel mantığından esinlenen,
bu proje için uydurulmuş bir biçimdir ve `docs/protocol.md` başında böyle etiketlenir.

Bilinen sınır: bekleyen mesaj kuyruğu arka planda bir zamanlayıcıyla değil, bir sonraki
işlem sırasında boşalır. Gerçek bir ATM kuyruğunu arka planda sürer; bu yapmıyor.

Bunlar eksik değil, sınırdır: sınırı yazılı olmayan bir çalışmanın ölçüsü de yoktur.

## Lisans

MIT — `LICENSE` dosyasına bak.
