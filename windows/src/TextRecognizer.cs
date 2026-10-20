using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace Kiskac
{
    // Görüntüdeki yazıyı Windows'un yerleşik OCR motoruyla (Windows.Media.Ocr, Windows 10+) okur.
    // Ek indirme yoktur; tanınan diller Windows'ta yüklü dil paketlerine bağlıdır.
    static class TextRecognizer
    {
        // Bu bilgisayarda en az bir OCR dili var mı
        public static bool Available
        {
            get
            {
                try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; }
                catch { return false; }
            }
        }

        // Kullanılacak dil (ör. "Türkçe"); yoksa null
        public static string LanguageName
        {
            get
            {
                try
                {
                    var e = CreateEngine();
                    return e == null ? null : e.RecognizerLanguage.DisplayName;
                }
                catch { return null; }
            }
        }

        static OcrEngine CreateEngine()
        {
            // Önce kullanıcının Windows dilleri, yoksa yüklü ilk OCR dili
            var e = OcrEngine.TryCreateFromUserProfileLanguages();
            if (e != null) return e;
            var langs = OcrEngine.AvailableRecognizerLanguages;
            return langs.Count > 0 ? OcrEngine.TryCreateFromLanguage(langs[0]) : null;
        }

        // Satırları alt alta döndürür; yazı yoksa boş dize. Uzun sürebilir: arayüz iş parçacığında çağırma.
        public static string Recognize(Bitmap image)
        {
            var engine = CreateEngine();
            if (engine == null)
                throw new NotSupportedException("Bu bilgisayarda metin tanıma dili yüklü değil. Windows Ayarları › Zaman ve dil › Dil bölümünden bir dil ekleyin.");

            // Küçük yazılar büyütülünce çok daha iyi tanınır. Motorun sınırı (MaxImageDimension) aşılmaz.
            int max = (int)OcrEngine.MaxImageDimension;
            int scale = image.Height < 150 ? 3 : image.Height < 800 ? 2 : 1;
            while (scale > 1 && (image.Width * scale > max || image.Height * scale > max)) scale--;
            double fit = Math.Min(1.0, Math.Min((double)max / image.Width, (double)max / image.Height));
            int w = Math.Max(1, (int)(image.Width * scale * fit)), h = Math.Max(1, (int)(image.Height * scale * fit));

            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(image, new Rectangle(0, 0, w, h));
                }
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var bytes = new byte[w * h * 4];
                try
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * w * 4, w * 4);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                // Ekran görüntüsünde saydamlık yok: tüm pikseller opak olsun
                for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;

                // .NET'in WinRT yardımcıları (AsBuffer, AsTask) Windows SDK'daki birleşik Windows.winmd'yi ister;
                // onun yerine Windows'un kendi tampon sınıfı ve işlemin durumunu bekleme kullanılır.
                var buffer = CryptographicBuffer.CreateFromByteArray(bytes);
                using (var sb = SoftwareBitmap.CreateCopyFromBuffer(buffer, BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied))
                {
                    var op = engine.RecognizeAsync(sb);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (op.Status == AsyncStatus.Started)
                    {
                        if (sw.ElapsedMilliseconds > 30000)
                        {
                            op.Cancel();
                            throw new TimeoutException("Metin tanıma zaman aşımına uğradı.");
                        }
                        System.Threading.Thread.Sleep(10);
                    }
                    if (op.Status == AsyncStatus.Error) throw new Exception("Metin tanınamadı.", op.ErrorCode);
                    var result = op.GetResults();
                    var text = new StringBuilder();
                    foreach (var line in result.Lines)
                        text.AppendLine(line.Text);
                    return text.ToString().TrimEnd();
                }
            }
        }
    }
}
