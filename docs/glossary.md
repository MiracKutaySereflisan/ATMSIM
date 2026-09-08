# Sözlük

Her terim bir cümleyle. Alan terimleri ve teknik terimler birlikte, alfabetik değil,
öğrenme sırasına göre.

## Alan terimleri (ATM / ödeme)

- **ATM:** Kart ve PIN ile kimlik doğrulayıp nakit veren, alan ve hesap bilgisi gösteren makine.
- **Host:** Bankanın merkezindeki, hesapların gerçek durumunu tutan ve işlemi onaylayan sunucu.
- **Terminal:** ATM'nin içindeki, ekranı ve para mekanizmasını yöneten bilgisayar.
- **Yetkilendirme (authorization):** Host'un "bu müşteri bu parayı çekebilir" cevabı; hesabı borçlandırır.
- **Ters kayıt (reversal):** Host'tan cevap gelmediğinde ya da para verilemediğinde, yapılmış olabilecek bir borçlandırmayı geri alan mesaj.
- **Kısmi dağıtım (partial dispense):** Makinenin yetkilendirilen tutardan azını verebilmesi.
- **Retract:** Nakit ağzına gelmiş ama müşterinin almadığı paranın makine tarafından geri çekilmesi.
- **Kaset (cassette):** ATM içinde tek bir kupürün (örneğin sadece 200 TL'lik) durduğu para kutusu.
- **Kupür:** Banknotun değeri (50, 100, 200 TL). "350 TL çek" isteği eldeki kupürlerle karşılanamayabilir.
- **Escrow (ara kasa):** Yatırılan paranın, müşteri onaylayana kadar beklediği ara bölme; henüz ne müşterinin ne bankanın.
- **Geri dönüşümlü (recycler) ATM:** Yatırılan paranın sayılıp, sonraki müşteriye çekim olarak verilebildiği ATM.
- **Gün sonu kesimi (cut-over):** İşlemlerin hangi iş gününe yazılacağını belirleyen an.
- **Mutabakat (reconciliation):** Terminal günlüğü ile host defterinin karşılaştırılıp farkların bulunması.
- **STAN:** Bir işlemi tekil olarak adlandıran sıra numarası; aynı isteğin iki kez işlenmesini engellemeye yarar.

## Teknik terimler

- **SDK:** Kod yazmak, derlemek ve çalıştırmak için gereken araç takımı.
- **Derleme (build):** Yazılan kaynak kodun çalışabilir programa çevrilmesi.
- **TCP soket:** İki program arasında açık kalan, iki yönlü veri hattı.
- **Kalıcı bağlantı:** Her istekte yeniden kurulmayan, açık tutulan bağlantı.
- **Uzunluk önekli çerçeveleme:** Her mesajın başına kaç bayt olduğunun yazılması; hattan akan bayt yığınının nerede bölüneceğini böyle biliriz.
- **WebSocket:** Tarayıcı ile sunucu arasında açık kalan iki yönlü bağlantı.
- **Arayüz (interface):** Bir parçanın "ne yapabildiğini" tanımlayan, "nasıl yaptığını" söylemeyen sözleşme; gerçeğin yerine sahtesini koyabilmemizi sağlar.
- **Değişmezlik (invariant):** Sistem ne yaparsa yapsın her zaman doğru kalması gereken cümle. Bizimki: para korunur.
- **İdempotency:** Aynı isteğin iki kez gelmesinin, bir kez gelmesiyle aynı sonucu vermesi.
- **Determinizm:** Aynı girdi ve aynı seed ile aynı sonucun birebir tekrar üretilebilmesi.
- **Seed:** Rastgeleliği tekrar üretilebilir kılan başlangıç sayısı.
- **xUnit:** C# için test çerçevesi; kodun beklendiği gibi çalıştığını makineyle kontrol eder.
- **Git:** Dosyaların her sürümünü saklayan kayıt sistemi; "commit" bir anlık kayıt demektir.

## Faz 0'da eklenen terimler (bölüm numaralarıyla)

| Terim | Bir cümleyle | İlk geçtiği bölüm |
|---|---|---|
| **Çerçeveleme (framing)** | TCP bir bayt akışıdır; mesajın nerede bittiğini söylemek göndericinin işidir. | B3 |
| **Uzunluk öneki (length prefix)** | Her mesajın başına kaç bayt olduğunu yazma yöntemi; çerçevelemenin en basit ve en sağlam biçimi. | B3 |
| **Zarf (envelope)** | Her mesajın taşıdığı ortak dış kısım: sürüm, tip, izleme numarası, tarih. | B3 |
| **STAN (izleme numarası)** | Terminalin her yeni işleme verdiği artan sayı; işlemin isteği, cevabı ve ters kaydı aynı STAN'ı taşır. | B3 |
| **Idempotency (tekrar bağışıklığı)** | Aynı isteğin iki kez ulaşmasının bir kez ulaşmasıyla aynı sonucu vermesi. | B3 |
| **İş günü (business date)** | Bankanın defterinde işlemin yazıldığı gün; takvim gününden farklı olabilir. | B3 |
| **Echo (canlılık kontrolü)** | Hattın gerçekten ayakta olduğunu anlamak için düzenli gönderilen küçük mesaj. | B3 |
| **Kaset (cassette)** | ATM'nin içinde tek bir kupürü tutan çekmece; her kupür ayrı kasettedir. | B4 |
| **Kupür (denomination)** | Bir banknotun değeri: 200, 100, 50, 20 TL. | B4 |
| **Escrow (ara kasa)** | Yatırılan paranın, müşteri onaylayana kadar beklediği ara bölme. | B4 |
| **Retract (geri alma)** | Nakit ağzına gelen ama alınmayan paranın makine tarafından geri çekilmesi. | B4 |
| **Defter (ledger)** | Hesabın bakiyesini değil, bakiyeyi oluşturan hareketlerin sırasını tutan kayıt. | B4 |
| **Kova (bucket)** | Paranın bulunabileceği sayılabilir yerlerden her biri: kaset, escrow, müşteri, geri alınan, sıkışan. | B4 |
| **Yetkilendirme (authorization)** | Hostun "bu para çekilebilir" cevabı; verildiği an hesabı borçlandırır. | B4 |
| **Kısmi dağıtım (partial dispense)** | Makinenin yetkilendirilenden azını vermesi; farkın düzeltilmesi gerekir, tam iptal değil. | B4 |
| **Mutabakat (reconciliation)** | İki tarafın kayıtlarını karşılaştırıp farkı arama işi. | B4 |
| **Test (birim testi)** | Kodun belirli bir davranışını makineyle kontrol eden küçük program. | B4 |
| **Test koşucusu (test runner)** | Testleri bulup sırayla çalıştıran ve sonucu raporlayan program. | B4 |
| **Paket (NuGet paketi)** | Başkasının yazdığı, projeye dışarıdan eklenen hazır kütüphane. | B4 |
| **Çözüm dosyası (solution)** | Birden çok projeyi tek komutla derlenecek şekilde bir arada tutan dosya. | B4 |
| **Arayüz (interface)** | "Bunu yapan şeyin şu yetenekleri olmalı" diyen sözleşme; kimin yaptığını söylemez. | B3 |
| **Sanal saat (virtual clock)** | Kendiliğinden ilerlemeyen, yalnızca söylendiğinde ilerleyen saat; testte bekleme yerine kullanılır. | B3 |
| **Kopma (sever)** | Kablonun çekilmesi: kimseye haber verilmez, iki uç da açık görünür, cevap hiç gelmez. | B3 |
| **Kapanma (close)** | Bir ucun nazikçe telefonu kapatması: o ucu kullanan kod hata alır. | B3 |
| **Sahte hat (in-memory link)** | Bellekte var olan, istediğimiz anda kesilebilen hat; arıza senaryolarının aracı. | B3 |
| **Determinizm** | Aynı girdinin her koşuda birebir aynı sonucu vermesi; bu projede sonucun geçerlilik şartı. | B3 |
| **Kupür planı (denomination plan)** | Bir tutarın hangi banknotlardan kaçar tane verileceğinin dökümü. | B7 |
| **Sınırlı bozuk para problemi (bounded coin change)** | "Şu değerlerdeki, şu adetlerdeki paralarla şu tutar tam olarak oluşturulabilir mi" sorusunun matematikteki adı. | B7 |
| **Açgözlü algoritma (greedy)** | Her adımda en büyüğünü seçen, geri dönmeyen yöntem; kupür seçiminde yanlış cevap üretir. | B7 |
| **Dinamik programlama** | Küçük soruların cevaplarını bir tabloda biriktirip büyük soruyu onlardan kuran yöntem. | B7 |
| **Defter bakiyesi (ledger balance)** | Hesabın geçmişinin toplamı; yalnızca gerçekleşmiş bir hareket onu değiştirir. | B7 |
| **Bloke (hold)** | Söz verilmiş ama henüz gitmemiş para. Kullanılabilir bakiye = defter bakiyesi − bloke. | B7 |
| **Yetkilendirme (authorisation)** | Hostun "bu tutar bu karttan verilebilir" cevabı; bizim modelimizde bloke koyar, defteri değiştirmez. | B7 |
| **Tek mesajlı / iki mesajlı model** | Yetkilendirmenin defteri hemen borçlandırdığı (tek) ya da blokede bırakıp bildirimi beklediği (iki) çalışma biçimi. | B7 |
| **Dağıtım bildirimi (dispense advice)** | Makinenin, nakde ne olduğunu hosta bildiren mesajı; defteri hareket ettiren şey budur. | B7 |
| **Geri alma (retract)** | Nakit ağzına gelen ama alınmayan paranın makine tarafından içeri çekilmesi; para üçüncü bir kovaya gider. | B7 |
| **Kısmi dağıtım (partial dispense)** | Yetkilendirilenden az para çıkması; doğru düzeltme tam iptal değil, farkın işlenmesidir. | B7 |
| **Asılı bloke (hanging hold)** | Kapanmamış bir yetkilendirmenin hesapta bıraktığı, hiçbir nakit hareketiyle karşılanmayan bloke. | B7 |
| **Nakit ağzı / Transit kovası** | Paranın çıkıp müşterinin alması beklenen yer; ne kasettedir ne müşterinin cebinde. | B7 |
| **Arıza enjeksiyonu (fault injection)** | Sistemin bozulma anını bilerek ve tekrarlanabilir biçimde üretmek. | B7 |
| **Ters kayıt (reversal)** | Hosta "az önceki yetkilendirmeyi geri al" diyen mesaj; olmadığını değil, olmuş olabileceğini varsayarak gönderilir. | B9 |
| **Geç ters kayıt (late reversal)** | Hat koptuğu için bekleyen, bağlantı gelince kuyruktan çıkıp giden ters kayıt. | B9 |
| **Beklenmeyen ters kayıt** | Zaten ödenmiş bir çekim için gelen ters kayıt; onaylanır, uygulanmaz, sayılır. | B9 |
| **Bekleme artışı (backoff)** | Cevapsız her denemeden sonra tekrar aralığının uzaması; çöken bir hostun makineyi de çökertmesini önler. | B9 |
| **Bekleyen mesaj kuyruğu** | Hostun henüz onaylamadığı, onay gelene kadar tekrar gönderilecek mesajların durduğu yer. | B9 |
| **Eklemeli kayıt (append-only)** | Yalnızca satır eklenen, yazılmış satırı değiştirmeyen kayıt biçimi. | B9 |
| **Geçici dosya + taşıma** | Dosyayı yerinde değil, yanında yazıp sonra taşıma; yarım dosya diye bir an bırakmaz. | B9 |

| **Para korunumu (money conservation)** | Paranın yaratılamayacağı ve yok edilemeyeceği kuralı; toplam değişmez, yalnızca yer değiştirir. | B10 |
| **Değişmez (invariant)** | Sistem ne yaparsa yapsın her zaman doğru kalması gereken cümle; bozulması hatanın kendisidir. | B10 |
| **Mutabakat (reconciliation)** | İki ayrı kaydın aynı olaylar hakkında aynı şeyi söyleyip söylemediğinin karşılaştırılması. | B10 |
| **Açıklanan fark (reconciling item)** | İki kayıt arasındaki, sebebi bilinen ve adı olan fark; alarm üretmez, listelenir. | B10 |
| **Açıklanamayan fark** | Hiçbir bilinen sebebin anlatmadığı fark; bu projenin ürettiği bulgu budur. | B10 |
| **Anlık görüntü (snapshot)** | Bir sistemin belirli bir andaki bütün ilgili sayılarının aynı anda okunmuş kopyası. | B10 |
| **Terminal işlem günlüğü (electronic journal)** | Makinenin kendi kaydı: kâğıda ne olduğunu yazar, mesajlara değil. | B10 |
| **Kilitlenme (deadlock)** | Bir olayın gelmesini, o olayı okuyan yerin içinde beklemek; iki taraf da ilerleyemez. | B5 |
| **Zaman aşımı bildirimi (tick)** | Tarayıcının saniyede bir gönderdiği "bir saniye geçti" mesajı; karar taşımaz. | B5 |
| **Bayt sırası işareti (BOM)** | Bazı araçların dosya başına koyduğu üç fazladan bayt; katı okuyucuları ilk satırda durdurur. | B10 |
| **Ara kasa (escrow)** | Müşterinin attığı paranın, o onaylamadan önce beklediği bölme; para makinede ama hâlâ müşterinin. | B8 |
| **Commit (kesinleştirme)** | Yatırmanın geri dönülemez hâle geldiği an; bu projede bir mesaj değil, fiziksel hareket. | B8 |
| **Geri dönüşüm kaseti (recycler)** | Hem para verilebilen hem para konulabilen çekmece; yatırılan banknot buradan başkasına verilebilir. | B8 |
| **Sıkışma (jam)** | Banknotun mekanizmada takılı kalması; para ne ara kasada ne kasette, makine açılmadan sayılamaz. | B8 |
| **Kabul edilen kupür** | Makinenin geri dönüşüm kaseti olan kupürler; ötekiler verilebilir ama alınamaz. | B8 |
| **Yatırma ağzı** | Banknotların makineye konduğu delik; nakit ağzından ayrıdır ve ters yönde çalışır. | B8 |
| **Onay ekranı** | Sayılan tutarın gösterilip müşterinin evet demesinin beklendiği adım; mülkiyet bu tuşta değişir. | B8 |
| **Banknot listesi** | Ekranın bildirdiği fiziksel olgu: hangi kupürden kaç adet kondu. Tutar değildir. | B8 |
| **Gün sonu kesimi (cutover / settlement)** | Makinenin bir iş gününü kapatıp yenisine geçtiği **an**; takvim günü değişince kendiliğinden olmaz. | B10 |
| **Kapanış toplamları (day totals)** | Bir iş gününde fiilen hareket etmiş para: müşteriye giden, kasete giren ve kaç işlemden geldiği. | B10 |
| **Mutabakat hatası (rc 95)** | Kesimde toplamların tutmaması ya da o güne ait açık işlem bulunması; gün kapanmaz. | B10 |
| **Askı hesabı (suspense)** | Gerçek bankacılıkta, gün kapanırken çözülememiş kalemin geçici olarak yazıldığı hesap. Bu projede yok. | B10 |
| **Türetilmiş değer (derived value)** | Başka bir kayıttan yeniden hesaplanabilen değer; bakiye böyledir, defter değildir. | B10 |
| **Açılış denetimi** | Host portu açmadan önce, hesap dosyasını defterden yeniden üretilen bakiyelerle karşılaştırması. | B10 |
| **Sessiz onarım** | Bir denetimin bulduğu farkı kendiliğinden düzeltmesi; hatanın kanıtını siler, bu projede yasak. | B10 |
| **Arıza senaryosu (fault scenario)** | Bir arızayı ve o arızada ne olması gerektiğini anlatan, koda değil dosyaya yazılan tarif. | B9 |
| **Kapsama matrisi (coverage matrix)** | İşlem × arıza × an hücrelerinden kaçının denendiği; boş hücreler adıyla raporlanır. | B10 |
| **Tespit noktası (detection point)** | Bir dengesizliğin görünür hâle geldiği an: anında, gün sonunda, ya da hiç. | B10 |
| **Switch (bankalar arası yönlendirici)** | Bankaların ATM isteklerini birbirine ileten ara sistem; kart başka bankaya aitse istek oraya yönlendirilir. Bu projede yok, tek host var. | B6 |
| **Durum makinesi (state machine)** | Sistemin sayılı sayıda durumdan birinde olduğunu ve her olayın onu belirli bir başka duruma taşıdığını söyleyen model. | B6 |
| **Sertleştirme anahtarı (hardening switch)** | Alanın bir kuralını kapatabilen mantıksal anahtar; naif karşılaştırma bunlarla yapılır. | B9 |
| **Naif akış** | Aynı kodun, alan kuralları kapalı hâlde koşulması. Ayrı bir kopya değildir. | B9 |
| **Arıza sınıfı (failure class)** | Aynı kökten gelen olay ailesi; tek bir senaryo değil. | B11 |
| **Manşet rakam** | Bir çalışmayı tek cümlede anlatan ölçüm; nasıl üretildiği söylenmeden yazılmaz. | B11 |
| **Depo (repository)** | Projenin bütün dosyalarının ve geçmişinin durduğu klasör. | B4A |
| **Betik (script)** | İçinde sırayla çalışacak komutlar yazılı metin dosyası; komut ezberlememek için vardır. | B4A |
| **Süreç (process)** | Çalışmakta olan bir program; aynı program iki kez başlatılırsa iki ayrı süreç olur. | B4A |
| **Port** | Bir bilgisayardaki süreçleri birbirinden ayıran numara; aynı portu aynı anda tek süreç tutar. | B4A |
| **Çıktı klasörleri (bin/obj)** | Derlemenin ürettiği dosyalar; depoya girmez, silinirse yeniden üretilir. | B4A |
