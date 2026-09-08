# KARARLAR — numaralı karar kaydı


Buradaki bir karar sessizce bozulamaz. Bozulacaksa gerekçesiyle yeni bir karar
olarak yazılır.

---

## KARAR-001 — .NET 10 (LTS) kullanılacak
- **Tarih:** 2026-08-24
- **Karar:** Hedef çatı `net10.0`. SDK sürümü `global.json` ile sabitlenir.
- **Gerekçe:** .NET 10, Kasım 2025'te çıkan uzun destekli (LTS) sürüm; desteği Kasım
  2028'e kadar sürüyor. Apple Silicon'da native çalışır. Ücretsiz.
- **Reddedilen alternatif:** .NET 8 — 10 Kasım 2026'da desteği bitiyor; proje
  sunulmadan destek dışına düşebilirdi.
- **Etkilediği dosyalar:** `global.json`, bütün `*.csproj`
- **Neden .NET 11 değil:** .NET 11 kısa destekli (STS) sürümdür — çıktıktan yaklaşık 18 ay
  desteklenir; .NET 10 ise 3 yıl (Kasım 2028). Ayrıca .NET 11 bu satırların yazıldığı
  tarihte henüz bitmiş bir ürün değil, önizleme aşamasında; önizleme sürümleri değişir ve
  "önizleme sürüm kullandım" cümlesi bir sunumda savunulacak bir şey değildir. Bu projenin
  ihtiyaç duyduğu her şey (TCP soket, C#, xUnit) iki sürümde de birebir aynıdır; 11'in
  getirdiği hiçbir yenilik bu projeye bir şey katmıyor.

---

## KARAR-002 — Kurum kimliği ve gerçek ATM ekranları depoya girmez
- **Tarih:** 2026-08-24
- **Karar:** Gerçek bir bankanın ATM ekran metinleri, menü ağacı, logosu, kurumsal
  renkleri ve bu ekranların fotoğraf/videosu bu projeye girmeyecek. Ekran, sektörde
  ortak olan ve kimseye ait olmayan ATM alışkanlıklarıyla kurulacak: ekranın iki yanında
  dörder fonksiyon tuşu; kırmızı İPTAL, sarı DÜZELT, yeşil GİRİŞ tuş takımı; kart
  yuvası, nakit ağzı, yatırma ağzı, makbuz yuvası.
- **Gerekçe:** Bir bankanın ATM ekran metinleri ve menü ağacı o kurumun fikri
  mülkiyetidir; logosu ve renkleri tescilli markasıdır. Kopyalanmış bir ekran projeyi
  teknik bir çalışmadan hukuki bir soruna çevirir ve sunumda savunulamaz. Ayrıca projenin
  değeri ekranın hangi bankaya benzediğinde değil, hat koptuğunda paranın korunmasında.
- **Reddedilen alternatif:** Gerçek ATM ekranlarının fotoğrafını çekip birebir taklit
  etmek.
- **Etkilediği dosyalar:** `src/Atm.Terminal/wwwroot/`, sunum, kitap Bölüm 10

---

## KARAR-003 — Ekranda kullanılacak uydurma banka adı
- **Tarih:** 2026-08-24 (aynı gün güncellendi)
- **Karar:** Banka adı: **ŞEREFLİŞAN BANK**. Kullanıcının seçimi.
- **Gerekçe:** KARAR-002 gereği bir ada ihtiyaç var; ekranda "banka adı buraya"
  yazamayız. Bu ad kontrol edildi: Türkiye'de bu adla faaliyet gösteren bir banka veya
  finans kuruluşu yok, dolayısıyla var olan bir markayla karışma riski taşımıyor.
- **Reddedilen alternatif:** İlk önerilen "DEMİR BANK" — kullanıcı değiştirdi.
- **Etkilediği dosyalar:** `src/Atm.Terminal/wwwroot/`, sunum, kitap Bölüm 10

---

## KARAR-005 — Çözüm dosyası `.slnx` biçiminde tutulur

**Tarih:** 2026-08-24

**Karar:** Projeleri bir arada tutan çözüm dosyası klasik `.sln` yerine `.slnx`
biçiminde yazılır (`ATMSIM.slnx`).

**Gerekçe:** Klasik `.sln` her proje ve her yapılandırma için birer GUID saklar; insanın
okuyamayacağı, elle düzeltilemeyecek bir dosyadır. `.slnx` düz XML'dir ve ne dediği
okunur. Bu projede **sahibinin her dosyayı anlatabilmesi** bir çıkış kriteri olduğu için,
okunamayan bir dosya baştan borçtur.

**Reddedilen alternatif:** Klasik `.sln`. Daha yaygın, daha çok araç destekliyor. Ama
`.slnx` .NET SDK 9.0.200'den beri destekleniyor, biz .NET 10 kullanıyoruz ve `dotnet
build ATMSIM.slnx` doğrulandı — çalışıyor.

**Etkilediği dosyalar:** `ATMSIM.slnx`, `scripts/build.sh`, `scripts/test.sh`

---

## KARAR-006 — Depo hiçbir dış paket kullanmaz; testler kendi koşucusuyla koşar

**Tarih:** 2026-08-24

**Karar:** Bu depo hiçbir NuGet paketine bağlı değildir. `NuGet.config` bütün paket
kaynaklarını temizler. Testler `tests/Atm.Tests/TestKit/` altındaki kendi koşucumuzla
koşar; test dosyaları **xUnit sözdizimiyle** yazılır (`[Fact]`, `Assert.Equal`) ve
`Xunit` isim alanı bizim tarafımızdan sağlanır.

**Gerekçe (zorlayan sebep):** Derlemenin yapıldığı bulut konteyneri `api.nuget.org`'a
erişemiyor (403). Doğrulandı: üç proje sorunsuz derlendi, yalnızca xUnit paketlerini
indiremeyen test projesi kaldı.

**Gerekçe (bunu iyi bir karar yapan sebep):** "Bu depo, internetsiz bir makinede tek
komutla derlenir ve test edilir" cümlesi, sunumda "xUnit kullandık" cümlesinden güçlüdür.
Bağımlılık disiplini kural §6b (docs/proje-kurallari.md)'nin zaten istediği şeydir; burada kapsamı genişletmiyor,
daraltıyoruz.

**Geri dönüş bedeli sıfırdır ve bilinçli olarak öyle tasarlandı:** Test dosyaları gerçek
xUnit'e karşı yazılmış gibi görünür. Paket erişimi olursa yapılacak iş: `Atm.Tests.csproj`
içine üç `PackageReference` satırı eklemek ve `TestKit/` klasörünü silmek. **Hiçbir test
dosyası değişmez.**

**Reddedilen alternatif:** xUnit paketlerini indirip depoya gömmek (yerel paket kaynağı).
Reddedildi çünkü (a) depoya ~10 MB derlenmiş ikili dosya girerdi, (b) her yeni paket
ihtiyacında kullanıcıdan komut çalıştırması istenirdi, (c) "hiç bağımlılık yok" cümlesi
"bağımlılıkları depoya kopyaladık" cümlesinden daha savunulabilir.

**Bilinçli kayıp:** VS Code'un test gezgini bu testleri listelemez; testler
`./scripts/test.sh` ile terminalden koşar. Kullanıcı zaten kod yazmadığı için bu kayıp
onun günlük işini etkilemiyor.

**Etkilediği dosyalar:** `NuGet.config`, `tests/Atm.Tests/Atm.Tests.csproj`,
`tests/Atm.Tests/TestKit/*`, `scripts/test.sh`

---

## KARAR-008 — Para kuruş cinsinden tam sayı olarak tutulur

**Tarih:** 2026-08-24

**Karar:** Bütün tutarlar `long` (tam sayı) ve **kuruş** cinsindendir. `150,75 TL` →
`15075`. Ondalıklı kayan noktalı tipler (`double`, `float`) para için kullanılmaz.

**Gerekçe:** Kayan noktalı sayılar ikili tabanda çalışır ve `0,1 + 0,2` tam olarak `0,3`
etmez. Bu fark tek bir işlemde görünmez; binlerce işlemin toplandığı gün sonu
mutabakatında "sebepsiz 1 kuruş farkı" olarak görünür. Bu projenin merkezî iddiası para
korunumu olduğu için, ölçüm aracının kendisinin gürültü üretmesine izin verilemez.

**Reddedilen alternatif:** `decimal`. Ondalık tabanda çalıştığı için para hesabında
doğrudur ve gerçek sistemlerde yaygın kullanılır. Yine de tam sayı seçildi: kuruş bu
projede bölünemeyen en küçük birimdir, bölünemeyen bir birimi ondalıklı bir tiple temsil
etmek "yarım kuruş" diye bir şeyin ifade edilebilir olması demektir.

**Etkilediği dosyalar:** `docs/protocol.md` §4, ileride hesap ve defter kodu

---

## KARAR-009 — İşlem kimliği baştan modele girer: `terminal + iş günü + STAN`

**Tarih:** 2026-08-24

**Karar:** Her işlem `terminal` + `bizDate` + `stan` üçlüsüyle kimliklenir. Host bu üçlüyü
saklar; aynı üçlü ikinci kez gelirse işlem **tekrar yapılmaz**, saklanan cevap aynen döner.

**Gerekçe:** kural §4.5 (docs/proje-kurallari.md): "Aynı istek iki kez gidebilir. İşlem kimliği eşleşmesi olmadan
yeniden deneme, çift borçlanmadır. Baştan modele girer, sonradan eklenmez." Sonradan
eklenemez, çünkü kimliği neyin oluşturduğu sorusu mesaj biçimini, defter kayıtlarını ve
mutabakat sorgusunu **birlikte** değiştirir.

**Reddedilen alternatif:** İsteğe rastgele bir UUID koymak. Daha kolay, ama gün sonu
mutabakatında insan tarafından takip edilemez; gerçek ödeme sistemleri sıralı izleme
numarası kullanır, çünkü mutabakat gözle de yapılabilmelidir.

**Bilinen açık uç:** STAN 999999'dan 1'e döndüğünde aynı iş gününde çakışma olabilir.
Uydurulmadı; açık soru olarak bırakıldı.

**Etkilediği dosyalar:** `docs/protocol.md` §2.1, §3

---

## KARAR-010 — PIN simülasyon içinde açık taşınır; şifreleme kapsam dışıdır

**Tarih:** 2026-08-24

**Karar:** `PinVerifyRequest` mesajı PIN'i açık taşır. Karşılaştırma host içinde yapılır,
dışarı yalnızca doğru/yanlış çıkar. PIN **hiçbir günlüğe, kayda veya hata mesajına
yazılmaz**; kart numarası kayıtlarda maskelenir (ilk 6 + son 4).

**Gerekçe:** kural §2 (docs/proje-kurallari.md): gerçek anahtar, HSM ve anahtar yükleme uygulanmaz; şifreleme
kapsam dışıdır ve raporda **bilinçli bir eksik** olarak beyan edilir. Bu simülatörde
saklanacak gerçek bir sır yoktur — kartlar uydurmadır. Gizlenmesi gereken şey veri değil,
**alışkanlıktır**; o yüzden maskeleme yine de uygulanır.

**Gerçekte nasıl:** Gerçek bir ATM'de PIN, tuş takımı donanımının içinde şifrelenir;
dışarı yalnızca "PIN bloğu" çıkar ve anahtarlar HSM adı verilen ayrı bir donanımda durur.
Bunu yapmıyoruz ve yapmadığımızı yazıyoruz.

**Reddedilen alternatif:** Şifrelemeyi taklit etmek (örneğin basit bir karıştırma).
Reddedildi: gerçek olmayan bir güvenlik, hiç güvenlik olmamasından kötüdür — çünkü
raporu okuyanı yanıltır.

**Etkilediği dosyalar:** `docs/protocol.md` §4.1, `reports/assumptions.md`

---

## KARAR-011 — Kupür seçimi açgözlü değil, tam çözümle yapılır

**Tarih:** 2026-08-24

**Karar:** İstenen tutarın hangi banknotlarla verileceği, kaset adetlerini de hesaba katan
bir **sınırlı bozuk para problemi** (bounded coin change) olarak, dinamik programlama ile
**tam** çözülür. Seçim yetkilendirmeden **önce** yapılır ve `denoms` alanıyla isteğe
konur.

**Gerekçe:** Açgözlü ("en büyük kupürden başla") algoritma, verilebilecek bir tutarı
verilemez ilan edebilir. Örnek: kasetlerde yalnızca 50'lik ve 20'lik varken 60 TL isteği.
Açgözlü önce bir 50'lik koyar, 10 TL kalır, 10'luk kupür yoktur, "veremem" der. Oysa üç
adet 20'lik ile tam olarak verilebilirdi.

**Reddedilen alternatif:** Açgözlü algoritma. Daha kısa ve daha hızlıdır. Reddedildi
çünkü **yanlış cevap üretir** ve ürettiği yanlış cevap müşterinin gözüne "makine para
vermedi" olarak görünür — yani sessiz değil, görünür bir hatadır.

**Maliyet endişesi neden yersiz:** Tablo, kupürlerin en büyük ortak böleni kadar adımlarla
ilerler; bu yüklemede adım 10 TL'dir, yani 5000 TL'lik bir istek için kaset başına 501
hücre. Bu, ölçülebilir bir maliyet değildir.

> **Düzeltme (2026-08-25):** Bu paragraf önceden "20 TL adım, 251 hücre" diyordu. Adım 20
> değil 10 TL'dir; kupürler 200/100/50/20 olduğunda en büyük ortak bölen 10 TL'dir.
> Kararın kendisi (açgözlü değil tam çözüm) değişmedi. Bkz. KARAR-028.

**Açık uç:** Sektörde hangisinin kullanıldığını bilmiyoruz; açık sorular listesi
soru 13 olarak yazıldı. Bizim tercihimiz doğruluk yönünde.

**Etkilediği dosyalar:** `docs/model.md` §3, `docs/protocol.md` §4.3, ileride terminal kodu

---

## KARAR-012 — Çerçeveleme eşzamanlı (senkron) yazılır

**Tarih:** 2026-08-24

**Karar:** `MessageFraming.Read` / `Write` bloklayan, eşzamanlı çağrılardır. Eşzamansız
(`async`) sürüm yazılmadı.

**Gerekçe:** Bu simülatörde tek terminal ve tek bağlantı var. Kendine ayrılmış bir iş
parçacığında bloklayan bir okuma, hem takip etmesi hem anlatması hem de testte
tekrarlanabilir kılması daha kolay. Eşzamansızlık, ölçtüğümüz hiçbir şeyi iyileştirmiyor;
buna karşılık testlere zamanlama belirsizliği sokuyor ve kural §5 (docs/proje-kurallari.md) "kararsız test yoktur"
diyor.

**Reddedilen alternatif:** `async`/`await` ile eşzamansız okuma. Binlerce eşzamanlı
bağlantı olsaydı zorunlu olurdu. Bir tane var.

**Yan etki ve alınan önlem:** Eşzamansız bir test, `Task` döndürüp henüz bitmeden
koşucuya geri dönebilir; koşucu beklemezse bir milisaniye sonra kırılan test **yeşil**
raporlanır. Sessizce yeşil yanan bir test, hiç olmayan bir testten kötüdür. Bu yüzden
`TestRunner`, `Task` dönen bir testi beklemek üzere güncellendi — ileride eşzamansız bir
test yazılırsa tuzak baştan kapalı.

**Etkilediği dosyalar:** `src/Atm.Protocol/MessageFraming.cs`,
`tests/Atm.Tests/TestKit/TestRunner.cs`

---

## KARAR-013 — Kalıcılık bir arayüzün arkasında durur; veritabanı kapsam dışı kalır

**Tarih:** 2026-08-24

**Karar:** Veritabanı (Oracle, SQL Server, PostgreSQL) kullanılmaz. Hesaplar bellekte,
defter ve bekleyen ters kayıt kuyruğu **diske eklemeli (append-only) dosya** olarak tutulur.
Ama bu depolama doğrudan iş mantığının içine yazılmaz: `IAccountStore` ve `IJournal`
arayüzlerinin arkasında durur.

**Bir veritabanı ne yapar, biz hangisini yapıyoruz:**

| Veritabanının işi | Bizde |
|---|---|
| Veriyi diskte tutmak, program kapansa bile kaybolmasın | **Yapıyoruz** — defter ve ters kayıt kuyruğu dosyaya yazılır |
| Sorgu cevaplamak ("bu hesabın bakiyesi ne") | **Yapıyoruz** — kendi kodumuzla |
| İşlem (transaction): yarım kalan bir değişiklik hiç görünmesin | **Kısmen** — tek süreç, tek bağlantı olduğu için sıraya sokmak yetiyor |
| Aynı anda yüzlerce kullanıcıyı yönetmek | **Yapmıyoruz** — tek ATM var (kapsam dışı) |
| Yedekleme, çoğaltma, yetkilendirme, denetim izi | **Yapmıyoruz** |

**Gerekçe:** Bu projenin sorusu "veri nasıl saklanır" değil, "arıza anında para korunuyor
mu". Bir veritabanı o soruya tek bir cevap katmaz; buna karşılık kurulum, şema ve
bağımlılık getirir ve Faz 4'ten — projenin tek manşet rakamının çıktığı yerden — zaman
çalar. kural §17 (docs/proje-kurallari.md) ayrıca kullanıcıya veritabanı kurdurulmasını yasaklıyor.

**Dikkat — .NET veritabanının işini yapmıyor.** .NET bir dil ve çalışma ortamı; depolama
kodunu biz yazıyoruz. "Veritabanı yok" demek "kalıcılık yok" demek değil: defterin diske
yazılması bir gereklilik, tercih değil — çünkü sözleşme §4.5, bekleyen bir ters kaydın
terminal kapanıp açılsa bile kaybolmamasını şart koşuyor.

**Reddedilen alternatif:** SQLite. Dosya tabanlı, kurulum istemiyor, gerçek işlem desteği
var. Reddedildi çünkü bir NuGet paketi gerektiriyor (KARAR-006 ile çelişir) ve çözdüğü
sorun bizde yok: tek yazıcı, tek süreç.

**Kapı neden açık bırakılıyor:** İş mantığı `IAccountStore` üzerinden konuştuğu için,
ileride gerçekten bir veritabanı istenirse yazılacak şey o arayüzün ikinci bir
uygulamasıdır. Çekim akışına, ters kayıt mantığına, mutabakata **dokunulmaz.** Bugünkü
maliyeti sıfır; ileride tasarrufu büyük.

**Açık uç:** Müdürün beklentisinin gerçekten bir veritabanı kurulumu olup olmadığı
bilinmiyor. Uydurulmadı; açık soru olarak bırakıldı.

**Etkilediği dosyalar:** Faz 1d'de yazılacak `src/Atm.Host/IAccountStore.cs`,
`src/Atm.Host/IJournal.cs` ve dosya tabanlı uygulamaları; `reports/assumptions.md`

---

## KARAR-016 — Saat ve hat arayüzleri `Atm.Protocol` içinde durur; sanal saat üretim kodudur

**Tarih:** 2026-08-24

**Karar:** `IClock`, `ITransport` ve bunların uygulamaları (`SystemClock`, `VirtualClock`,
`InMemoryLink`, `InMemoryTransport`) `src/Atm.Protocol/` altında durur. Test projesine
konmadı, beşinci bir proje açılmadı.

**Gerekçe — iki ayrı sebep:**

1. **Yer:** `Atm.Protocol`, host ile terminalin **ikisinin de** referans verdiği tek
   projedir. Saat ve hat ikisi tarafından da kullanılacak. kural §6c (docs/proje-kurallari.md) klasör yapısını
   Faz 0'da sabitledi ve "sonra değişmez" dedi; yeni bir proje açmak o kararı bozardı.

2. **Sanal saat neden test kodu değil:** Faz 4'ün senaryo koşucusu **gerçek işlem akışını**
   sanal saatin üstünde çalıştıracak. "Hattı 12. saniyede kes" cümlesinin tekrarlanabilir
   olmasının tek yolu budur. Yani `VirtualClock` bir test taklidi değil, simülatörün
   üzerinde koştuğu saattir. Test projesine konsaydı, Faz 4'te üretim kodu test projesine
   bağımlı hâle gelirdi — bağımlılık yönü ters dönerdi.

**Aynı sebeple `InMemoryLink` de üretim kodudur:** kesilebilir sahte hat, arıza
enjeksiyonunun aracıdır; onsuz Faz 4 yoktur.

**Reddedilen alternatifler:**
- *Beşinci bir proje (`Atm.Core`).* Somut bir sorunu çözmüyor, §6a'nın "bir katman somut
  bir sorunu çözmüyorsa girmez" kuralına takılıyor.
- *Sahtelerin test projesinde durması.* Yukarıdaki bağımlılık tersleşmesi.

**Modellemede alınan asıl karar — kopma ile kapanma ayrı şeylerdir:**
`Close()` nazik bir vedadır: kapatılan ucu kullanmaya çalışan kod hata alır, çünkü o uç ne
olduğunu bilir. `Sever()` kablonun çekilmesidir: **iki uç da açık görünmeye devam eder**,
gönderim sessizce yutulur, bekleyen taraf zaman aşımına düşer ve `null` alır. İkincisi
gerçeğe uyan davranıştır ve protokol §4.0 (echo) tam olarak bu yüzden vardır. Bu ayrım bir
yorum satırı olarak değil, `BothEndsStillLookOpenAfterTheCableIsCut` testi olarak durur.

**`Receive` zaman aşımında `null` döner, hata fırlatmaz.** Zaman aşımı bir hata değildir ve
kesinlikle bir ret değildir — host işi yapmış olabilir (kural §4.1 (docs/proje-kurallari.md)). `null`, "cevap yok"
demektir; bunun para açısından ne anlama geldiğine çağıran karar verir.

**Etkilediği dosyalar:** `src/Atm.Protocol/IClock.cs`, `ITransport.cs`, `InMemoryLink.cs`,
`InMemoryTransport.cs`, `tests/Atm.Tests/ClockTests.cs`, `InMemoryLinkTests.cs`,
`tests/Atm.Tests/TestKit/Assert.cs`

---

## KARAR-017 — PIN saklanmaz; kart başına tuzlanmış bir doğrulama değeri saklanır

**Tarih:** 2026-08-24

**Karar:** Host, PIN'i hiçbir biçimde tutmaz. Kart başına iki şey tutar: kısa rastgele bir
metin (**tuz / salt**) ve `SHA-256(tuz + PIN)` değeri. Gelen PIN aynı işlemden geçirilir ve
iki değer **sabit sürede** karşılaştırılır. Doğrulama değerinden PIN geri hesaplanamaz.

**Gerekçe:** kural §2 (docs/proje-kurallari.md) PIN'in üretilmesini, saklanmasını ve loglanmasını yasaklıyor.
"Dikkat ederiz" bir kural değildir; kuralın kodda karşılığı olması gerekir. Bu yüzden:
- `JournalEntry`'de **PIN alanı yoktur** — "hata ayıklarken isteği loglayayım" diyen biri
  PIN yazamaz, çünkü yazacak yer yok.
- `JournalEntry.Pan` yalnızca **yazılabilir** bir kapıdır ve içeri gireni maskeler; kayda
  açık kart numarası koymak mümkün değil.

**Tuz neden var:** Tuzsuz olsaydı aynı PIN'e sahip iki kart **aynı** değeri saklardı ve
kayda bakan biri hangi müşterilerin aynı PIN'i kullandığını görebilirdi. Bu, tek başına
bir sızıntıdır. `TwoCardsWithTheSamePinDoNotLookAlike` testi bunu tutuyor.

**Sabit süreli karşılaştırma neden:** İlk farklı karakterde duran bir karşılaştırma,
tahminin ne kadarının doğru olduğunu **süre üzerinden** sızdırır; saldırgan bunu ölçe ölçe
ilerler. Gerçek kartlı bir sistemde bu bilinen bir açıktır. Maliyeti sıfır olduğu için
doğrusu yapıldı.

**İddianın sınırı — abartmıyoruz.** Bu projedeki kartlar uydurmadır; korunacak gerçek bir
sır yoktur. Demo PIN'leri `docs/kurulum.md`'de yazılıdır, çünkü demoyu kullanabilmek için
gerekir — tıpkı bir test ortamının test PIN'lerinin yazılı olması gibi. Uygulanan özellik
daha dar ve **tam olarak şudur:** sistemin kendisi PIN'i saklamaz, loglamaz ve hiçbir kod
yolu PIN'i geri veremez. Bunu "PIN'imiz güvenli" diye anlatmak yanlış olurdu.

**Gerçek ATM ağında bu nasıl, farkımız ne:** Tuş takımı PIN'i donanım içinde şifreler,
dışarı yalnızca şifreli "PIN bloğu" çıkar; banka, anahtarları HSM denen kurcalamaya
dayanıklı bir kutuda duran bir doğrulama değeri tutar. Tuzlanmış özet **aynı fikirdir**
(karşılaştırılabilen ama geri çevrilemeyen bir değer sakla), **aynı mekanizma değildir.**
Şifreleme ve anahtar yönetimi `reports/assumptions.md` V-02'de bilinçli kapsam dışı olarak
yazılıdır — yarım yapılmış bir şifreleme, gerçeği taklit ettiği için yokluğundan kötüdür.

**Reddedilen alternatifler:**
- *PIN'i düz metin sabit olarak tutmak.* En basiti, ve tam olarak §2'nin yasakladığı şey.
- *Şifreleyip saklamak.* Şifre çözülebilir; doğrulama için geri çevrilebilir bir değere
  ihtiyaç yok. Anahtar yönetimi de kapsam dışı.
- *Deneme sayacını hesabın içinde tutmak.* Sayaç paranın değil kartın özelliği; hesabın
  içinde olsaydı bakiye taşıyan her yapıya bulaşırdı.

**Etkilediği dosyalar:** `src/Atm.Host/PinVerifier.cs`, `src/Atm.Host/IJournal.cs`,
`src/Atm.Host/HostService.cs`, `tests/Atm.Tests/PinVerifierTests.cs`,
`tests/Atm.Tests/HostServiceTests.cs`

---

## KARAR-018 — Tekrar bağışıklığı ilk günden host'un içinde; host soket bilmez

**Tarih:** 2026-08-24

**Karar:** İki karar, tek yerde çünkü aynı dosyada buluşuyorlar (`HostService.cs`).

1. **Host, cevapladığı her işlem kimliğini ve verdiği cevabı saklar.** Aynı kimlik ikinci
   kez gelirse iş **tekrar yapılmaz**, önceki cevap aynen döner. Bu, bugün parası olmayan
   bir akışa bile konuldu.
2. **Host'un davranışı soketten habersiz yazıldı.** `HostService.Handle(Envelope) →
   Envelope`. Faz 1e'de gelecek TCP dinleyicisinin işi yalnızca bayt taşımak olacak.

**Birinci kararın gerekçesi:** kural §4.5 (docs/proje-kurallari.md) nettir — tekrar bağışıklığı (idempotency)
baştan modele girer, sonradan eklenmez. Sebebi sıradan: terminal sordu, cevap gelmedi,
tekrar sordu; iki kopya da hosta ulaşabilir. Host işi iki kez yaparsa müşteri **iki kez**
borçlanır. Faz 2'de akışlar yazıldıktan sonra eklenmeye kalkılsaydı, yazılmış her akışın
içine ayrı ayrı girmek gerekirdi.

**Saklanan şey "görüldü" değil, verilen cevabın kendisidir.** "Bunu zaten sormuştun" demek
çözüm değildir: terminal ilk cevabı **hiç almadı**, zaten o yüzden tekrar sordu. İhtiyacı
olan şey cevaptır, uyarı değil.

**Echo bunun dışında:** Canlılık kontrolü para taşımaz ve otuz saniyede bir gelir; her
birini hatırlamak, hiçbir şeyi korumadan hafızayı sonsuza kadar büyütmek olurdu.

**İkinci kararın gerekçesi:** Ağdan okuyan bir akış, ağ olmadan test edilemez; ağ gerektiren
bir test ise parayla ilgisi olmayan sebeplerle kırmızı yanar (kural §6a (docs/proje-kurallari.md)). Karar veren her
satır burada, saniyede binlerce kez koşabilecek bir yerde duruyor.

**Reddedilen alternatif:** Tekrarı terminalin `retry` alanına bakarak anlamak. Reddedildi:
o alanı doğru doldurmak terminalin sorumluluğu ve unutulabilir. Güvenlik, hatırlanması
gereken bir alana bağlanmaz (protokol §3 aynı sebeple kimliği üç alandan kuruyor).

**Etkilediği dosyalar:** `src/Atm.Host/HostService.cs`, `tests/Atm.Tests/HostServiceTests.cs`

---

## KARAR-019 — Nazik kapanma karşı uca bildirilir; kopma bildirilmez

**Tarih:** 2026-08-24

**Karar:** `InMemoryTransport.Close()` karşı ucu bilgilendirir: o uç, **bekleyen bütün
mesajları okuduktan sonra** akışın bittiğini görür (`Receive` boş döner, `IsOpen` yanlış
olur). `InMemoryLink.Sever()` ise hiçbir şey bildirmez — iki uç da açık görünmeye devam
eder ve okuma her seferinde zaman aşımıyla boş döner.

**Bu karar bir hatanın sonucudur ve hata şöyle bulundu:** Faz 1e'de hostun konuşma
döngüsü yazıldı ve `AConversationEndsWhenTheTerminalHangsUp` testi **askıda kaldı** —
kırmızı yanmadı, hiç bitmedi. Sebep: sahte hatta terminal ucu kapandığında host ucu bunu
hiç öğrenmiyordu; döngü, gelmesi imkânsız bir mesajı sonsuza kadar bekliyordu.

**Neden model yanlıştı:** Gerçek bir TCP bağlantısında nazik kapanma **bildirilir**.
Karşı taraf kapattığında okuma, yolda kalanları teslim ettikten sonra "akış bitti" der.
Sahte hattımız bunu taklit etmiyordu; yani sahte hat, gerçek hattın **yapabildiği** bir
şeyi yapamıyordu. Bu, modelin gerçekten daha karamsar olduğu bir yerdi ve test kodunu
değil modeli düzeltmeyi gerektirdi (kural §9 (docs/proje-kurallari.md): sistemi mi düzelttim, testi mi
zayıflattım?).

**Bekleyen mesajlar neden önce teslim ediliyor:** Gerçek bir bağlantı, kapanmadan önce
gönderilmiş veriyi teslim eder, sonra akışın bittiğini söyler. Kapanışta bekleyeni atmak,
**gerçekten ulaşmış** bir mesajı kaybetmek olurdu.

**İki bozulma arasındaki farkın tamamı budur ve projenin konusu tam olarak bu fark:**

| | Karşı uca ne bildirilir | Okuyan taraf ne görür |
|---|---|---|
| `Close()` — nazik kapanma | Akış bitti | Bekleyenleri okur, sonra boş döner ve uç kapanır |
| `Sever()` — kablo çekilir | **Hiçbir şey** | Her okuma zaman aşımıyla boş döner, uç açık görünür |

**Doğrulama:** `_peerHungUp` kontrolü bilerek devre dışı bırakıldığında test takımı 45
saniyede bitmedi (çıkış kodu 124), yani döngü yine askıda kaldı; geri alınınca 65/65 yeşil
ve 473 ms. Askıda kalan bir test de kırmızıdır — sadece daha pahalı bir kırmızıdır.

**Etkilediği dosyalar:** `src/Atm.Protocol/InMemoryTransport.cs`,
`tests/Atm.Tests/InMemoryLinkTests.cs`, `src/Atm.Host/HostConnection.cs`

---

## KARAR-020 — Host tek terminale, kalıcı bağlantıyla hizmet eder

**Tarih:** 2026-08-24

**Karar:** `HostServer` aynı anda **tek** bağlantıya hizmet eder; ikinci terminal ancak
birincinin hattı bittikten sonra kabul edilir. Bağlantı, her istek için açılıp kapanmaz;
konuşma boyunca açık kalır.

**Tek bağlantı neden:** Bu simülatörde tek ATM var (kural §3 (docs/proje-kurallari.md), kapsam). Birden çok
terminal kabul etmek, iki terminalin aynı milisaniyede aynı hesabı sorması durumunda ne
olacağına karar vermeyi gerektirir. Bu gerçek ve ilginç bir sorudur; bu proje onu **kötü
cevaplamak yerine kapsam dışı bıraktığını** ilan etti.

**Kalıcı bağlantı neden:** Gerçek ATM–merkez ilişkisi böyledir, ve asıl sebep projenin
konusudur: her istek için açılıp kapanan bir bağlantıda **yolda hiçbir şey uzun süre
kalmaz**, dolayısıyla "hat öldüğünde yolda olan mesaja ne oldu" sorusu görünmez hâle
gelir. Bizim incelemek istediğimiz an tam olarak o andır.

**`NoDelay = true` neden:** Küçük bir cevap, işletim sisteminin arabelleğinde arkadaş
beklemek yerine hemen gönderilir. Aksi hâlde onlarca milisaniyelik, **bizim istemediğimiz
ve açıklayamayacağımız** bir gecikme ölçümlere karışır.

**Reddedilen alternatif:** HTTP. Her istek kendi bağlantısını açar, çerçeveleme sorununu
kütüphane çözer, hattın koptuğu an kütüphanenin içinde kalır — yani projenin konusu
görünmez olur (kural §3 (docs/proje-kurallari.md) bunu zaten kilitlemişti).

**Etkilediği dosyalar:** `src/Atm.Host/HostServer.cs`, `src/Atm.Host/HostConnection.cs`,
`src/Atm.Protocol/StreamTransport.cs`

---

## KARAR-021 — Terminal cevabı kimlikle eşler, hattı echo ile yoklar

**Tarih:** 2026-08-24

**Karar:** `TerminalClient` iki kuralı uygular:
1. **Cevap, işlem kimliğiyle eşlenir; geliş sırasıyla değil.** Beklediğimiz kimlikten
   başka bir cevap gelirse **alınmaz**, sayılır (`LateAnswerCount`) ve atılır.
2. **Hat, echo ile yoklanır.** Arka arkaya üç echo cevapsız kalırsa hat ölü sayılır,
   bağlantı bırakılır ve yenisi kurulur. Cevaplanan tek bir echo sayacı sıfırlar.

**Birincinin gerekçesi:** Host istekleri farklı sürelerde işleyebilir ve cevaplar farklı
sırada dönebilir. Hattan gelen **ilk** mesajı "benim cevabım" saymak, bir müşteriye başka
bir işlemin sonucunu göstermek demektir. Protokol §2.1 zaten eşlemeyi STAN'a bağlamıştı;
bu, o kuralın koddaki karşılığıdır. Gerçek hayatta bu, zaman aşımına uğrayıp vazgeçtiğimiz
bir işlemin cevabının biz başka bir işlemi beklerken gelmesiyle olur — ve tam da o an,
gösterilecek en yanlış rakam elimizdedir.

**İkincinin gerekçesi:** Açık görünen bir soket hiçbir şey kanıtlamaz. Kablo çekilirse veya
karşı makine donarsa bu uç dakikalarca hattın sağlam olduğuna inanabilir. Ölü bir hattı
anlamanın tek güvenilir yolu düzenli olarak bir şey gönderip cevabını beklemektir.

**Sayaç neden sıfırlanıyor:** Cevap veren bir hat yeniden canlıdır. Sayaç hattın **şu anki
hâli** hakkındadır, geçmişi hakkında değil. Sıfırlanmasaydı, gün içine yayılmış üç sessiz
an, çalışan bir hattı düşürürdü.

**Cevap gelmediğinde ne olur:** `Exchange` **null** döner. Hata bildirmez ve işlemin
olmadığına karar vermez — bilmiyor. Host işi yapmış ve cevabı dönüş yolunda kaybetmiş
olabilir. Bundan sonra ne yapılacağı çağıranın kararıdır ve Faz 2'den itibaren o karar
çoğunlukla **ters kayıt üretmek** olacaktır (kural §4.1 (docs/proje-kurallari.md)).

**Testler neden iş parçacığı kullanmıyor:** Terminal echo'larını 1, 2, 3... diye
numaralandırdığı için cevabın kimliği önceden bilinir; test, echo gönderilmeden **önce**
cevabı hatta koyabiliyor. Böylece bütün echo/yeniden bağlanma testleri tek iş parçacığında,
gerçek zaman harcamadan ve her koşuda aynı sonucu vererek koşuyor. İkinci bir iş parçacığı
zamanlama getirirdi; zamanlama da beşte dört koşan testler (kural §5 (docs/proje-kurallari.md)).

**Reddedilen alternatif:** Bağlantının canlılığını işletim sisteminin TCP keep-alive
mekanizmasına bırakmak. Reddedildi: eşiği bizim değil işletim sisteminin kontrolünde,
dakikalar mertebesinde, ve uygulama katmanının donmasını (host süreci ayakta ama cevap
vermiyor) hiç görmez.

**Etkilediği dosyalar:** `src/Atm.Terminal/TerminalClient.cs`, `src/Atm.Terminal/HostLink.cs`,
`tests/Atm.Tests/TerminalClientTests.cs`

---

## KARAR-022 — Depo MIT lisansıyla lisanslanır

**Tarih:** 2026-08-25

**Karar:** Depo köküne `LICENSE` dosyası konur, içeriği standart MIT lisans metnidir,
telif satırı `Copyright (c) 2026 Mirac Kutay Sereflisan`.

**Gerekçe:** Lisans dosyası olmayan bir depoda hukuki varsayım zaten "bütün hakları
saklı"dır — yani koruma açısından yeni bir şey kazanmıyoruz. Kazandığımız şey **okunma
biçimidir.** Bir depoyu inceleyen kişi lisans dosyasının yokluğunu "bilinçli olarak
kapalı" diye değil, "tamamlanmamış" diye okur. Bu proje bir yetkinlik göstergesi olarak
sunulacağı için (kural §1 (docs/proje-kurallari.md)), deponun bitmiş görünmesi işin bir parçasıdır.

**MIT neden:** En yaygın, en kısa ve en tanınan izin verici lisans. Sahibine hiçbir
yükümlülük getirmez, sorumluluğu açıkça reddeder ve telif sahibinin adını dosyaya yazar.

**Reddedilen alternatifler:**
- *Lisanssız bırakmak.* Hukuken yeterli, izlenim olarak yetersiz.
- *Kendi yazdığımız "gösterim amaçlıdır" metni.* Standart dışı bir lisans metni,
  gözden geçiren tarafta güven değil tereddüt üretir.

**Etkilediği dosyalar:** `LICENSE`, `README.md`, `INDEX.md`

---

## KARAR-024 — Ekran sözleşmesi host protokolünden tamamen ayrıdır

**Tarih:** 2026-08-25

**Karar:** Tarayıcı ile terminal arasında, `docs/protocol.md`'deki mesajlarla hiçbir
ilgisi olmayan ikinci ve ayrı bir sözleşme kullanılır (`src/Atm.Terminal/ScreenMessage.cs`).
İki yön ve iki tip vardır:

- **terminal → ekran: `ScreenView`** — "tam olarak şunu çiz". Başlık, satırlar, sekiz
  tuşun etiketi, kaç yıldız çizileceği, kart/nakit/makbuz yuvalarının durumu, geri sayım.
- **ekran → terminal: `ScreenEvent`** — "şu fiziksel şey oldu". Tuşa basıldı, kart
  takıldı, para alındı, makbuz alındı.

**Neden ayrı:** Tarayıcıya hostun cevabı verilseydi, tarayıcının içinde bir yerde bir
satır kod bir cevap kodunu okuyup **ne anlama geldiğine karar vermek** zorunda kalırdı.
O karar iş mantığıdır ve tarayıcıda duran her şey, tarayıcının geliştirici araçlarıyla
değiştirilebilir. Para hakkında karar veren makine müşterinin tarayıcısı olamaz
(kural §8 (docs/proje-kurallari.md): "iş mantığını tarayıcıya kaçırmak").

**Neden niyet değil fiziksel olay:** Gerçek bir ATM'nin ön paneli işlem diye bir şey
bilmez. Bildiği şey bir tuşa basıldığıdır. Tarayıcının "200 lira çek" diyebilmesi,
müşterinin para çekmek istediğine **tarayıcının karar vermiş olması** demektir.

**Somut sonucu — PIN yıldızları:** PIN hanelerini terminal sayar. Tarayıcıya "üç yıldız
çiz" denir; tarayıcı ne haneleri tutar ne de kaç tane çizeceğine karar verir. Ekrandaki
her yıldız terminalden gelmiştir. Bu, "tarayıcı karar vermez" iddiasının gözle
görülebilen hâlidir ve sunumda tek cümleyle gösterilebilir.

**Basitleştirme (reports/assumptions.md):** Gerçek bir ATM'de PIN, mühürlü bir tuş
takımına (şifreleyen PIN pad) girilir ve haneler hiçbir zaman hane olarak yol almaz.
Burada tuş olayları olarak, yerel bir bağlantı üzerinde gidiyorlar. Şifreleme bütün
proje için kapsam dışıdır — KARAR-010.

**Reddedilen alternatif:** Ekrana host cevabını olduğu gibi verip metni tarayıcıda
oluşturmak. Daha az kod, ama iş mantığının yarısı tarayıcıya taşınırdı.

**Etkilediği dosyalar:** `src/Atm.Terminal/ScreenMessage.cs`,
`src/Atm.Terminal/wwwroot/index.html`, `tests/Atm.Tests/ScreenContractTests.cs`

---

## KARAR-025 — WebSocket el sıkışması ve çerçevelemesi elle yazılır

**Tarih:** 2026-08-25

**Karar:** Tarayıcı bağlantısının el sıkışması (`WebSocketHandshake.cs`) ve çerçevelemesi
(`WebSocketFraming.cs`) bu depoda, RFC 6455'e göre yazılır. Hazır bir WebSocket sunucu
katmanı kullanılmaz.

**Gerekçe — birincisi test edilebilirlik:** Hazır bir katman kullanıldığında el sıkışması
da çerçeveleme de bir soketin **içinde** kalır. Bu depoda ise ikisi de bayt dizileri
üzerinde çalışır: "mesaj beş parça hâlinde geldi" ve "iki mesaj yapışık geldi" durumları
beklenen kazalar değil, koşulan testlerdir. Bu, `IClock`/`ITransport` kararının
(KARAR-016) aynısıdır ve kural §6a (docs/proje-kurallari.md)'nın tek kuralıdır.

**İkincisi — Faz 4:** Arıza enjeksiyonu ekran hattını da kesecek. Çerçeveleme platformun
içindeyse hattı istediğimiz baytın ortasında kesemeyiz.

**Üçüncüsü — tutarlılık:** Host hattında zaten kendi çerçevelememizi yazdık (KARAR-012).
İki hatta iki farklı yaklaşım kullanmak, ikisini de yarım anlamak demektir.

**Ölçüm notu — dürüstlük kaydı:** Hazır yolun (`HttpListener.AcceptWebSocketAsync`) bu
platformda çalışıp çalışmadığı ölçülmek istendi; deneme sonuçsuz kaldı (ne başarı ne
hata döndü). **Bu karar o ölçüme dayanmıyor;** yukarıdaki üç gerekçeye dayanıyor.

**Reddedilen alternatif:** ASP.NET Core'un hazır WebSocket desteği. Reddedildi: yukarıdaki
üç gerekçeye ek olarak, depo bugün hiçbir dış çatıya bağlı değil (KARAR-006) ve bu
bağımsızlık bilinçli.

**Bu kararın hemen ürettiği bulgu:** El sıkışmada kullanılan sabit metin (RFC 6455'in
GUID'i) **hafızadan yanlış yazıldı.** Yakalandı, çünkü testin beklediği değer bizim
kodumuzdan değil standardın kendisinden alınmıştı.

**Etkilediği dosyalar:** `src/Atm.Terminal/WebSocketHandshake.cs`,
`src/Atm.Terminal/WebSocketFraming.cs`, `src/Atm.Terminal/ScreenServer.cs`

---

## KARAR-028 — Verilebilirlik bölünebilme kuralıyla değil, planlayıcı çalıştırılarak bilinir

**Tarih:** 2026-08-25

**Karar:** "Bu tutar verilebilir mi" sorusunun tek cevap yeri `DenominationPlanner`'dır.
Hiçbir yerde — ne ekranda, ne terminal akışında, ne hostta — "tutar şu sayının katı mı"
biçiminde bir kısayol kontrolü yazılmaz. Tutarın adım aralığı, kupürlerin **en büyük ortak
böleninden** hesaplanır (bu yüklemede 10 TL), kasetlerin listesinden okunur, sabit
yazılmaz.

**Gerekçe:** `docs/model.md` §2'de "20'nin katı olmayan hiçbir tutar verilemez" yazıyordu.
Yanlıştı ve kod yazılırken yakalandı: 70 TL 20'nin katı değil ama 50 + 20 ile tam olarak
verilir. Hata, yazan kişinin dikkatsizliği değil, kısayolun kendisidir — kupürlerin
listesine bakmadan verilebilirlik hakkında konuşan her cümle bu hatayı yapmaya adaydır.
Aynı yanlışın pahalı biçimi de var: **verilebilir bir tutarı verilemez ilan etmek** (bu),
ve **verilemez bir tutarı verilebilir sanmak** — ikincisi yetkilendirmeden sonra fark
edilirse iptal edilecek bir borçlanma üretir (kural §4.6 (docs/proje-kurallari.md)).

**Reddedilen alternatif:** Ekrana "yalnızca 100'ün katları" gibi bir tuş takımı kısıtı
koyup soruyu ortadan kaldırmak. Gerçek ATM'lerde hazır tutar tuşları vardır ve bu makul
görünür. Reddedildi çünkü kısıtı ekrana koymak, kararı tarayıcıya taşımaktır (KARAR-024) ve
kaset boşaldığında kısıt yanlışa döner: 100'lük kaset bittiğinde "100'ün katı" hâlâ
seçilebilir görünür ama artık verilemez. Ekran hazır tutar **önerebilir**; verilebilirliğe
planlayıcı karar verir.

**Düzeltme kaydı:** `docs/model.md` §2 ve §3 düzeltildi; §3'teki "20 TL adım, 251 hücre"
cümlesi de yanlıştı (adım 10 TL, 501 hücre). KARAR-011'in gerekçesindeki aynı rakam da bu
karara bağlanır — KARAR-011'in **kararı** (açgözlü değil tam çözüm) değişmedi, yalnızca
gerekçesindeki tablo boyutu düzeltildi.

**Etkilediği dosyalar:** `src/Atm.Terminal/DenominationPlanner.cs`,
`src/Atm.Terminal/DenominationPlan.cs`, `docs/model.md` §2–§3, `KARARLAR.md` (KARAR-011)

---

## KARAR-029 — Yetkilendirme bloke koyar; defteri dağıtım bildirimi işler

**Tarih:** 2026-08-25

**Karar:** `WithdrawalAuthRequest` onaylandığında host **defteri değiştirmez**, hesaba
tutar kadar **bloke** koyar. Defter bakiyesi ancak `DispenseAdvice` geldiğinde ve
**gerçekten verilen** tutar kadar değişir. Ters kayıt blokeyi çözer. `Account` üzerindeki
`HoldAmount` alanı bu kararın taşıyıcısıdır; `AvailableBalance = LedgerBalance - HoldAmount`
ve yetersiz bakiye kontrolü **kullanılabilir** bakiyeye bakar.

**Gerekçe:** Bu karar, `docs/protocol.md` içindeki bir **çelişkiyi** kapatmak için
alınmak zorunda kaldı. Faz 0'da yazılan §4.2, "Faz 2'de bir çekim yetkilendirildiği anda
para bloke edilir ama defter henüz değişmemiş olabilir" diyordu; aynı dosyanın §4.3'ü ise
"yetkilendirme borçlandırır: `rc: 00` dönen an hesaptan düşülmüştür" diyordu. İkisi aynı
anda doğru olamaz. Çelişki, kod yazılmaya başlandığında değil, **kod yazılmadan önce
sözleşme okunduğunda** yakalandı — kural §5 (docs/proje-kurallari.md)'in "önce protokol, sonra kod" kuralının
karşılığı budur.

Bloke modeli iki sebeple seçildi:

1. **`DispenseAdvice: FULL` mesajının bir işi olur.** Defter yetkilendirmede borçlanmış
   olsaydı, "tamamı verildi" bildirimi hostta hiçbir şeyi değiştirmezdi; gönderilen ama
   hiçbir işe yaramayan bir mesaj, sözleşmeyi okuyan kıdemli birinin ilk işaret edeceği
   şeydir. Bloke modelinde bildirim defteri işler — mesajın varlık sebebi vardır.
2. **Bir arıza sınıfı daha ölçülebilir hâle gelir.** Bildirim kaybolursa bu modelde
   ortada **asılı kalmış bir bloke** kalır: müşterinin parası ne hesabındadır ne de
   gitmiştir, ve bu ancak gün sonu mutabakatında görünür. Projenin ürünü arıza
   kataloğudur; ölçülebilir gerçek bir arıza sınıfı, kazançtır.

**Reddedilen alternatif — tek mesajlı model (yetkilendirmede defteri borçlandırmak).**
Gerçek ATM ağlarının büyük kısmının böyle çalıştığını biliyoruz: ATM finansal isteği
yollar, host onayladığı anda defteri borçlandırır, başarı hâlinde ikinci mesaj yoktur ve
ters kayıt yalnızca iş ters gittiğinde gider. Bu alternatifin **standart olduğu** kabul
edilir ve reddedilme sebebi "yanlış" olması değildir; yukarıdaki iki kazancı vermemesidir.
Bedeli açıkça yazılıyor: **bizim modelimiz bu noktada gerçeğin çoğunluğundan ayrılır.**
Bu ayrım `docs/protocol.md` §4.3'te bir `:::gercek` kutusuyla, `reports/assumptions.md`'de
bir varsayım olarak açıkça söylenir. Faz 4'te "asılı bloke"
sınıfı raporlanırken, bunun **modelimizin ürettiği** bir sınıf olduğu birlikte yazılır
(kural §9.1 (docs/proje-kurallari.md)).

**Açık soru:** Gerçek bir hostun tek mesajlı mı iki mesajlı mı çalıştığı ve bloke ile
defter arasındaki boşluğun gün sonunda nasıl raporlandığı bu projede doğrulanmadı.
Cevabı bilinirse bu karar yeniden değerlendirilir.

**Etkilediği dosyalar:** `docs/protocol.md` §4.2–§4.3, `src/Atm.Host/Account.cs`,
`src/Atm.Host/HostService.cs`, `src/Atm.Protocol/MessageType.cs`,
`reports/assumptions.md`, `book/src/07-para-cekme.md`

---

## KARAR-030 — Defter kaydına tutar alanı eklenir

**Tarih:** 2026-08-25

**Karar:** `JournalEntry` bir `Amount` alanı taşır: **o satırın konusu olan tutar**,
kuruş cinsinden. Parayla ilgisi olmayan satırlarda (echo, PIN kontrolü, reddedilen istek)
0'dır. `IJournal.Append` bu alanı isteğe bağlı bir parametreyle alır.

Alan bilerek "defter ne kadar hareket etti" **değildir.** Bir yetkilendirme, defteri hiç
hareket ettirmeden bir tutar hakkındadır (KARAR-029); oraya 0 yazmak, satırın var olma
sebebi olan sayıyı kaybetmek olurdu. Bu yüzden gün sonu mutabakatı bu sütunu **mesaj tipi
başına** toplar — zaten toplamak zorundadır, çünkü bir yetkilendirme ile bir dağıtım
bildirimi aynı para hakkında farklı şeyler söyler.

**Gerekçe:** Faz 3'ün çıkış kriteri "host defteri = terminal günlüğü, sıfır fark". Tutarı
olmayan bir defterle bu karşılaştırma yapılamaz; yalnızca "kaç işlem oldu" sorulabilir,
"ne kadar para" sorulamaz. Alan Faz 3'te eklenseydi, o güne kadar yazılmış bütün defter
satırları tutarsız kalırdı — eklemeli (append-only) bir kayıtta geçmiş satırlar
düzeltilemez. Bu yüzden alan, defterin ilk para hareketini yazdığı gün konur.

**Reddedilen alternatif:** Tutarı `Note` metnine yazmak. Reddedildi: metinden sayı
ayıklayarak mutabakat yapan bir kod, biçim değiştiği gün sessizce yanlış toplar.

**Etkilediği dosyalar:** `src/Atm.Host/IJournal.cs`, `src/Atm.Host/HostService.cs`

---

## KARAR-031 — Tekrar tablosunun anahtarı işlem kimliği **artı mesaj tipidir**

**Tarih:** 2026-08-25

**Karar:** Hostun "bu isteği daha önce cevapladım" tablosu, `terminal + bizDate + stan`
üçlüsüyle değil, o üçlü **artı mesaj tipiyle** anahtarlanır. Kodda bunun adı
`HostService.MessageKey`. KARAR-018'in kararını değiştirmez, **anahtarını düzeltir.**

**Gerekçe:** Bir çekimin yetkilendirmesi, dağıtım bildirimi ve ters kaydı aynı STAN'ı
taşır (`docs/protocol.md` §2.1) ve bu bilinçli bir tasarımdır — ters kaydın hangi çekimi
geri aldığını söylemesinin yolu budur. Tablo yalnızca işlem kimliğiyle anahtarlanırsa
şu olur: dağıtım bildirimi hosta ulaşır, host onu **yetkilendirmenin tekrarı sanar**, ve
saklanan yetkilendirme cevabını geri döner. Defter hiç hareket etmez. Para müşteriye
gitmiştir ama hesapta durmaktadır ve **hiçbir hata mesajı bunu söylemez** — bloke sonsuza
kadar açık kalır, fark ancak gün sonu mutabakatında görünür.

Hata, Faz 2c'nin kodu yazılmadan önce, "bildirim hangi anahtarla gelecek" sorusu sorulduğu
anda yakalandı. KARAR-018 Faz 1d'de yazıldığında hostun tek tip para mesajı yoktu; anahtar
o gün doğruydu ve **ikinci mesaj tipi eklendiği gün yanlış hâle geldi.** Kayda geçirilmesinin
sebebi budur: doğru bir karar, kapsam büyüdüğünde sessizce yanlışa dönebilir.

**Reddedilen alternatif:** Her mesaja kendi STAN'ını verdirmek, yani bildirimi ayrı bir
işlem saymak. Reddedildi çünkü o zaman "bu bildirim hangi çekime ait" sorusunun cevabı
`authId` alanına — yani mesajın **gövdesine** — bağlanır. Gövdedeki bir alan yazılmayı
unutulabilir, yanlış kopyalanabilir, biçim değiştirebilir; zarftaki kimlik unutulamaz.
Aynı gerekçe ters kayıt için de geçerlidir (§4.5: "ters kayıt aynı STAN'ı taşır").

**Etkilediği dosyalar:** `src/Atm.Host/HostService.cs`, `docs/protocol.md` §3.1 ve §4.4,
`KARARLAR.md` (KARAR-018)

---

## KARAR-032 — Hiçbir şeyi değiştirmemiş bir cevap hatırlanmaz

**Tarih:** 2026-08-25

**Karar:** Host, verdiği cevabı tekrar tablosuna **yalnızca bir şey değiştirdiyse** yazar.
Kodda bunun adı `HostService.Answer.Keep` ve `Answer.Forget`; her işleyici hangisini
ürettiğini kendisi söyler.

- **Hatırlanır:** onaylanan yetkilendirme (bloke kondu), uygulanan dağıtım bildirimi
  (defter hareket etti), PIN doğrulama — **yanlış PIN dahil**, çünkü deneme sayacı düştü,
  bakiye sorgusu (terminalin kaçırdığı cevabın aynısını alması için).
- **Hatırlanmaz:** echo, bilinmeyen mesaj tipi, ve **reddedilen her istek** — reddedilen
  yetkilendirme, reddedilen bildirim.

**Gerekçe:** Tekrar tablosunun varlık sebebi, bir işin iki kez yapılmasını önlemektir. Hiç
iş yapmamış bir cevabın koruyacağı bir şey yoktur; onu hatırlamak, **reddedilen tek bir
mesajı o izleme numarası hakkında kalıcı bir hükme çevirir.**

Bu, kodu yazarken bir testin kırmızı yanmasıyla ortaya çıktı. `ARefusedAdviceLeavesThe-`
`AuthorisationOpen` şunu sınıyordu: makine bozuk bir dağıtım bildirimi gönderir (host
reddeder), sonra doğrusunu gönderir. İkinci mesaj, birincinin tekrarı sayıldı ve **red
cevabı geri döndü.** Sonucu şuydu: para blokede kalır, defter hiç hareket etmez ve bu
blokeyi çözebilecek hiçbir mesaj kalmaz. Bozuk tek bir mesaj, müşterinin parasını süresiz
olarak ortada bırakır.

**Reddedilen alternatif 1:** Cevabı hata koduna bakarak hatırlamak (`rc == "00"` ise
hatırla). Reddedildi çünkü yanlış PIN cevabı `55` döner ve **hatırlanmak zorundadır** —
aynı yanlış denemeyi ikinci kez saymak, bir kez tahmin etmiş müşteriden bir hak alır.
Hata kodu, "bir şey değişti mi" sorusunun cevabı değildir.

**Reddedilen alternatif 2:** Her cevabı hatırlayıp, bozuk bildirim gelen işlemleri elle
temizleyecek bir yol açmak. Reddedildi: bir arızayı düzeltmek için elle müdahale gerektiren
tasarım, gece yarısı çalışan bir ATM için tasarım değildir.

**Etkilediği dosyalar:** `src/Atm.Host/HostService.cs`, `docs/protocol.md` §3,
`tests/Atm.Tests/WithdrawalAuthTests.cs`, `tests/Atm.Tests/DispenseAdviceTests.cs`

---

## KARAR-033 — Ödenmiş bir çekimin ters kaydı onaylanır, uygulanmaz ve sayılır

**Tarih:** 2026-08-25

**Karar:** Host, zaten ödenmiş (dağıtım bildirimi uygulanmış) bir çekim için ters kayıt
aldığında üç şeyi birden yapar: **onaylar** (`rc: 00`), **hiçbir şeyi geri almaz**, ve
çelişkiyi hem deftere `UNEXPECTED` etiketiyle yazar hem de `UnexpectedReversalCount`
sayacında sayar.

**Gerekçe:** Bu durum bir çelişkidir — makine "geri al" diyor, kayıt paranın müşteriye
gittiğini söylüyor. Host bir banknotu geri alamaz; elindeki tek gerçek, kendi kaydıdır.
Üç seçenek vardı ve ikisi kötüydü:

1. **Reddetmek (`12`).** Sözleşme §4.5 gereği ters kayıt onaylanana kadar tekrar gönderilir.
   Reddetmek, makineyi gün boyu aynı mesajı yollamaya mahkûm eder ve hiçbir şeyi düzeltmez.
2. **Uygulamak** — yani müşterinin aldığı parayı hesabına geri koymak. Bu, parayı iki kez
   vermektir: nakit müşterinin cebinde, tutar hesabında.
3. **Onaylamak, uygulamamak, saymak.** Seçilen.

Üçüncüsünün riski açık: onay, "hallettim" gibi görünür. Bu yüzden **sessiz olmaması**
şart. Sessizce onay verip geçmek, kural §6b (docs/proje-kurallari.md)'nin yasakladığı sessiz onarımdır — hatanın
demoya ulaşma yoludur. Sayaç ve defter etiketi, bu onayın bir düzeltme **olmadığını**
söyleyen yerlerdir. Faz 4'te bu sayacın sıfırdan büyük olduğu her senaryo, arıza
kataloğuna girer.

**Reddedilen alternatif (ayrıca):** Bu durumu hiç modellememek, çünkü "normal akışta
olmaz" — ters kayıt yalnızca yetkilendirme cevabı gelmediğinde üretilir, o durumda da
bildirim gönderilmemiştir. Doğru, ama yalnızca hat bir yönde koptuğunda. Bildirim gidip
cevabı kaybolur, terminal yeniden başlar ve kuyruğundaki ters kaydı önce gönderirse bu
durum gerçekleşir. Modellenmemiş bir durum, olmayan bir durum değildir — **fark edilmeyen
bir durumdur.**

**Etkilediği dosyalar:** `src/Atm.Host/HostService.cs`, `docs/protocol.md` §4.5,
`tests/Atm.Tests/ReversalTests.cs`

---

## KARAR-034 — Terminal kendi işlem günlüğünü tutar

**Tarih:** 2026-08-25

**Karar:** Terminal, hostunkinden ayrı, kendine ait bir işlem günlüğü tutar
(`ITerminalJournal`, bellekte ve dosyada iki uygulaması var). Günlük **olaya** yazar,
işleme değil: para ağza geldi, para alındı, para geri çekildi, bildirim gönderildi,
bildirim kuyruğa girdi. Kart numarası yalnızca maskeli girebilir, PIN alanı **yoktur.**

**Gerekçe:** kural §3 (docs/proje-kurallari.md)'ün merkezî iddiasının son satırı `host defteri = terminal günlüğü`.
Terminal günlüğü olmadan bu satır yazılamaz — karşılaştırılacak ikinci bir kayıt yoktur ve
host kendi kaydına göre **tanım gereği** haklıdır.

Somut durum: terminal 350 lirayı verdi, hosta gönderdiği bildirim kayboldu. Hostun kaydı
bir yetkilendirme ve **hiçbir borçlanma** gösterir; hostun defteri kusursuz denk. Para
makineden çıkmış, kimsenin defterinde yok. **Terminal günlüğü olmasa bu durum görünmez.**

İkinci sebep: olay ile mesaj aynı şey değil. "Para ağza geldi" ve "para alındı" iki ayrı
olaydır (§4.4) ve hiçbir mesaj ikincisini tek başına anlatmaz. Kaydı mesajlara göre tutan
bir günlük, alınmayan parayı anlatamaz.

**Reddedilen alternatif 1:** Hostun defterini tek kayıt saymak ve terminale kayıt
tutturmamak. Bu, tek doğruluk kaynağı olduğunu varsaymaktır; kural §4.10 (docs/proje-kurallari.md) tam tersini
söyler — terminal durumu ile host durumu **ayrı ayrı** bozulur. Mutabakat zaten bu yüzden
vardır.

**Reddedilen alternatif 2:** Günlüğü işlem başına tek satır tutmak ("işlem 201: 350 lira,
başarılı"). Kısa olurdu ve kısmi dağıtımı, alınmayan parayı, geç ulaşan bildirimi
anlatamazdı. Bir satıra sığdırılmış bir işlem, sonradan hangi anın bozulduğu sorulduğunda
cevap veremez.

**Etkilediği dosyalar:** `src/Atm.Terminal/ITerminalJournal.cs`,
`src/Atm.Terminal/FileTerminalJournal.cs`, `src/Atm.Terminal/WithdrawalFlow.cs`

---

## KARAR-035 — Para korunumu denetleyicisi ayrı bir projedir ve farkı ikiye ayırır

**Tarih:** 2026-08-25

**Karar:** Denetim kodu `src/Atm.Audit` adında **ayrı bir projede** durur; hostu da
terminali de görür, ikisi de onu görmez. Denetleyici bulduğu her farkı ikiye ayırır:
**açıklanan** (kuyrukta bekleyen bir mesaj bu farkı zaten anlatıyor) ve **açıklanamayan**
(hiçbir şey anlatmıyor). Yalnızca ikincisi ihlaldir ve `ThrowIfViolated` ile yüksek sesle
hata verir.

**Gerekçe — neden ayrı proje:** Hostun içinde duran bir mutabakat, hostu yalnızca kendisiyle
karşılaştırabilir; bu, hiçbir zaman başarısız olamayacak tek kontroldür. Gerçek bir bankada
bu işi ne makine yapar ne switch — üçüncü bir taraf yapar. Kodun da aynı şeyi söylemesi,
mimarinin alanı taklit etmesi değil, kontrolün anlamlı olmasının şartıdır.

**Gerekçe — neden açıklanan/açıklanamayan ayrımı:** Para, verildiği an ile hostun haber
aldığı an arasında **gerçekten** havadadır. O aralıkta defterler denk değildir ve denk
olmamaları doğrudur. Her farka bağıran bir denetleyici her çekimde bağırır ve ikinci hafta
kapatılır; hiçbir farka bağırmayan denetleyici zaten yoktur. Ayakta kalan, "dört fark var,
üçünün adı ve sebebi belli, şu dördüncüsünün yok — buna bak" diyendir. Gerçek mutabakat
raporları da tam olarak böyle okunur: yoldaki kalemler **listelenir**, alarm üretmez.

**Reddedilen alternatif 1:** Denetimi testlerin içinde bırakmak. Testler yalnızca test
koşarken çalışır; kural §3 (docs/proje-kurallari.md) "koşulabilir bir denetleyici" diyor ve Faz 4'ün manşet rakamı
onu senaryo koşucusundan çağıracak. Ayrıca aynı kontrol her test dosyasına elle yazılırdı.

**Reddedilen alternatif 2:** Farkı ikiye ayırmamak, "kuyruk boşalana kadar denetleme"
demek. Bu, denetimin yalnızca sistem sakinken çalışması demektir — yani arıza anında,
**tam olarak bakılması gereken anda**, kör olması demektir.

**Bilinçli sınır:** Denetleyici hiçbir şeyi düzeltmez. Bulduğunu sayar, adlandırır ve
durdurur. Düzelten bir denetleyici, §6b'nin yasakladığı sessiz onarımı yapar ve bir sonraki
koşuda kanıtı silinmiş temiz bir defter gösterir.

**Etkilediği dosyalar:** `src/Atm.Audit/*` (beş dosya), `scripts/check.sh`,
`tests/Atm.Tests/ConservationTests.cs`, `tests/Atm.Tests/WithdrawalEndToEndTests.cs`,
`ATMSIM.slnx`

---

## KARAR-036 — Ekran zamanın geçtiğini bildirir, ne anlama geldiğine terminal karar verir

**Tarih:** 2026-08-25

**Karar:** Tarayıcı saniyede bir `tick` olayı gönderir ve **hiçbir şeye karar vermez.**
Sürenin dolup dolmadığına terminal kendi saatine bakarak karar verir. Ayrıca çekim akışı
tam olarak "para ağza geldi" anında ikiye bölündü: `Begin` (say, yetkilendir, parayı ver)
ve `Complete(alındı mı)`.

**Gerekçe — neden tick:** Bir geri sayımın dolması bir **karardır**: kartı iade et, parayı
geri çek. Karar tarayıcıda durmaz (KARAR-024). Tarayıcının dürüstçe bilebileceği tek şey
bir saniyenin geçtiğidir; onu bildirir. Terminal kendi saatiyle karşılaştırır ve gerekeni
yapar. Test tarafındaki karşılığı da bu: saat sanal olduğu için otuz saniyelik bir zaman
aşımı **beklenmeden** sınanır — saat ileri alınır, bir tick gönderilir.

**Gerekçe — neden akış ikiye bölündü:** "Vermek" ile "alınmak" yalnızca isim olarak değil
**zaman olarak da** ayrıdır (§4.4). Tek parça bir çağrı, "müşteri parayı aldı mı?" sorusunu
bir fonksiyona sorar — ve o fonksiyona cevabı yalnızca **cevabı zaten bilen** bir şey
verebilir: bir test ya da bir senaryo dosyası. Ekran veremez, çünkü cevap sonra, bir olay
olarak gelir ve makinenin bu arada çizmeye ve dinlemeye devam etmesi gerekir. Akış tam o
anda bölündü.

**Reddedilen alternatif 1:** Ekran akışının, `Withdraw` çağrısı içinde cevabı **bekleyen**
bir fonksiyon vermesi. Bu, mesaj döngüsünü kilitler: tarayıcı "parayı aldım" mesajını
gönderemez, çünkü terminal onu okuyacak yerde bekliyordur. Klasik kilitlenme.

**Reddedilen alternatif 2:** Geri sayımı tarayıcıda tutup süre dolunca tarayıcının
"zaman aşımı oldu" demesi. O zaman ekran, parayı geri çektiren tarafa dönüşür — ve
tarayıcı konsolundan gönderilecek tek bir mesaj, makinenin elindeki parayı geri çekebilir.

**Reddedilen alternatif 3:** `Withdraw`'ı tamamen kaldırıp yalnızca `Begin`/`Complete`
bırakmak. `Withdraw` senaryo dosyasının süreceği biçimdir ve on dokuz testin okunabilirliği
ona bağlı; iki metodun üstünde üç satırlık bir sarmalayıcı olarak duruyor, kopya değil.

**Etkilediği dosyalar:** `src/Atm.Terminal/WithdrawalFlow.cs`,
`src/Atm.Terminal/TerminalFlow.cs`, `src/Atm.Terminal/ScreenMessage.cs`,
`src/Atm.Terminal/wwwroot/index.html`, `src/Atm.Terminal/Program.cs`

---

## KARAR-037 — Diske yazılan kayıtlar bayt sırası işareti (BOM) taşımaz

**Tarih:** 2026-08-25

**Karar:** `FileJournal`, `FileTerminalJournal` ve `FilePendingHostMessages`, dosyalarını
`UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` ile yazar.

**Gerekçe:** .NET'in `Encoding.UTF8`'i, oluşturduğu dosyanın başına üç bayt fazladan yazar
(`EF BB BF`). Kendi okuyucumuz bunu atladığı için bütün testlerimiz geçiyordu. Canlı
koşudan çıkan defteri **bizim olmayan** bir araçla (düz bir Python betiği) okumaya
çalıştığımızda ilk satırda durdu.

Bu dosyalar tam olarak **başkaları okuyabilsin diye** metin dosyası. Yalnızca bizim
okuyabildiğimiz bir kayıt, kanıt değil, özel bir nottur. KARAR-013 veritabanı yerine dosya
seçerken gerekçesi buydu; BOM o gerekçeyi sessizce iptal ediyordu.

**Nasıl yakalandı:** Test yazarak değil, **çıktıyı başka bir araçla okumaya çalışarak.**
Bir sözleşmenin gerçekten tutulup tutulmadığı, ancak sözleşmenin karşı tarafı taklit
edilerek anlaşılır. Şimdi bir test var ve dosyanın **ilk üç baytına** bakıyor.

**Etkilediği dosyalar:** `src/Atm.Host/FileJournal.cs`,
`src/Atm.Terminal/FileTerminalJournal.cs`, `src/Atm.Terminal/FilePendingHostMessages.cs`,
`tests/Atm.Tests/PersistenceTests.cs`

---

## KARAR-038 — Para yatırmada fiziksel hareket, mesajdan **önce** yapılır

**Tarih:** 2026-08-25

**Karar:** Müşteri yatırmayı onayladığında sıra şudur: **önce** banknotlar ara kasadan
(escrow) geri dönüşüm kasetine alınır, **sonra** hosta bildirilir. Tersi yapılmaz.

**Gerekçe:** kural §4.8 (docs/proje-kurallari.md) şu soruyu soruyor: "onay anı ile hesabın işlendiği an arasında
hat koparsa escrow'daki para kimin?" Bu sorunun cevabı bir kural değil, bir **sıralama**
kararıdır.

İki sıralama mümkün:

1. **Önce hosta sor, cevap gelince kasete al.** Hat koptuğunda para escrow'da kalır ve
   sahibi belirsizdir: host hesabı artırmış olabilir (o zaman para bankanındır) veya
   artırmamış olabilir (o zaman müşterinindir). Makine hangisi olduğunu **bilemez** ve
   escrow'daki paranın iki sahibi olur.
2. **Önce kasete al, sonra hosta söyle.** Hat koptuğunda para fiziksel olarak kasettedir.
   Belirsiz olan tek şey hesabın artıp artmadığıdır — ve bu, **mesaj tekrarlanarak**
   çözülebilir bir belirsizliktir. Seçilen.

İkinci sıralamanın kurduğu asimetri kasıtlıdır ve alanın temel kuralıdır: **kasete girmiş
bir banknot geri alınamaz, ama bir mesaj sonsuza kadar tekrar gönderilebilir.** Belirsizliği
her zaman geri alınabilir tarafa taşırsın.

**Reddedilen alternatif:** Escrow'da bekleyen para için "şüpheli" diye üçüncü bir sahiplik
durumu tanımlamak. Bu, sorunu çözmez, adlandırır — ve gün sonunda hâlâ birinin o parayı
kime yazacağına karar vermesi gerekir.

**Bedeli — kabul edilen:** kasete alma işlemi sırasında sıkışma olursa (`JAMMED`) para ne
escrow'dadır ne kasette. Bu durum modellenmek zorunda (KARAR-039) ve gerçek hayatta bir
servis çağrısıdır. Bu bedeli kabul ediyoruz, çünkü alternatifin bedeli **her hat kopmasında**
sahipsiz para, bunun bedeli ise **yalnızca sıkışmada** fiziksel olarak sıkışmış paradır.

**Etkilediği dosyalar:** `docs/protocol.md` §4.6, Faz 3'ün bütün kodu

---

## KARAR-039 — Nakit kovaları ikiye daha ayrılır: ara kasa ve sıkışan

**Tarih:** 2026-08-25

**Karar:** `CashPosition`'a iki kova eklenir: `InEscrow` (müşterinin attığı, henüz
onaylanmamış para) ve `Jammed` (kasete alınırken sıkışan, fiziksel olarak hiçbir yere ait
olmayan para). Liste yine kapalıdır: bu altısından birinde olmayan bir banknot, bu projenin
hesabını veremediği bir banknottur.

**Gerekçe:** Kova listesi "para nerede" sorusunun cevabıdır ve para yatırma iki yeni yer
üretir. `InEscrow`'suz bir model, yatırılan parayı ya müşterinin cebinde ya kasette
göstermek zorunda kalır — ikisi de yanlıştır, para makinenin içinde ve **müşterinindir.**

`Jammed` daha da önemli: KARAR-038'in kabul ettiği bedel tam olarak bu kovadır. Sıkışan
para için bir kova **olmasaydı**, para korunumu denetleyicisi sıkışmayı "para yok oldu"
diye rapor ederdi — yani gerçek bir olayı, modelin hatası gibi gösterirdi. Ya da daha kötüsü,
sıkışan para sessizce kasete sayılırdı ve kasette olmayan bir para varmış gibi görünürdü.

**Reddedilen alternatif:** Sıkışan parayı `Retracted` (geri alınan) kovasına yazmak. İkisi
farklı şeyler: geri alınan para makinenin **bilerek** aldığı, sayılabilir bir destedir;
sıkışan para mekanizmanın içinde, sayılamaz durumdadır. Aynı kovaya yazmak, gün sonunda
sayılabilir olanla olmayanı karıştırmaktır.

**Etkilediği dosyalar:** `src/Atm.Terminal/CashDispenser.cs`,
`src/Atm.Audit/ConservationChecker.cs`, `docs/model.md`

---

## KARAR-040 — Para yatırmada tekrar edilen şey **commit**'tir, ters kayıt değil

**Tarih:** 2026-08-25

**Karar:** Yatırma akışında hat koptuğunda ne yapılacağı, **koptuğu ana** göre değişir:

| Kopma anı | Paranın fiziksel yeri | Yapılan |
|---|---|---|
| Yetkilendirme cevabı gelmedi | Escrow (müşterinin) | Para **iade edilir**, hosta ters kayıt gönderilir |
| Onaydan sonra, kasete alınırken | Sıkışmış | Hosta `JAMMED` bildirilir, hesap **artmaz**, servis işi |
| Kasete alındı, commit cevabı gelmedi | Geri dönüşüm kaseti (bankanın) | **Commit tekrar gönderilir**, onay gelene kadar durmaz |

**Gerekçe:** Çekimde tekrar edilen şey ters kayıttır, çünkü para henüz çıkmamıştır ve
yapılacak iş bir sözü geri almaktır. Yatırmada bunun **aynadaki karşılığı** ters kayıt
değildir: KARAR-038 gereği para commit'ten önce fiziksel olarak kasete girmiştir ve geri
alınamaz. Yapılacak iş bir sözü geri almak değil, **olan biteni duyurmaktır** — ve
duyurulamayan bir şey, duyulana kadar tekrar duyurulur.

Yetkilendirme dalı bunun tersi: orada para hâlâ escrow'dadır, yani müşterinindir ve geri
verilebilir. Verilir. Hosta ters kayıt gitmesinin sebebi hesap değil, hostun kaydında
asılı kalabilecek bir yatırma kaydıdır.

**Bu tabloyu tersine çevirmek, alanın en pahalı hatasıdır:** cevapsız bir commit'i "olmadı"
sayıp parayı müşteriye iade etmek, müşteriye hem parayı hem bakiyeyi vermektir.

**Tekrarın güvenli olmasının şartı:** aynı işlem kimliği + mesaj tipi ile gelen ikinci
commit, hesabı ikinci kez artırmaz (KARAR-031, KARAR-018). Bu şart sağlanmadan tekrar
mekanizması, çift alacak makinesidir.

**Etkilediği dosyalar:** `docs/protocol.md` §4.6, Faz 3'ün terminal ve host tarafı

---

## KARAR-041 — Kasete alma niyeti, alma işleminden **önce** günlüğe yazılır

**Tarih:** 2026-08-25

**Karar:** Terminal, escrow'daki parayı kasete almadan **önce** günlüğüne
`DEPOSIT_STACKING` satırı yazar; işlem bittiğinde `DEPOSIT_STACKED` (veya `DEPOSIT_JAMMED`,
`DEPOSIT_RETURNED`) yazar. Açılışta ilki olup ikincisi olmayan bir yatırma bulunursa,
terminal **tahmin etmez**: durumu "bilinmiyor" diye raporlar.

**Gerekçe:** KARAR-038 fiziksel hareketi mesajdan öne aldı; geriye bir tek pencere kalıyor —
makine tam kasete alırken elektriği giderse, açıldığında paranın kasete girip girmediğini
bilmez. Kovaları sayarak da bilemez: sayaç da o anda güncelleniyordu.

Niyeti önce yazmak bu pencereyi kapatmaz — **kapatılamaz**, çünkü fiziksel bir hareketle
bir yazma işlemi hiçbir zaman tam olarak aynı anda olmaz. Yaptığı şey, pencereyi
**görünür** kılmaktır: açılışta yarım kalmış bir yatırma olduğu **bilinir**, ve bilinen bir
belirsizlik, fark edilmeyen bir belirsizlikten iyidir.

**Reddedilen alternatif:** Açılışta yarım kalmış yatırmayı "muhtemelen olmuştur" diye
tamamlamak. Bu, sistemin kendi kendine para yaratabildiği tek yerdir: kasete girmemiş bir
parayı kasede saymak, kasada olmayan parayı varmış gibi göstermektir. kural §6b (docs/proje-kurallari.md)'nin
yasakladığı sessiz onarım budur.

**Etkilediği dosyalar:** `src/Atm.Terminal/ITerminalJournal.cs`, Faz 3'ün yatırma akışı,
`reports/failure-catalog.md`

---

## KARAR-042 — Onay ekranında sessizlik **ret** sayılır: para geri verilir

**Tarih:** 2026-08-25

**Karar:** Ara kasada (escrow) para beklerken müşteri cevap vermezse ve ekranın süresi
dolarsa, terminal `Complete(handle, confirmed: false)` çağırır — yani banknotlar **dışarı**
verilir. Aynı şey ekranın kırmızı İPTAL tuşu için de geçerlidir.

**Gerekçe:** Bu, çekimdeki zaman aşımının **tersi** yöndür ve ikisi yan yana okunduğunda
projenin bütün konusu görünür:

| Durum | Kimin parası | Makine ne yapar |
|---|---|---|
| Nakit ağızda, kimse almadı | Bankanın | İçeri çeker (retract) |
| Escrow'da para, kimse onaylamadı | Müşterinin | Dışarı verir (iade) |

Ara kasadaki para, onay anına kadar müşterinindir (kural §4.7 (docs/proje-kurallari.md)). Cevap gelmemesi bir onay
değildir; onay, basılan bir tuştur. Sessizliği onay saymak, müşterinin son vazgeçme hakkını
— hem de elinden çıkmış bir para üzerinde — makinenin kendi kendine kullanması olurdu.

**Reddedilen alternatif 1 — sessizliği onay saymak.** "Nasılsa parayı kendi attı, niyeti
belli." Bu, cebinden yanlışlıkla fazla banknot çıkmış ya da ekrandaki tutarı görünce
vazgeçmiş müşteriyi, sırf orada durup düşündüğü için borçlandırır. Bir tuşa basılmadan
mülkiyet değiştiren tek yol budur ve bir daha geri alınamaz (KARAR-038: kasete giren
banknot geri çıkmaz).

**Reddedilen alternatif 2 — hiçbir şey yapmayıp beklemek.** Müşteri gitmişse makine, içinde
başkasının parasıyla süresiz kilitli kalır ve bir sonraki müşteri o parayı kendi işleminin
parçası olarak bulur.

**Bilinçli olarak kapsam dışı bırakılan alt senaryo:** iade edilen banknotları da kimsenin
almaması. Gerçek bir makine onları da geri çeker ve **üçüncü bir kovaya** koyar — ne
müşterinin ne bankanın. Bu, çekimdeki `retract` kovasının yatırma tarafındaki karşılığıdır
ve modelde yeri hazırdır; Faz 4'te arıza enjeksiyonuyla gelir. Şu an makine, iade edilen
paranın alındığını varsayar. `reports/assumptions.md`'ye yazıldı.

**Etkilediği dosyalar:** `src/Atm.Terminal/TerminalFlow.cs`,
`tests/Atm.Tests/TerminalDepositTests.cs`, `book/src/08-para-yatirma.md`

---

## KARAR-043 — Yatırılan tutar ekrandan gelmez; ekran yalnızca **hangi banknotların**
girdiğini bildirir

**Tarih:** 2026-08-25

**Karar:** Ekran sözleşmesine `notes` adlı yeni bir olay eklendi. Taşıdığı değer bir tutar
değil, bir banknot listesidir: `"200x2,50x1"` — iki iki yüzlük ve bir ellilik. Tutarı
makinenin kabul edicisi (`CashAcceptor`) sayar.

**Gerekçe:** Ekran karar veremez (KARAR-024), ama fiziksel bir olguyu bildirebilir — kart
takıldı, para alındı, saniye geçti. "Şu banknotlar ağza kondu" bunlardan biridir; "şu kadar
lira yatırıyorum" değildir. Aradaki fark denetlenebilirliktir: sayılan tutarın kaynağı
sayaçsa, makbuzdaki rakamın arkasında fiziksel bir sayım vardır. Kaynağı müşterinin beyanı
olsaydı, hesaba geçen tutarın arkasında hiçbir şey olmazdı — ve gerçek bir ATM'de tutarı
müşteriye yazdıran zarflı yatırma yöntemi tam olarak bu yüzden terk edilmiştir.

Bu aynı zamanda hangi kupürün kabul edilip edilmeyeceğini de **makinenin** kararı yapar:
bu makinede 200'lük kaset yalnızca dağıtım yapar (`DispenseOnly`), bu yüzden 200'lükler
ağızda geri verilir ve müşteri bunu onay ekranında görür.

**Reddedilen alternatif:** Çekimdeki gibi tutar tuşlatmak (`AmountEntry`). Simetrik
görünüyor ve daha az iş; ama simetri burada yanlış cevaptır — çekimde tutarı müşteri
söyler, yatırmada makine sayar. İkisini aynı yapmak, projenin anlatmaya çalıştığı ayrımı
silerdi.

**Etkilediği dosyalar:** `src/Atm.Terminal/ScreenMessage.cs`,
`src/Atm.Terminal/TerminalFlow.cs`, `src/Atm.Terminal/wwwroot/index.html`,
`book/src/08-para-yatirma.md`

---

## KARAR-044 — İş günü takvimden okunmaz; terminal onu **tutar** ve yalnızca kesimde
değiştirir

**Tarih:** 2026-08-25

**Karar:** Terminalin iş günü artık `clock.UtcNow.ToString("yyyy-MM-dd")` değil, tek bir
`BusinessDay` nesnesinde **tutulan** bir değerdir. Bu değeri değiştirebilen tek şey,
hostun onayladığı bir gün sonu kesimidir. Üç akış (`WithdrawalFlow`, `DepositFlow`,
`TerminalFlow`) aynı nesneyi paylaşır.

**Gerekçe:** kural §4.9 (docs/proje-kurallari.md) "gün sonu kesimi bir tarih değil, bir andır" diyor. Tarihi
takvimden okuyan bir makinede o an hiç yoktur: gün, gece yarısı UTC'de kendiliğinden
döner, kimse toplamları karşılaştırmaz, kimse "kapandı" demez. İki somut sonucu vardı.
Birincisi, gün İstanbul'da saat 03:00'te ve kimsenin haberi olmadan dönüyordu. İkincisi
ve daha ağırı: kasayı sayan insanın "gün"ü ile defterin "gün"ü aynı gün değildi, ve iki
tarafın farklı gün tanımı mutabakatta tam olarak "sebepsiz fark" diye görünür.

Ayrıca aynı satır üç dosyada üç kez yazılıydı. Üç kopyanın ikisi düzeltilip biri
unutulsaydı, makine kendi içinde iki farklı güne inanırdı — ve bu, hiçbir hata mesajı
üretmeyen türden bir tutarsızlıktır.

**Reddedilen alternatif:** Takvimden okumaya devam edip kesim saatini (örneğin 23:00)
tarihe eklemek: `UtcNow.AddHours(1).Date`. Tek satır ve çalışıyor gibi görünüyor. Ama gün
yine kendiliğinden dönerdi — sadece başka bir saatte. Kesimin varlık sebebi tarihi
kaydırmak değil, **iki tarafın kapanışta anlaşmasıdır.**

**Etkilediği dosyalar:** `src/Atm.Terminal/BusinessDay.cs` (yeni),
`src/Atm.Terminal/WithdrawalFlow.cs`, `src/Atm.Terminal/DepositFlow.cs`,
`src/Atm.Terminal/TerminalFlow.cs`, `src/Atm.Terminal/Program.cs`, `docs/protocol.md` §4.7

---

## KARAR-045 — Kesimde iki taraf birbirinin rakamını kopyalamaz; ikisi de kendi
defterinden sayar

**Tarih:** 2026-08-25

**Karar:** `CutoverRequest` terminalin kendi toplamlarını taşır, `CutoverResponse` hostun
kendi toplamlarını taşır. Host, terminalin gönderdiği rakamı kaydetmez; kendi defterinden
sayar ve karşılaştırır. Toplamların sayılma kuralı iki tarafta da aynıdır: **karşılığı
fiilen hareket etmiş para** — müşterinin eline geçen ve kasete giren. Yetkilendirme,
reddedilen istek, iade ve ters kayıt sayılmaz.

**Gerekçe:** Bir mutabakatın anlamı, iki bağımsız kaydın aynı sonucu vermesidir. Taraflardan
biri diğerinin rakamını alıp yazsaydı, karşılaştırılan iki sayı aslında tek sayı olurdu ve
kontrol her zaman geçerdi — kural §9 (docs/proje-kurallari.md)'un "bu sonucu bir sahtekârlıkla üretebilir miydim"
sorusunun en kolay cevabı budur.

Denetleyicimiz (`Atm.Audit`) zaten işlem işlem karşılaştırma yapıyor. Kesim onun yerine
geçmez, **başka bir şeydir:** denetleyici iki defteri birden görebilen bir test aracıdır;
gerçek hayatta kimse iki defteri aynı anda göremez, bu yüzden iki makine toplamlarını
telden değişmek zorundadır. Kesim farkın **var olduğunu** söyler; hangi işlemde olduğunu
söyleyemez. Denetleyici bunun için vardır.

**Reddedilen alternatif:** Hostun günün toplamını terminale bildirmesi ve karşılaştırmayı
yalnızca terminalin yapması. Daha az mesaj alanı; ama farkı yalnızca farkın tarafı bilirdi
ve hostun kaydında "bugün mutabakat tutmadı" diye bir satır hiç oluşmazdı.

**Etkilediği dosyalar:** `src/Atm.Protocol/MessageType.cs`,
`src/Atm.Host/HostDayTotals.cs` (yeni), `src/Atm.Terminal/TerminalDayTotals.cs` (yeni),
`src/Atm.Host/HostService.cs`, `src/Atm.Terminal/CutoverFlow.cs` (yeni)

---

## KARAR-046 — Açık işi olan bir gün kapanmaz; iki taraf da kendi tarafından bakar

**Tarih:** 2026-08-25

**Karar:** Terminal, kuyruğunda bekleyen bir bildirim veya ters kayıt varken hosta kesim
isteği **göndermez** (`CUTOVER_BLOCKED`). Host, kapatılmak istenen güne ait açık bir
yetkilendirmesi veya açık bir yatırması varsa kesimi `rc=95` ile reddeder. Cevap gelmezse
gün kapanmaz ve terminal tarihini değiştirmez.

**Gerekçe:** İki taraf da kendi bildiği açık işi görür ve karşı taraf onu göremez.
Terminal, hostun defterine ulaşmamış bir bildirimi bilir; host, terminalin hiç göndermediği
bir yetkilendirmeyi bilir. İkisi birden kontrol edilmezse, kapanmış bir güne geç bir
bildirimin düşebileceği bir pencere kalır — ve o bildirim, defteri kapanmış bir günün
altına para yazar.

İkisi birden kontrol edildiğinde pencere bir kuralla değil, **bir sırayla** kapanır: açık
iş varken gün kapanamadığı için, kapanmış bir güne ait geç bildirim diye bir şey olamaz.

Ayrıca "kuyruk doluyken zaten toplamlar tutmaz" doğrudur ama yeterli değildir: tutmama
sebebi *fark* değil *gecikme*dir. İkisi aynı `rc` ile karışırsa, gerçek bir fark "herhalde
kuyruktandır" diye geçiştirilir. Bu yüzden terminal o durumda hiç sormaz.

**Reddedilen alternatif:** Günü her hâlükârda kapatıp açık kalemi bir askı hesabına
(suspense) düşürmek — gerçek bankacılıkta olan budur. Reddedildi çünkü askı hesabını,
yaşlanmasını ve elle çözülmesini modellemiyoruz; modellemediğimiz bir şeyi varmış gibi
göstermek, olmayan bir güvence vermektir. Basitleştirme `reports/assumptions.md`'de
etkisiyle birlikte yazılı.

**Etkilediği dosyalar:** `src/Atm.Terminal/CutoverFlow.cs`, `src/Atm.Host/HostService.cs`,
`docs/protocol.md` §4.7, `reports/assumptions.md`

---

## KARAR-047 — Hesap dosyası bir **anlık görüntüdür**; doğruluk kaynağı defterdir ve ikisi
açılışta karşılaştırılır

**Tarih:** 2026-08-25

**Karar:** Hesap bakiyeleri artık diskte (`_veri/host-accounts.jsonl`). Ama bu dosya
**tek doğruluk kaynağı değildir.** Doğruluk kaynağı, eklemeli olan ve hiçbir zaman
yeniden yazılmayan defterdir. Host açılırken defteri açılış bakiyelerinden itibaren
yeniden oynatır, çıkan bakiyeleri dosyadakiyle karşılaştırır ve **fark varsa portu hiç
açmadan durur.** Blokeler de aynı şekilde karşılaştırılır: dosyadaki bloke, defterde
kapanmamış sözlerin toplamına eşit olmalıdır. Host, kapanmamış yetkilendirmeleri,
kapanmamış yatırmaları ve kapatılmış günleri de açılışta defterden geri kurar.

**Gerekçe:** Bakiyeler bellekte olduğu sürece host'un her yeniden başlatılışı **para
basıyordu** — herkesin sabah bakiyesi geri geliyordu, sessizce, ve ekranda her şey
kusursuz görünüyordu. Bu kapatılması gereken bir açıktı.

Ama sadece "sözlüğü dosyaya yaz" demek, aynı parayı iki ayrı yerde tutup hiç
karşılaştırmamak olurdu. Karşılaştırılmayan iki kayıt, iki kayıt değildir: bir kayıt ve
bir söylentidir. Defterin eklemeli olmasının bütün bedeli tam olarak bunun için ödendi —
**yeniden üretilebilen bir kayıt kayıttır, üretilemeyen bir kayıt log'dur.**

Güven yönü tercih değil, yapı gereğidir: defter eklemelidir, her satırı paranın hareket
ettiği anda yazılmıştır ve sonraki hiçbir olay önceki bir satırı değiştiremez. Hesap
dosyası ise her hareket üzerine baştan yazılan bir anlık değerdir. İkisi çeliştiğinde
bozulmuş olan, üzerine yazılandır.

**Onarmıyoruz, duruyoruz.** Kendi defterine bakıp hesap dosyasını sessizce düzelten bir
host, yanlış olduğu hiçbir zaman yakalanamayan bir hosttur — ve bozuk olan defterse,
"onarım" gerçekte ne olduğunun tek kanıtını siler (kural §6b (docs/proje-kurallari.md)).

**Reddedilen alternatifler.**
(a) *Sadece dosyaya yazmak, karşılaştırmamak.* Daha az iş; ama yarım kalmış bir yazma ya
da elle yapılmış bir düzeltme hiçbir zaman fark edilmezdi.
(b) *Dosyayı hiç tutmayıp her açılışta defteri baştan oynatmak.* Kavramsal olarak en temiz
seçenek ve tek kayıt bırakırdı — ama karşılaştıracak ikinci bir şey kalmazdı, ve gün sayısı
büyüdükçe açılış süresi defterin boyuyla büyürdü.
(c) *Veritabanı.* KARAR-013'te kapsam dışı bırakıldı; kararı değiştiren bir şey olmadı.

**Etkilediği dosyalar:** `src/Atm.Host/FileAccountStore.cs` (yeni),
`src/Atm.Host/LedgerRebuild.cs` (yeni), `src/Atm.Host/JournalReplay.cs` (yeni),
`src/Atm.Host/HostService.cs`, `src/Atm.Host/Program.cs`, `scripts/demo.sh`,
`tests/Atm.Tests/RestartTests.cs`

---

## KARAR-048 — Defter satırı **hesabı** adlandırır, kartı değil

**Tarih:** 2026-08-25

**Karar:** `JournalEntry` yeni bir alan taşıyor: `AccountId`. Hostun iki mesaj arasında
hatırladığı her şey — açık yetkilendirme, açık yatırma — artık kartı değil **hesabı**
adlandırıyor. Maskeli kart numarası satırda kalmaya devam ediyor, ama bir kimlik olarak
değil, kaydı okuyan insan için.

**Gerekçe:** İki sebep, ikisi de bağımsız olarak yeterli.

Birincisi alan gerçeği: **defter hesaba yazar, karta değil.** Bir hesaba birden çok kart
bağlanabilir; kart, müşterinin hesaba ulaşma yoludur. Yetkilendirme de hesabın parasını
bloke eder, kartın değil.

İkincisi teknik ve daha keskin: maskeleme **bilerek kayıplıdır.** Deftere yazılan kart
numarası `411111******1111` biçimindedir ve iki farklı kart aynı maskeye düşebilir.
KARAR-047 bakiyeleri defterden yeniden üretiyor; kimliği maskeli kart olan bir kayıttan
yeniden üretim, iki müşteriyi birbirine toplayabilirdi. Yani "kartı sakla" seçeneği,
maskelemeyi bozmadan çalışamazdı — ve maskelemeyi bozmak KARAR-010'u çiğnemekti.

**Reddedilen alternatif:** Deftere tam kart numarasını yazmak. Kimlik sorununu çözerdi ve
projenin en temel güvenlik kuralını çiğnerdi (kural §2 (docs/proje-kurallari.md)). Bu alternatif, doğru cevabın
zaten hesap olduğunu gösteren şeydir: kimliği yanlış yerde aradığımızda tek çıkış yolu bir
kuralı çiğnemek oluyordu.

**Bir uyarı da kaydedildi:** Bu değişiklikten önce yazılmış defterlerde `AccountId` alanı
yoktur. Böyle bir defter bakiyelerle karşılaştırılamaz, ve **sessizce atlanmaz** —
`LedgerRebuild` böyle satırları sayar ve host'u durdurur. Eski bir kaydı yarım inanmak
yerine arşivlemek gerekir: `./scripts/demo.sh --yeni-gun`.

**Etkilediği dosyalar:** `src/Atm.Host/IJournal.cs`, `src/Atm.Host/FileJournal.cs`,
`src/Atm.Host/HostService.cs`, `src/Atm.Host/IAccountStore.cs`,
`src/Atm.Host/JournalReplay.cs`, `src/Atm.Host/LedgerRebuild.cs`

---

## KARAR-049 — Senaryo dosyası biçimi: üç eksen, sıralı adımlar, önceden yazılmış beklenti

**Tarih:** 2026-08-25

**Karar:** Arıza senaryoları `scenarios/*.json` altında, `docs/scenarios.md`'deki sözleşmeye
göre yazılır. Bir senaryo dört şey taşır: **kimlik ve seed** (birebir yeniden üretim),
**eksen** (`işlem × arıza × an` — kapsama matrisinin koordinatları), **sıralı adımlar**
(işlem, hat arızası, makine arızası, zaman, kuyruk), ve **koşudan önce yazılmış beklenti**
(müşteriye giden, hesap farkı, denetim verdikti, **tespit noktası**).

**Gerekçe — üç ayrı karar var ve her biri ayrı gerekçeyi hak ediyor.**

**(a) Neden dosyada, kodda değil.** kural §5 (docs/proje-kurallari.md) böyle diyor, ama sebebi şu: kapsama matrisi
senaryoların **kendisinden** üretilecek. Kodun içine gömülü senaryoları saymak, kodu okuyup
yorumlamak demektir — ve "kaç senaryo koştuk" sorusunun cevabı yoruma açık olamaz. Ayrıca
bir senaryo eklemek kod değiştirmeyi gerektirseydi, her yeni senaryo "senaryoyu mu ekledim,
davranışı mı değiştirdim" sorusunu doğururdu.

**(b) Neden `eksen` ayrı bir alan ve neden değer listesi kapalı.** Kapsama, isimlerden
tahmin edilerek değil, açıkça yazılan koordinatlardan üretilir. Liste kapalı olduğu için,
yeni bir arıza tipi eklemek **bilinçli bir karardır**: matrise yeni bir boyut değeri girer
ve o boyutun bütün boş hücreleri raporda görünür hâle gelir. Açık bir liste, kapsamayı
sessizce seyreltmenin en kolay yoludur — yeni bir isim uydurup tek senaryo yazarsınız ve
matris "yeni bir alan kapsandı" der.

**(c) Neden `tespit` alanı var.** "Kaç ihlal çıktı" tek başına eksik bir ölçüdür. Asıl
soru şudur: **ihlali kim, ne zaman fark ediyor.** Anında görülen bir fark ile gün sonunda
görülen bir fark ile hiç görülmeyen bir fark, aynı sayının üç çok farklı hâlidir — ve
üçüncüsü bu projenin bulabileceği en kötü sonuçtur. kural §3 (docs/proje-kurallari.md) "tespit noktası"nı ölçülecek
şeylerden biri olarak sayıyor; bu alan onu ölçülebilir yapıyor.

**Reddedilen alternatifler.**
- *Senaryoları xUnit testleri olarak yazmak.* En az iş, çalışan altyapı. Reddedildi:
  kapsama matrisi test isimlerinden çıkarılamaz, ve senaryo eklemek derleme gerektirirdi.
- *Beklentiyi koşudan sonra doldurmak (altın çıktı / golden file).* Çok yaygın ve burada
  yanlış: bu projenin ürünü, kodun ne yaptığı değil, kodun **ne yapması gerektiği** ile
  arasındaki farktır. Çıktıyı beklenti ilan etmek o farkı tanım gereği sıfırlar.
- *Beklentiye defter satırlarını yazmak.* Kırılgan: hiçbir davranış değişmeden, tek bir
  not metni değişince bütün senaryolar kırmızı yanardı. Defter düzeyindeki iddialar
  testlerin işi; senaryolar davranışın işi.

**Etkilediği dosyalar:** `docs/scenarios.md` (yeni), `scenarios/*.json`, Faz 4'te yazılacak
koşucu, `reports/failure-catalog.md`

---

## KARAR-050 — Ters kaydı yapılmış bir işlem bir daha yetkilendirilemez

**Tarih:** 2026-08-25

**Karar:** Host, ters kaydını onayladığı her işlem kimliğini işaretler. O kimlikle gelen
**yeni bir yetkilendirme isteği** — çekim ya da yatırma — `rc=12` ile reddedilir, ve bu
kontrol tekrar tablosuna bakılmadan **önce** yapılır. İşaret, host yeniden başlatılırsa
defterden geri kurulur.

**Bu bir bulgudur, bir tasarım değil.** Senaryo `C-CI-YETKI` bu davranışı ölçmek için
yazıldı, beklentisi koşudan **önce** yazıldı, ve kod beklentiyi karşılamadı: 350 lira
müşteriye gitti, hiçbir hesaptan düşülmedi, denetleyici anında yakaladı.

**Ne oluyordu.** Yetkilendirme onaylandı, cevabı kayboldu, terminal ters kayıt gönderdi,
host blokeyi çözdü. Aynı kimlik ikinci kez geldiğinde tekrar tablosu **hatırladığı onayı**
döndürdü — o onay verildiği anda doğruydu, ters kayıt onu yanlış yapmıştı. Terminal
yetkilendirildiğini sandı, parayı verdi; dağıtım bildirimi geldiğinde hostta açık bir
yetkilendirme yoktu ve bildirim reddedildi. Nakit dışarıda, defterde karşılığı yok.

**Gerekçe.** Tekrar tablosunun görevi, aynı işin iki kez yapılmasını önlemektir (KARAR-032);
"verdiğim cevap hâlâ geçerli mi" sorusunu cevaplamaz. Ters kayıt, cevabı geçersiz kılan
olaydır ve bir yerde kaydedilmesi gerekir.

**Neden reddetmek, yeniden onaylamak değil.** Alternatif, ikinci isteği sıfırdan
değerlendirmekti: bakiye yeter, yeni bir bloke koy, devam et. Reddedildi, çünkü aynı işlem
kimliği altında iki ayrı para hareketi olurdu ve kayıt çift anlamlı hâle gelirdi — bu
projenin kimliğe yüklediği bütün ağırlık (KARAR-009, KARAR-031) o çift anlamı önlemek
üzerine kurulu. Gerçek switch'ler de kapatılmış bir işlem numarasının yeniden sunulmasını
kabul etmez; terminalin yeni bir STAN'a geçmesi beklenir.

**Bu gerçek dünyada olur mu (kural §9.1 (docs/proje-kurallari.md)).** Terminal normalde STAN'ı artırır. Ama STAN altı
hanelidir ve 999999'dan sonra başa döner — aynı iş gününde çakışma ihtimali
Zaten açık bir soru olarak duruyor. Ayrıca sayacını
kaybederek yeniden başlayan bir terminal de numaraları tekrar kullanır.

**Etkilediği dosyalar:** `src/Atm.Host/HostService.cs`, `src/Atm.Host/JournalReplay.cs`,
`tests/Atm.Tests/ReversalTests.cs`, `tests/Atm.Tests/RestartTests.cs`,
`scenarios/C-CI-YETKI.json`, `reports/failure-catalog.md` (A-02)

---

## KARAR-051 — Naif akış ayrı bir kod kopyası değil, kapatılabilir yedi anahtardır

**Tarih:** 2026-08-25

**Karar:** Alan gerçeklerinin her biri (`kural §4.1 (docs/proje-kurallari.md)–§4.7`) `Hardening` kaydında bir
mantıksal anahtarla temsil edilir. Naif koşu, aynı kodu bu anahtarlar kapalı çalıştırır.
Varsayılan **hepsi açık**; çalışan host ve terminal her zaman `Hardening.Full` kullanır ve
naif mod demoda erişilebilir değildir. Rapor iki tablo basar: hepsi kapalı karşılaştırması,
ve **her anahtarın tek başına** kapatıldığı tablo.

**Gerekçe.** kural §16.5 (docs/proje-kurallari.md) "naif akış ile sertleştirilmiş akışın eşleşen koşulda
karşılaştırması"nı istiyor. Eşleşen koşul, aynı senaryo dosyaları ve aynı seed'ler demektir;
ikinci bir kod kopyası bunu ilk gün sağlar, üçüncü ay sağlamaz — kopyalar kayar ve
karşılaştırma, "bir kuralı olmayan aynı program" ile değil, "ayrıca yanlış olan başka bir
program" ile yapılmaya başlar. Tek program + yedi anahtar, kaymanın imkânsız olduğu tek
düzendir.

**Neden ikinci tablo (kural kural) zorunlu.** Hepsi kapalı sayısı, en çok senaryoyu bozan
anahtarın gölgesinde kalır: tekrar bağışıklığı tek başına kapatıldığında senaryoların
yarısından fazlası bozuluyor ve geri kalan altı kural bedavaymış gibi görünüyor. "Hangi
kural neyi ayakta tutuyor" sorusunun cevabı ancak tek tek kapatılarak alınır.

**Reddedilen alternatifler.**
- *`git checkout` ile eski bir sürümü koşmak.* Gerçekten "naif" bir kod verirdi ama eski
  sürümde senaryo koşucusu yok; eşleşen koşul kurulamazdı.
- *Naif akışı ayrı bir projede yazmak.* Yukarıdaki kayma sorunu.
- *Anahtarları varsayılan kapalı yapmak.* Hatırlanması gereken bir anahtar, bir gün
  unutulan anahtardır; `new()` her zaman güvenli yapılandırma olmalı.

**Bilinen sınır (raporda yazılı):** gerçek bir naif geliştirici yedi kuralı birden bilmezdi
ama bizim modellemediğimiz başka hatalar da yapardı. "Hepsi kapalı" sütunu bir alt sınır
değil, bir örnektir. `reports/naive-mode.md`.

**Etkilediği dosyalar:** `src/Atm.Protocol/Hardening.cs` (yeni),
`src/Atm.Terminal/WithdrawalFlow.cs`, `src/Atm.Terminal/DepositFlow.cs`,
`src/Atm.Host/HostService.cs`, `src/Atm.Audit/SimulatedDay.cs`,
`src/Atm.Audit/ScenarioRunner.cs`, `src/Atm.Audit/ScenarioReport.cs`,
`reports/naive-mode.md` (yeni)

---

## KARAR-053 — Demo arıza tetikleyicisi: ekran "operatör bastı" der, ne olacağına terminal karar verir

**Tarih:** 2026-08-25

**Karar:** Ekran sözleşmesine `demo` adında bir olay eklendi. Değeri bir arıza **adıdır**
(`hat-kes`, `cevap-yut`, `eksik-ver`, `sikisma`, `hat-gelsin`, `makine-duzelsin`), bir emir
değil. Tarayıcıda gizli bir servis paneli var (Ctrl+Shift+S ya da banka adına üç dokunuş).
Anahtarların hangisi açık olduğunu `DemoFaults` tutar; **etkilerini** `Atm.Terminal/Program.cs`
uygular — gerçek hattı ve gerçek dağıtıcıyı orada sarmalayarak. Açık olan her arıza,
`ScreenView.Demo` alanıyla **her ekrana** damgalanır.

**Gerekçe.** kural §7 (docs/proje-kurallari.md) sunumda "şimdi hattı kesiyorum" denip kesilebilmesini istiyor.
Bunsuz canlı demo yalnızca mutlu yolu gösterebilir, ve yalnızca mutlu yolu gösteren bir
simülatörün GitHub'da yüzlercesi var (§8).

İş bölümü kasıtlı ve kural §8 (docs/proje-kurallari.md)'in "iş mantığını tarayıcıya kaçırmak" maddesinin doğrudan
karşılığı: tarayıcı yalnızca bir kelime gönderir, o kelimenin ne anlama geldiğine terminal
karar verir. Tarayıcı "hattı kes" komutunu **uygulasaydı** — örneğin mesaj göndermeyi
bıraksaydı — makinenin davranışının bir kısmı müşterinin değiştirebileceği bir yerde
yaşıyor olurdu.

Arızaların etkisinin akışta değil **kenarda** uygulanması da aynı sebeple: bir hattın kopması
ve bir dağıtıcının sıkışması, makine hakkında birer olgudur, işlem hakkında birer karar
değil. Akışın içine konsaydı, para çekme mantığının içinde "demoda mıyım" sorusu dururdu.

**Her ekrana damgalanması pazarlığa kapalı.** Arızası sessizce açık kalmış bir gösteri
makinesi, bir gün birine normal çalışıyormuş gibi gösterilecek makinedir — ve bu, demonun
çökmesinden kötüdür, çünkü sistem hakkında kimsenin yanlış olduğunu göremeyeceği bir iddiadır.

**Anahtar geri sayımı sıfırlamaz.** Aynı ekranı yeniden çizmek onun geri sayımını yeniden
başlatırdı; bunun en çok önem taşıdığı ekran, sessizliğin ret sayıldığı yatırma onay ekranıdır
(KARAR-042). Testle korunuyor.

**Reddedilen alternatifler.**
- *Arıza tetikleyicisini terminal konsoluna koymak.* Sunumda ekrandan konsola geçmek gerekirdi
  ve izleyici, arızanın nereden geldiğini göremezdi.
- *Senaryo dosyalarını demodan çalıştırmak.* Senaryolar deterministik ve sanal saatlidir;
  canlı demo gerçek saatte ve insan hızında akar. İkisini birleştirmek her ikisini de bozardı.
- *Tarayıcının arızayı kendisinin uygulaması.* En az kod, ve KARAR-024'ün ihlali.

**İki hata bu karar sırasında bulundu ve düzeltildi:**
(a) ekran sözleşmesinin tanıdığı olay listesine `demo` eklenmemişti — servis panelinin ilk
tuşu bağlantıyı sessizce kapatıyordu; (b) host ayakta değilken terminal yığın iziyle
çöküyordu, bir cümle yazması gerekirken.

**Etkilediği dosyalar:** `src/Atm.Terminal/DemoFaults.cs` (yeni),
`src/Atm.Terminal/ScreenMessage.cs`, `src/Atm.Terminal/TerminalFlow.cs`,
`src/Atm.Terminal/Program.cs`, `src/Atm.Terminal/wwwroot/index.html`,
`tests/Atm.Tests/DemoFaultTests.cs` (yeni), `tests/Atm.Tests/ScreenServerTests.cs`,
`scripts/ekran-provasi.py` (yeni)

---

## KARAR-054 — Ekranın bilmediği bir kelime makineyi öldürmez; kabul edilen kupür önceden söylenir

**Tarih:** 2026-09-06 · **Bağlam:** sunum öncesi elle uçtan uca deneme.

İki bulgu çıktı, ikisi de akış mantığında değil, makinenin dış dünyayla temas ettiği yerde.

**(a) Tanınmayan ekran komutu terminali çökertiyordu.** `DemoFaults.Apply` bilmediği bir
komutta `ArgumentException` atıyor — bu doğru ve testte öyle kalmalı. Ama `Program.cs` bu
istisnayı yakalamıyordu: tarayıcıdan gelen tek bir bozuk kelime, çalışan ATM sürecini yığın
iziyle sonlandırıyordu. Karar: **dış dünyadan gelen girdi hatası sınırda yakalanır**, konsola
yüksek sesle yazılır ve makine ayakta kalır; ekran neyi gösteriyorsa onu göstermeye devam eder.
Bu, KARAR-006'daki "değişmezlik ihlali yüksek sesle hata verir" kuralıyla çelişmez: burada
ihlal edilen bir değişmezlik yok, tanınmayan bir girdi var.

**(b) Yatırma ekranı kabul ettiği kupürleri söylemiyordu.** Makinenin yalnızca geri dönüşüm
kaseti olan kupürleri (50 ve 100 TL) kabul etmesi doğrudur ve `CashAcceptor.AcceptIntoEscrow`
bunu zaten yapıyor; 200'lük banknot ağızdan geri çıkıyor. Ama bunu müşteri ancak parayı
attıktan sonra öğreniyordu ve bu, dışarıdan arıza gibi okunuyor — sunumda da öyle okundu.
Karar: makine kabul ettiği kupürleri **banknotlar içeri girmeden önce** ekranda söyler.

Kupür listesi ekranda hesaplanmaz; terminal, kaset takımından okuyup `TerminalFlow`'a verir
(KARAR-024: ekran karar vermez). Parametre isteğe bağlıdır, verilmezse satır hiç yazılmaz —
yatırmayla ilgilenmeyen hiçbir test etkilenmez.

**Reddedilen alternatifler.**
- *200'lük kaseti geri dönüşümlü yapmak.* Nakit modelini, senaryoları ve dağıtım
  davranışını değiştirirdi; ölçüm sonuçları yeniden koşulmadan geçersiz olurdu.
- *Tarayıcıdaki 200 TL düğmesini kaldırmak.* Reddedilen banknot yedi kovadan biridir; onu
  ekrandan silmek, gösterebildiğimiz gerçek bir davranışı gizlemek olurdu.
- *Bilinmeyen komutu sessizce yutmak.* Hatayı görünmez yapardı; konsola yazılıyor.

**Doğrulama:** 373 test yeşil, 30 senaryo beklendiği gibi, `check.sh` sıfır farkla kapandı;
ayrıca tarayıcı yerine WebSocket'ten sürülen elle deneme ile bakiye, çekim, "diğer tutar",
yanlış PIN, kısmi dağıtım, yatırma (kabul/karışık/ret), yatırmada vazgeçme ve arıza
tetikleyicileri tek tek koşuldu.

**Etkilediği dosyalar:** `src/Atm.Terminal/TerminalFlow.cs`, `src/Atm.Terminal/Program.cs`

---

## Buradan sonrası: dokümantasyon üretimiyle ilgili kararlar

KARAR-054 ve sonrası, projeyi anlatan dokümanların nasıl üretildiğine dair kararlardır
(cilt ayrımı, okuma modları, kod örneklerinin dosyadan doldurulması, satır yerine metin
çapası). Bu kopya çalışan sistemi teslim ettiği için o kararlar buraya alınmadı: bu depoda
karşılığı olan dosya yok ve karşılığı olmayan bir karar kaydı, okuyanı var olmayan bir
dosyaya gönderir.

Koda dokunan bütün kararlar (KARAR-001 … KARAR-053) yukarıdadır.
