# Mesaj sözleşmesi (protokol)

Sürüm: `ATMSIM/0.1`
Durum: **yazıldı** — 2026-08-24, Faz 0.

> **invented for simulation** — Bu protokol bu proje için uydurulmuştur. Hiçbir kurumun
> mesaj spesifikasyonundan, switch dokümanından veya iç standardından kopyalanmamıştır.
> Kamuya açık ödeme standartlarının **genel mantığından** esinlenir: her isteğin bir
> izleme numarası taşıması, cevapsız kalan bir isteğin ters kayıtla kapatılması, işlem
> kimliğinin tekrarları yakalaması. Alan adları, sayılar ve kodlar bize aittir.

> **Bu dosya tek doğruluk kaynağıdır.** Sözleşme değişecekse **önce burası** güncellenir,
> sonra kod. Kodun bu dosyaya uyduğu `tests/Atm.Tests/ProtocolVersionTests.cs` ile
> makine tarafından denetlenir: yukarıdaki `Sürüm` satırı ile
> `src/Atm.Protocol/ProtocolVersion.cs` ayrışırsa test kırmızı yanar.

---

## 1. Taşıma katmanı

**Kalıcı TCP soket.** Terminal açılışta hosta bağlanır ve bağlantıyı **kapatmaz**.
Bütün işlemler aynı bağlantı üzerinden akar.

Neden HTTP değil: HTTP'de her istek kendi bağlantısını kurar ve biter; "hat koptu" diye
bir an yoktur, sadece "istek başarısız oldu" vardır. Bu projenin konusu tam olarak hattın
koptuğu, cevabın geciktiği, isteğin gidip cevabın gelmediği anlardır. Kalıcı soket bu
anları görünür kılar, HTTP saklar.

### 1.1 Çerçeveleme (framing)

:::terim
**Çerçeveleme (framing):** TCP bir mesaj hattı değil, bir bayt akışıdır. Gönderdiğin iki
mesaj karşı tarafa tek parça hâlinde yapışık gelebilir, ya da tek mesaj ikiye bölünmüş
gelebilir. Nerede bittiğini **sen** söylemek zorundasın. Buna çerçeveleme denir.
:::

Her mesaj şöyle gider:

```
[4 bayt uzunluk, big-endian, işaretsiz][UTF-8 kodlanmış JSON gövde]
```

- Uzunluk alanı **yalnızca gövdeyi** sayar, kendini saymaz.
- En büyük gövde: **64 KiB (65536 bayt)**. Bundan büyüğünü bildiren bir çerçeve gelirse
  bağlantı **derhal kapatılır** — çünkü bu noktada akışın neresinde olduğumuzu artık
  bilmiyoruz ve tahmin yürütmek uydurma veri üretmektir.
- Big-endian: ağ protokollerinin geleneksel bayt sırası. Tercih meselesi, ama **yazılı**
  olması gerekir; iki taraf farklı sırada okursa 1 sayısı 16777216 olur.

:::tuzak
**Klasik tuzak:** "Ben JSON'u newline ile ayırırım, daha kolay." Gövdenin içinde bir
newline geçtiği gün — bir adres alanında, bir hata mesajında — mesaj ortadan ikiye bölünür
ve iki tarafın çözümleyicisi (parser) birbirinden farklı yerde kaybolur. Uzunluk öneki bu
soruyu tamamen ortadan kaldırır: kaç bayt okuyacağını baştan bilirsin.
:::

### 1.2 Neden JSON

Gerçek ödeme ağları ikili (binary), bit haritalı, yer kaplamayan formatlar kullanır.
Biz JSON kullanıyoruz ve bunun bir bedeli var: daha çok bayt, daha yavaş çözümleme.
Karşılığında aldığımız şey, bu projenin amacına daha uygun: **bir mesaj kayıtta görüldüğü
gibi okunabilir**, bir arıza anında "ne gitti"
sorusunun cevabı gözle görülür. Bu bir simülatör; ölçtüğümüz şey saniyedeki işlem sayısı
değil, paranın korunup korunmadığı.

Bu bir basitleştirmedir ve `reports/assumptions.md`'ye yazılmıştır.

---

## 2. Ortak zarf (envelope)

Her mesaj — istek olsun cevap olsun — aynı dış zarfı taşır:

```json
{
  "v":        "ATMSIM/0.1",
  "type":     "BalanceRequest",
  "stan":     104,
  "terminal": "ATM-01",
  "bizDate":  "2026-08-24",
  "sentAt":   "2026-08-24T09:14:02.000Z",
  "retry":    0,
  "body":     { }
}
```

| Alan | Anlamı | Neden var |
|---|---|---|
| `v` | Protokol sürümü | Uyuşmayan sürüm sessizce yanlış çalışmasın diye |
| `type` | Mesaj tipi (§4) | Gövdenin nasıl okunacağını söyler |
| `stan` | İzleme numarası (§2.1) | İşlemi baştan sona tek bir kimlikle takip etmek için |
| `terminal` | Terminal kimliği | Çok ATM kapsam dışı, ama alan **şimdi** konur (§2.2) |
| `bizDate` | İş günü (§2.3) | Gün sonu mutabakatı hangi güne yazılacağını buradan bilir |
| `sentAt` | Gönderim anı, sanal saatten | Determinizm: gerçek saat kullanılmaz (kural §5, docs/proje-kurallari.md) |
| `retry` | Kaçıncı deneme (0 = ilk) | Tekrarı çift işlemden ayırmak için (§3) |
| `body` | Tipe özel içerik | — |

Cevap mesajı, cevapladığı isteğin `stan`, `terminal` ve `bizDate` alanlarını **birebir
aynen** taşır. Cevabı isteğe bağlayan şey budur; sıra değil.

:::tuzak
"Cevaplar sırayla gelir, ilk gelen ilk isteğin cevabıdır" varsayımı kalıcı bir sokette
yanlıştır. Host iki isteği farklı sürede işleyebilir ve ikincinin cevabı önce gelebilir.
Cevap eşleştirmesi **her zaman** `stan` üzerinden yapılır.
:::

### 2.1 STAN — izleme numarası

:::terim
**STAN (System Trace Audit Number) — izleme numarası:** terminalin her yeni işleme
verdiği, artan bir sayı. Bir işlemin isteği, cevabı, dağıtım bildirimi ve gerekirse ters
kaydı **aynı** STAN'ı taşır. "Bu ters kayıt hangi çekime ait" sorusunun cevabı budur.
:::

- 1'den başlar, her **yeni işlemde** bir artar. Tekrar denemede **artmaz** — tekrar
  deneme yeni bir işlem değildir.
- 6 basamakta başa döner (999999 → 1). Gerçek sistemlerde de sınırlıdır; başa dönmenin
  mutabakatta yarattığı belirsizlik açık soru olarak bırakıldı.

### 2.2 Terminal kimliği

Tek ATM var ve çok ATM kapsam dışı. Alan yine de **bugün** konuluyor, çünkü işlem
kimliğinin parçası (§3) ve sonradan eklenirse daha önce yazılmış bütün kayıtların kimliği
değişir. Sabit değer: `ATM-01`.

### 2.3 İş günü (`bizDate`)

:::terim
**İş günü (business date):** bankanın defterinde işlemin yazıldığı gün. Takvim gününden
farklı olabilir: gün sonu kesimi saat 00:00'da değil, örneğin 23:00'te yapılırsa,
23:30'daki bir çekim **ertesi** iş gününe yazılır.
:::

Terminal ve host bu tarihi **kendi başlarına** hesaplamaz; terminal isteğinde bildirir,
host aynen kabul eder ve kendi defterine o günle yazar. Sebebi §4.9: iki tarafın kesim
anını bağımsız hesaplaması, kesim anına düşen işlemin iki farklı güne yazılması demektir
ve mutabakatta "sebepsiz fark" olarak görünür.

---

## 3. İşlem kimliği ve tekrar (idempotency)

:::terim
**Idempotency — tekrar bağışıklığı:** aynı isteğin iki kez ulaşmasının, bir kez ulaşmasıyla
aynı sonucu vermesi. Para çekmede bunun karşılığı şudur: aynı işlem iki kez gelirse hesap
**bir kez** borçlanır.
:::

**İşlem kimliği = `terminal` + `bizDate` + `stan`.** Host bu üçlüyü bir tabloda tutar.

Kural:

1. Host bir istek aldığında önce bu üçlüye bakar.
2. **Görülmemişse:** işlemi yapar, sonucu üçlüyle birlikte saklar, cevabı döner.
3. **Görülmüşse:** işlemi **tekrar yapmaz**; sakladığı **aynı cevabı** aynen döner ve
   cevaba `"replayed": true` koyar.

`retry` alanı yalnızca kayıt ve teşhis içindir; kararı `retry` değil, kimlik tablosu verir.
Terminal `retry` koymayı unutsa bile host çift borçlandırmaz. Güvenlik, hatırlanması
gereken bir alana bağlanmaz.

### 3.1 Tablonun anahtarı işlem kimliği **artı mesaj tipidir**

Yukarıdaki üçlü, "hangi işlem" sorusunun cevabıdır. "Hangi mesaj" sorusunun cevabı değildir.

Bir çekimin yetkilendirmesi, onu izleyen dağıtım bildirimi ve gerekirse ters kaydı **aynı
STAN'ı taşır** (§2.1) — ve bu bir kusur değil, tasarımdır: ters kaydın hangi çekimi geri
aldığını söylemesinin yolu budur. Bunun sonucu şudur: tablo yalnızca işlem kimliğiyle
anahtarlanırsa, dağıtım bildirimi hosta ulaştığında **"bunu zaten sormuştun" muamelesi
görür** ve host ona yetkilendirmenin cevabını döner. Defter hiç hareket etmez, para
müşteriye gitmiş olmasına rağmen hesapta durur ve bunu hiçbir hata mesajı söylemez.

**Doğru anahtar: `terminal + bizDate + stan + type`.** Aynı tipteki ikinci mesaj bir
tekrardır ve saklanan cevabı alır; farklı tipteki mesaj yeni bir iştir. Karar KARAR-031.

### 3.2 Yalnızca **bir şeyi değiştirmiş** cevaplar hatırlanır

Tablo, bir işin iki kez yapılmasını önlemek için vardır. Hiç iş yapmamış bir cevabın
koruyacağı bir şey yoktur.

| Cevap | Hatırlanır mı | Neden |
|---|---|---|
| Onaylanan yetkilendirme | Evet | Bloke kondu |
| Uygulanan dağıtım bildirimi | Evet | Defter hareket etti |
| PIN doğrulama — **yanlış PIN dahil** | Evet | Deneme sayacı düştü |
| Bakiye sorgusu | Evet | Terminalin kaçırdığı cevabın aynısını alması için |
| Echo | Hayır | Otuz saniyede bir gelir; hatırlamak belleği sonsuza kadar büyütür |
| **Reddedilen her istek** | **Hayır** | Hiçbir şey kıpırdamadı |

:::tuzak
**Reddi hatırlamak, bozuk tek bir mesajı kalıcı bir hükme çevirir.** Makine bozuk bir
dağıtım bildirimi gönderir, host reddeder, makine doğrusunu gönderir — ve doğrusu
"bunu zaten sormuştun" cevabını alır. Para blokede kalır, defter hiç hareket etmez ve o
blokeyi çözebilecek hiçbir mesaj kalmaz. Karar KARAR-032.
:::

:::tuzak
**Alanın klasik hatası:** idempotency'i "sonra ekleriz" diye bırakmak. Sonra eklenemez —
çünkü kimliği ne oluşturuyor sorusunun cevabı mesaj formatını, defter kayıtlarını ve
mutabakat sorgusunu birlikte değiştirir. Bu yüzden §4.5 gereği **baştan** buradadır.
:::

---

## 4. Mesaj tipleri

Bütün tutarlar **kuruş cinsinden tam sayıdır**. `150.75 TL` → `15075`. Ondalıklı sayı
(`double`, `float`) para için **kullanılmaz**: 0.1 + 0.2 ikili tabanda tam olarak 0.3
etmez ve bu fark bir gün mutabakatta 1 kuruş olarak karşımıza çıkar. Bkz. KARAR-008.

### 4.0 Echo — canlılık kontrolü

| | |
|---|---|
| `EchoRequest` | `body: {}` |
| `EchoResponse` | `body: {}` |

Terminal **30 saniyede bir** yollar. Arka arkaya **3** echo cevapsız kalırsa terminal hattı
kopmuş sayar, soketi kapatır ve yeniden bağlanmaya başlar.

:::tuzak
"Soket açık görünüyorsa hat sağlamdır" yanlıştır. Kablo çekilirse veya karşı taraf donarsa
işletim sistemi bunu dakikalarca fark etmeyebilir; soket açık **görünmeye devam eder**.
Ölü bir hattı ancak düzenli olarak bir şey gönderip cevabını bekleyerek anlarsın.
Benzetmeyle: telefon hattında karşı taraf sustuğunda anlarsın; sokette anlamazsın.
:::

### 4.1 PIN doğrulama

| | |
|---|---|
| `PinVerifyRequest` | `body: { "pan": "...", "pin": "...." }` |
| `PinVerifyResponse` | `body: { "ok": true, "remainingTries": 3 }` |

- Karşılaştırma **host içinde** yapılır. Dışarı yalnızca doğru/yanlış çıkar.
- **`pin` alanı hiçbir günlüğe, hiçbir kayda, hiçbir hata mesajına yazılmaz.** Kayıt
  katmanı bu alanı tip düzeyinde maskeler; maskelemeyi unutmak mümkün olmamalıdır.
- `pan` (kart numarası) kayıtlarda **maskeli** tutulur: ilk 6 + son 4, arası `*`.
- Kartlar uydurmadır, Luhn-geçerlidir, gerçek hiçbir karta karşılık gelmez.

:::gercek
Gerçek bir ATM PIN'i açık göndermez: tuş takımı donanımı PIN'i kendi içinde şifreler ve
hatta yalnızca şifreli "PIN bloğu" çıkar; anahtarlar HSM adı verilen bir donanımda durur.
Biz şifreleme yapmıyoruz — bu **bilinçli bir kapsam dışıdır**, `reports/assumptions.md`'ye
yazılmıştır ve raporda eksik olarak beyan edilir. Simülasyonda gizlenecek gerçek bir sır
yoktur; gizlenmesi gereken şey alışkanlıktır, o yüzden maskeleme yine de uygulanır.
:::

### 4.2 Bakiye sorgu

| | |
|---|---|
| `BalanceRequest` | `body: { "pan": "..." }` |
| `BalanceResponse` | `body: { "rc": "00", "available": 250000, "ledger": 250000 }` |

`available` (kullanılabilir) ile `ledger` (defter) neden ayrı: defter bakiyesi hesabın
işlenmiş hâlidir; kullanılabilir bakiye ondan bloke tutarlar düşülmüş hâlidir. Faz 1'de
ikisi eşittir, ama alan bugün konur — çünkü Faz 2'de bir çekim yetkilendirildiği anda para
**bloke edilir** ama defter henüz değişmemiş olabilir.

### 4.3 Para çekme — yetkilendirme

| | |
|---|---|
| `WithdrawalAuthRequest` | `body: { "pan": "...", "amount": 35000, "denoms": [{"d":20000,"n":1},{"d":10000,"n":1},{"d":5000,"n":1}] }` |
| `WithdrawalAuthResponse` | `body: { "rc": "00", "authId": "...", "available": 215000, "ledger": 250000 }` |

**`denoms` alanı isteğe konur, çünkü kupür kısıtı yetkilendirmeden ÖNCE bakılır.**
Terminal, kasetlerinden istenen tutarı verebileceğini **önce** kanıtlar; veremiyorsa isteği
hiç göndermez ve müşteriye ekranda söyler.

:::tuzak
**Alanın klasik hatası:** önce yetkilendir, sonra parayı saymaya kalk. 350 TL isteği
200'lük ve 100'lük kasetlerle karşılanamaz. Bunu yetkilendirmeden sonra fark edersen
elinde iptal edilmesi gereken bir borçlanma kalır — yani gereksiz yere bir ters kayıt
üretmiş olursun. Her gereksiz ters kayıt, kaybolma ihtimali olan bir mesajdır.
:::

#### Yetkilendirme ne yapar: bloke koyar, defteri değiştirmez

`rc: "00"` dönen an hesaba **bloke** konmuştur: kullanılabilir bakiye tutar kadar düşer,
**defter bakiyesi değişmez.** Defter, nakde ne olduğu §4.4 ile bildirildiğinde değişir.

350 TL'lik bir çekimde (tutarlar TL, hesap `TR-DEMO-001`):

| An | Defter | Bloke | Kullanılabilir |
|---|---|---|---|
| Yetkilendirme öncesi | 2.500 | 0 | 2.500 |
| `rc: "00"` döndükten sonra | 2.500 | 350 | 2.150 |
| `DispenseAdvice: FULL` sonrası | 2.150 | 0 | 2.150 |
| `DispenseAdvice: NONE` sonrası | 2.500 | 0 | 2.500 |

Bloke, "bu para söz verildi ama daha gitmedi" demenin tek dürüst yoludur. Tek sayıyla
çalışan bir hesap bu cümleyi kuramaz: ya parayı gitmiş sayar (gitmediyse müşteri yok yere
eksik görünür) ya da hiç ayırmaz (aynı para ikinci bir çekime söz verilebilir).

:::gercek
Gerçek ATM ağlarının büyük kısmı **tek mesajlı** çalışır: ATM bir finansal istek yollar,
host onayladığı anda **defteri borçlandırır**, ve işler yolundaysa ikinci bir mesaj yoktur
— yalnızca iş ters gittiğinde ters kayıt gider. Biz **iki mesajlı** modeli seçtik:
yetkilendirme bloke koyar, dağıtım bildirimi defteri işler. Gerekçe ve reddedilen
alternatif KARAR-029'da. Seçimin bedeli de orada yazılıdır: bu modelde **bildirimin
kaybolması asılı kalmış bir bloke üretir**, tek mesajlı modelde üretmez. Bu, Faz 4'te
ölçtüğümüz arıza sınıflarından biridir; modelin eseri olduğunu bilerek ölçüyoruz.
:::

#### Host `denoms` ile ne yapar: kabul etmez, denetler

Terminal dökümü gönderir; host onu doğru varsaymaz, sırayla denetler:

1. `amount` pozitif değilse → `12`.
2. `denoms` boşsa → `12`.
3. Bir `n` (adet) pozitif değilse, ya da bir `d` (kupür) `docs/model.md` §2'deki kupür
   listesinde yoksa → `12`.
4. **`sum(d × n)` ile `amount` eşit değilse → `12`.** Bu sözleşmenin en çok işe yarayan
   satırıdır: dökümü hesaplayan taraf ile parayı bloke eden taraf aynı sayıyı okumak
   zorundadır.
5. Kart tanınmıyorsa → `14`.
6. **Kullanılabilir** bakiye yetmiyorsa → `51`. Defter değil, kullanılabilir: aynı hesapta
   kapanmamış başka bir yetkilendirme varsa o para ikinci kez söz verilemez.
7. Hepsi geçtiyse bloke konur, `authId` üretilir, `00` döner.

Host **kendi kupür planını yapmaz.** Kasetlerde ne olduğunu bilmez ve bilmesi de gerekmez;
kaset terminalin fiziksel gerçeğidir. Hostun denetlediği tek şey, gelen dökümün istenen
tutarı **tam olarak** verdiğidir.

:::tuzak
"Dökümü zaten terminal hesapladı, host tekrar toplamasın" demek, iki tarafın farklı sayıya
inandığı bir işlemi mümkün kılar. Terminal 350 hesaplayıp 300'lük döküm gönderirse — bir
yazılım hatası, yarım kalmış bir kupür değişikliği, elle kurcalanmış bir istek — host 350
bloke eder, makine 300 verir ve aradaki 50 TL kimsenin defterinde görünmez. Toplama işlemi
ucuzdur; **yapılmayan** toplama pahalıdır.
:::

`authId`, işlem kimliğinin metin hâlidir: `ATM-01/2026-08-25/000104`. Ayrı bir sayaç
üretilmez, çünkü ters kayıt zaten aynı STAN'ı taşır (§4.5); ikinci bir kimlik, ikisinin
uyuşmadığı bir günü mümkün kılmaktan başka bir şey yapmaz.

### 4.4 Dağıtım sonucu bildirimi

| | |
|---|---|
| `DispenseAdvice` | `body: { "authId": "...", "outcome": "FULL", "dispensed": 35000, "retracted": 0 }` |
| `DispenseAdviceResponse` | `body: { "rc": "00" }` |

`outcome` alabileceği değerler:

| Değer | Anlamı | `dispensed` | `retracted` | Deftere işlenen |
|---|---|---|---|---|
| `FULL` | Tamamı verildi, müşteri aldı | = yetkilendirilen | 0 | tamamı |
| `PARTIAL` | Yetkilendirilenden **azı** verildi | 0 < x < yetkilendirilen | 0 | verilen kadarı |
| `NONE` | Hiç verilemedi | 0 | 0 | hiçbir şey |
| `RETRACTED` | Verildi ama müşteri **almadı**, makine geri aldı | > 0 | = `dispensed` | hiçbir şey |

**Bloke her durumda tamamen çözülür.** Kısmi dağıtımda "farkı iade etmek", blokenin bir
kısmını açık bırakmak değildir: bloke tamamen kalkar ve deftere yalnızca gerçekten verilen
tutar işlenir. Sonuç aynı, ama açık kalan hiçbir söz kalmaz.

#### Deftere ne işlenir: tek satırlık kural

```
müşteriye ulaşan = dispensed − retracted
defter          −= müşteriye ulaşan
bloke           −= yetkilendirilen tutar
```

Dört sonucun dördü de bu iki satırdan çıkar; ayrı ayrı kural yazılmaz. `RETRACTED`'da
`dispensed − retracted = 0` olduğu için defter kıpırdamaz — geri alınan para `Retract`
kovasındadır (`docs/model.md` §1), ne müşterinin cebinde ne kasettedir.

#### Host bildirime inanmadan önce ne kontrol eder

Bildirimi gönderen taraf, raporu sorgulanan taraftır. Host şunları denetler:

1. Bu işlem kimliği için **açık bir yetkilendirme var mı.** Yoksa `12` — ya hiç
   yetkilendirilmemiştir ya da daha önce kapanmıştır. İki kez uygulanan bir bildirim,
   defteri iki kez hareket ettirir.
2. `authId` mesajın geldiği işlem kimliğiyle **aynı mı.**
3. `dispensed` ve `retracted` negatif değil.
4. `dispensed`, yetkilendirilen tutardan **büyük değil.** Büyükse doğru okuma "para
   yanlış" değil, "mesaj yanlış"tır.
5. `retracted`, `dispensed`'dan büyük değil.
6. **`outcome` kelimesi ile sayılar aynı şeyi söylüyor mu.** İkisi aynı olayın iki
   anlatımıdır; kendisiyle çelişen bir makinenin raporu defteri hareket ettiremez.

:::tuzak
**Bildirimi doğrulamadan uygulamak.** "Makine ne dediyse odur" demek makul görünür — sonuçta
parayı o verdi. Ama bildirim, tam da makinenin ne yaptığının bilinmediği anlarda gelir.
`outcome: FULL` diyen ama `dispensed: 0` yazan bir mesaj, hangi yarısına inanılacağı
belirsiz bir mesajdır; ikisine birden inanmak mümkün değildir. Reddedip kayda yazmak,
yanlış yarısını seçmekten iyidir.
:::

:::tuzak
**Verilmek ile alınmak farklıdır.** Para nakit ağzına geldi diye müşteriye geçmiş sayılmaz.
Alınmazsa makine geri çeker (retract). Geri alınan para **ne müşterinin cebindedir, ne de
kasettedir** — üçüncü bir kovadadır ve gün sonunda elle sayılır. Bunu "kasede geri döndü"
saymak, kasetteki parayı olduğundan fazla göstermek demektir.
:::

:::tuzak
**Kısmi dağıtımda tam iptal.** 500 TL yetkilendirildi, makine 300 verdi. Doğru davranış
200'ü iade etmektir. Tamamını iade edersen müşteri 300 TL'yi cebine koymuş ama hesabı hiç
borçlanmamış olur. Bu, bilançoda görünen bir açıktır.
:::

### 4.5 Ters kayıt (reversal)

| | |
|---|---|
| `ReversalRequest` | `body: { "authId": "...", "amount": 35000, "reason": "TIMEOUT" }` |
| `ReversalResponse` | `body: { "rc": "00" }` |

:::terim
**Ters kayıt (reversal):** hosta "az önceki borçlandırmayı geri al" diyen mesaj. Bir
işlemin yapılmadığını değil, **yapılmış olabileceğini** varsayarak gönderilir.
:::

`reason` değerleri: `TIMEOUT` · `DISPENSE_FAILED` · `PARTIAL` · `RETRACTED` · `CANCELLED`.

**Ters kayıt aynı STAN'ı taşır.** Böylece host onu hangi işleme uygulayacağını bilir ve
aynı ters kayıt iki kez gelirse §3 gereği ikinci kez uygulamaz.

#### Tekrar deneme kuralı — geç ters kayıt

:::tuzak
**Alanın bir numaralı hata kaynağı:** cevap gelmemesini "işlem olmadı" sanmak. Zaman aşımı
**ret değildir.** İstek hosta ulaşmış, hesap borçlanmış, sadece cevabı yolda kaybolmuş
olabilir. Doğru davranış işlemi yok saymak değil, **ters kayıt üretmektir.**
:::

:::tuzak
**İkinci hata:** ters kaydı gönderdim diye ulaştı saymak. Ters kayıt da kaybolabilir ve
kaybolan bir ters kayıt, müşterinin parasının hesapta eksik kalması demektir.
:::

#### Host ters kayıt aldığında ne yapar — üç durum, üç farklı cevap

Ters kayıt hosta ulaştığında, ortada üç ayrı durum olabilir ve dışarıdan üçü de aynı
görünür. Farkı **hostun kendi kaydı** söyler:

| Durum | Host ne yapar | `rc` |
|---|---|---|
| **Açık bir yetkilendirme var** | Blokeyi çözer, deftere dokunmaz | `00` |
| **Host bu işlemi hiç görmedi** | Geri alacak bir şey yok; onaylar ve kayda yazar | `00` |
| **Bu çekim zaten ödendi** (bildirim uygulanmış) | **Hiçbir şeyi geri almaz**, onaylar, çelişkiyi yüksek sesle kaydeder ve sayar | `00` |

Üçünde de `00` dönmesi tesadüf değil. **Onaylanmayan bir ters kayıt sonsuza kadar tekrar
gönderilir** (aşağıdaki kural). Reddetmek, makineyi gün boyu aynı mesajı yollamaya mahkûm
eder ve hiçbir şeyi düzeltmez.

İkinci durum ortalama bir gün içinde **beklenen** bir durumdur: yetkilendirme isteği yolda
kaybolduysa host o işlemi hiç görmemiştir, ama terminal bunu bilemez ve doğru davranış yine
ters kayıt göndermektir (§4.5'in başındaki tuzak kutusu).

Üçüncü durum bir **çelişkidir** ve öyle raporlanır: makine "geri al" diyor, kayıt ise
paranın müşteriye gittiğini söylüyor. Host bir banknotu geri alamaz. Yapabileceği tek
dürüst şey, onaylamak ve çelişkiyi saymaktır (`UnexpectedReversalCount`). Sessizce onay
verip geçmek, kural §6b (docs/proje-kurallari.md)'nin yasakladığı **sessiz onarımdır**; reddetmek ise sonsuz
tekrar üretir. Karar KARAR-033.

Host ayrıca iki şeyi denetler ve uymuyorsa `12` döner: `authId` zarftaki kimlikle aynı mı,
ve ters kaydın tutarı **hostun tuttuğu blokeyle** aynı mı. İkincisi uymuyorsa makine ile
host farklı bir sayıya inanıyor demektir; hangisinin doğru olduğunu tahmin etmek yerine
mesaj reddedilir ve söz açık bırakılır.

Kural: ters kayıt **onaylanana kadar** ölmez.

- `ReversalResponse` gelene kadar tekrar gönderilir.
- Aralar: **10 sn, 30 sn, 60 sn, sonra 5 dakikada bir.**
- Hat kopuksa kuyrukta bekler; bağlantı kurulur kurulmaz **ilk gönderilen şey** bekleyen
  ters kayıtlardır (buna geç ters kayıt / late reversal denir).
- Kuyruk **diske yazılır**: terminal kapanıp açılsa bile bekleyen ters kayıt kaybolmaz.

### 4.6 Para yatırma

Dört mesaj, dört ayrı an. Sebebi §4.7: yatırılan para hesaba **anında geçmez.**

:::terim
**Escrow (ara kasa):** müşterinin attığı paranın, o daha onaylamadan önce beklediği ara
bölme. Para makinenin içindedir ama ne müşterinindir ne bankanındır — daha doğrusu, **hâlâ
müşterinindir**: vazgeçerse oradan geri verilir.
:::

:::terim
**Commit (kesinleştirme):** yatırmanın geri dönülemez hâle geldiği an. Bu projede commit,
banknotların escrow'dan geri dönüşüm kasetine geçmesidir — mesaj değil, **fiziksel hareket**
(KARAR-038).
:::

#### Sıra — değiştirilemez

```
1. müşteri parayı atar        -> makine sayar, ESCROW'a koyar        (hesap değişmez)
2. DepositAuthRequest         -> host kartı/hesabı doğrular          (hesap değişmez)
3. müşteri ekranda onaylar
4. escrow -> geri dönüşüm kaseti  ** FİZİKSEL HAREKET, MESAJDAN ÖNCE **
5. DepositCommitAdvice        -> host hesabı ARTIRIR
```

**4. adımın 5. adımdan önce olması bir tercih değil, bu bölümün tamamının dayandığı
karardır (KARAR-038).** Sebebi tek cümlede: kasete girmiş bir banknot geri alınamaz, ama
bir mesaj sonsuza kadar tekrar gönderilebilir. Belirsizlik her zaman geri alınabilir tarafa
taşınır.

#### Mesajlar

| | |
|---|---|
| `DepositAuthRequest` | `body: { "pan": "...", "counted": 50000, "denoms": [ {"d":10000,"n":5} ] }` |
| `DepositAuthResponse` | `body: { "rc": "00", "available": 250000, "ledger": 250000 }` |

Makine parayı saydı, escrow'da tutuyor, hosttan izin istiyor. **Hesap henüz değişmedi.**
Host bu isteği reddedebilir (kapalı hesap, tanınmayan kart, kabul edilmeyen kupür).

| | |
|---|---|
| `DepositCommitAdvice` | `body: { "txn": "...", "outcome": "STACKED", "stacked": 50000, "returned": 0, "jammed": 0 }` |
| `DepositCommitResponse` | `body: { "rc": "00", "available": 300000, "ledger": 300000 }` |

`outcome` alabileceği değerler:

| Değer | Anlamı | Hesaba etkisi |
|---|---|---|
| `STACKED` | Para geri dönüşüm kasetine girdi | `stacked` kadar **artar** |
| `RETURNED` | Müşteri vazgeçti veya host reddetti; para iade edildi | değişmez |
| `JAMMED` | Kasete alınırken sıkıştı; para ne escrow'da ne kasette | **artmaz** — servis işi |
| `PARTIAL` | Bir kısmı kasete girdi, kalanı sıkıştı | yalnızca `stacked` kadar artar |

Hostun uyguladığı tek satır, çekimdeki `handedOver = dispensed − retracted` satırının
aynadaki karşılığıdır:

```
credited = stacked
```

`returned` ve `jammed` hesabı **hiç** hareket ettirmez. İade edilen para müşteriye geri
gitmiştir; sıkışan para hiçbir yere ait değildir ve bir insanın makineyi açması gerekir.

#### Hat koptuğunda — ana göre değişir (KARAR-040)

| Kopma anı | Para nerede | Terminal ne yapar |
|---|---|---|
| `DepositAuthResponse` gelmedi | Escrow (müşterinin) | Parayı **iade eder**, hosta ters kayıt gönderir |
| Kasete alırken sıkışma | Sıkışmış | `JAMMED` bildirir; onay gelene kadar tekrarlar |
| `DepositCommitResponse` gelmedi | Geri dönüşüm kaseti (bankanın) | **Commit'i tekrar gönderir**, onay gelene kadar durmaz |

:::tuzak
**Cevapsız bir commit'i "olmadı" sayıp parayı iade etmek.**

Bu, çekimdeki "zaman aşımı ret değildir" hatasının yatırmadaki hâlidir ve daha pahalıdır:
para zaten kasete girmiştir, iade edilecek bir şey yoktur — makine ikinci bir desteyi
müşteriye verir. Müşteri hem parayı hem bakiyeyi alır.

Yatırmada tekrar edilen şey **ters kayıt değil, commit'tir.** Yapılacak iş bir sözü geri
almak değil, olan biteni duyurmaktır; duyurulamayan bir şey, duyulana kadar tekrar
duyurulur.
:::

:::tuzak
**Escrow'u atlayıp parayı sayar saymaz hesaba geçirmek.** Müşteri vazgeçerse veya makine
parayı iade ederse, hesabı artırdığın para geri verilmiş olur — müşteri hem parayı hem
bakiyeyi almıştır. Escrow bu yüzden vardır: **onay anı ile hesabın işlendiği an ayrıdır.**
:::

#### Kapatılamayan pencere — dürüstçe yazılmıştır

4. adım (fiziksel hareket) ile onu izleyen günlük satırı hiçbir zaman **tam olarak aynı
anda** olamaz. Makinenin tam kasete alırken elektriği giderse, açıldığında paranın kasete
girip girmediğini bilemez; kovaları saymak da işe yaramaz, çünkü sayaç da o anda
güncelleniyordu.

Bu pencere kapatılamaz. Yapılan şey onu **görünür** kılmaktır (KARAR-041): terminal kasete
almadan önce günlüğüne "alıyorum" yazar. Açılışta "alıyorum" olup "aldım" olmayan bir kayıt
bulunursa, terminal **tahmin etmez** — durumu "bilinmiyor" diye raporlar ve gün sonu
mutabakatına açıklanamayan bir fark olarak düşer.

Bilinen bir belirsizlik, fark edilmeyen bir belirsizlikten iyidir.

### 4.7 Gün sonu kesimi

:::terim
**Gün sonu kesimi (cutover / settlement):** makinenin bir iş gününü kapatıp yeni güne
geçtiği **an**. Takvim günü değişince kendiliğinden olmaz; birinin başlatması gerekir.
:::

:::terim
**Mutabakat (reconciliation):** iki tarafın aynı günün toplamlarını karşılaştırıp
"aynı şeyi mi yazdık" diye sorması. Fark çıkarsa gün kapanmaz.
:::

#### Kesimi kim başlatır ve neden terminal

Kesimi **terminal başlatır.** Gerekçe §2.3'te yazılı: iş gününü terminal bildirir, host
aynen kabul eder. Günü kim söylüyorsa, günün ne zaman değiştiğini de o söylemek
zorundadır. İki taraf kesim anını bağımsız hesaplasaydı, kesim anına düşen bir işlem iki
farklı güne yazılırdı — kural §4.9 (docs/proje-kurallari.md)'un tarif ettiği "sebepsiz fark" tam olarak budur.

Gerçek ağlarda bunun tek bir doğrusu yoktur; bazı kurumlarda host da kesim
zorlayabiliyor. Emin olmadığımız için açık soru olarak bırakıldı.

#### Mesajlar

| | |
|---|---|
| `CutoverRequest` | `body: { "newBizDate": "2026-08-26", "totals": { "withdrawals": 55000, "deposits": 30000, "count": 7 } }` |
| `CutoverResponse` | `body: { "rc": "00", "totals": { "withdrawals": 55000, "deposits": 30000, "count": 7 }, "reason": "" }` |

**Kapatılan gün gövdede yazmaz** — zarfın `bizDate` alanıdır. Aynı bilgiyi iki yere
yazmak, ikisinin çelişebileceği bir gün üretmektir.

`totals` alanı **isteği gönderenin kendi toplamlarıdır.** Cevaptaki `totals` ise
**hostun kendi toplamları.** İki taraf birbirinin rakamını kopyalamaz; ikisi de kendi
defterinden sayar ve mesaj bu iki sayının karşılaştığı tek yerdir. Farkı okuyan biri,
tek mesaja bakarak iki rakamı da görür.

Toplamların üçü de aynı kuralla sayılır — **karşılığı fiilen hareket etmiş para:**

| Alan | Terminal neyi sayar | Host neyi sayar |
|---|---|---|
| `withdrawals` | `CASH_TAKEN` satırları — müşterinin eline geçen | onaylanmış `DispenseAdvice` satırları — hesaptan düşülen |
| `deposits` | `DEPOSIT_STACKED` satırları — kasete giren | onaylanmış `DepositCommitAdvice` satırları — hesaba yazılan |
| `count` | yukarıdaki satırlardan **kaç ayrı işlem** çıkıyorsa | aynısı |

Yetkilendirmeler, reddedilen istekler, iade edilen paralar ve ters kayıtlar toplamlara
**girmez.** Sayılan şey niyet değil, hareket.

#### Kesim reddedilirse — üç durum

| Durum | `rc` | Ne olur |
|---|---|---|
| Toplamlar tutuyor | `00` | Gün kapanır, host günü kapalı işaretler, terminal yeni güne geçer |
| Toplamlar tutmuyor | `95` | Gün **kapanmaz**, terminal tarihini değiştirmez, fark kayda yazılır |
| Hostta o güne ait kapanmamış işlem var | `95` | Gün **kapanmaz**, sebep `reason` alanında |
| O gün zaten kapalı | `98` | Hiçbir şey değişmez, cevapta ilk kapanışın toplamları döner |

`95` ve `98` kamuya açık standartların genel mantığından alınmıştır: `95` mutabakat
hatası, `98` tekrarlanan mutabakat isteği.

#### Terminal, kuyruğu boşalmadan kesim istemez

Kuyrukta bekleyen bir dağıtım bildirimi veya ters kayıt varsa, terminal hosta **hiç
sormaz**: kendi kaydına `CUTOVER_BLOCKED` yazar ve kesimi erteler. Sebebi basit — o
bildirim hostun defterine daha ulaşmamıştır, dolayısıyla toplamlar zaten tutmayacaktır ve
tutmadığı için değil, **henüz ulaşmadığı için** tutmayacaktır. Bunlar farklı iki şeydir
ve karıştırılırsa gerçek bir fark, "herhalde kuyruktandır" diye geçiştirilir.

#### Cevap gelmezse gün kapanmaz

`CutoverRequest`'e 30 saniyede cevap gelmezse terminal **tarihini değiştirmez.** Sessizlik
burada da onay değildir. Kesim, hat geri geldiğinde yeniden denenir. Bu, çekimdeki
kuralın (§4.5) aynısıdır: cevabı bilmediğimiz bir şeyi olmuş saymayız.

Tersi neden yanlış olurdu: terminal günü kendi başına çevirseydi, hostun hâlâ açık
saydığı bir güne terminal "kapandı" derdi ve ertesi günün işlemleri, hostun defterinde
**dünkü** günün altına düşerdi.

#### Kapalı bir güne geç işlem düşemez — bunun nedeni bir kural değil, bir sıra

Bir güne ait yetkilendirme hostta hâlâ açıkken host o günü kapatmayı reddeder. Terminal
tarafında da kuyruk boşalmadan kesim istenmez. İkisi birden sağlandığında, kapanmış bir
güne ait geç bir bildirimin gelebileceği bir pencere kalmaz.

:::gercek
Gerçek bankacılıkta gün yine de kapanır ve açık kalan kalem bir **askı hesabına**
(suspense) düşüp ertesi gün elle çözülür. Biz kapanmayı reddediyoruz — daha basit ve bu
projede daha dürüst, çünkü askı hesabını modellemiyoruz. Etkisi
`reports/assumptions.md`'de yazılı.
:::

:::tuzak
**İş gününü takvimden okumak.** Kesimden önceki hâlimizde iş günü
`clock.UtcNow.ToString("yyyy-MM-dd")` idi: yani gün, gece yarısı UTC'de **kendiliğinden**
dönüyordu. Kimse toplamları karşılaştırmıyordu, kimse "kapandı" demiyordu; sadece tarih
değişiyordu. İki sonucu vardı: (a) gün, İstanbul'da saat 03:00'te, hiç kimse haberi
olmadan dönüyordu; (b) kasayı sayan insanın "gün"ü ile defterin "gün"ü aynı gün değildi.
Kesim bunun için var: **iş günü bir takvim okuması değil, bir karardır.** KARAR-044.
:::

---

## 5. Hata kodları (`rc` — response code)

| Kod | Anlamı | Terminal ne yapar |
|---|---|---|
| `00` | Başarılı | Devam |
| `14` | Kart tanınmadı | Kartı iade et, mesaj göster |
| `51` | Yetersiz bakiye | Mesaj göster, tutar ekranına dön |
| `55` | Yanlış PIN | Tekrar sor (kalan hak `remainingTries`) |
| `75` | PIN deneme hakkı bitti | Kartı **tut**, işlem yok |
| `61` | Tutar limiti aşıldı | Mesaj göster |
| `12` | Geçersiz işlem (kimlik bilinmiyor, alan eksik) | İşlemi iptal et, kayda yaz |
| `96` | Host iç hatası | Ters kayıt üret |
| `91` | Host geçici olarak cevap veremiyor | Ters kayıt üret |
| `95` | Mutabakat hatası: toplamlar tutmuyor veya o güne ait açık işlem var | Günü **kapatma**, farkı kayda yaz |
| `98` | O gün zaten kapatılmış | Hiçbir şey yapma; cevaptaki toplamlar ilk kapanışın toplamlarıdır |

`rc` **her cevapta** vardır. `00` dışındaki her kod için terminalin ne yapacağı yukarıda
yazılıdır — "duruma bakarız" diye bir satır yoktur.

:::tuzak
Ekranda gösterilen mesaj bu kodların çevirisi **değildir.** Müşteri "96 host iç hatası"
görmez; "İşleminiz tamamlanamadı, lütfen daha sonra tekrar deneyin" görür. Teknik kod
kayda yazılır, ekrana değil.
:::

---

## 6. Zaman aşımı süreleri

| Ne | Süre | Süre dolduğunda |
|---|---|---|
| `PinVerifyRequest` cevabı | 15 sn | İşlemi iptal et, kartı iade et |
| `BalanceRequest` cevabı | 15 sn | İşlemi iptal et (borçlanma yok, ters kayıt gerekmez) |
| `WithdrawalAuthRequest` cevabı | **30 sn** | **Ters kayıt üret** (§4.5) |
| `DispenseAdvice` cevabı | 30 sn | Bildirimi tekrar dene (kuyruğa gir) |
| `DepositAuthRequest` cevabı | 30 sn | Parayı escrow'dan müşteriye iade et |
| `DepositCommitAdvice` cevabı | 30 sn | Kuyruğa gir, tekrar dene |
| `EchoRequest` cevabı | 10 sn | 3 kere üst üste kaçarsa hat kopuk sayılır |
| `CutoverRequest` cevabı | 30 sn | **Günü kapatma**, tarihi değiştirme; hat gelince tekrar dene |

Bütün süreler **sanal saatten** ölçülür. Testlerde gerçek bekleme yoktur; saat ileri
sarılır. Aynı senaryo aynı seed ile birebir aynı sonucu verir — vermiyorsa sonuç yoktur.

---

## 7. Bu sözleşmede henüz cevabı olmayanlar

Uydurmak yerine açık soru olarak bırakıldı:

- STAN 999999'dan 1'e döndüğünde aynı iş gününde çakışma nasıl önlenir?
- `RETRACTED` durumunda geri alınan para gerçekte hangi kovaya yazılır, gün sonunda nasıl
  mutabık kılınır?
- Escrow'da `JAMMED` (sıkışma) durumunda müşteriye ne söylenir, hesap ne zaman düzeltilir?
  (Bizim cevabımız: hesap **hiç** artmaz ve durum servis işidir — ama gerçekte sıkışan
  paranın müşteriye iadesi hangi süreçle ve kaç günde yapılıyor, bilmiyoruz.)
- Kasete alma sırasında elektrik kesilirse, gerçek makineler açılışta ne yapıyor: sayaçtan
  mı okuyor, insan sayımına mı bırakıyor?
- Gün sonu kesimini terminal mi host mu başlatır? (Bizim cevabımız: terminal — §4.7'deki
  gerekçeyle. Gerçekte hostun kesim zorladığı kurumlar var mı, bilmiyoruz.)
- Kesim anında hostta açık kalan bir yetkilendirme gerçekte günü kapatmayı engelliyor mu,
  yoksa askı hesabına mı düşüyor? Askıdaki kalem kaç günde ve kim tarafından çözülüyor?
