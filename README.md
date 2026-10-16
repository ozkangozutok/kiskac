<p align="center">
  <img src="logo.png" width="128" alt="Kıskaç logosu">
</p>

<h1 align="center">Kıskaç</h1>

<p align="center">
  Windows için hafif, hızlı ekran görüntüsü aracı. <b>PrtSc</b>'ye bas, alanı seç, üstüne çiz, kopyala.
</p>

---

Kıskaç, Lightshot benzeri bir ekran görüntüsü aracıdır. Tek bir küçük `.exe` dosyasıdır, kurulum gerektirmez ve sistem tepsisinde sessizce çalışır.

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

## Kısayollar

| Tuş | İşlev |
|---|---|
| `PrtSc` | Ekran görüntüsü al |
| `Ctrl+C` / `Enter` / çift tık | Panoya kopyala ve kapat |
| `Ctrl+S` | Dosyaya kaydet |
| `Ctrl+Z` | Geri al |
| `Esc` / sağ tık | İptal |
| `S` `P` `L` `A` `R` `M` `T` | Seç, Kalem, Çizgi, Ok, Dikdörtgen, Fosforlu kalem, Metin |
| Fare tekerleği | Kalem kalınlığı |

## Derleme

Ek bir şey kurman gerekmez. Windows 10 ve 11 ile gelen .NET Framework 4.8 C# derleyicisi yeterlidir:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Çıktı `bin\Kiskac.exe` olarak oluşur. Uygulama simgesi derleme sırasında koddan ([src/Logo.cs](src/Logo.cs)) üretilir.

## Proje yapısı

| Dosya | Görevi |
|---|---|
| `src/Program.cs` | Giriş noktası, tek örnek kontrolü |
| `src/TrayContext.cs` | Tepsi simgesi, menü, çekimi başlatma |
| `src/KeyboardHook.cs` | Sistem genelinde PrtSc yakalama (ayrı iş parçacığında) |
| `src/OverlayForm.cs` | Seçim ekranı, araç çubuğu, çizim ve kaydetme |
| `src/Shapes.cs` | Kalem, çizgi, ok, dikdörtgen, metin şekilleri |
| `src/Logo.cs` | Kodla çizilen logo |

## Lisans

[MIT](LICENSE)
