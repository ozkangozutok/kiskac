# Güvenlik

## Açık bildirme

Bir güvenlik açığı bulduysan lütfen **herkese açık issue açma**. Bunun yerine GitHub'ın özel bildirim özelliğini kullan:

**[Security › Report a vulnerability](https://github.com/ozkangozutok/kiskac/security/advisories/new)**

Açığı, etkilenen sürüm ve platformu, yeniden üretme adımlarını yaz. Bildirimlere en kısa sürede dönülür; düzeltme yayınlandıktan sonra açık birlikte duyurulur.

## Desteklenen sürümler

Yalnızca [en son sürüm](https://github.com/ozkangozutok/kiskac/releases/latest) güvenlik düzeltmesi alır. Kıskaç kendini otomatik güncellediği için güncel kalmak kolaydır.

## Kıskaç neye erişir

- **Ekran:** yalnızca kısayola bastığında, seçim ekranı için. Görüntü bellekte tutulur; sen kaydetmedikçe diske yazılmaz (Wayland'de portalın verdiği geçici dosya okunur okunmaz silinir).
- **Ağ:** yalnızca güncelleme denetimi için `api.github.com` ve `github.com`. Ekran görüntüleri hiçbir yere gönderilmez; telemetri yoktur.
- **Klavye:** yalnızca seçtiğin kısayol. Windows'ta kısayol düşük seviyeli bir klavye kancasıyla yakalanır; kanca yalnızca kısayolu karşılaştırır, tuşları kaydetmez.

## Güncellemelerin doğrulanması

Güncelleyici yalnızca bu deponun GitHub sürümlerinden dosya indirir ve kurmadan önce:

- GitHub'ın verdiği **SHA256** özetini,
- dosyanın **sürüm numarasını** (etiketle aynı olmalı),
- macOS'ta ayrıca **Developer ID imzasını ve ekip kimliğini**

denetler. Windows exe'si ve Linux AppImage'ı kod imzalı değildir; bu denetimler indirmenin bozulmadığını garanti eder, ancak GitHub hesabının ele geçirilmesine karşı koruma sağlamaz.
