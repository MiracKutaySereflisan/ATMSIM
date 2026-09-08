# Naif akış — hangi anahtar hangi alan gerçeğini kapatıyor

Bu proje bir simülatörün çalıştığını değil, **belirli kuralların parayı koruduğunu** iddia
ediyor. Böyle bir iddia ancak karşılaştırmayla ölçülür, ve karşılaştırma ancak **eşleşen
koşulda** okunur (kural §5 (docs/proje-kurallari.md)): aynı senaryo dosyaları, aynı seed'ler, aynı adımlar.

Bu yüzden naif akış ayrı bir kod kopyası değil. Tek bir program var; iki koşu arasındaki
tek fark, aşağıdaki yedi mantıksal anahtar. Kopya olsaydı, iki kopya zamanla birbirinden
kayardı ve karşılaştırma sessizce anlamını yitirirdi.

Anahtarların tanımı: `src/Atm.Protocol/Hardening.cs`. Varsayılan **hepsi açık** — çalışan
host ve terminal her zaman `Hardening.Full` kullanır; naif mod yalnızca bir ölçüm aletidir
ve demoda erişilebilir değildir.

---

## Yedi anahtar

| Anahtar | Alan gerçeği | Kapalıyken makine ne yapar |
|---|---|---|
| `TimeoutMakesReversal` | §4.1 — zaman aşımı ret değildir | Sessizliği "işlem olmadı" sayar; ters kayıt üretmez. Yatırmada tersi: sessizliği onay sayar ve banknotları kasete koyar. |
| `RetryUntilAcknowledged` | §4.2 — ters kayıt da kaybolabilir | Bildirimi ve ters kaydı bir kez gönderir; cevap gelmezse unutur. |
| `ReportPartialDispense` | §4.3 — kısmi dağıtım gerçektir | Ne çıktığına bakmadan, **yetkilendirilen tutarı** verdim diye bildirir. |
| `RetractIsNotHandedOver` | §4.4 — verilmek ile alınmak farklıdır | Geri çektiği parayı müşteri almış gibi bildirir. |
| `RepeatImmunity` | §4.5 — aynı istek iki kez gidebilir | Tekrar tablosunu **yalnızca işlem kimliğiyle** anahtarlar (mesaj tipini katmaz) ve ters kayıtla kapatılmış bir işlemi yeniden yetkilendirir. |
| `NoteCheckBeforeAuthorisation` | §4.6 — kupür kısıtı yetkilendirmeden önce bakılır | Kupür hesabı yapmadan hosta sorar; veremeyeceğini sözü verdikten sonra öğrenir. |
| `CreditWhatReachedADrawer` | §4.7 — yatırılan para önce ara kasada bekler | Hesaba **sayılanı** yazar; banknotun kasete girip girmediğine bakmaz. |

---

## Naif dallar gerçekten "birinin yazacağı" şeyler mi?

kural §9 (docs/proje-kurallari.md) bunu sormamızı istiyor, çünkü kasten kötü yazılmış bir karşılaştırma hiçbir şey
kanıtlamaz. Her dalın, o adımın **en basit makul** hâli olmasına dikkat edildi:

- Cevap gelmediğinde "olmadı" saymak — bir ağ çağrısı zaman aşımına uğradığında yazılımcının
  ilk refleksi budur.
- Bildirimi bir kez gönderip geçmek — "gönderdim" ile "ulaştı"yı ayırmayan her kod böyledir.
- Yetkilendirilen tutarı bildirmek — makinenin ne verdiğini ayrıca saymayan her kod böyledir.
- Sayılanı hesaba yazmak — "müşteri 500 lira attı, hesabına 500 lira geçsin" cümlesinin
  doğrudan kodu budur.

Hiçbiri "hata ekleyelim" diye yazılmadı; hepsi **eksik bilgiyle yazılmış doğru görünen kod**.
kural §4 (docs/proje-kurallari.md)'ün her maddesini bir tuzak olarak sayması da tam bu yüzden.

---

## Ölçüm nasıl okunur

`./scripts/run-scenarios.sh` iki tablo basar.

**Karşılaştırma tablosu** hepsi kapalı ile hepsi açık arasındadır. Sorusu şudur: *bu kuralları
hiç bilmeyen biri aynı simülatörü yazsaydı ne olurdu?*

**Kural kural tablosu** her anahtarı **tek başına** kapatır. Sorusu farklıdır: *hangi kural
neyi ayakta tutuyor?* İkinci tablo gereklidir, çünkü birincisi en çok senaryoyu bozan
anahtarın gölgesinde kalır — tekrar bağışıklığı tek başına kapatıldığında senaryoların
yarısından fazlası bozulur ve öteki altısı bedavaymış gibi görünür.

Sayılan şey **senaryo adedidir, para değil.** Bir senaryoda kaybolan tutarı tek bir sayıya
indirmek, aynı parayı birden fazla denetimin adlandırdığı durumlarda yanıltır.

Bir anahtarın karşısında `0` görülüyorsa üç ayrı ihtimal vardır ve rapor bunları ayırır:
senaryoların davranışı değişiyor ama defter dengesiz kalmıyor · o kuralı sınayan hiçbir
senaryo yok · gerçekten hiçbir gözlenebilir farkı yok. İlkini "kural gereksiz" diye okumak
hatadır; ikincisi kapsama açığıdır ve kapsama tablosunda görünür.

---

## Bu ölçümün bilinçli sınırları

- **Naif akış tek bir noktada gerçekçi değil:** gerçek bir naif geliştirici yedi kuralı da
  aynı anda bilmezdi ama başka, bizim modellemediğimiz hatalar da yapardı. Yani "hepsi
  kapalı" sütunu bir alt sınır değil, bir **örnektir**.
- **Senaryo kümesi kapsamayı belirler.** 61 anlamlı hücrenin hepsi dolu değil; boş hücreler
  adıyla raporlanıyor. Bir kuralın "az senaryoyu bozuyor" görünmesi, o kuralı sınayan
  senaryonun az olmasından da kaynaklanabilir.
- **Denetleyici de bizim.** Karşılaştırmanın iki tarafı da aynı denetleyiciyle ölçülüyor.
  Denetleyicinin kendisi ayrı sabotaj turlarıyla sınandı, ama bu,
  bağımsız bir üçüncü taraf denetimi değildir.
