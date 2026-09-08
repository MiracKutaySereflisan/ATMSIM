# Arıza senaryoları — dosya biçimi

Bu dosya, `scenarios/*.json` altındaki senaryo dosyalarının sözleşmesidir. Kod yazılmadan
önce yazıldı (kural §5 (docs/proje-kurallari.md)) ve değişmesi gerekirse **önce burası** değişir.

Buradaki hiçbir biçim gerçek bir kurumun senaryo formatından alınmamıştır; simülasyon için
uyduruldu (**invented for simulation**).

---

## 1. Neden senaryolar bir dosyada, kodun içinde değil

kural §5 (docs/proje-kurallari.md): *"Arıza senaryoları koda gömülmez, veriye yazılır."* Üç sebebi var ve üçü de
bu projenin iddiasıyla doğrudan ilgili.

**Bir senaryo eklemek kod değiştirmeyi gerektirmemeli.** Gerektirseydi, her yeni senaryo
"acaba senaryoyu mu ekledim, davranışı mı değiştirdim" sorusunu doğururdu.

**Senaryolar sayılabilir olmalı.** Kapsama matrisi (§5 aşağıda) dosyaların kendisinden
üretiliyor. Kodun içine gömülü senaryoları saymak, kodu okuyup yorumlamak demektir.

**Beklenen davranış, koddan önce yazılabilmeli.** Bir senaryo dosyası, kod o davranışı
gösterebilmeden önce yazılabilir ve kırmızı yanar. Önce kodu yazıp çıkan sonucu "beklenen"
ilan etmek, kendi kendini onaylamaktır.

---

## 2. Bir senaryo dosyasının tamamı

```json
{
  "id": "C-ZA-YETKI",
  "baslik": "Çekimde yetkilendirme cevabı gelmiyor",
  "aciklama": "Host parayı bloke etmiş olabilir de olmayabilir de. Terminal bilmiyor.",
  "seed": 4001,
  "eksen": { "islem": "cekim", "ariza": "cevap-kaybi", "an": "yetkilendirme" },
  "adimlar": [
    { "adim": "cevaplari-yut", "mesaj": "WithdrawalAuthRequest" },
    { "adim": "cek", "kart": "4111111111111111", "tutar": 35000, "stan": 401 },
    { "adim": "hat-gelsin" },
    { "adim": "zaman-gecir", "saniye": 300 },
    { "adim": "kuyrugu-bosalt" }
  ],
  "beklenen": {
    "musteriye-giden": 0,
    "hesap-farki": 0,
    "denetim": "temiz",
    "tespit": "yok"
  }
}
```

Her alan aşağıda tek tek açıklanıyor. **Bilinmeyen bir alan hata verir**, sessizce
atlanmaz: yazım hatası yüzünden hiç uygulanmamış bir arıza, yeşil yanan bir senaryo üretir
ve yeşil yanan yanlış senaryo, hiç yazılmamış senaryodan kötüdür.

---

## 3. Üst düzey alanlar

| Alan | Zorunlu | Ne işe yarar |
|---|---|---|
| `id` | evet | Senaryonun tekil adı. Rapor, kapsama matrisi ve hata kataloğu bu adla bağlanır. Dosya adı da bu olmalıdır. |
| `baslik` | evet | Bir cümlelik Türkçe özet; rapor satırında görünür. |
| `aciklama` | hayır | Bu senaryonun **neden** ilginç olduğu. Kapsama sayısı değil, anlamı. |
| `seed` | evet | Rastgeleliğin tohumu. Aynı `seed` + aynı `id` = birebir aynı koşu (kural §5 (docs/proje-kurallari.md)). |
| `eksen` | evet | Kapsama matrisinin üç koordinatı. Bkz. §5. |
| `adimlar` | evet | Sırayla uygulanan adımlar. Bkz. §4. |
| `beklenen` | evet | Koşudan **önce** yazılan beklenti. Bkz. §6. |

### `id` biçimi

`<işlem>-<arıza>-<an>` kısaltmaları: `C` çekim, `Y` yatırma, `B` bakiye · `ZA` zaman aşımı,
`HK` hat kopması, `CI` çift istek, `KD` kısmi dağıtım, `BK` boş kaset, `HR` host yeniden
başlatma. Örnek: `C-KD-DAGITIM` — çekimde, kısmi dağıtım, dağıtım anında.

Bu kısaltmalar insanlar içindir; makine `eksen` alanını okur. **İkisi çelişirse `eksen`
geçerlidir** ve koşucu uyarı basar.

---

## 4. Adımlar

Adımlar **sırayla** uygulanır. Bir arıza adımı, kendisinden sonraki adımları etkiler;
kapatılana kadar açık kalır.

### 4.1 İşlem adımları

| `adim` | Alanlar | Ne yapar |
|---|---|---|
| `cek` | `kart`, `tutar`, `stan` | Bir çekim koşar (baştan sona). |
| `yatir` | `kart`, `banknotlar`, `onay`, `stan` | Bir yatırma koşar (baştan sona). `banknotlar`: `"100x5,50x2"` — **lira** cinsinden. `onay`: müşteri onay ekranında ne yaptı. |
| `yatir-basla` | `kart`, `banknotlar`, `stan` | Yatırmanın yalnızca ilk yarısı: para sayılır, ara kasada bekler. |
| `yatir-bitir` | `onay` | Bekleyen yatırmayı bitirir. İkisi arasına arıza adımı konabilir — kural §4.8 (docs/proje-kurallari.md)'in tam olarak sorduğu an. |
| `bakiye` | `kart`, `stan` | Bakiye sorgular. |
| `gun-kapat` | `stan` | Gün sonu kesimi dener. |

### 4.2 Hat arızaları

| `adim` | Alanlar | Ne yapar |
|---|---|---|
| `hat-kes` | — | Hiçbir mesaj hosta ulaşmaz. |
| `hat-gelsin` | — | Bütün hat arızalarını kaldırır (yutmalar dahil). |
| `istekleri-yut` | `mesaj` | Yalnızca o tip **isteği** hosta ulaştırmaz. Host hiç duymaz. |
| `cevaplari-yut` | `mesaj` | İstek ulaşır, host işi yapar, **cevap** dönmez. |

`istekleri-yut` ile `cevaplari-yut` arasındaki fark bu projenin merkezindedir: birincisinde
işlem hostta **olmadı**, ikincisinde **oldu** — ve terminal ikisini birbirinden ayıramaz.
Bir arıza kümesi bu ikisini ayrı senaryolamıyorsa, zaman aşımı konusunu hiç ele almamış
demektir.

### 4.3 Makine arızaları

| `adim` | Alanlar | Ne yapar |
|---|---|---|
| `eksik-ver` | `en-fazla` | Dağıtıcı, istenen tutardan azını verir (en fazla bu kadar). |
| `sikisma` | — | Dağıtıcı hiç veremez. |
| `dagitici-duzelsin` | — | Makine arızasını kaldırır. |
| `yatirma-sikismasi` | — | Yatırılan banknotların hiçbiri kasete ulaşamaz. |
| `yatirma-eksik-alsin` | `en-fazla` | Banknotların yalnızca bu kadarı kasete ulaşır, gerisi sıkışır. |
| `yatirma-duzelsin` | — | Yatırma arızasını kaldırır. |
| `para-alinmasin` | — | Ağza gelen parayı kimse almaz (retract). |
| `para-alinsin` | — | Varsayılan davranışa döner. |
| `kaset-ayarla` | `kupur`, `adet` | O kupürün kasetindeki banknot sayısını ayarlar. **Yalnızca kurulum adımıdır:** ilk işlemden sonra kullanılamaz. |

`kaset-ayarla` neden yalnızca başta çalışır: bir kasetin içeriğini işlemlerin ortasında
değiştirmek, para korunumu denklemine dışarıdan el atmaktır — kasetten eksilen para bir yere
gitmeden yok olur ve denetleyici haklı olarak "para kayboldu" der. Gerçek hayatta kaseti
boşaltan da bir insandır ve o iş **işlem dışıdır**. Bu yüzden boş kaset senaryoları,
makinenin o kupürle **sabaha başladığı** senaryolardır; koşucu, işlem başladıktan sonra
gelen bir `kaset-ayarla` adımını hata sayar.

### 4.4 Zaman ve kuyruk

| `adim` | Alanlar | Ne yapar |
|---|---|---|
| `zaman-gecir` | `saniye` | Sanal saati ileri alır. **Gerçek bekleme yoktur.** |
| `kuyrugu-bosalt` | — | Bekleyen bildirim ve ters kayıtları göndermeyi dener. |
| `host-yeniden-baslat` | — | Host'u kapatıp açar; durumunu defterinden geri kurar (KARAR-047). |

---

## 5. `eksen` — kapsama matrisinin koordinatları

Kapsama matrisi `işlem × arıza × an` üç boyutludur ve **senaryo dosyalarından** üretilir.

| Boyut | Geçerli değerler |
|---|---|
| `islem` | `cekim`, `yatirma`, `bakiye`, `gun-sonu` |
| `ariza` | `cevap-kaybi`, `istek-kaybi`, `hat-kopmasi`, `cift-istek`, `kismi-dagitim`, `alinmayan-para`, `bos-kaset`, `sikisma`, `host-yeniden-baslatma`, `yok` |
| `an` | `yetkilendirme`, `dagitim`, `bildirim`, `ters-kayit`, `escrow`, `onay`, `kesim`, `bosta` |

Değer listesi **kapalıdır**. Yeni bir değer eklemek, matrise yeni bir satır/sütun eklemektir
ve bilinçli bir karardır — bu yüzden koşucu, listede olmayan bir değeri hata sayar.

**Boş hücreler adıyla raporlanır.** Kapsama bir övünme sayısı değil, bir dürüstlük
sayısıdır (kural §3 (docs/proje-kurallari.md)): koşulmayan `cekim × bos-kaset × dagitim` hücresi, raporda tam bu
adla görünür.

---

## 6. `beklenen` — koşudan önce yazılan beklenti

| Alan | Zorunlu | Anlamı |
|---|---|---|
| `musteriye-giden` | evet | Senaryo bittiğinde müşterilerin elindeki toplam nakit, kuruş. |
| `hesap-farki` | evet | İşlemi yapan kartın defter bakiyesindeki net değişim, kuruş. Çekim negatif, yatırma pozitif. |
| `denetim` | evet | `temiz` (açıklanamayan fark yok) veya `ihlal`. |
| `tespit` | evet | Bir ihlal varsa **nerede görünüyor**: `aninda`, `gun-sonu`, `yok`. Temiz senaryolarda `yok`. |
| `ihlal-sayisi` | hayır | Beklenen açıklanamayan fark sayısı. Yazılmazsa yalnızca `denetim` bakılır. |
| `gun-kapandi` | hayır | Senaryoda `gun-kapat` varsa, kapanıp kapanmadığı. |

### `tespit` ne demek, ne demek değil

- **`aninda`** — senaryo biter bitmez, kuyruk boşalmadan, denetleyici farkı görüyor.
- **`gun-sonu`** — kuyruk boşaldıktan sonra hâlâ fark var; gün sonu denetiminde ya da
  kesimde görünüyor.
- **`yok`** — hiçbir yerde görünmüyor. **Temiz bir senaryoda doğru cevap budur; ihlalli bir
  senaryoda ise bu, projenin bulabileceği en kötü sonuçtur** — para kaybolmuş ve hiçbir
  mekanizma haber vermemiş.

`tespit` alanı, "kaç ihlal var" sorusundan daha önemli olan soruyu taşır: **ihlali kim, ne
zaman fark ediyor.**

---

## 7. Beklenti tutmazsa ne olur

Koşucu **hata verir ve senaryoyu kırmızı sayar.** Beklentiyi çıkan sonuca göre
güncellemek yasaktır — o, kendi kendini onaylamaktır (kural §5 (docs/proje-kurallari.md)). Doğru sıra şudur:

1. Beklenti neden yazıldığı gibi? Alan gerçeğine (kural §4 (docs/proje-kurallari.md)) dayanıyor mu?
2. Dayanıyorsa **kod yanlış**; kod düzeltilir, senaryo aynı kalır.
3. Dayanmıyorsa beklenti yanlış yazılmıştı; düzeltilir **ve neden yanlış yazıldığı
   kayda geçirilir.** Sessizce düzeltilen bir beklenti, bir sonraki sefer yine sessizce
   düzeltilir.

---

## 8. Naif akış karşılaştırması

Aynı senaryo kümesi iki kez koşulur: **sertleştirilmiş** akışla (bugünkü kod) ve **naif**
akışla (alan gerçeklerinin bilerek uygulanmadığı hâl). Karşılaştırma ancak **eşleşen
koşulda** okunur (kural §5 (docs/proje-kurallari.md)): aynı dosyalar, aynı seed'ler, aynı adımlar.

Naif akış ayrı bir kod kopyası değildir — kopya olsaydı iki kopya birbirinden kayardı ve
karşılaştırma anlamını yitirirdi. Bunun yerine sertleştirmeler **kapatılabilir** hâlde
tutulur; hangi anahtarın hangi alan gerçeğini kapattığı `reports/naive-mode.md`'de yazılı
olacaktır.

Manşet rakam buradan çıkar: *"N senaryonun naif akışta kaçı kapatılamayan dengesizlikle
bitiyor, sertleştirilmiş akışta kaçı."*

---

## 9. Bu biçimin bilinçli sınırları

- **Tek terminal, tek host.** Çok ATM kapsam dışı (kural §3 (docs/proje-kurallari.md)).
- **Adımlar sıralıdır, eşzamanlılık yoktur.** Gerçek bir ATM'de iki şey aynı anda olabilir;
  bizde olamaz. Bu, modeli **iyimser** yapan bir sınırdır ve `reports/assumptions.md`'de
  yazılıdır.
- **Senaryo, defterin içeriğini doğrulamaz**, yalnızca sonuçları ve denetim verdiktini.
  Defter satırı düzeyindeki iddialar testlerin işidir; senaryolar davranışın işidir.
