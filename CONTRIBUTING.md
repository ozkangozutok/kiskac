# Kıskaç'a katkı

Katkın için teşekkürler. Bu belge projenin yapısını, derleme ve test yollarını ve sürüm yayınlama sürecini anlatır. Kullanıcı belgeleri [README.md](README.md)'dedir.

## Başlamadan önce

- Hata bildirmek ya da özellik önermek için bir [issue](https://github.com/ozkangozutok/kiskac/issues/new/choose) aç. Büyük değişikliklerde önce issue'da konuşmak boşa emek harcanmasını önler.
- Güvenlik açıklarını herkese açık issue ile değil, [SECURITY.md](SECURITY.md)'deki yolla bildir.
- Katkıların [MIT lisansı](LICENSE) altında yayımlanır.

## Proje yapısı

Kıskaç üç ayrı, yerel uygulamadan oluşur. Ortak bir çapraz platform katmanı yoktur; her platform kendi en iyi API'lerini kullanır. Davranış ve arayüz üç platformda aynı tutulur.

```
VERSION              Ortak sürüm numarası (üç platform da buradan okur)
logo.png             README logosu (uygulama simgeleri koddan üretilir)
windows/             C# · WinForms · .NET Framework 4.8
  build.ps1          Derleme betiği (Windows'la gelen csc.exe)
  app.manifest       DPI farkındalığı (PerMonitorV2)
  src/
macos/               Swift · AppKit · ScreenCaptureKit (Xcode projesi yok, swiftc)
  build.sh           Evrensel (arm64 + x86_64) derleme ve imzalama
  release.sh         İmzala, notarize et, GitHub sürümünü oluştur
  Info.plist
  Sources/
linux/               C++17 · Qt 6 · xcb · D-Bus portalları
  CMakeLists.txt
  build.sh
  kiskac.desktop
  src/
.github/workflows/   windows.yml ve linux.yml: derleme, iç denetimler, sürüme dosya ekleme
```

Her platformda aynı bileşenler vardır:

| Bileşen | Windows | macOS | Linux |
|---|---|---|---|
| Giriş, tek örnek | `Program.cs` | `main.swift` | `main.cpp` |
| Tepsi / menü çubuğu, çekim akışı | `TrayContext.cs` | `AppDelegate.swift` | `App.cpp` |
| Ekran yakalama | `Util.cs` (GDI) | `Capture.swift` (ScreenCaptureKit) | `Capture.cpp` (X11 / xdg-desktop-portal) |
| Genel kısayol | `KeyboardHook.cs` (WH_KEYBOARD_LL) | `HotKeys.swift` (Carbon) | `GlobalHotKey.cpp` (XGrabKey / GlobalShortcuts portalı) |
| Kısayol modeli ve doğrulama | `HotKey.cs` | `HotKeys.swift` | `Settings.cpp` |
| Seçim ekranı ve araç çubuğu | `OverlayForm.cs` | `Overlay.swift` | `Overlay.cpp` |
| Çizim şekilleri | `Shapes.cs` | `Shapes.swift` | `Shapes.cpp` |
| Ayarlar | `Util.cs`, `SettingsForm.cs` | `Settings.swift`, `SettingsWindow.swift` | `Settings.cpp`, `SettingsDialog.cpp` |
| Güncelleme | `Updater.cs` | `Updater.swift` | `Updater.cpp` |
| Logo ve simgeler | `Logo.cs` | `Logo.swift` | `Logo.cpp` |
| İç denetimler | `SelfTest.cs` | — | `SelfTest.cpp` |

### Tasarım ilkeleri

- **Ek bağımlılık yok.** Windows sürümü yalnızca .NET Framework 4.8'le, macOS sürümü yalnızca sistem çerçeveleriyle, Linux sürümü yalnızca Qt 6 ve xcb ile derlenir.
- **Seçim ekranı akıcı olmalı.** Ekran görüntüsü ve karartılmış hali bir kez hazırlanır; fare hareketinde yalnızca değişen bölge yeniden çizilir. 4K/5K ekranda tüm ekranı her karede yeniden çizen değişiklikler kabul edilmez.
- **Tam piksel çıktı.** Seçim mantıksal koordinatlarda tutulur, çıktı her zaman cihaz piksellerinde (Retina, kesirli ölçekleme) üretilir.
- **Kod yorumları ve arayüz metinleri Türkçedir.** Değişkenler ve API adları İngilizce kalır.
- **Logo koddan çizilir.** Üç platformda aynı geometri kullanılır (`Logo.*`); simge dosyaları derleme sırasında üretilir.

## Derleme

### Windows

```powershell
powershell -ExecutionPolicy Bypass -File windows\build.ps1
```

`csc.exe` (C# 5) kullanılır: C# 6 ve sonrası söz dizimi (`?.`, `$""`, `=>` gövdeli üyeler, `nameof`) **derlenmez**.

### macOS

```sh
xcode-select --install   # bir kez
./macos/build.sh
```

Yerel derleme ad-hoc imzalanır; her derlemede imza değiştiği için macOS ekran kaydı iznini yeniden isteyebilir. Sabit bir imza için: `SIGN_IDENTITY="Apple Development: …" ./macos/build.sh`.

### Linux

```sh
sudo apt install build-essential cmake ninja-build pkg-config qt6-base-dev libxcb1-dev libxcb-keysyms1-dev
./linux/build.sh
```

Fedora: `sudo dnf install gcc-c++ cmake ninja-build qt6-qtbase-devel libxcb-devel xcb-util-keysyms-devel`
Arch: `sudo pacman -S base-devel cmake ninja qt6-base libxcb xcb-util-keysyms`

## Test

Windows ve Linux sürümlerinde gizli bir `--selftest` modu vardır; CI her push'ta bunu çalıştırır:

```powershell
# Windows: sonuçlar dosyaya yazılır, başarısızlıkta çıkış kodu 1
$p = Start-Process bin\Kiskac.exe -ArgumentList '--selftest', "$PWD\selftest.log" -Wait -PassThru
Get-Content selftest.log; $p.ExitCode
```

```sh
# Linux: X11 olmadan Xvfb ile
xvfb-run -a -s "-screen 0 1280x800x24" dbus-run-session -- bin/linux/kiskac --selftest
```

Denetimler kısayol doğrulamasını, güncelleme yanıtının ayrıştırılmasını ve doğrulamasını, Linux'ta ayrıca seçim → çizim → kırpma zincirini ve X11 yakalama/kısayolu kapsar. Davranış değiştiren bir katkıda ilgili denetimi eklemen beklenir.

Otomatik denetimlerin kapsamadığı yerleri (gerçek masaüstünde kısayol, Wayland portalları, çoklu ekran, ayarlar penceresinin görünümü) PR açıklamasında nasıl denediğini yazarak belirt.

## Pull request

1. Depoyu çatalla (fork) ve `main`'den bir dal aç.
2. Değişikliği yap; mümkünse üç platformda aynı davranışı koru. Yalnızca bir platformu değiştiriyorsan PR'da belirt.
3. Derlemenin ve iç denetimlerin geçtiğinden emin ol (CI PR'da otomatik çalışır).
4. Kullanıcıyı etkileyen bir değişiklikse [CHANGELOG.md](CHANGELOG.md)'deki "Yayınlanmadı" bölümüne ekle.
5. Commit mesajlarını Türkçe, kısa ve ne yapıldığını anlatan biçimde yaz (ör. `Linux: Wayland'de pano kaybını düzelt`).

## Sürüm yayınlama (bakımcılar için)

1. `VERSION` dosyasını artır, `CHANGELOG.md`'yi güncelle ve commit et.
2. macOS makinesinde yayınla:
   ```sh
   ./macos/release.sh [sürüm-notları.md]
   ```
   Betik macOS uygulamasını Developer ID ile imzalar, `notarytool` profili (`kiskac-notary`) varsa notarize eder, `Kiskac-macOS.zip`'i üretir ve `vX.Y.Z` GitHub sürümünü oluşturur.
3. Sürüm yayınlanınca GitHub Actions `Kiskac.exe` ve `Kiskac-x86_64.AppImage` dosyalarını derler, iç denetimlerden geçirir ve sürüme ekler. Etiket ile `VERSION` uyuşmazsa iş başarısız olur.

Uygulama içi güncelleyiciler `releases/latest` yanıtındaki dosyayı indirir, GitHub'ın verdiği SHA256 özetiyle ve sürüm numarasıyla doğrular. macOS'ta ayrıca Developer ID ekip kimliği denetlenir. Dosya adlarını (`Kiskac.exe`, `Kiskac-macOS.zip`, `Kiskac-x86_64.AppImage`) değiştirmek eski sürümlerin güncellenmesini bozar.
