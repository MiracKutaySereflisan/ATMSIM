# Varsayımlar

Alan gerçeğini basitleştirdiğimiz her yer, muhtemel etkisiyle birlikte buraya yazılır.
Kodda karşılığı `// ASSUMPTION:` yorumudur.

| # | Varsayım | Neden basitleştirdik | Muhtemel etkisi |
|---|---|---|---|
| V-01 | Mesajlar JSON metni olarak taşınır | Gerçek ödeme ağları ikili, bit haritalı format kullanır. JSON daha çok yer kaplar ve daha yavaştır. | Ölçtüğümüz şey hız değil para korunumu. Etki: performans rakamları gerçek bir sistemle kıyaslanamaz — ve zaten kıyaslanmayacak. KARAR-007 |
| V-02 | PIN mesajda açık taşınır | Şifreleme ve HSM kapsam dışı (kural §2 (docs/proje-kurallari.md)). | Gerçek bir sistemde bu kabul edilemez. Raporda bilinçli eksik olarak beyan edilir. Kartlar uydurma olduğu için simülasyonda korunacak gerçek bir sır yok. KARAR-010 |
| V-03 | Tek ATM, tek host | Çok ATM ve çok bankalı yönlendirme kapsam dışı. | `terminal` alanı yine de bugün konuldu; sonradan eklenirse bütün geçmiş kayıtların kimliği değişirdi. KARAR-009 |
| V-04 | İş gününü terminal bildirir, host aynen kabul eder | İki tarafın kesim anını bağımsız hesaplaması, kesime düşen işlemin iki farklı güne yazılması demek. | Gerçekte kesimi kimin başlattığı bilinmiyor; açık soru olarak bırakıldı. |
| V-05 | Sanal saat kullanılır, testlerde gerçek bekleme yok | Determinizm mutlak (kural §5 (docs/proje-kurallari.md)): aynı seed aynı sonucu vermeli. | Gerçek zamanlama yarışları (race condition) bu modelde görünmez. Bulduğumuz her arıza, modellediğimiz arızadır — modellemediğimiz değil. |
| V-06 | Sahte hat mesajları **sırasında ve tek kopya** taşır | Gerçek ağlar mesajları yeniden sıralayabilir ve çoğaltabilir. Elli koşuda bir tekrarlanan bir senaryo, senaryo değildir (kural §5 (docs/proje-kurallari.md)). | Ağ kaynaklı çift mesaj ve sıra bozulması bu modelde kendiliğinden çıkmaz. Bu projede çift istek **bilerek** üretilir (protokol §3 tekrar deneme yolları), şansa bırakılmaz. Etkisi: "ağ mesajı iki kez teslim etti" sınıfı bir arıza kataloğa şansa değil, senaryoyla girer. KARAR-016 |
| V-07 | Dört kaset ve adetleri (200x500, 100x1000, 50x1000, 20x500) bizim seçimimiz | Gerçek bir makinenin yüklemesi bankanın nakit yönetimine aittir ve kurum içi bilgidir (kural §2 (docs/proje-kurallari.md)). | Kupür senaryolarının zorluğu bu yüklemeye bağlı. Farklı bir yükleme farklı "verilemez" tutarlar üretirdi; kod hiçbir yerde bu rakamlara bağlı değil, `CassetteSet.Standard()` dışında geçmiyor. |
| V-08 | Kaset kapasitesi (bir kasete en fazla kaç banknot sığdığı) modellenmedi | Kapasite yalnızca para **yatırma** yolunda anlam kazanır; o yol Faz 3'te yazılacak. | Bugün hiçbir şeyi bozmuyor. Faz 3'te "geri dönüşüm kaseti doldu" senaryosu yazılacaksa kapasite o zaman eklenir; eklenmezse o senaryo kataloğa "kapatılamayan" olarak girer. |
| V-09 | Yetkilendirme **bloke** koyar, defteri dağıtım bildirimi işler (iki mesajlı) | Gerçek ATM ağlarının büyük kısmı tek mesajlıdır ve yetkilendirmede defteri borçlandırır. | Bilinçli sapma. Kazancı: dağıtım bildiriminin gerçek bir işi olur ve ölçülebilir bir arıza sınıfı (asılı bloke) doğar. Bedeli: o sınıf **bizim modelimizin eseridir**, raporda öyle işaretlenir. Ayrıntı aşağıda, KARAR-029, soru 15 |

## Bilinçli eksikler (kapsam dışı, §4)
- Veritabanı yok — hesaplar bellekte, defter ve ters kayıt kuyruğu diske eklemeli dosyada.
  Kalıcılık `IAccountStore` / `IJournal` arayüzlerinin arkasında; ileride veritabanı
  gerekirse iş mantığına dokunmadan takılır (KARAR-013). Elde etmediğimiz şeyler:
  gerçek işlem (transaction) güvencesi, eşzamanlı erişim, çoğaltma, denetim izi.
- Şifreleme, anahtar yönetimi, HSM yok. Gerçek bir ATM'de PIN bloğu şifreli taşınır;
  biz taşımıyoruz. Bu raporda bilinçli eksik olarak yazılır, gizlenmez.
- Gerçek XFS / donanım katmanı yok; dispenser sahtedir.
- Dördüncü işlem, kartsız akış, çok ATM, çok bankalı yönlendirme yok.

## Faz 1g — PIN, mühürlü tuş takımı yerine tuş olayı olarak taşınıyor

**Basitleştirme:** Gerçek bir ATM'de PIN, şifreleyen bir tuş takımına (encrypting PIN pad,
EPP) girilir. O tuş takımı bir bilgisayar parçası değil, kendi başına mühürlü bir cihazdır
ve hanelerin kendisini dışarı hiç vermez; yalnızca şifrelenmiş bir blok verir. Bizim
modelimizde haneler, tarayıcıdan terminale **tuş olayı** olarak (`{"event":"key",
"value":"5"}`), yerel bir bağlantı üzerinde gidiyor.

**Muhtemel etkisi:** Gerçek bir sistemde "PIN nerede açık hâlde bulunur" sorusunun cevabı
"hiçbir yerde"dir; bizde cevabı "tarayıcı ile terminal arasındaki yerel hatta"dır. Bu, PIN
sızıntısıyla ilgili hiçbir arıza senaryosunun bu simülatörde anlamlı biçimde
çalıştırılamayacağı anlamına gelir. Böyle bir senaryo yazılırsa sonucu modelin eseri olur,
gerçeğin değil (kural §8 (docs/proje-kurallari.md)).

**Neden kabul edildi:** Şifreleme ve anahtar yönetimi bütün proje için kapsam dışıdır
(KARAR-010) ve raporda bilinçli bir eksik olarak yazılıdır. Bu basitleştirme o kararın
ekran tarafındaki sonucudur, ayrı bir taviz değil.

**Neyi bozmuyor:** "Tarayıcı karar vermez" iddiasını bozmuyor. Haneleri terminal sayar;
tarayıcıya yalnızca kaç yıldız çizeceği söylenir (KARAR-024).

---

## V-09 — Yetkilendirme defteri değil blokeyi hareket ettirir (iki mesajlı model)

**Basitleştirme değil, bilinçli sapma.** Gerçek ATM ağlarının büyük kısmı tek mesajlı
çalışır: ATM finansal isteği yollar, host onayladığı anda **defteri borçlandırır**, ve
işler yolundaysa ikinci bir mesaj yoktur. Bizim hostumuz iki mesajlı çalışır:
yetkilendirme bloke koyar, defter ancak dağıtım bildirimiyle hareket eder (KARAR-029).

**Muhtemel etkisi:** Bu seçim, gerçekte olmayan bir arıza sınıfı **üretir** — dağıtım
bildirimi kaybolduğunda ortada asılı kalmış bir bloke kalır. Faz 4'te bu sınıf
ölçüldüğünde, raporda "modelimizin ürettiği sınıf" olarak işaretlenecektir (kural §9.1 (docs/proje-kurallari.md)).
Tersi de doğru: tek mesajlı modelde görülen "cevabı kaybolan yetkilendirmede defter zaten
borçlanmıştır" durumu bizde daha yumuşaktır, çünkü defter henüz kıpırdamamıştır.

**Neden kabul edildi:** İki kazancı var. (1) `DispenseAdvice: FULL` mesajının gerçek bir
işi olur; defter yetkilendirmede borçlansaydı o mesaj hiçbir şeyi değiştirmezdi ve
gönderilmesinin sebebi kalmazdı. (2) Ölçülebilir, gerçek hayatta karşılığı olan bir arıza
sınıfı (asılı bloke) kataloğa girer — projenin ürünü bu katalogdur.

**Neyi bozmuyor:** Para korunumu iddiasını bozmuyor; yalnızca kovaların adını değiştiriyor.
"Hesap borçlandıysa ya nakit verildi ya ters kayıt üretildi" cümlesi, bizde "bloke konduysa
ya defter işlendi ya bloke çözüldü" hâline gelir ve aynı biçimde koşulabilir.

**Açık soru.**

## 2026-08-25 — Faz 2h

- **Makbuza kalan bakiye yazılmıyor.** Gerçek makbuzlar genellikle taşır ve host da dağıtım
  bildiriminin cevabında gönderiyor. **Etkisi:** basılan bakiye, kâğıdın üstündeki andan
  farklı bir anda okunmuş olurdu; yazmak yerine eksik bırakıldı.
- **Kart okuyucu tek bir kartı döndürüyor.** Gerçekte manyetik şerit/çip okunur.
  **Etkisi:** kart okuma hatası diye bir arıza sınıfı üretemiyoruz.
- **Zaman aşımı süreleri sabit 30 saniye.** Gerçek makinelerde ekran başına farklıdır ve
  bankanın ayarıdır. **Etkisi:** "müşteri yavaş" senaryoları tek bir sayıya bağlı.
- **Tarayıcı saniyede bir tick gönderiyor.** Sekmeyi arka plana alan bir tarayıcı bu
  aralığı uzatabilir. **Etkisi:** demoda ekran arka plandayken zaman aşımı gecikebilir;
  gerçek bir ATM'de böyle bir bağımlılık olmaz, orada saati makine tutar.

## 2026-08-25 — Faz 3

- **Banknot doğrulama yok.** Gerçek makineler yıpranmışlığa ve sahtelik şüphesine bakar;
  bizde bir banknot ya kabul edilir ya kupürü yüzünden reddedilir. **Etkisi:** "sahte
  banknot" diye bir arıza sınıfı üretemiyoruz.
- **Yatırılan para anında dağıtılabilir.** Gerçekte bu bir banka ayarıdır ve bekleme süresi
  olabilir. **Etkisi:** geri dönüşümün gecikmesinden doğan kaset doluluk senaryoları yok.
- **Sıkışan paranın iade süreci modellenmedi.** Hesap hiç artmıyor ve para kendi kovasında
  sayılıyor; oradan sonrası (müşteriye iade) kapsam dışı. Soru 16 olarak yazıldı.
- **Kasete alma sırasında elektrik kesintisi kapatılamıyor.** Niyet önce yazılarak
  **görünür** kılındı (KARAR-041), ama pencere kapanmadı. Gerçek makinelerin açılışta ne
  yaptığını bilmiyoruz; soru 17.
- **İade edilen banknotun alınmaması modellenmedi.** Escrow'daki para iade edildiğinde
  makine, müşterinin onu aldığını varsayar. Gerçek makine almazsa geri çeker ve üçüncü bir
  kovaya koyar (çekimdeki `retract`'in yatırma karşılığı). **Etkisi:** iade yolunda
  kapatılamayan bir dengesizlik oluşamaz, çünkü para hep "müşteride" sayılır — yani bu
  varsayım denetleyiciyi bu tek noktada olduğundan iyimser gösterir. KARAR-042'de yazılı,
  Faz 4'te arıza enjeksiyonuyla kapatılacak.
- **Host'un yatırmayı reddetmesi ekrandan tetiklenemiyor.** Demo host yalnızca tanınmayan
  kartta reddediyor, tanınmayan kart da PIN'i geçemediği için menüye ulaşamıyor. Akış
  düzeyinde `DepositTests.cs` kapsıyor; ekran düzeyinde Faz 4'ün arıza enjeksiyonunu
  bekliyor. **Etkisi:** ekran tarafında bu dalın metni hiç görülmedi.

## 2026-08-25 — Faz 3e (gün sonu kesimi)

- **Kesimi başlatan bir saat yok.** Gün sonu kesimi çağrıldığında oluyor, kendiliğinden
  değil. Gerçek bir ATM'de kesim saati bir ayardır ve makine o saatte kendi başlatır.
  **Etkisi:** "operatör kesimi çalıştırmayı unuttu" ve "kesim saati iki taraf arasında
  farklı ayarlanmış" diye iki gerçek arıza sınıfı üretemiyoruz. İkisi de gerçek mutabakat
  farkı kaynağıdır.
- **Açık kalem varken gün hiç kapanmıyor.** Gerçek bankacılıkta gün kapanır ve çözülememiş
  kalem bir **askı hesabına** (suspense) düşüp ertesi gün elle çözülür. Biz kapanmayı
  reddediyoruz. **Etkisi:** askı hesabını, kalemin yaşlanmasını ve elle çözülmesini
  modellemiyoruz; buna karşılık "kapanmış bir güne geç bildirim düştü" senaryosu bizde
  yapısal olarak imkânsız — yani modelimiz bu noktada gerçekten **daha katı**, ama
  gerçekten **daha dar**. Açık soru olarak bırakıldı.
- **Kesim tek terminal içindir.** Gerçekte bir host binlerce terminalin kesimini ayrı ayrı
  ve gün sonunda toplu olarak yapar; araya takas ve kurumlar arası mutabakat girer.
  **Etkisi:** "bir terminal kapandı, öteki kapanmadı" ve kurumlar arası fark senaryoları
  kapsam dışı (kural §3 (docs/proje-kurallari.md), çok ATM).
- **Nakit sayım farkı yok.** Kesimde karşılaştırılan iki kayıt da yazılımın kaydı. Gerçek
  bir kesimde üçüncü bir sayı vardır: kaseti açıp sayan insanın saydığı. **Etkisi:**
  modelimiz sistematik olarak iyimser — gerçek hayatta bulunan farkların bir kısmı hiç
  yazılımdan kaynaklanmaz.

## 2026-08-25 — Faz 3f (hesapların kalıcı olması)

- **Defter hiç kesilmiyor.** Bakiyeler açılış listesinden itibaren **bütün** defter
  oynatılarak yeniden üretiliyor; yani defterin ilk satırı dünyanın başlangıcı sayılıyor.
  Gerçek bir bankada defter gün sonunda arşivlenir ve ertesi gün açılış bakiyesi bir
  dosyadan gelir. **Etkisi:** defter büyüdükçe açılış kontrolü yavaşlar, ve arşivlenmiş bir
  defterle çalışmayı modellemiyoruz. `./scripts/demo.sh --yeni-gun` arşivleme yerine geçen
  tek şey.
- **Tekrar tablosu (hangi cevabı verdim) yeniden başlatmayı atlatmıyor.** Cevapların
  kendisi deftere yazılmıyor, yalnızca ne karar verdikleri yazılıyor. **Etkisi:** yeniden
  başlatılmış bir host, tekrarlanan bir PIN veya bakiye sorusuna birebir aynı cevabı
  veremez — yeniden hesaplar. Para hareketi olan mesajlar bundan etkilenmiyor: ikinci kez
  gelen bir dağıtım bildirimi açık yetkilendirme bulamayıp reddediliyor
  (`AnAdviceRepeatedAcrossARestartStillMovesTheLedgerOnce`).
- **Sıkışan para sayacı kalıcı değil.** `JammedInDeposits` bellekte; yeniden başlatmada
  sıfırlanıyor. **Etkisi:** denetleyici yalnızca tek bir süreç ömrü içinde koşturulduğu
  için bugün bir şeyi bozmuyor, ama host'un yeniden başlatıldığı bir gün için sıkışan para
  toplamı eksik çıkar.
- **Hesap dosyası tek bir makine içindir.** İki host aynı dosyayı aynı anda açarsa
  sonuncusu ötekini ezer; kilit yok. **Etkisi:** çok host senaryosu kapsam dışı (kural §3 (docs/proje-kurallari.md)).
- **Kart numarası hesap dosyasında açık duruyor.** Defterde ve her kayıtta maskeli, ama
  hesap dosyasında tam — çünkü hesabı karttan bulmak gerekiyor ve maskeli numara aranamaz.
  Gerçek bir bankada bu arama şifreli ya da tokenlanmış bir depodan geçer; şifreleme kapsam
  dışı (KARAR-010, kural §2 (docs/proje-kurallari.md)). **Etkisi:** bu dosyadaki numaralar uydurma ve Luhn-geçerli
  test numaralarıdır, gerçek hiçbir karta karşılık gelmez — basitleştirme yalnızca bu
  yüzden karşılanabilir.

## 2026-08-25 — Faz 5a (demo arıza tetikleyicisi)

- **Kesik hat anında cevapsız döner, zaman aşımı süresini beklemez.** Gerçekte ölü bir soket
  de anında hata verir, ama "cevap yutulan" durumda gerçek makine 30 saniye bekler. Biz
  beklemiyoruz. **Etkisi:** sunumda geri sayımın dolmasını izlemek yerine sonucu hemen
  görüyoruz; zaman aşımının **süresi** demoda görünmüyor, yalnızca sonucu görünüyor.
  Senaryo koşucusunda süre sanal saatle tam olarak modelleniyor, yani ölçüm bu
  basitleştirmeden etkilenmiyor.
- **Servis paneli gizli ama korumasız.** Tuş kombinasyonunu bilen herkes açabilir; şifre,
  yetki ya da kilit yok. Gerçek bir ATM'nin servis moduna fiziksel anahtarla girilir.
  **Etkisi:** bu ekran yalnızca sunum ve geliştirme içindir; bir ağa açılan bir makinede
  böyle bir panel bulunmamalıdır ve raporda böyle yazılıdır.
- **Demo arızaları senaryo koşucusuna dâhil değildir.** İkisi ayrı yollardır: senaryolar
  deterministik ve sanal saatlidir, demo gerçek saatte insan hızında akar. **Etkisi:** demoda
  gösterilen bir arızanın ölçülmüş karşılığı senaryolarda ayrıca vardır; demo bir ölçüm
  aracı değildir.


---

## V-10 — Bekleyen mesaj kuyruğunun arka plan pompası yok

**Basitleştirme:** Kuyruk, arka planda çalışan bir zamanlayıcıyla değil, bir sonraki işlem
sırasında boşalır (`WithdrawalFlow`, bekleyen mesajları işleyen döngü). Açılıp kimse
kullanmadan bekleyen bir makine, kuyruğundaki ters kaydı göndermez.

**Muhtemel etkisi:** Ölçümleri değiştirmez — senaryo koşucusu ve testler işlem üreterek
ilerlediği için kuyruk her koşuda boşalır. Etkisi gerçek bir kullanımdadır: sessiz bir
makinede ters kayıt, ilk müşteriye kadar bekler. Gerçek bir ATM kuyruğunu arka planda
sürer.

**Neden böyle bırakıldı:** Zamanlayıcı eklemek, bu projede bilinçli olarak yok edilen
şeyi — testlerde gerçek zaman geçişini — geri getirir. Sanal saatle sürülen bir arka plan
görevi yazılabilirdi; kapsam gereği yazılmadı ve eksikliği burada yazılıdır.
