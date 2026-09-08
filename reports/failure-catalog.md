# Arıza kataloğu

**Projenin ürünü bu dosyadır.** Her arıza sınıfı, bir senaryo ve bir testle bağlanır.

Her kayıt şu alanları taşır:
kimlik · tetikleyici · yanlış davranış · doğru davranış · tespit noktası ·
senaryo dosyası · test adı · §10'un üç cevabı (gerçek mi / sahtekârlıkla üretilebilir
miydi / 15 yıllık biri ne sorar).

---

> **Durum (2026-08-25, Faz 4):** senaryo koşucusu geldi. İki arıza sınıfı kayıtlı, ikisi de
> bir senaryo dosyasına ve bir teste bağlı. Kapsama matrisi ve naif akış karşılaştırması
> `reports/runs/4-senaryolar-ve-naif-karsilastirma.txt` içinde; anahtarların ne yaptığı
> `reports/naive-mode.md`'de.

## A-01 — Müşteriye giden nakdin hiçbir deftere yazılmaması

**Kimlik:** A-01 · sınıf: karşılıksız nakit çıkışı (unposted dispense)

**Tetikleyici:** Hostun "bu isteği zaten cevaplamıştım" tablosunun yalnızca işlem
kimliğiyle anahtarlanması. Yetkilendirme ile dağıtım bildirimi **aynı işlem kimliğini**
taşıdığı için host, bildirimi yetkilendirmenin tekrarı sanır ve yetkilendirmeye verdiği
eski cevabı geri döner.

**Yanlış davranış:** Defter hiç hareket etmez. Bloke sonsuza kadar açık kalır. Terminal
`00` cevabı aldığı için her şeyin yolunda olduğunu düşünür; ekranda hiçbir uyarı yoktur;
müşteri parayı alır ve hesabından hiçbir şey düşülmez.

**Doğru davranış:** Tekrar tablosunun anahtarı işlem kimliği **artı mesaj tipidir**
(KARAR-031). Bildirim, yetkilendirmenin tekrarı değildir; defteri o hareket ettirir.

**Tespit noktası:** İhlal anında **değil.** Sistem hiçbir hata üretmez. Yakalanma yeri
gün sonu mutabakatıdır: `MUSTERI-DEFTER` ve `MUTABAKAT-FARKI` kontrolleri
(`src/Atm.Audit/ConservationChecker.cs`). Denetleyici olmadan tespit noktası **kasa sayan
insandır** ve o da ertesi gündür.

**Ölçüm (2026-08-25, seed 20260825, 12 çekim):** hata koda geri konduğunda
`./scripts/check.sh` **10 açıklanamayan fark** buldu; müşterilere giden 55.000 kuruşun
tamamı hiçbir hesaptan düşülmemişti ve yedi yetkilendirme gün sonunda açık kalmıştı.
Ham çıktı: `reports/runs/2g-sabotaj-KARAR-031-geri-alindi.txt`. Düzeltilmiş kodda aynı
koşu: `reports/runs/2g-temiz-gun-seed20260825.txt` — fark yok.

**Senaryo dosyası:** (henüz yok — Faz 4)

**Test adı:** `HostServiceTests` içinde tekrar tablosu testleri; mutabakat tarafında
`ConservationTests.CashHandedOverWithNoDebitBehindItIsReported` ve
`...TheTwoRecordsDisagreeingAboutOneTransactionIsReported`.

**§9'un üç cevabı:**

1. **Gerçek dünyada karşılığı var mı?** Evet. Aynı işlem numarasını taşıyan farklı mesaj
   tiplerinin tekrar denetiminde karıştırılması, ödeme sistemlerinde bilinen bir hata
   sınıfıdır — bizim modelimizin eseri değil, iki mesajlı akışın doğal tuzağı. Not: tek
   mesajlı akış kullanan sistemlerde bu sınıf **yoktur**; bizde olması KARAR-029'un
   bilinçli bedelidir ve `reports/assumptions.md`'de yazılı.
2. **Bu sonucu bir sahtekârlıkla üretebilir miydim?** Kontrol edildi. Denetleyicinin altı
   kontrolü tek tek bozuldu; on sabotajın onunda da en az bir test kırmızı yandı — yani
   denetleyici her şeyi onaylayan bir kabuk değil. Ayrıca "naif akış" özel olarak zayıf
   yazılmadı: bozulan tek satır, gerçekten yazılmış ve gerçekten düzeltilmiş olan
   `MessageKey` satırıdır (KARAR-031).
3. **15 yıldır bu işi yapan biri ne sorar?** Muhtemelen: *"neden iki mesaj kullanıyorsunuz,
   biz bunu tek mesajda çözüyoruz?"* Cevabımız KARAR-029'da yazılı ve seçimin bedeli —
   asılı bloke ve bu arıza sınıfı — bilinçli olarak kabul edildi. Soru
   açık sorular listesi 15'te duruyor.

## A-01 için ikinci tespit noktası — gün sonu kesimi (2026-08-25, Faz 3e)

A-01'in tespit noktası kataloğa ilk yazıldığında yalnızca **denetleyici** idi. Faz 3e'de
gün sonu kesimi yazıldıktan sonra aynı ölçüm tekrarlandı: KARAR-031'de düzeltilen hata koda
geri kondu ve `./scripts/check.sh` yeniden koşuldu.

**Sonuç: kesim de yakalıyor, ama başka bir şey söyleyerek.**

| | Denetleyici | Gün sonu kesimi |
|---|---|---|
| Ne gördü | 10 açıklanamayan fark, her biri işlem numarasıyla | gün kapanmadı; "o güne ait 7 işlem hâlâ açık", iki taraf arasında 55.000 kuruş fark |
| Neye bakabiliyor | iki deftere birden | yalnızca kendi defterine + karşı tarafın üç sayısına |
| Gerçek bir ağda var mı | **hayır** — kimse iki defteri aynı anda göremez | **evet** — iki makinenin birbirine kanıtlayabildiği tek şey bu |

Bu, kataloğa yazılmaya değer bir ayrımdır: bu projenin ürettiği kanıt (denetleyici) ile
gerçek bir bankanın elindeki kanıt (kesim) aynı şey değildir, ve **aynı hatayı ikisi de
yakalıyor.** Denetleyici hangi işlemde olduğunu söylüyor; kesim yalnızca farkın var
olduğunu söyleyebiliyor — ama o, sahada gerçekten çalışan mekanizma.

Ham çıktılar: `reports/runs/3e-sabotaj-A01-kesim-de-yakaliyor.txt` (hata koda geri
konmuş hâli) ve `reports/runs/3e-temiz-gun-kesimli-seed20260825.txt` (temiz hâli, gün sıfır
farkla kapanıyor).

**§9'un üç sorusu.**
1. *Gerçek dünyada karşılığı var mı?* Var. Mutabakat farkı ve kapanmayan gün, gerçek ATM
   işletmeciliğinin günlük konularıdır.
2. *Bu sonucu bir sahtekârlıkla üretebilir miydim?* En kolay sahtekârlık, hostun terminalin
   gönderdiği toplamı kaydedip kendi rakamı diye karşılaştırmasıydı — o hâlde kontrol asla
   başarısız olamazdı. Bu ihtimal KARAR-045'te reddedildi ve `HostDayTotals` ayrı bir
   dosyada, terminalin gönderdiği gövdeye hiç bakmadan sayıyor. Sabotajla doğrulandı: host
   terminalin rakamını kopyalar hâle getirildiğinde `ADifferenceRefusesTheCloseAndSaysHowBig`
   kırmızı yandı.
3. *15 yıldır bu işi yapan biri ilk ne sorar?* Muhtemelen: "gün kapanmıyorsa şube ne
   yapıyor?" Bizim cevabımız yok — gerçekte açık kalem askı hesabına düşer ve bunu
   modellemiyoruz. Açık soru.

---

## A-02 — Ters kaydı yapılmış bir işlemin yeniden yetkilendirilmesi

**Kimlik:** A-02 · sınıf: geçersizleşmiş onayın tekrar tablosundan geri dönmesi
(stale approval replay)

**Tetikleyici:** Bir yetkilendirme onaylanır, cevabı kaybolur, terminal ters kayıt gönderir
ve host blokeyi çözer. **Aynı işlem kimliği ikinci kez** yetkilendirme istediğinde, tekrar
tablosu hatırladığı "onaylandı" cevabını döndürür.

**Yanlış davranış:** Terminal yetkilendirildiğini sanır ve parayı verir. Dağıtım bildirimi
geldiğinde hostta o işleme ait açık bir yetkilendirme yoktur (ters kayıt kapatmıştır) ve
bildirim reddedilir. **Nakit dışarıda, defterde karşılığı yok.** Ölçüldü: 350 lira.

**Doğru davranış:** Ters kaydı yapılmış bir işlem kapanmıştır. O kimlikle gelen yeni bir
yetkilendirme isteği `rc=12` ile reddedilir; kontrol tekrar tablosundan **önce** yapılır.
Terminalin yeni bir izleme numarasına geçmesi beklenir. KARAR-050.

**Tespit noktası:** **Anında.** Denetleyici, senaryo biter bitmez (kuyruk boşalmadan)
`MUTABAKAT-FARKI` ile yakalıyor — çünkü terminal para verdiğini yazmış, host düşmemiştir ve
farkı açıklayacak kuyrukta bir şey yoktur.

**Senaryo:** `scenarios/C-CI-YETKI.json`

**Test:** `ReversalTests.AReversedTransactionCannotBeAuthorisedAgain` ·
yeniden başlatma tarafı: `RestartTests.AReversedTransactionIsStillClosedAfterARestart`

**§9'un üç cevabı.**

1. *Gerçek dünyada karşılığı var mı?* Var, ama dar bir kapıdan. Terminal normalde STAN'ı
   artırır; aynı numaranın aynı iş gününde tekrar gelmesi için ya sayacın 999999'dan başa
   dönmesi ya da terminalin sayacını kaybederek yeniden başlaması gerekir. Birincisi zaten
   açık bir soru. **Dürüst hâli:** bu, günlük
   değil, nadir bir olaydır — ama sonucu tam olarak "hiçbir hata mesajı üretmeyen para
   kaybı" sınıfındadır.
2. *Bu sonucu bir sahtekârlıkla üretebilir miydim?* En kolay sahtekârlık, senaryoyu koddan
   sonra yazıp çıkanı "beklenen" ilan etmek olurdu. Tam tersi yapıldı: `C-CI-YETKI`'nin
   beklentisi koşulmadan önce yazıldı, koşuldu ve **kırmızı yandı**; düzeltilen kod oldu,
   beklenti değil (docs/scenarios.md §7). İkinci ihtimal, testi zayıf yazmaktı: sabotajla
   sınandı — işaret koyma satırı kaldırıldığında hem test hem senaryo kırmızı yanıyor.
3. *15 yıldır bu işi yapan biri ilk ne sorar?* Muhtemelen: *"terminaliniz neden aynı STAN'ı
   yeniden kullanıyor? Bizde bu zaten olmaz."* Cevabımız: normalde olmaz; biz de bu yüzden
   reddediyoruz, üretmiyoruz. Ama "bizde olmaz" bir varsayımdır ve host tarafında
   karşılanmayan her varsayım, terminalin bir gün ihlal edeceği varsayımdır.

---

## A-03 — Yeniden başlatılan terminalin işlem numarasını sıfırlaması

**Kimlik:** A-03 · sınıf: kimliği yeniden kullanılan işlem, farklı bir isteğe eski cevabın
dönmesi (identity reuse / mismatched replay)

**Bulunuş:** 2026-09-01. Sunum provası hazırlanırken, "terminali fişten çekip taksak ne
olur" diye denendi. A-02'nin §9 cevabında *"terminalin sayacını kaybederek yeniden
başlaması"* zaten olası bir kapı olarak yazılıydı; bu, o kapının gerçekten açık olduğunun
ölçülmesidir.

**Tetikleyici:** Terminal, işlem numarası sayacını yalnızca bellekte tutuyordu. Aynı iş
günü içinde yeniden başlatıldığında sayaç bire dönüyor, iş günü değişmediği için işlem
kimliği (terminal + iş günü + işlem numarası) daha önce kullanılmış bir kimlikle
çakışıyordu.

**Yanlış davranış:** Host, çakışan kimliği "aynı isteğin tekrarı" sayıp sakladığı cevabı
döndürüyordu — **isteğin içeriğinin farklı olduğuna bakmadan.** Ölçülen koşu: birinci
oturumda 500 TL çekildi ve hesaptan düştü; terminal yeniden başlatıldı; ikinci oturumda
100 TL istendi, host 500 TL'ye verdiği onayı geri döndürdü, makine 100 TL verdi ve hiçbir
hesap hareket etmedi. **Müşteriye giden 600 TL, hesaptan düşen 500 TL, açık 100 TL.**

**Doğru davranış — iki tarafta birden, çünkü her biri tek başına ötekini gizler:**

1. *Host:* Tekrar, ancak istek de aynıysa tekrardır. Saklanan cevabın yanında cevaplanan
   isteğin gövdesi de tutulur; gelen istek farklıysa cevap **verilmez**, `rc=12` ile
   reddedilir ve deftere "same transaction id, different request" yazılır. İdempotentlik
   "aynı şeyi iki kez yapma" der; "kullanılmış bir numarayla gelen her şeye o numaranın
   eski cevabını ver" demez.
2. *Terminal:* Çakışan kimlik hiç üretilmemelidir. İşlem numarası artık kendi günlüğünden
   yeniden kuruluyor: bugüne ait en büyük numara okunup sayaç oradan devam ediyor. Ayrı bir
   sayaç dosyası tutulmadı — kayıtla çelişebilen bir sayaç, ikinci ve sessiz bir kayıttır
   (KARAR-047'nin aynı gerekçesi).

**Tespit noktası:** **Anında** — para korunumu denetleyicisi `KARSILIKSIZ-BORC` ile
yakalıyor: makineden çıkan nakde karşılık gelen onaylanmış bir yetkilendirme yok.

**Test:** `ReusedTraceNumberTests` — beş test. İkisi hostun reddettiğini, biri gerçek bir
tekrarın hâlâ eski cevabı aldığını (düzeltme tekrarları bozmuyor), ikisi terminalin
günlükten devam ettiğini sabitliyor.

**§9'un üç cevabı.**

1. *Gerçek dünyada karşılığı var mı?* Var ve A-02'dekinden geniş bir kapıdır: bir ATM'nin
   gün içinde yeniden başlatılması olağandır (bakım, güç, yazılım güncellemesi). Gerçek
   sistemlerde işlem numarası kalıcıdır; bizde değildi.
2. *Bu sonucu bir sahtekârlıkla üretebilir miydim?* Buradaki risk tersiydi: hata,
   **aranırken değil, demo provası yapılırken** çıktı — yani senaryo sonuca göre yazılmadı.
   Bulgu, sıfırdan bir günle (`demo.sh --yeni-gun`) ve iki ayrı terminal oturumuyla
   yeniden üretildi; düzeltmeden sonra aynı koşu 600 TL çıkışa karşı 600 TL borçlanma
   veriyor, açık sıfır.
3. *15 yıldır bu işi yapan biri ilk ne sorar?* Muhtemelen: *"STAN'ı neden kalıcı
   tutmuyorsunuz, bu ilk günde çözülür."* Cevap: haklı, çözülmemişti; şimdi çözüldü ve
   ayrıca host tarafına da bir kapı kondu — çünkü terminalin doğru davranacağı varsayımı,
   hostun kontrol etmediği her yerde bir gün ihlal edilir.

---

## Kapatılamayan senaryolar

Faz 4 sonunda, sertleştirilmiş akışta **kapatılamayan dengesizlik yok**: 30 senaryonun
30'u temiz bitiyor. Bu, "her şey kapsandı" demek değil — kapsanmayanlar aşağıda.

**Kapsanmayan hücreler (33/61).** Tam liste her koşuda basılıyor:
`reports/runs/4-senaryolar-ve-naif-karsilastirma.txt`. Öne çıkanlar ve neden açık kaldıkları:

- `yatirma × cift-istek × *` — yatırmada çift istek senaryosu yok. Çekimdeki karşılığı
  (`C-CI-YETKI`) yazıldı ve bir bulgu üretti; yatırma tarafının aynı kapıyı açıp açmadığı
  **ölçülmedi.**
- `yatirma × host-yeniden-baslatma × onay/bildirim/ters-kayit` — yalnızca `escrow` anı
  koşuldu.
- `cekim × cift-istek × bildirim` ve `× ters-kayit` — bildirimin ve ters kaydın ikinci kez
  gelmesi testlerle kapsanıyor (`ReversalTests`, `DispenseAdviceTests`) ama senaryo
  düzeyinde koşulmadı.
- `gun-sonu × hat-kopmasi × kesim` — kesim anında hattın tamamen kopması; `cevap-kaybi` ve
  `istek-kaybi` koşuldu, tam kopma koşulmadı.

**Modelin kapsayamadığı şeyler** (senaryo yazılarak kapanmaz, `reports/assumptions.md`):

- İade edilen banknotun müşteri tarafından alınmaması (yatırmada `retract` karşılığı).
- Kasete alma sırasında elektrik kesintisi — niyet günlüğe yazılarak **görünür** kılındı
  (KARAR-041) ama pencere kapanmadı.
- Kesimi başlatan bir saat yok; "operatör kesimi unuttu" üretilemiyor.
- Eşzamanlılık yok: adımlar sıralı. Gerçek bir ATM'de iki şey aynı anda olabilir.
- Nakit sayım farkı yok: kaseti açıp sayan insan modelde yok.
