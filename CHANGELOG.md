# Değişiklikler

Bu dosya kullanıcıyı etkileyen değişiklikleri listeler. Sürümler [anlamsal sürümlemeye](https://semver.org/lang/tr/) uyar.

## Yayınlanmadı

- **Windows: ekran videosu ve GIF kaydı.** Seçim ekranındaki ● (MP4) ya da GIF düğmesiyle seçili alan kaydedilir. MP4'e sistem sesi ve/veya mikrofon eklenebilir (H.264 + AAC, Windows'un kendi kodlayıcılarıyla; ek indirme yok). Kayıt sırasında çerçeve ve süre/duraklat/durdur çubuğu görünür; kısayola yeniden basmak kaydı durdurur. Yeni ayarlar: video klasörü, ses kaynağı, MP4 ve GIF kare hızı, imleci kaydetme.
- **Windows: kayıtta ses seçimi.** Kayıt 3 saniye geri sayarak başlar; bu sırada ve kayıt boyunca kontrol çubuğundaki düğmelerle sistem sesi ve mikrofon açılıp kapatılabilir; simgenin üzerinde fare tekerleği o kaynağın seviyesini (%0–%200) değiştirir ve canlı seviye göstergesi vardır (seçimler hatırlanır, Ayarlar'dan da değiştirilebilir). Geri sayım iptal edilebilir, beklemeden başlatılabilir ya da Ayarlar'dan kapatılabilir.
- **Windows: tam ekran video.** Shift+PrtSc (kısayolun Shift'li hâli) imlecin bulunduğu ekranı seçim yapmadan kaydetmeye başlar ve durdurur. Tepsi menüsündeki "Tam ekran video" ile belirli bir ekran ya da tüm ekranlar kaydedilebilir.
- Windows kaynak kodu `windows/` klasörüne taşındı; README kullanıcı odaklı yeniden yazıldı, katkı rehberi eklendi.

## 1.4.0

- **Linux sürümü** (Qt 6): tek dosyalık AppImage, X11 ve Wayland desteği, ayarlar ve otomatik güncelleme.
- macOS ve Windows: değişiklik yok.

## 1.3.0

- **Windows:** ayarlar penceresi (değiştirilebilir kısayol, karartma, kayıt biçimi ve klasörü, sormadan kaydetme…) ve otomatik güncelleme.
- **macOS:** bir uyarı penceresi açıkken kısayola basıldığında seçim ekranının kilitlenmesi düzeltildi.

## 1.2.0

- **macOS:** ayarlar penceresi, değiştirilebilir kısayol ve otomatik güncelleme. Uygulama Developer ID ile imzalı; ekran kaydı izni güncellemelerde korunur.

## 1.1.0

- **macOS sürümü:** menü çubuğu uygulaması, ScreenCaptureKit ile Retina çekim, çoklu ekran desteği.

## 1.0.0

- İlk sürüm: Windows için PrtSc ile ekran görüntüsü, seçim, çizim araçları, kopyalama ve kaydetme.
