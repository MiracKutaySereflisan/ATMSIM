# Para nerede durur — hesap, defter, kaset, kova modeli

Durum: **yazıldı** — 2026-08-24, Faz 0. Kod yazılmadan önce, kâğıt üstünde.

Bu dosya `docs/protocol.md`'nin tamamlayıcısıdır. Protokol **iki makinenin ne
konuştuğunu** yazar; bu dosya **paranın nerede durduğunu** yazar. Para korunumu iddiası
ancak paranın bulunabileceği yerlerin tamamı sayılabilirse denetlenebilir.

---

## 1. Kovalar — paranın bulunabileceği yerler

:::terim
**Kova (bucket):** bir banknotun bulunabileceği, sayılabilir yerlerden her biri.
:::

Bir banknot her an **tam olarak bir** kovadadır. Kova listesi kapalıdır: burada yazmayan
bir yerde para olamaz. Liste eksikse para korunumu denetleyicisi yalan söyler.

| Kova | Nerede | Kim sayar |
|---|---|---|
| `Cassette` | Dağıtım kasetlerinin içi | Terminal |
| `Recycler` | Geri dönüşüm kasetlerinin içi (yatırılan, tekrar dağıtılabilir para) | Terminal |
| `Escrow` | Ara kasa — yatırılan, henüz onaylanmamış para | Terminal |
| `Transit` | Nakit ağzına gelmiş, müşterinin alması beklenen para | Terminal |
| `Customer` | Müşterinin cebi — sistemden **çıkmış** para | Kimse (çıkış noktası) |
| `Retract` | Alınmadığı için geri çekilen para | Terminal, gün sonunda elle |
| `Reject` | Tanınmayan / sahte / yırtık olduğu için ayrılan banknot | Terminal, gün sonunda elle |

:::tuzak
**Geri alınan parayı "kasede geri döndü" saymak.** `Retract` ayrı bir kovadır. Geri
alınan banknotlar dağıtım kasetine geri konmaz — makine onları tanımadığı bir sırayla
almıştır ve tekrar dağıtılamazlar. Kasete geri saymak, kasetteki parayı olduğundan fazla
göstermek ve gün sonunda "sebepsiz fazla" üretmek demektir.
:::

### 1.1 Korunum denklemi

Her an, sistemdeki banknot sayısı için şu doğru olmak zorundadır:

```
başlangıç_kaset + başlangıç_recycler
  = Cassette + Recycler + Escrow + Transit + Retract + Reject
  + (Customer'a çıkan)
  - (Customer'dan giren)
```

Bu bir cümle değil, **koşulabilir bir denetleyicidir.** Her arıza senaryosu ondan sonra
puanlanır. Faz 2'de kod olarak yazılacak; şimdilik yazılı hâli budur.

Hesap tarafındaki karşılığı ayrıdır ve **ayrı ayrı bozulabilir**:

```
hesap borçlandıysa → ya nakit müşteriye verildi ya da bir ters kayıt üretildi
nakit çıktıysa     → ona karşılık gelen onaylanmış bir yetkilendirme var
host defteri       = terminal günlüğü (gün sonu mutabakatında)
```

:::neden
İki taraf neden ayrı ayrı denetleniyor: tek doğruluk kaynağı yoktur. Terminal parayı
saydığını sanabilir, host hesabı işlediğini sanabilir, ikisi aynı anda yanılabilir.
Mutabakat tam olarak bu yüzden vardır — biri diğerini doğrulamak için değil, **ikisinin
ayrıştığı anı yakalamak için.**
:::

---

## 2. Kasetler

:::terim
**Kaset (cassette):** ATM'nin içinde tek bir kupürü tutan çekmece. Her kupür ayrı
kasettedir; bir kasette karışık banknot bulunmaz.
:::

:::terim
**Kupür (denomination):** bir banknotun değeri. Bizde 200, 100, 50, 20 TL.
:::

Başlangıç yapılandırması (`ASSUMPTION`: rakamlar bizim seçimimiz, gerçek bir ATM'nin
yüklemesi değildir):

| Kaset | Kupür | Adet | Tip |
|---|---|---|---|
| `K1` | 200 TL | 500 | Geri dönüşümlü |
| `K2` | 100 TL | 1000 | Geri dönüşümlü |
| `K3` | 50 TL | 1000 | Geri dönüşümlü |
| `K4` | 20 TL | 500 | Yalnızca dağıtım |

:::terim
**Geri dönüşümlü kaset (recycler):** yatırılan banknotların gittiği ve oradan tekrar
dağıtılabildiği kaset. Geri dönüşümsüz bir ATM'de yatırılan para ayrı bir kasaya gider ve
bir daha müşteriye verilmez.
:::

10 TL ve altı kupür yoktur. Bu, çekilebilecek tutarları sınırlar — ama sınırın nerede
olduğu bakışla bulunamaz.

> **Düzeltme (2026-08-25, Faz 2a).** Bu satır önceden şöyle yazıyordu: *"20'nin katı
> olmayan hiçbir tutar verilemez."* **Bu yanlıştı** ve kod yazılırken yakalandı. 70 TL
> 20'nin katı değildir, ama 50 + 20 ile tam olarak verilir. 110 TL de öyle: 50 + 20 + 20 +
> 20. Doğru ifade şudur: her kupür 10 TL'nin tam katı olduğu için **10'un katı olmayan
> hiçbir tutar verilemez**; 10'un katı olan tutarların hangisinin verilebileceği ise ancak
> kupür planlayıcısı çalıştırılarak bilinir (30 TL 10'un katıdır ve verilemez). Bölünebilme
> kuralıyla karar vermek, tam olarak KARAR-011'in reddettiği kısayolun başka bir biçimidir.
> Ayrıntı: KARAR-028.

Ekran, verilebilirliği müşteriye tutar seçilirken söyler — hostun cevabını bekledikten
sonra değil.

---

## 3. Kupür seçimi — hangi banknotlardan kaç tane

Müşteri "350 TL" der. Makine bunu hangi banknotlarla vereceğine **yetkilendirmeden önce**
karar vermek zorundadır (kural §4.6 (docs/proje-kurallari.md)).

:::tuzak
**En yaygın hata: açgözlü (greedy) algoritma.** "En büyük kupürden başla, sığdıkça koy."
Basit, hızlı ve **yanlış.** 60 TL isteyen bir müşteri düşün; kasetlerde yalnızca 50'lik ve
20'lik kalmış olsun. Açgözlü algoritma önce bir 50'lik koyar, geriye 10 TL kalır, 10'luk
kupür yoktur ve algoritma "veremem" der. Oysa **3 adet 20'lik** ile 60 TL tam olarak
verilebilirdi. Açgözlü algoritma, verilebilecek bir tutarı verilemez ilan eder.
:::

**Seçtiğimiz yöntem:** tam çözüm. Kaset adetlerini de hesaba katan bir sınırlı bozuk para
problemi (bounded coin change), dinamik programlama ile çözülür. Tablo, kupürlerin en
büyük ortak böleni kadar adımlarla ilerler; bu yüklemede o adım 10 TL'dir, yani 5000 TL'lik
bir istek için kaset başına 501 hücre. (Önceden burada "20 TL adım, 251 hücre" yazıyordu;
adım 20 değil 10 TL'dir — yukarıdaki düzeltme.)

Birden çok geçerli kombinasyon varsa sıralama şudur:

1. **En az banknot** — müşteri için de makine için de iyi.
2. Eşitlikte: **en dolu kasetten harca.** Sebebi, kasetlerin aynı anda değil, sırayla
   bitmesi; hepsi aynı anda biterse ATM tamamen durur.

**Sonuç `denoms` alanında yetkilendirme isteğine konur** ve host tutar ile kupür
dökümünün tutarlı olduğunu doğrular. Tutmuyorsa istek `12` (geçersiz işlem) ile reddedilir
— iki tarafın aynı sayıyı farklı biçimde anlaması, hatanın en pahalı türüdür.

---

## 4. Hesap ve defter

:::terim
**Defter (ledger):** hesabın bakiyesini değil, bakiyeyi oluşturan **hareketlerin sırasını**
tutan kayıt. Bakiye, defterin toplamıdır.
:::

:::neden
Neden bakiyeyi tek bir sayı olarak tutmuyoruz: bir sayı "şu an ne kadar var" sorusuna
cevap verir, "nasıl bu hâle geldi" sorusuna vermez. Bu projenin bütün konusu ikinci soru.
Bakiye bozulduğunda tek sayılık bir modelde geriye dönüp bakılacak hiçbir şey yoktur.
:::

Hesap:

| Alan | Tip | Not |
|---|---|---|
| `AccountId` | metin | Uydurma, örn. `TR-DEMO-001` |
| `Pan` | metin | Uydurma kart numarası, Luhn-geçerli, kayıtlarda maskeli |
| `LedgerBalance` | tam sayı (kuruş) | Defterin toplamı |
| `HoldAmount` | tam sayı (kuruş) | Yetkilendirilmiş ama henüz kapanmamış tutar |
| `AvailableBalance` | hesaplanır | `LedgerBalance - HoldAmount` |

Defter hareketi:

| Alan | Not |
|---|---|
| `Seq` | Artan sıra numarası; defter sıralıdır |
| `BizDate` | Hangi iş gününe yazıldı |
| `Stan` | Hangi işleme ait (KARAR-009) |
| `Kind` | `Withdrawal` · `Deposit` · `Reversal` · `Adjustment` |
| `Amount` | Kuruş, işaretli: çekim negatif, yatırma pozitif |
| `Note` | İnsan için kısa açıklama |

**Bir hareket silinmez.** Yanlış bir hareket, onu geri alan **ikinci bir hareketle**
düzeltilir (ters kayıt veya düzeltme). Sebebi tek cümleyle: silinen bir hareket,
olmamış gibi görünen bir hata demektir; hatanın kendisi kadar, olduğu da kayıtlıdır.

### 4.1 Başlangıç hesapları

`ASSUMPTION`: tamamı uydurma. Gerçek hiçbir hesaba, karta veya kişiye karşılık gelmez.

| Hesap | Kart (PAN) | Başlangıç bakiyesi | Ne için |
|---|---|---|---|
| `TR-DEMO-001` | `4111 1111 1111 1111` | 2 500,00 TL | Normal akış |
| `TR-DEMO-002` | `4222 2222 2222 2220` | 45,00 TL | Yetersiz bakiye ve 20'nin katı olmayan bakiye |
| `TR-DEMO-003` | `4333 3333 3333 3335` | 100 000,00 TL | Kaset tükenmesi senaryosu |

PIN'ler kaynak kodda değil, host'un başlangıç verisinde durur ve **hiçbir kayda
yazılmaz** (KARAR-010).

---

## 5. Bu modelde henüz cevabı olmayanlar

Uydurulmadı; açık soru olarak bırakıldı:

- `Retract` kovasındaki para gerçekte gün sonunda hangi hesaba yazılır — müşteri hesabına
  iade mi edilir, banka kasasına mı geçer, ne kadar bekletilir?
- Geri dönüşümlü kasette müşterinin yatırdığı banknot, bir sonraki müşteriye verilmeden
  önce ne kadar doğrulamadan geçer?
- Kaset adetleri gerçekte ne mertebededir; "az kaldı" eşiği hangi orandır?
