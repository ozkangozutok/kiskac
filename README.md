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

Kıskaç zaten çalışırken uygulamayı yeniden açmak da ekran görüntüsü alır.

## Ayarlar

Tepsi simgesine sağ tıklayıp (macOS'ta menü çubuğundaki simgeye tıklayıp) **Ayarlar…**'ı seç.

- **Kısayol:** düğmeye tıkla ve yeni tuşlara bas. Kopyala/yapıştır gibi standart kısayollar ve sistem kısayolları seçilemez.
- **Genel:** oturum açılınca başlat, çekimde deklanşör sesi (macOS).
- **Seçim ekranı:** karartma yoğunluğu, ipucu metni, son renk ve kalınlığı hatırlama.
- **Kaydetme ve kopyalama:** kayıt klasörü (varsayılan *Resimler › Kıskaç*), biçim ve JPEG kalitesi, sormadan kaydetme, kaydettikten sonra panoya da kopyalama.
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
