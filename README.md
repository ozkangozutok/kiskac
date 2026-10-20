<p align="center">
  <img src="logo.png" width="128" alt="Kıskaç logosu">
</p>

<h1 align="center">Kıskaç</h1>

<p align="center">
  Windows, macOS ve Linux için hafif, hızlı ve açık kaynaklı ekran görüntüsü aracı.<br>
  Kısayola bas, alanı seç, üstüne çiz, kopyala.
</p>

<p align="center">
  <a href="https://github.com/ozkangozutok/kiskac/releases/latest"><img alt="Son sürüm" src="https://img.shields.io/github/v/release/ozkangozutok/kiskac?label=s%C3%BCr%C3%BCm"></a>
  <a href="LICENSE"><img alt="Lisans: MIT" src="https://img.shields.io/github/license/ozkangozutok/kiskac?label=lisans"></a>
  <a href="https://github.com/ozkangozutok/kiskac/actions"><img alt="Derleme" src="https://img.shields.io/github/actions/workflow/status/ozkangozutok/kiskac/linux.yml?label=derleme"></a>
</p>

---

## Özellikler

- **Anında çekim:** kısayola bastığında ekran kararır, alanı sürükleyerek seçersin. Tek tıklarsan o ekranın tamamı seçilir.
- **Seçimi düzenleme:** seçimi taşı, 8 tutamaçtan boyutlandır. Boyut piksel olarak görünür.
- **Çizim araçları:** kalem, çizgi, ok, dikdörtgen, fosforlu kalem ve metin; 6 renk ve ayarlanabilir kalınlık.
- **Kopyala veya kaydet:** panoya kopyala ya da PNG / JPEG olarak kaydet.
- **Metni kopyala (Windows, macOS):** seçili alandaki yazıyı (ekran görüntüsü, video karesi, düzenlenemeyen metin) okuyup panoya kopyalar. Türkçe karakterler dahil; sistemin yerleşik metin tanıması kullanılır, internete bağlanmaz.
- **Ekran videosu ve GIF (Windows, macOS):** seçili alanı sistem sesi ve/veya mikrofonla MP4 olarak ya da GIF olarak kaydet.
- **Çoklu ekran ve yüksek çözünürlük:** Retina, 4K ve kesirli ölçeklemede net, tam piksel görüntü.
- **Arka planda sessizce çalışır:** sistem tepsisinde (macOS'ta menü çubuğunda) durur, oturum açılınca kendiliğinden başlar.
- **Ayarlanabilir:** kısayol, karartma, kayıt klasörü ve biçimi, sormadan kaydetme ve daha fazlası.
- **Kendini günceller:** yeni sürümler GitHub'dan indirilir, doğrulanır ve kurulur.
- **Hafif:** kurulum gerektirmez, reklam ya da hesap yoktur, internete yalnızca güncelleme denetimi için bağlanır.

## İndir

| Platform | Dosya | Gereksinim |
|---|---|---|
| Windows | [**Kiskac.exe**](https://github.com/ozkangozutok/kiskac/releases/latest/download/Kiskac.exe) | Windows 10 veya 11 |
| macOS | [**Kiskac-macOS.zip**](https://github.com/ozkangozutok/kiskac/releases/latest/download/Kiskac-macOS.zip) | macOS 14 Sonoma veya üstü (Apple Silicon ve Intel) |
| Linux | [**Kiskac-x86_64.AppImage**](https://github.com/ozkangozutok/kiskac/releases/latest/download/Kiskac-x86_64.AppImage) | 64 bit, X11 veya Wayland |

Tüm sürümler ve değişiklikler: [Releases](https://github.com/ozkangozutok/kiskac/releases) · [CHANGELOG](CHANGELOG.md)

## Çalıştırma

### Windows

1. **Kiskac.exe**'yi indir ve istediğin bir klasöre koy (ör. `Belgeler\Kıskaç`). Kurulum yoktur.
2. Çift tıklayarak çalıştır. Kıskaç sistem tepsisine yerleşir.
3. **PrtSc** tuşuna bas.

> İlk çalıştırmada **Windows SmartScreen** uyarı verebilir, çünkü exe dijital olarak imzalı değil. **Ek bilgi → Yine de çalıştır** ile devam et.

### macOS

1. **Kiskac-macOS.zip**'i indir, aç ve **Kiskac.app**'i **Uygulamalar** klasörüne taşı.
2. Uygulamayı aç. Kıskaç menü çubuğuna yerleşir (Dock'ta görünmez).
3. **⇧⌘2** tuşlarına bas. İlk çekimde macOS **Ekran ve Sistem Sesi Kaydı** izni ister: **Sistem Ayarları › Gizlilik ve Güvenlik** bölümünden Kıskaç'ı aç ve uygulamayı yeniden başlat.

> İlk açılışta "doğrulanamadı" uyarısı çıkarsa: **Sistem Ayarları › Gizlilik ve Güvenlik › Yine de Aç**.
> PC klavyesi kullanıyorsan **PrtSc** tuşu da çalışır (macOS'ta F13 olarak gelir).

### Linux

```sh
chmod +x Kiskac-x86_64.AppImage
./Kiskac-x86_64.AppImage
```

Kıskaç sistem tepsisine yerleşir. Kısayol masaüstü ortamına göre farklı çalışır:

| Ortam | Kısayol |
|---|---|
| **X11** (GNOME, KDE, Xfce, Cinnamon, MATE…) | Kıskaç kısayolu kendisi yakalar. Varsayılan **PrtSc**'dir; masaüstünün kendi ekran görüntüsü aracı bu tuşu kullanıyorsa Ayarlar açılır ve başka bir kısayol (ör. **Ctrl+Shift+X**) seçmeni ister. |
| **Wayland**: KDE Plasma, GNOME 48+, Hyprland | Kısayol masaüstüne kaydedilir; masaüstü ilk seferde onay ister. |
| **Diğer Wayland ortamları** | Masaüstü ayarlarında (*Klavye › Özel kısayollar*) şu komuta bir kısayol ata: `/yol/Kiskac-x86_64.AppImage --capture`. Ayarlar penceresi tam komutu gösterir. |

> Wayland'de ekran görüntüsü masaüstünün izin sistemi (xdg-desktop-portal) üzerinden alınır; ilk seferde izin istenebilir.
> GNOME'da tepsi simgesi için *AppIndicator* desteği gerekir (Ubuntu'da hazır gelir; diğer dağıtımlarda "AppIndicator and KStatusNotifierItem Support" eklentisini kur).

## Kullanım

Kısayola bastığında ekran kararır. Alanı sürükleyerek seç (tek tıklama o ekranın tamamını seçer), sonra araç çubuğuyla üstüne çiz.

| İşlev | Windows / Linux | macOS |
|---|---|---|
| Ekran görüntüsü al | `PrtSc` | `⇧⌘2` |
| Panoya kopyala ve kapat | `Ctrl+C`, `Enter` ya da çift tık | `⌘C`, `↩` ya da çift tık |
| Dosyaya kaydet | `Ctrl+S` | `⌘S` |
| Geri al | `Ctrl+Z` | `⌘Z` |
| İptal | `Esc` ya da sağ tık | `Esc` ya da sağ tık |
| Seç / taşı | `S` | `S` |
| Kalem · Çizgi · Ok | `P` · `L` · `A` | `P` · `L` · `A` |
| Dikdörtgen · Fosforlu kalem · Metin | `R` · `M` · `T` | `R` · `M` · `T` |
| Düz çizgi (45°) / kare | çizerken `Shift` | çizerken `⇧` |
| Kalınlık | fare tekerleği | kaydırma |
| Metinde yeni satır | `Shift+Enter` | `⇧↩` |
| Seçili alandaki metni kopyala | `O` | `O` |
| Video (MP4) · GIF kaydet | `V` · `G` | `V` · `G` |
| Tam ekran video başlat / durdur | `Shift+PrtSc` | `⌥⇧⌘2` |

Kıskaç zaten çalışırken uygulamayı yeniden açmak da ekran görüntüsü alır.

### Metni kopyala

Alanı seç ve araç çubuğundaki **metni kopyala** düğmesine (köşeli çerçeve içinde **T**) ya da **`O`**'ya bas. Seçili alandaki yazı satır düzeniyle panoya kopyalanır; bildirimde kaç satır kopyalandığı ve metnin başı görünür. Okuma Windows'un yerleşik metin tanımasıyla bilgisayarında yapılır, hiçbir şey internete gönderilmez.

Tanınan diller Windows'ta yüklü dillere bağlıdır (Türkçe Windows'ta Türkçe ve İngilizce metin okunur). Başka bir dili eklemek için **Windows Ayarları › Zaman ve dil › Dil ve bölge**. Simgeler bazen harf gibi okunabilir; en iyi sonuç için yalnızca yazının olduğu alanı seç.

macOS'ta okuma Apple'ın metin tanımasıyla (Vision) yapılır. macOS 15'te bu motor Türkçe bilmez ve ş, ğ, ı harflerini s, g, i okur; Kıskaç Türkçe satırlardaki kelimeleri macOS'un Türkçe yazım denetleyicisiyle düzeltir. Kelimelerin çoğu doğru düzelir (Siparişiniz, müşteri, İSTANBUL), ama iki biçimi de geçerli olan kelimelerde (sis/şiş, kurt/kürt) yanılabilir. Türkçe destekleyen bir macOS sürümünde motorun kendi Türkçesi kullanılır.

### Video ve GIF kaydı

1. Kısayola bas (ya da tepsi menüsünden, macOS'ta menü çubuğundaki menüden **Video kaydet…**'i seç) ve kaydedilecek alanı seç.
2. Araç çubuğunda **●** (MP4) ya da **GIF** düğmesine bas. Alanın çevresinde bir çerçeve ve altında küçük bir kontrol çubuğu belirir; çubuk **Hazır** durumda bekler.
3. İstersen çubuktaki **🔊 sistem sesi** ve **🎤 mikrofon** düğmeleriyle sesi açıp kapat. Simgenin üzerinde **fare tekerleği** (macOS'ta kaydırma) o kaynağın ses seviyesini değiştirir (%0–%200); yanındaki küçük gösterge sesin gelip gelmediğini gösterir. Bunlar kayıt boyunca da çalışır ve bir sonraki kayıt için hatırlanır.
4. **▶ Başlat**'a bas: 3 saniye geri sayılır ve kayıt başlar (geri sayımda ● beklemeden başlatır). Vazgeçmek için **✕**.
5. Durdurmak için çubuktaki **■** düğmesine ya da yeniden kısayola bas (macOS'ta menü çubuğundaki kırmızı simgeye tıklamak da durdurur). **⏸** ile duraklatabilirsin; çubuğu boş yerinden sürükleyebilirsin.

Kayıt *Videolar › Kıskaç* (macOS'ta *Filmler › Kıskaç*) klasörüne kaydedilir; bildirime tıklayınca dosya klasörde gösterilir. macOS'ta mikrofon ilk açıldığında izin istenir; sistem sesi ekran kaydı izniyle gelir.

**Tam ekran:** seçim yapmadan **Shift+PrtSc**'ye (macOS'ta **⌥⇧⌘2**) bas; imlecin bulunduğu ekran için kayıt çubuğu açılır. **▶**'ye (ya da yeniden Shift+PrtSc'ye) basınca kayıt başlar, aynı tuş kaydı durdurur. Tepsi menüsündeki **Tam ekran video** alt menüsünden belirli bir ekranı ya da tüm ekranları da seçebilirsin. (Kısayolu değiştirdiysen tam ekran kısayolu onun Shift'li hâlidir; macOS'ta kısayolda ⇧ zaten varsa ⌥ eklenmiş hâlidir.)

- **MP4:** H.264 video, AAC ses. Varsayılan ses kaynağı (sistem sesi, mikrofon, ikisi ya da yok) Ayarlar'dan da seçilir. Geri sayımı Ayarlar'dan kapatabilirsin.
- **GIF:** ses yoktur; kısa klipler içindir. En uzun kenarı 1280 piksele küçültülür, varsayılan hız 10 kare/sn.
- Çerçeve ve kontrol çubuğu kayda girmez. Fare imleci kayda girer (Ayarlar'dan kapatılabilir).

## Ayarlar

Tepsi simgesine sağ tıklayıp (macOS'ta menü çubuğundaki simgeye tıklayıp) **Ayarlar…**'ı seç.

- **Kısayol:** düğmeye tıkla ve yeni tuşlara bas. Kopyala/yapıştır gibi standart kısayollar ve sistem kısayolları seçilemez.
- **Genel:** oturum açılınca başlat, çekimde deklanşör sesi (macOS).
- **Seçim ekranı:** karartma yoğunluğu, ipucu metni, son renk ve kalınlığı hatırlama.
- **Kaydetme ve kopyalama:** kayıt klasörü (varsayılan *Resimler › Kıskaç*), biçim ve JPEG kalitesi, sormadan kaydetme, kaydettikten sonra panoya da kopyalama.
- **Video kaydı:** video klasörü (varsayılan *Videolar › Kıskaç*, macOS'ta *Filmler › Kıskaç*), ses kaynağı, sistem sesi ve mikrofon seviyesi, MP4 ve GIF kare hızı, imleci kaydetme, geri sayım.
- **Güncelleme:** sürüm bilgisi, şimdi denetle, otomatik denetim.

## Güncelleme

Kıskaç yeni sürümleri günde en fazla bir kez denetler. Yeni sürüm çıktığında sürüm notlarını gösterir; **Şimdi güncelle**'yi seçersen indirip doğrular, kurar ve kendini yeniden açar. Menüdeki **Güncellemeleri denetle…** ile elle de denetleyebilirsin; otomatik denetimi Ayarlar'dan kapatabilirsin.

Uygulamanın bulunduğu klasöre yazma iznin yoksa (ör. `Program Files`) indirme sayfası açılır.

## Sorun giderme

<details>
<summary><b>Kısayola basınca bir şey olmuyor</b></summary>

- Tepsi (ya da menü çubuğu) simgesinin göründüğünden, yani Kıskaç'ın çalıştığından emin ol.
- Başka bir ekran görüntüsü uygulaması (Lightshot, ShareX, Flameshot, masaüstünün kendi aracı) aynı tuşu kullanıyor olabilir. Ayarlar'dan başka bir kısayol seç.
- Linux'ta Ayarlar penceresi kısayolun durumunu ve gerekirse masaüstüne atayabileceğin komutu gösterir.
</details>

<details>
<summary><b>Windows'ta videoda mikrofon sesi yok</b></summary>

- Ayarlar'da **Ses (MP4)** seçeneğinin *Mikrofon* ya da *Sistem sesi + mikrofon* olduğundan emin ol.
- **Windows Ayarları › Gizlilik ve güvenlik › Mikrofon** bölümünde "Masaüstü uygulamalarının mikrofonunuza erişmesine izin verin" açık olmalı.
- Kıskaç varsayılan kayıt aygıtını kullanır; kayıt bittiğinde mikrofon açılamadıysa bildirimde yazar.
</details>

<details>
<summary><b>macOS'ta videoda mikrofon sesi yok</b></summary>

- Kayıt çubuğundaki mikrofon simgesi turuncuysa izin yok: **Sistem Ayarları › Gizlilik ve Güvenlik › Mikrofon** bölümünde Kıskaç'ı aç.
- Kıskaç varsayılan giriş aygıtını kullanır (**Sistem Ayarları › Ses › Giriş**).
</details>

<details>
<summary><b>macOS'ta ekran görüntüsü boş ya da yalnızca masaüstü görünüyor</b></summary>

Ekran kaydı izni eksik. **Sistem Ayarları › Gizlilik ve Güvenlik › Ekran ve Sistem Sesi Kaydı** bölümünde Kıskaç'ı aç ve uygulamayı yeniden başlat. Kıskaç listede varsa ama çalışmıyorsa listeden kaldırıp yeniden ekle.
</details>

<details>
<summary><b>Linux'ta tepsi simgesi görünmüyor</b></summary>

GNOME tepsi simgelerini varsayılan olarak göstermez. "AppIndicator and KStatusNotifierItem Support" eklentisini kur (Ubuntu'da hazır gelir). Simge olmasa da Kıskaç kısayolla çalışır; AppImage'ı yeniden açmak da ekran görüntüsü alır.
</details>

<details>
<summary><b>Linux'ta AppImage açılmıyor</b></summary>

Dosyanın çalıştırılabilir olduğundan emin ol: `chmod +x Kiskac-x86_64.AppImage`. Sorun sürerse terminalden çalıştırıp çıktıyı bir [hata bildirimiyle](https://github.com/ozkangozutok/kiskac/issues/new/choose) paylaş.
</details>

<details>
<summary><b>Kıskaç'ı tamamen kaldırmak</b></summary>

Ayarlar'dan "oturum açılınca başlat"ı kapat, menüden **Çıkış**'ı seç ve uygulama dosyasını sil. Ayarlar şuralarda kalır:

- Windows: `HKEY_CURRENT_USER\Software\Kiskac` (kayıt defteri)
- macOS: `~/Library/Preferences/com.kiskac.app.plist`
- Linux: `~/.config/Kiskac/Kiskac.ini`
</details>

## Kaynaktan derleme

Her platform kendi yerel teknolojisiyle yazılmıştır.

| Platform | Teknoloji | Gerekenler | Komut | Çıktı |
|---|---|---|---|---|
| Windows | C# · WinForms (.NET Framework 4.8) | Yok (Windows'la gelir) | `powershell -ExecutionPolicy Bypass -File windows\build.ps1` | `bin\Kiskac.exe` |
| macOS | Swift · AppKit · ScreenCaptureKit | Xcode Komut Satırı Araçları | `./macos/build.sh` | `bin/Kiskac.app` |
| Linux | C++17 · Qt 6 | `build-essential cmake ninja-build qt6-base-dev libxcb1-dev libxcb-keysyms1-dev` | `./linux/build.sh` | `bin/linux/kiskac` |

Proje yapısı, testler ve sürüm yayınlama için: [CONTRIBUTING.md](CONTRIBUTING.md)

## Katkı

Hata bildirimleri, öneriler ve katkılar memnuniyetle karşılanır.

- Hata ya da öneri: [Issues](https://github.com/ozkangozutok/kiskac/issues/new/choose)
- Kod katkısı: önce [CONTRIBUTING.md](CONTRIBUTING.md)'yi oku
- Güvenlik açığı: herkese açık issue yerine [SECURITY.md](SECURITY.md)'deki yolu izle

## Lisans

Kıskaç [MIT lisansı](LICENSE) ile dağıtılan özgür yazılımdır.
