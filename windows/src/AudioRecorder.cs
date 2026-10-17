using System;

namespace Kiskac
{
    // Geçici: ses kaydı sonraki aşamada (WASAPI) eklenecek.
    sealed class AudioRecorder : IDisposable
    {
        public AudioRecorder(AudioMode mode, RecordingClock clock, Mp4Writer writer) { }
        public bool Paused { get; set; }
        public string Start() { return null; }
        public void Stop() { }
        public void Dispose() { }
    }
}
