# ATMSIM

ATM simülatörü — üç işlem, enjekte edilen arızalar, makineyle denetlenen para korunumu.

Üç işlem: bakiye sorgu, para çekme, para yatırma (geri dönüşümlü). Tarayıcıda gerçek bir
ATM gibi görünen ekran → terminal uygulaması → kalıcı TCP soketle bağlı host sunucu.
Asıl konu mutlu yol değil, arıza anları: hat kopması, zaman aşımı, çift istek, kısmi
dağıtım — ve her birinde paranın korunup korunmadığı.

## Ölçülen sonuç

Otuz arıza senaryosu veriden okunuyor ve aynı tohumla birebir yeniden üretiliyor. Aynı
senaryolar, aynı adımlar, aynı tohumlarla iki kez koşuluyor:

| | naif akış | sertleştirilmiş akış |
|---|---|---|
| açıklanamayan farkla biten senaryo | **21 / 30** | **0 / 30** |
| bunlardan ancak gün sonunda görülen | 10 | 0 |

"Naif akış" ikinci bir kod kopyası değildir: aynı program, alanın yedi kuralı tek tek
kapatılarak koşulur (`src/Atm.Protocol/Hardening.cs`). Kural kural dağılım
`./scripts/run-scenarios.sh` çıktısındadır.

## Nasıl çalıştırılır

```
./scripts/demo.sh              # host + terminal + tarayıcı, tek komut
./scripts/demo.sh --yeni-gun   # önceki günün kayıtlarını arşivler, temiz günle başlar
./scripts/demo.sh --kapat      # açık kalmış süreçleri kapatır

./scripts/build.sh             # bütün projeleri derler
./scripts/test.sh              # testleri koşar; yeşilse 0, kırmızıysa 1 döner
./scripts/run-scenarios.sh     # 30 arıza senaryosu + naif karşılaştırma
./scripts/check.sh [tohum]     # bir günü koşar, para korunumunu ve gün sonunu denetler
```

Gereken: .NET 10 SDK. Başka hiçbir bağımlılık yok — `NuGet.config` paket kaynaklarını
bilerek boşaltır, böylece derlemenin ağ erişimine ihtiyacı olmadığı kanıtlanır.

Demo değerleri: kart `4111 1111 1111 1111`, PIN `1234`, bakiye 2.500,00 TL.

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
| `tests/Atm.Tests/` | Testler ve kendi koşucusu |
| `scenarios/` | 30 arıza senaryosu (JSON) |
| `scripts/` | Derleme, test, senaryo, denetim ve demo betikleri |

Okumaya nereden başlamalı: `src/Atm.Terminal/WithdrawalFlow.cs` (bir çekimin dört adımı) →
`src/Atm.Audit/ConservationChecker.cs` (iddianın koşulabilir hâli) →
`src/Atm.Protocol/MessageCodec.cs` (iki makinenin konuştuğu biçim).

## Sınırlar

Bu depoda gerçek banka verisi, gerçek kart/PIN, kurum logosu ve kurum ekran metni yoktur.
Ekranda uydurma bir banka adı kullanılır. Kart numaraları üretilmiştir, Luhn-geçerlidir
ve hiçbir gerçek karta karşılık gelmez.

Kapsam dışı bırakılanlar bilinçlidir: veritabanı (dosya ve bellek yeterli) · şifreleme ve
anahtar yönetimi · gerçek donanım (XFS) katmanı · dördüncü bir işlem · kartsız/karekod
akışı · çok ATM · çok bankalı yönlendirme · eşzamanlı müşteri. Mesaj biçimi ISO 8583
değildir; kamuya açık standartların genel mantığından esinlenen, bu proje için
uydurulmuş bir biçimdir.

Bilinen sınır: bekleyen mesaj kuyruğu arka planda bir zamanlayıcıyla değil, bir sonraki
işlem sırasında boşalır. Gerçek bir ATM kuyruğunu arka planda sürer; bu yapmıyor.

Bunlar eksik değil, sınırdır: sınırı yazılı olmayan bir çalışmanın ölçüsü de yoktur.

## Lisans

MIT — `LICENSE` dosyasına bak.
