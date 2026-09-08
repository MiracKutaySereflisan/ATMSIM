# Kurulum

Bu projeyi çalıştırmak için iki araç gerekir. İkisi de ücretsizdir ve bu işin
standardıdır; kurulum yaklaşık 15 dakika sürer. Git macOS'te hazır gelir.

---

## 1) .NET 10 SDK — projenin motoru

**Ne işe yarar:** Yazdığımız C# kodunu çalıştırılabilir programa çeviren araç takımı.
Bu kurulmadan projede hiçbir şey çalışmaz. ("SDK" = Software Development Kit,
yazılım geliştirme takımı.)

**Nasıl kurulur:**

1. Şu adrese gidin: **https://dotnet.microsoft.com/download/dotnet/10.0**
2. **SDK** başlığını seçin. ("Runtime" değil: Runtime yalnızca çalıştırır, SDK ayrıca
   derler. Bu proje SDK ister.)
3. İşlemciye uyan satırdaki **Installer** bağlantısını indirin: Apple Silicon için
   **macOS → Arm64**, Intel için **macOS → x64**.
4. İnen `.pkg` dosyasını çalıştırın ve kurulumu tamamlayın.

**Kurulduğu nasıl doğrulanır:** Terminal'i açıp (Spotlight: ⌘ + boşluk → "Terminal")
şu komutu çalıştırın:

```
dotnet --version
```

`10.` ile başlayan bir sürüm numarası görünüyorsa kurulum tamamdır.

---

## 2) Visual Studio Code + C# Dev Kit — kodun görüldüğü yer

**Ne işe yarar:** Kodun okunduğu ve gezildiği editör. Proje bu editör olmadan da
derlenir ve çalışır; editör, dosyalar arasında gezinmeyi ve sunumda kod göstermeyi
kolaylaştırır.

**Nasıl kurulur:**

1. **https://code.visualstudio.com** adresinden **Download for Mac** ile indirin.
2. İnen dosyayı açıp **Visual Studio Code** uygulamasını **Applications** klasörüne taşıyın.
3. VS Code'u açın; sol kenar çubuğundaki **Extensions** (Eklentiler) bölümüne girin.
4. **C# Dev Kit** eklentisini arayıp Microsoft'un yayımladığı sürümü kurun.
   (Bu eklenti C# dosyalarını renklendirir, hataları anında gösterir, testleri
   düğmeyle çalıştırır.)

---

## Kurmayacağımız şeyler ve nedeni

| Ne | Neden gerekmiyor |
|---|---|
| Visual Studio (büyük olan) | Mac sürümü emekliye ayrıldı; VS Code + C# Dev Kit standart yol. |
| Docker | Tek makinede iki program çalıştırıyoruz; konteyner gereksiz karmaşıklık. |
| SQL Server / PostgreSQL | Veritabanı bilinçli olarak kapsam dışı; dosya ve bellek yeter. |
| Homebrew | Bu iki kurulum için gerekmiyor. |
| Node.js | Ekran tek bir HTML dosyası; dış bağımlılık yok. |

Depo hiçbir dış pakete bağlı değildir: testler kendi koşucusuyla koşar, ekran tek bir
HTML dosyasıdır ve hiçbir dış kaynak çağırmaz.

---

## Projeyi çalıştırma

Depo klasörünün içinden:

```
./scripts/demo.sh --yeni-gun   # temiz bir iş günüyle başlar
./scripts/demo.sh              # host + terminal + tarayıcı ekranı
```

Ekran, terminalin servis ettiği adresten açılır (`http://127.0.0.1:8080/`). HTML dosyası
doğrudan çift tıklanarak açılmaz: o hâlde arkasında bağlanacağı bir terminal olmaz.

---

## Demo kartları ve PIN'leri

Bunların hepsi **uydurmadır.** Gerçek hiçbir karta, hesaba veya kişiye karşılık gelmez;
kart numaraları yalnızca biçim olarak geçerlidir (Luhn kuralına uyar).

| Hesap | Kart numarası | PIN | Bakiye | Ne için |
|---|---|---|---|---|
| TR-DEMO-001 | 4111 1111 1111 1111 | **1234** | 2 500,00 TL | Normal akış |
| TR-DEMO-002 | 4222 2222 2222 2220 | **2468** | 45,00 TL | Yetersiz bakiye |
| TR-DEMO-003 | 4333 3333 3333 3339 | **1357** | 100 000,00 TL | Kaset tükenmesi |

**Bu PIN'ler neden burada yazılı:** Demoyu kullanabilmek için gerekiyor — tıpkı bir test
ortamının test PIN'lerinin yazılı olması gibi. Kaynak kodda PIN **yoktur**: host yalnızca
geri çevrilemez bir doğrulama değeri saklar (KARAR-017). Yani sistemin kendisi PIN'i
saklamaz ve hiçbir kayda yazmaz; bu tablo bir demo belgesidir, sistemin bir parçası değil.

**Deneme hakkı:** Üç yanlış PIN'den sonra kart tutulur (cevap kodu `75`). Doğru PIN sayacı
sıfırlar. Sayaç karta aittir, oturuma değil.
