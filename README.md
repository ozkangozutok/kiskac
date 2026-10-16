<p align="center">
  <img src="logo.png" width="128" alt="Kıskaç logosu">
</p>

<h1 align="center">Kıskaç</h1>

<p align="center">
  Windows, macOS ve Linux için hafif, hızlı ekran görüntüsü aracı. <b>PrtSc</b> (macOS'ta <b>⇧⌘2</b>) bas, alanı seç, üstüne çiz, kopyala.
</p>

---

Kıskaç, Lightshot benzeri bir ekran görüntüsü aracıdır. Windows'ta tek bir küçük `.exe` dosyasıdır, kurulum gerektirmez ve sistem tepsisinde sessizce çalışır. macOS'ta yerel (Swift + AppKit) bir menü çubuğu uygulamasıdır. Linux'ta (Qt 6) tek dosyalık bir AppImage olarak X11 ve Wayland'de çalışır.

## Özellikler

- **PrtSc ile anında çekim:** ekran kararır, alanı sürükleyerek seçersin. Tek tıklarsan o monitörün tamamı seçilir.
- **Çoklu monitör ve yüksek DPI desteği:** farklı ölçeklerdeki monitörlerde net çekim yapar.
- **Seçimi düzenleme:** seçimi taşıyabilir, 8 tutamaçtan boyutlandırabilirsin. Boyut piksel olarak görünür.
- **Çizim araçları:** kalem, çizgi, ok, dikdörtgen, fosforlu kalem ve metin.
  - Shift'e basılıyken çizgi ve ok 45° adımlarla, dikdörtgen kare çizilir.
- **6 renk:** kalınlığı fare tekerleğiyle ayarlarsın.
- **Kopyala veya kaydet:** panoya kopyalayabilir ya da PNG, JPG veya BMP olarak kaydedebilirsin.
- **Sistem tepsisi ve otomatik başlatma:** Windows açılınca kendiliğinden başlar (tepsi menüsünden kapatılabilir).
- **Diğer kısayollar korunur:** Alt+PrtSc ve Win+PrtSc Windows'ta eskisi gibi çalışır.
- **Ayarlar:** kısayol, karartma, kayıt biçimi ve klasörü, sormadan kaydetme ve daha fazlası.
- **Otomatik güncelleme:** yeni sürümler GitHub Releases'tan indirilir, doğrulanır ve kurulur.

## Kısayollar (Windows)

| Tuş | İşlev |
|---|---|
| `PrtSc` | Ekran görüntüsü al (Ayarlar'dan değiştirilebilir) |
| `Ctrl+C` / `Enter` / çift tık | Panoya kopyala ve kapat |
| `Ctrl+S` | Dosyaya kaydet |
| `Ctrl+Z` | Geri al |
| `Esc` / sağ tık | İptal |
| `S` `P` `L` `A` `R` `M` `T` | Seç, Kalem, Çizgi, Ok, Dikdörtgen, Fosforlu kalem, Metin |
| Fare tekerleği | Kalem kalınlığı |

## Derleme (Windows)

Ek bir şey kurman gerekmez. Windows 10 ve 11 ile gelen .NET Framework 4.8 C# derleyicisi yeterlidir:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Çıktı `bin\Kiskac.exe` olarak oluşur. Uygulama simgesi derleme sırasında koddan ([src/Logo.cs](src/Logo.cs)) üretilir. Sürüm numarası kökteki [VERSION](VERSION) dosyasından alınır (macOS ile ortak).

GitHub'da her push'ta Windows derlemesi [GitHub Actions](.github/workflows/windows.yml) ile denetlenir. Bir sürüm yayınlanınca `Kiskac.exe` orada derlenip sürüme eklenir.

### Ayarlar (Windows)

Tepsi simgesine sağ tıklayıp **Ayarlar…** ile açılır:

| Bölüm | Ayar |
|---|---|
| Kısayol | Ekran görüntüsü kısayolunu kaydet: düğmeye tıkla, yeni tuşlara bas (PrtSc de kaydedilebilir) |
| Genel | Windows açılınca başlat |
| Seçim ekranı | Karartma yoğunluğu, ipucu metni, son renk ve kalınlığı hatırlama |
| Kaydetme ve kopyalama | Kayıt klasörü, varsayılan biçim (PNG/JPEG/BMP) ve JPEG kalitesi, `Ctrl+S` ile sormadan kaydetme, kaydettikten sonra panoya da kopyalama |
| Güncelleme | Sürüm, şimdi denetle, otomatik denetim |

Kısayolda Ctrl olmalı (PrtSc, F tuşları, Pause ve Scroll Lock tek başına da olur). Win tuşlu kısayollar, Ctrl+C/V gibi standart kısayollar ve AltGr ile karakter yazan birleşimler (Türkçe klavyede Ctrl+Alt+Q = @ gibi) seçilemez.

### Güncelleme (Windows)

Kıskaç yeni sürümleri GitHub Releases'tan kendisi denetler (açılıştan kısa süre sonra, günde en fazla bir kez). Tepsi menüsünden **Güncellemeleri denetle…** ile elle de denetlenebilir. Güncellemede `Kiskac.exe` exe'nin yanına indirilir, SHA256 özeti ve sürümü doğrulanır, çalışan exe yeniden adlandırılıp yenisi yerine konur ve Kıskaç yeniden açılır. Exe'nin bulunduğu klasöre yazma iznin yoksa indirme sayfası açılır.

## macOS

macOS sürümü ayrı ve tamamen yerel bir uygulamadır ([macos/](macos/)). Apple Silicon ve Intel için evrensel derlenir. macOS 14 (Sonoma) veya üstü gerekir.

- **Menü çubuğunda çalışır:** Dock'ta görünmez. Simge açık ve koyu menü çubuğuna kendiliğinden uyar.
- **ScreenCaptureKit ile çekim:** tüm ekranlar aynı anda, her biri kendi Retina çözünürlüğünde yakalanır. Kaydedilen ve kopyalanan görüntü tam piksel çözünürlüğündedir; boyut etiketi de piksel gösterir.
- **Akıcı seçim:** ekran görüntüsü bir kez GPU katmanına verilir. Fare hareketinde yalnızca karartmadaki delik ve değişen bölge yeniden çizilir.
- **Çoklu ekran ve tam ekran uygulamalar:** her ekran kendi seçim penceresini alır, tam ekran uygulamaların üstünde de açılır.
- **İz dörtgeni:** kalınlık iki parmak kaydırmayla ayarlanır.
- **Kapatınca önceki uygulamaya döner:** kopyaladıktan hemen sonra ⌘V ile yapıştırabilirsin.

### Kısayollar (macOS)

| Tuş | İşlev |
|---|---|
| `⇧⌘2` veya `F13` | Ekran görüntüsü al (Ayarlar'dan değiştirilebilir; PC klavyelerindeki PrtSc tuşu macOS'ta F13 olarak gelir) |
| `⌘C` / `↩` / çift tık | Panoya kopyala ve kapat |
| `⌘S` | Dosyaya kaydet (PNG veya JPEG) |
| `⌘Z` | Geri al |
| `Esc` / sağ tık | İptal |
| `S` `P` `L` `A` `R` `M` `T` | Seç, Kalem, Çizgi, Ok, Dikdörtgen, Fosforlu kalem, Metin |
| `⇧↩` | Metinde yeni satır |

Kıskaç zaten çalışırken uygulamayı yeniden açmak (Spotlight, Finder) da ekran görüntüsü alır.

### Ayarlar (macOS)

Menü çubuğundaki Kıskaç simgesine tıklayıp **Ayarlar…** seçeneğiyle açılır:

| Bölüm | Ayar |
|---|---|
| Kısayol | Ekran görüntüsü kısayolunu kaydet: düğmeye tıkla, yeni tuşlara bas. PrtSc (F13) tuşunu aç/kapat |
| Genel | Oturum açılınca başlat, çekimde deklanşör sesi |
| Seçim ekranı | Karartma yoğunluğu, ipucu metni, son renk ve kalınlığı hatırlama |
| Kaydetme ve kopyalama | Kayıt klasörü, varsayılan biçim (PNG/JPEG) ve JPEG kalitesi, `⌘S` ile sormadan kaydetme, kaydettikten sonra panoya da kopyalama, Retina görüntüyü 1x boyutuna küçültme |

Kısayolda ⌘ veya ⌃ tuşlarından en az biri olmalı (F tuşları tek başına da olur). Etkin macOS sistem kısayolları (⇧⌘3/4/5, Spotlight vb.) ve ⌘C, ⌘V, ⌘Q gibi standart uygulama kısayolları seçilemez.

### Derleme (macOS)

Yalnızca Xcode Komut Satırı Araçları gerekir (`xcode-select --install`). Xcode projesi yoktur:

```sh
./macos/build.sh
```

Çıktı `bin/Kiskac.app` olarak oluşur. **İlk açmadan önce Uygulamalar klasörüne taşı:** ilk açılışta oturum açılınca başlatma, uygulamanın o anki konumuna kaydedilir. Uygulama simgesi derleme sırasında koddan ([macos/Sources/Logo.swift](macos/Sources/Logo.swift)) üretilir.

### Güncelleme (macOS)

Kıskaç yeni sürümleri GitHub Releases'tan kendisi denetler: açılıştan kısa süre sonra ve günde en fazla bir kez. Menüden **Güncellemeleri denetle…** ile elle de denetlenebilir, otomatik denetim Ayarlar'dan kapatılabilir.

Yeni sürüm bulununca sürüm notları gösterilir. **Şimdi güncelle** seçilirse `Kiskac-macOS.zip` indirilir, ardından:

- SHA256 özeti GitHub'daki değerle karşılaştırılır,
- paket kimliği, sürümü ve Developer ID imzası (aynı ekip) doğrulanır,
- Kıskaç kapanınca eski uygulama yenisiyle değiştirilip yeniden açılır.

Uygulamanın bulunduğu klasöre yazma iznin yoksa indirme sayfası açılır.

#### Sürüm yayınlama

1. Kökteki `VERSION` dosyasını artır ve commit et.
2. İsteğe bağlı: notarize için notarytool profilini bir kez oluştur. Profil yoksa notarize atlanır; o zaman ilk elle kurulumda Gatekeeper uyarısı çıkar, uygulama içi güncellemeler etkilenmez. Komut uygulamaya özel parolayı sorar ([appleid.apple.com](https://appleid.apple.com) › Oturum Açma ve Güvenlik):
   ```sh
   xcrun notarytool store-credentials kiskac-notary --apple-id <apple-id> --team-id XZPCTAZZ9W
   ```
3. Yayınla:
   ```sh
   ./macos/release.sh [sürüm-notları.md]
   ```
   Betik Developer ID ile imzalar, profil varsa notarize edip zımbalar (staple), `Kiskac-macOS.zip` dosyasını üretir ve `vX.Y.Z` GitHub sürümünü oluşturur. `Kiskac.exe` birkaç dakika içinde GitHub Actions tarafından sürüme eklenir.

### Ekran kaydı izni

İlk çekimde macOS, **Sistem Ayarları › Gizlilik ve Güvenlik › Ekran ve Sistem Sesi Kaydı** iznini ister. İzni verdikten sonra Kıskaç'ı yeniden başlatman gerekebilir.

Yayınlanan sürümler Developer ID ile imzalı olduğundan izin güncellemelerde korunur. `build.sh` ise yerelde varsayılan olarak ad-hoc imzalar: yeniden derlediğinde imza değişir, macOS da izni yeniden isteyebilir. Bu durumda listeden Kıskaç'ı kaldırıp yeniden ekle ya da sabit bir imzayla derle: `SIGN_IDENTITY="Apple Development: ..." ./macos/build.sh`.

## Linux

Linux sürümü C++ ve Qt 6 ile yazılmış yerel bir uygulamadır ([linux/](linux/)). Tek dosyalık **Kiskac-x86_64.AppImage** olarak dağıtılır; kurulum gerektirmez, X11 ve Wayland'de çalışır.

```sh
chmod +x Kiskac-x86_64.AppImage
./Kiskac-x86_64.AppImage
```

- **Tepsi simgesi ve tek örnek:** Kıskaç arka planda çalışır. Yeniden açmak ya da `Kiskac-x86_64.AppImage --capture` ekran görüntüsü alır.
- **Çekim:** X11'de her ekran doğrudan yakalanır. Wayland'de uygulamalar ekranı doğrudan okuyamaz; `xdg-desktop-portal` ekran görüntüsü arayüzü kullanılır (masaüstü ilk seferde izin isteyebilir).
- **Akıcı seçim:** ekran görüntüsü ve karartılmış hali bir kez hazırlanır; fare hareketinde yalnızca değişen bölge yeniden çizilir. Yüksek DPI ve kesirli ölçeklemede tam piksel çözünürlüğünde kaydeder.
- **Ayarlar ve otomatik güncelleme:** Windows ve macOS ile aynı seçenekler. AppImage kendini GitHub Releases'tan günceller (SHA256 ve sürüm doğrulamasıyla).

### Kısayol (Linux)

| Ortam | Nasıl çalışır |
|---|---|
| X11 (GNOME, KDE, Xfce, Cinnamon, MATE…) | Kıskaç kısayolu kendisi yakalar. PrtSc masaüstünün kendi ekran görüntüsü aracına atanmışsa Ayarlar'da uyarı çıkar: ya orada kaldır ya da başka bir kısayol seç. |
| Wayland + genel kısayol portalı (KDE Plasma, GNOME 48+, Hyprland) | Kısayol masaüstüne kaydedilir; masaüstü ilk seferde onay ister. |
| Diğer Wayland ortamları | Masaüstü ayarlarında (Klavye › Özel kısayollar) `Kiskac-x86_64.AppImage --capture` komutuna bir kısayol ata. Ayarlar penceresi tam komutu gösterir. |

GNOME'da tepsi simgesi için *AppIndicator* desteği gerekir (Ubuntu'da hazır gelir; diğerlerinde "AppIndicator and KStatusNotifierItem Support" eklentisi).

### Derleme (Linux)

```sh
sudo apt install build-essential cmake ninja-build qt6-base-dev libxcb1-dev libxcb-keysyms1-dev
./linux/build.sh
```

Çıktı `bin/linux/kiskac` olarak oluşur. AppImage, [GitHub Actions](.github/workflows/linux.yml) ile Ubuntu 22.04 üzerinde derlenir ve her push'ta Xvfb altında iç denetimlerden geçer; bir sürüm yayınlanınca o sürüme eklenir.

## Proje yapısı

| Dosya | Görevi |
|---|---|
| `src/Program.cs` | Giriş noktası, tek örnek kontrolü |
| `src/TrayContext.cs` | Tepsi simgesi, menü, çekimi başlatma |
| `src/KeyboardHook.cs` | Sistem genelinde kısayol yakalama ve kısayol kaydı (ayrı iş parçacığında) |
| `src/OverlayForm.cs` | Seçim ekranı, araç çubuğu, çizim ve kaydetme |
| `src/Shapes.cs` | Kalem, çizgi, ok, dikdörtgen, metin şekilleri |
| `src/Logo.cs` | Kodla çizilen logo |
| `src/HotKey.cs` | Kısayol modeli, doğrulama ve tuş adları |
| `src/SettingsForm.cs` | Ayarlar penceresi |
| `src/Updater.cs` | GitHub Releases ile otomatik güncelleme |
| `macos/Sources/AppDelegate.swift` | macOS: menü çubuğu, kısayol, izinler |
| `macos/Sources/SettingsWindow.swift` | macOS: ayarlar penceresi ve kısayol kaydı |
| `macos/Sources/HotKeys.swift` | macOS: sistem geneli kısayol ve tuş adları |
| `macos/Sources/Updater.swift` | macOS: GitHub Releases ile otomatik güncelleme |
| `macos/release.sh` | macOS: imzalama, notarize ve GitHub sürümü yayınlama |
| `linux/src/App.cpp` | Linux: tepsi, tek örnek, çekim akışı |
| `linux/src/Capture.cpp` | Linux: X11 ve Wayland (portal) ekran yakalama |
| `linux/src/GlobalHotKey.cpp` | Linux: X11 ve Wayland (portal) genel kısayol |
| `linux/src/Overlay.cpp` | Linux: seçim ekranı, araç çubuğu, kopyalama ve kaydetme |
| `linux/src/Updater.cpp` | Linux: AppImage otomatik güncelleme |
| `macos/Sources/Capture.swift` | macOS: ScreenCaptureKit ile çoklu ekran çekimi |
| `macos/Sources/Overlay.swift` | macOS: seçim ekranı, araç çubuğu, kopyalama ve kaydetme |
| `macos/Sources/Shapes.swift` | macOS: çizim şekilleri |
| `macos/Sources/Logo.swift` | macOS: logo, uygulama ve menü çubuğu simgesi |

## Lisans

[MIT](LICENSE)
