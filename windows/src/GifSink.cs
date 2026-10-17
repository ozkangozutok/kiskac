using System;

namespace Kiskac
{
    // Geçici: GIF kodlayıcı sonraki aşamada eklenecek.
    sealed class GifSink : IVideoSink
    {
        public const int MaxSide = 1280;
        public GifSink(string path, int w, int h) { throw new NotSupportedException("GIF henüz yok."); }
        public int Width { get { return 0; } }
        public int Height { get { return 0; } }
        public void WriteFrame(IntPtr bgra, long time, long duration) { }
        public void Finish() { }
        public void Dispose() { }
    }
}
