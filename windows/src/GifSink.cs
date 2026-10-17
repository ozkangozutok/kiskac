using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Kiskac
{
    // Hareketli GIF yazar. GDI+'nın GIF kodlayıcısı sabit yarım ton palet kullandığı için kendi kodlayıcımız var:
    //  • her kareye kendi 256 renklik paleti (medyan kesim),
    //  • yalnızca önceki kareden değişen dikdörtgen yazılır, değişmeyen kareler öncekinin süresine eklenir,
    //  • LZW sıkıştırma (GIF89a).
    sealed class GifSink : IVideoSink
    {
        public const int MaxSide = 1280;

        readonly int width, height;
        readonly FileStream file;
        readonly int[] prev, cur;
        readonly Quantizer quantizer = new Quantizer();
        readonly LzwEncoder lzw = new LzwEncoder();
        bool hasPrev;

        // Yazılmayı bekleyen kare: süresi bir sonraki farklı kare gelince belli olur.
        Frame pending;
        long lastEnd;

        sealed class Frame
        {
            public Rect R;
            public int[] Palette;
            public int Bits;
            public byte[] Indices;
            public long Time;
        }

        struct Rect
        {
            public int X, Y, W, H;
        }

        public GifSink(string path, int width, int height)
        {
            this.width = width;
            this.height = height;
            prev = new int[width * height];
            cur = new int[width * height];
            file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);

            Write("GIF89a");
            WriteShort(width);
            WriteShort(height);
            file.WriteByte(0x70); // genel renk tablosu yok, renk çözünürlüğü 8 bit
            file.WriteByte(0);
            file.WriteByte(0);
            // Sonsuz döngü (NETSCAPE2.0)
            file.Write(new byte[] { 0x21, 0xFF, 0x0B }, 0, 3);
            Write("NETSCAPE2.0");
            file.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 }, 0, 5);
        }

        public int Width { get { return width; } }
        public int Height { get { return height; } }

        public void WriteFrame(IntPtr bgra, long time, long duration)
        {
            Marshal.Copy(bgra, cur, 0, cur.Length);
            lastEnd = time + duration;

            Rect r;
            if (!hasPrev) r = new Rect { X = 0, Y = 0, W = width, H = height };
            else if (!Changed(out r)) return; // değişiklik yok: önceki kare daha uzun görünür

            Flush(time);
            pending = Encode(r, time);
            Array.Copy(cur, prev, cur.Length);
            hasPrev = true;
        }

        public void Finish()
        {
            Flush(lastEnd);
            file.WriteByte(0x3B); // dosya sonu
            file.Flush();
        }

        public void Dispose()
        {
            file.Dispose();
        }

        // Önceki kareden farklı piksellerin sınır dikdörtgeni (alfa yok sayılır)
        bool Changed(out Rect r)
        {
            r = new Rect();
            int top = -1, bottom = -1;
            for (int y = 0; y < height && top < 0; y++)
                if (RowDiffers(y)) top = y;
            if (top < 0) return false;
            for (int y = height - 1; y >= top; y--)
                if (RowDiffers(y)) { bottom = y; break; }

            int left = width, right = -1;
            for (int y = top; y <= bottom; y++)
            {
                int row = y * width;
                for (int x = 0; x < left; x++)
                    if (((cur[row + x] ^ prev[row + x]) & 0xFFFFFF) != 0) { left = x; break; }
                for (int x = width - 1; x > right; x--)
                    if (((cur[row + x] ^ prev[row + x]) & 0xFFFFFF) != 0) { right = x; break; }
            }
            r = new Rect { X = left, Y = top, W = right - left + 1, H = bottom - top + 1 };
            return true;
        }

        bool RowDiffers(int y)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
                if (((cur[row + x] ^ prev[row + x]) & 0xFFFFFF) != 0) return true;
            return false;
        }

        Frame Encode(Rect r, long time)
        {
            var px = new int[r.W * r.H];
            for (int y = 0; y < r.H; y++)
                Array.Copy(cur, (r.Y + y) * width + r.X, px, y * r.W, r.W);
            var f = new Frame { R = r, Time = time, Indices = new byte[px.Length] };
            f.Palette = quantizer.Quantize(px, f.Indices);
            f.Bits = 1;
            while ((1 << f.Bits) < f.Palette.Length) f.Bits++;
            return f;
        }

        void Flush(long endTime)
        {
            if (pending == null) return;
            var f = pending;
            pending = null;

            // Mutlak zamanlar yuvarlanır: kısa kareler birikip toplam süreyi kaydırmasın.
            int delay = (int)(Math.Round(endTime / 100000.0) - Math.Round(f.Time / 100000.0));
            delay = Math.Max(2, Math.Min(65535, delay)); // tarayıcılar 2'nin altını 10 sayar

            // Grafik denetim uzantısı: imha yöntemi 1 (yerinde bırak), saydamlık yok
            file.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 }, 0, 4);
            WriteShort(delay);
            file.WriteByte(0);
            file.WriteByte(0);

            // Görüntü tanımlayıcı + yerel renk tablosu
            file.WriteByte(0x2C);
            WriteShort(f.R.X);
            WriteShort(f.R.Y);
            WriteShort(f.R.W);
            WriteShort(f.R.H);
            file.WriteByte((byte)(0x80 | (f.Bits - 1)));
            int size = 1 << f.Bits;
            var table = new byte[size * 3];
            for (int i = 0; i < f.Palette.Length; i++)
            {
                table[i * 3] = (byte)(f.Palette[i] >> 16);
                table[i * 3 + 1] = (byte)(f.Palette[i] >> 8);
                table[i * 3 + 2] = (byte)f.Palette[i];
            }
            file.Write(table, 0, table.Length);

            lzw.Encode(file, f.Indices, Math.Max(2, f.Bits));
        }

        void Write(string ascii)
        {
            foreach (char c in ascii) file.WriteByte((byte)c);
        }

        void WriteShort(int v)
        {
            file.WriteByte((byte)v);
            file.WriteByte((byte)(v >> 8));
        }
    }

    // Medyan kesim ile en fazla 256 renk seçer. Renkler 5-5-5 bit kutulara toplanır (32768 kutu); ekran
    // görüntülerinde genelde 256'dan az kutu dolu olur ve o zaman renkler neredeyse birebir korunur.
    sealed class Quantizer
    {
        readonly int[] count = new int[32768];
        readonly long[] sumR = new long[32768], sumG = new long[32768], sumB = new long[32768];
        readonly int[] map = new int[32768];

        public int[] Quantize(int[] px, byte[] indices)
        {
            Array.Clear(count, 0, count.Length);
            Array.Clear(sumR, 0, sumR.Length);
            Array.Clear(sumG, 0, sumG.Length);
            Array.Clear(sumB, 0, sumB.Length);

            for (int i = 0; i < px.Length; i++)
            {
                int c = px[i];
                int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                int k = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
                count[k]++;
                sumR[k] += r;
                sumG[k] += g;
                sumB[k] += b;
            }

            int used = 0;
            for (int k = 0; k < 32768; k++) if (count[k] > 0) used++;
            var bins = new int[used];
            used = 0;
            for (int k = 0; k < 32768; k++) if (count[k] > 0) bins[used++] = k;

            int[] palette;
            if (bins.Length <= 256)
            {
                palette = new int[bins.Length];
                for (int i = 0; i < bins.Length; i++)
                {
                    palette[i] = Average(bins, i, 1);
                    map[bins[i]] = i;
                }
            }
            else
                palette = MedianCut(bins, 256);

            for (int i = 0; i < px.Length; i++)
            {
                int c = px[i];
                int k = ((((c >> 16) & 255) >> 3) << 10) | ((((c >> 8) & 255) >> 3) << 5) | ((c & 255) >> 3);
                indices[i] = (byte)map[k];
            }
            return palette;
        }

        struct Box
        {
            public int Start, Length;
        }

        int[] MedianCut(int[] bins, int maxColors)
        {
            var boxes = new System.Collections.Generic.List<Box> { new Box { Start = 0, Length = bins.Length } };
            var keys = new int[bins.Length];
            while (boxes.Count < maxColors)
            {
                // Bölünecek kutu: piksel sayısı × en geniş renk aralığı en büyük olan
                int best = -1, bestAxis = 0;
                long bestScore = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    var bx = boxes[i];
                    if (bx.Length < 2) continue;
                    int axis, range;
                    LongestAxis(bins, bx, out axis, out range);
                    long pixels = 0;
                    for (int j = bx.Start; j < bx.Start + bx.Length; j++) pixels += count[bins[j]];
                    long score = pixels * range;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                        bestAxis = axis;
                    }
                }
                if (best < 0) break;

                var b = boxes[best];
                int shift = bestAxis == 0 ? 10 : bestAxis == 1 ? 5 : 0;
                for (int j = b.Start; j < b.Start + b.Length; j++) keys[j] = (bins[j] >> shift) & 31;
                Array.Sort(keys, bins, b.Start, b.Length);

                // Piksel sayısına göre medyan
                long total = 0, half = 0;
                for (int j = b.Start; j < b.Start + b.Length; j++) total += count[bins[j]];
                int split = b.Start + 1;
                for (int j = b.Start; j < b.Start + b.Length - 1; j++)
                {
                    half += count[bins[j]];
                    split = j + 1;
                    if (half * 2 >= total) break;
                }
                boxes[best] = new Box { Start = b.Start, Length = split - b.Start };
                boxes.Add(new Box { Start = split, Length = b.Start + b.Length - split });
            }

            var palette = new int[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            {
                palette[i] = Average(bins, boxes[i].Start, boxes[i].Length);
                for (int j = boxes[i].Start; j < boxes[i].Start + boxes[i].Length; j++) map[bins[j]] = i;
            }
            return palette;
        }

        static void LongestAxis(int[] bins, Box b, out int axis, out int range)
        {
            int rMin = 31, rMax = 0, gMin = 31, gMax = 0, bMin = 31, bMax = 0;
            for (int j = b.Start; j < b.Start + b.Length; j++)
            {
                int k = bins[j];
                int r = k >> 10, g = (k >> 5) & 31, bl = k & 31;
                if (r < rMin) rMin = r; if (r > rMax) rMax = r;
                if (g < gMin) gMin = g; if (g > gMax) gMax = g;
                if (bl < bMin) bMin = bl; if (bl > bMax) bMax = bl;
            }
            int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
            if (gr >= rr && gr >= br) { axis = 1; range = gr; }
            else if (rr >= br) { axis = 0; range = rr; }
            else { axis = 2; range = br; }
        }

        int Average(int[] bins, int start, int length)
        {
            long n = 0, r = 0, g = 0, b = 0;
            for (int j = start; j < start + length; j++)
            {
                int k = bins[j];
                n += count[k];
                r += sumR[k];
                g += sumG[k];
                b += sumB[k];
            }
            if (n == 0) return 0;
            return (int)((r / n) << 16 | (g / n) << 8 | (b / n));
        }
    }

    // GIF LZW kodlayıcı (değişken kod uzunluğu, en fazla 12 bit). Klasik compress algoritması;
    // sözlük her temizlemede silinmek yerine "kuşak" numarasıyla geçersiz kılınır.
    sealed class LzwEncoder
    {
        const int MaxBits = 12, MaxCode = 4096;
        readonly int[] dict = new int[1 << 20];
        readonly int[] gen = new int[1 << 20];
        int generation;

        Stream output;
        readonly byte[] block = new byte[256];
        int blockLen;
        int acc, accBits;
        int initBits, codeSize, maxCode, clearCode, eoiCode, nextCode;
        bool clearFlag;

        public void Encode(Stream output, byte[] pixels, int minCodeSize)
        {
            this.output = output;
            output.WriteByte((byte)minCodeSize);
            blockLen = 0;
            acc = 0;
            accBits = 0;

            initBits = minCodeSize + 1;
            codeSize = initBits;
            maxCode = (1 << codeSize) - 1;
            clearCode = 1 << minCodeSize;
            eoiCode = clearCode + 1;
            nextCode = clearCode + 2;
            clearFlag = false;
            generation++;

            Output(clearCode);
            int ent = pixels.Length > 0 ? pixels[0] : 0;
            for (int i = 1; i < pixels.Length; i++)
            {
                int c = pixels[i];
                int key = (c << MaxBits) + ent;
                if (gen[key] == generation)
                {
                    ent = dict[key];
                    continue;
                }
                Output(ent);
                ent = c;
                if (nextCode < MaxCode)
                {
                    dict[key] = nextCode++;
                    gen[key] = generation;
                }
                else
                {
                    // Sözlük doldu: temizle
                    generation++;
                    nextCode = clearCode + 2;
                    clearFlag = true;
                    Output(clearCode);
                }
            }
            Output(ent);
            Output(eoiCode);

            if (blockLen > 0) FlushBlock();
            output.WriteByte(0); // blok sonu
        }

        void Output(int code)
        {
            acc |= code << accBits;
            accBits += codeSize;
            while (accBits >= 8)
            {
                PutByte((byte)acc);
                acc >>= 8;
                accBits -= 8;
            }

            // Kod uzunluğu, son eklenen kod sığmayınca bir sonraki çıktıdan itibaren artar.
            if (nextCode > maxCode || clearFlag)
            {
                if (clearFlag)
                {
                    codeSize = initBits;
                    maxCode = (1 << codeSize) - 1;
                    clearFlag = false;
                }
                else
                {
                    codeSize++;
                    maxCode = codeSize == MaxBits ? MaxCode : (1 << codeSize) - 1;
                }
            }

            if (code == eoiCode)
            {
                while (accBits > 0)
                {
                    PutByte((byte)acc);
                    acc >>= 8;
                    accBits -= 8;
                }
                accBits = 0;
                acc = 0;
            }
        }

        void PutByte(byte b)
        {
            block[blockLen++] = b;
            if (blockLen == 255) FlushBlock();
        }

        void FlushBlock()
        {
            output.WriteByte((byte)blockLen);
            output.Write(block, 0, blockLen);
            blockLen = 0;
        }
    }
}
