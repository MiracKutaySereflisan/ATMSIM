# Proje kuralları

Bu depodaki kod ve dokümanlar yer yer "kural §4.1", "kural §5" gibi numaralara atıf yapar.
Numaralar bu dosyaya aittir. İki grup var: **alan gerçekleri** (§3, §4) — ATM ve ödeme
alanının, bu proje ne yaparsa yapsın değişmeyen gerçekleri — ve **çalışma kuralları**
(§5–§9) — bu projede nasıl çalışıldığına dair kararlar.

Bu dosya bir referanstır, bir gerekçe listesi değildir: her kuralın kodda nerede karşılık
bulduğu, o kuralı uygulayan dosyanın kendi açıklama bloğunda yazılıdır.

---

## §3 — Merkezî iddia

Projenin taşıdığı iddia şudur ve `src/Atm.Audit/ConservationChecker.cs` içinde koşulabilir
hâldedir:

```
hesap borçlandıysa → ya nakit müşteriye ulaştı ya da bir ters kayıt var
hesap alacaklandıysa → arkasındaki banknotlar kasete girdi
makineden nakit çıktıysa → ona karşılık gelen onaylanmış bir yetkilendirme var
kasetten eksilen = müşteriye verilen + geri alınan + ağızda bekleyen
host defteri = terminal günlüğü   (gün sonu mutabakatında)
```

---

## §4 — Alan gerçekleri

Bunlar bu projenin icat ettiği kurallar değildir; ATM ve ödeme sistemlerinin gerçekleridir.
Her biri `src/Atm.Protocol/Hardening.cs` içinde tek tek kapatılabilen bir anahtara karşılık
gelir; kapatıldığında ne olduğu `./scripts/run-scenarios.sh` çıktısında ölçülür.

**§4.1 — Zaman aşımı ret değildir.** Cevap gelmemesi "işlem olmadı" demek değildir; işlem
merkezde gerçekleşmiş olabilir. Doğru davranış, işlemi geri alacak bir ters kayıt
üretmektir. Alanın bir numaralı hata kaynağı budur.

**§4.2 — Ters kayıt da kaybolabilir.** Gönderildi diye ulaştı sayılmaz; onay gelene kadar
artan aralıklarla tekrar gönderilir.

**§4.3 — Kısmi dağıtım gerçektir.** Makine yetkilendirilenden azını verebilir. Gereken tam
iptal değil, farkın olduğu gibi bildirilmesidir.

**§4.4 — Verilmek ile alınmak farklıdır.** Nakit ağza gelir, müşteri almaz, makine geri
alır. Geri alınan para ne müşteridedir ne kasettedir; üçüncü bir kovadadır.

**§4.5 — Aynı istek iki kez gelebilir.** İşlem kimliği eşleşmesi olmadan yeniden deneme,
çift borçlanmadır. Kimlik üç alandan oluşur: terminal + iş günü + işlem numarası. Bir
tekrarın gerçekten tekrar olması için isteğin de aynı olması gerekir; aynı kimlik altında
gelen farklı bir istek, tekrar değil çakışmadır ve reddedilir.

**§4.6 — Kupür kısıtı yetkilendirmeden ÖNCE bakılır.** Elindeki kupürlerle
oluşturulamayan bir tutar merkeze hiç sorulmaz; sonradan fark etmek, iptal edilecek bir
borçlanma üretir.

**§4.7 — Yatırılan para önce ara kasada (escrow) bekler.** Müşteri onaylayana kadar hesaba
geçmez ve iade edilebilir. Yalnızca kasete ulaşan para hesaba yazılır.

**§4.8 — Onay anı ile hesabın işlendiği an arasında hat kopabilir.** Bu yüzden sıralama
kararı gerekir: önce kasete al, sonra merkeze bildir. Kasete girmiş bir banknot geri
alınamaz, ama bir mesaj sonsuza kadar tekrar gönderilebilir.

**§4.9 — Gün sonu kesimi bir tarih değil, bir andır.** İş günü kendiliğinden dönmez; iki
tarafın da onayıyla ilerler. Kesimin iki yanına düşen işlem yanlış iş gününe yazılır ve
mutabakatta "sebepsiz fark" olarak görünür.

**§4.10 — Terminal durumu ile host durumu ayrı ayrı bozulur.** Tek doğruluk kaynağı
yoktur; mutabakat tam olarak bu yüzden vardır.

---

## §5 — Determinizm

Sanal saat, tohumlu rastgelelik, testlerde gerçek bekleme yok. Her senaryo `tohum + senaryo
kimliği`nden birebir yeniden üretilir. Aynı komut aynı sonucu vermiyorsa sonuç yoktur.
Sınaması kolaydır: `./scripts/check.sh 7` iki kez koşulur, çıktılar birebir aynı olmalıdır.

---

## §6a — Dış dünya kenarda durur

İş mantığı; gerçek bir soket, gerçek bir saat veya gerçek bir dosya olmadan çalışabilmelidir.
Dış dünya arayüz olarak içeri verilir: `IClock`, `ITransport`, `ICashDispenser`,
`ITerminalJournal`, `IAccountStore`. Bu bir süsleme değil, projenin çalışma şartıdır: hattı
tam istenen milisaniyede koparabilmenin tek yolu, o hattın yerine sahtesini koyabilmektir.

## §6b — Değişmezlik ihlali yüksek sesle hata verir

Sessiz onarım yoktur. Tutarsızlık bulan kod düzeltmez; sayar, adlandırır ve durur. Sessizce
onaran bir denetim, hatanın kanıtını silerek temiz bir defter gösterir.

---

## §7 — Ekran gerçekliği

Ekran bir web formu gibi değil, bir ATM gibi görünür ve öyle kullanılır: kasa görüntüsü, yan
fonksiyon tuşları, kırmızı İPTAL / sarı DÜZELT / yeşil GİRİŞ tuş takımı, ayrı kart, nakit,
yatırma ve makbuz yuvaları, kart ve nakit animasyonları, geri sayımlı zaman aşımı, ATM
diliyle hata mesajları. Tek HTML dosyasıdır ve hiçbir dış kaynak çağırmaz: sunum salonunda
internet olmayabilir. Ekranda hiçbir iş mantığı durmaz; ekran karar vermez, gösterir.

---

## §9 — Kendi sonucuna saldır

Bir bulgu kayda geçmeden önce üç soru sorulur:

1. Bu, gerçek dünyada karşılığı olan bir olay mı, yoksa modelin kendi eseri mi?
2. Bu sonucu bir sahtekârlıkla üretebilir miydim? (Aşırı naif bir "önceki" akış, testi
   zayıflatarak geçirmek, eşleşmeyen senaryo kümeleri, ihlali sayan kodun kendisinin hatalı
   olması.) Kontrol edildi mi, kontrol edildiği yazıldı mı?
3. Bu işi uzun süredir yapan biri ilk hangi soruyu sorar?

Her düzeltmede sorulan soru: sistemi mi düzelttim, testi mi zayıflattım? Bu depoda üç kez
sorunun cevabı "ikisi de değil, testi hiç yazmamıştım" oldu ve eksik testler o zaman
yazıldı.
