using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace VoxDeck.Audio;

public record DeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed class PlayHandle
{
    public string Tag; public object Owner; public AudioClip Clip;
    public volatile bool Stopped; public volatile float Volume = 1; public double Speed = 1; public volatile bool Loop;
    internal int Active; public double Pos;
    public event Action<PlayHandle> Ended;
    internal void VoiceDone() { if (Interlocked.Decrement(ref Active) == 0) { try { Ended?.Invoke(this); } catch { } } }
    public void Stop() => Stopped = true;
    public double Progress => Clip == null || Clip.Frames == 0 ? 1 : Math.Clamp(Pos / Clip.Frames, 0, 1);
}

sealed class Voice { public PlayHandle H; public double Pos; public bool Master; }

/// <summary>48 kHz stereo mix bus pulled by an output device.</summary>
public sealed class Bus
{
    readonly List<Voice> voices = new(); readonly object lk = new();
    public volatile float Volume = 1;
    public Action<float[], int, int> Extra;          // (buffer, offset, frames) additive stereo
    public volatile float Peak;
    readonly float[] scope = new float[4096]; int scopeW;
    public int ActiveVoices { get { lock (lk) return voices.Count; } }

    internal void Add(Voice v) { lock (lk) voices.Add(v); }

    public int Read(float[] buf, int offset, int count)
    {
        Array.Clear(buf, offset, count);
        int frames = count / 2; List<Voice> done = null;
        lock (lk)
        {
            for (int vi = voices.Count - 1; vi >= 0; vi--)
            {
                var v = voices[vi]; var h = v.H; var d = h.Clip.Data; int total = d.Length / 2; bool open = h.Clip.Open;
                float vol = h.Volume; double sp = h.Speed; bool fin = h.Stopped;
                if (!fin)
                {
                    for (int i = 0; i < frames; i++)
                    {
                        if (v.Pos >= total - 1) { if (open) break; if (h.Loop && total > 2) v.Pos = 0; else { fin = true; break; } }
                        int ip = (int)v.Pos; float fr = (float)(v.Pos - ip);
                        int a = ip * 2, b = a + 2;
                        buf[offset + 2 * i] += (d[a] + (d[b] - d[a]) * fr) * vol;
                        buf[offset + 2 * i + 1] += (d[a + 1] + (d[b + 1] - d[a + 1]) * fr) * vol;
                        v.Pos += sp;
                    }
                    if (v.Master) h.Pos = v.Pos;
                }
                if (fin) { voices.RemoveAt(vi); (done ??= new()).Add(v); }
            }
        }
        try { Extra?.Invoke(buf, offset, frames); } catch { }
        float volm = Volume, pk = 0;
        for (int i = 0; i < count; i++)
        {
            float x = buf[offset + i] * volm;
            if (x > 0.95f) x = 0.95f + 0.05f * (float)Math.Tanh((x - 0.95f) * 20);
            else if (x < -0.95f) x = -0.95f + 0.05f * (float)Math.Tanh((x + 0.95f) * 20);
            buf[offset + i] = x;
            float a = x < 0 ? -x : x; if (a > pk) pk = a;
        }
        for (int i = 0; i < frames; i++) { scope[scopeW] = (buf[offset + 2 * i] + buf[offset + 2 * i + 1]) * 0.5f; scopeW = (scopeW + 1) & 4095; }
        Peak = Math.Max(pk, Peak * 0.9f);
        if (done != null) foreach (var v in done) v.H.VoiceDone();
        return count;
    }

    public void StopAll(string tag)
    {
        lock (lk) foreach (var v in voices) if (tag == null || v.H.Tag == tag) v.H.Stopped = true;
    }

    readonly Complex[] fft = new Complex[1024];
    /// <summary>Fills 'bars' with 0..1 spectrum magnitudes (log-spaced).</summary>
    public void Spectrum(float[] bars)
    {
        int st = (scopeW - 1024) & 4095;
        for (int i = 0; i < 1024; i++) { fft[i].X = scope[(st + i) & 4095] * (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / 1024)); fft[i].Y = 0; }
        FastFourierTransform.FFT(true, 10, fft);
        int n = bars.Length;
        for (int b = 0; b < n; b++)
        {
            double f0 = 60 * Math.Pow(12000.0 / 60, (double)b / n), f1 = 60 * Math.Pow(12000.0 / 60, (double)(b + 1) / n);
            int k0 = Math.Max(1, (int)(f0 / 48000 * 1024)), k1 = Math.Max(k0 + 1, (int)(f1 / 48000 * 1024));
            double m = 0; for (int k = k0; k < k1 && k < 512; k++) m = Math.Max(m, Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y));
            double db = 20 * Math.Log10(m + 1e-7);
            bars[b] = (float)Math.Clamp((db + 72) / 60, 0, 1);
        }
    }
}

sealed class FloatRing
{
    readonly float[] b; int r, w, count; readonly object lk = new();
    public FloatRing(int cap) { b = new float[cap]; }
    public int Count { get { lock (lk) return count; } }
    public void Write(float[] src, int off, int n)
    {
        lock (lk)
        {
            for (int i = 0; i < n; i++)
            {
                b[w] = src[off + i]; w = (w + 1) % b.Length;
                if (count == b.Length) r = (r + 1) % b.Length; else count++;
            }
        }
    }
    public int Read(float[] dst, int off, int n)
    {
        lock (lk)
        {
            int m = Math.Min(n, count);
            for (int i = 0; i < m; i++) { dst[off + i] = b[r]; r = (r + 1) % b.Length; }
            count -= m; return m;
        }
    }
    public void Skip(int n) { lock (lk) { int m = Math.Min(n, count); r = (r + m) % b.Length; count -= m; } }
    public void Clear() { lock (lk) { r = w = count = 0; } }
}

sealed class BusWave : IWaveProvider
{
    readonly ISampleProvider src; float[] tmp = new float[8192];
    public WaveFormat WaveFormat => src.WaveFormat;
    public BusWave(ISampleProvider s) { src = s; }
    public int Read(byte[] buffer, int offset, int count)
    {
        int n = count / 4; if (tmp.Length < n) tmp = new float[n];
        int r = src.Read(tmp, 0, n);
        if (r < n) Array.Clear(tmp, r, n - r);
        Buffer.BlockCopy(tmp, 0, buffer, offset, n * 4);
        return n * 4;
    }
}
sealed class BusSample : ISampleProvider
{
    readonly Bus bus;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public BusSample(Bus b) { bus = b; }
    public int Read(float[] buffer, int offset, int count) => bus.Read(buffer, offset, count);
}

interface IOut : IDisposable { }

sealed class WasapiOutput : IOut
{
    readonly WasapiOut o;
    public WasapiOutput(MMDevice dev, Bus bus, int latencyMs)
    {
        int rate = 48000;
        try { rate = dev.AudioClient.MixFormat.SampleRate; } catch { }
        ISampleProvider sp = new BusSample(bus);
        if (rate != 48000 && rate >= 8000) sp = new WdlResamplingSampleProvider(sp, rate);
        o = new WasapiOut(dev, AudioClientShareMode.Shared, true, latencyMs);
        o.Init(new BusWave(sp));
        o.Play();
    }
    public void Dispose() { try { o.Stop(); } catch { } try { o.Dispose(); } catch { } }
}

/// <summary>Real-time clocked output without a device (used when no audio hardware is available).</summary>
sealed class NullOutput : IOut
{
    volatile bool run = true; readonly Thread t;
    public NullOutput(Bus bus)
    {
        t = new Thread(() =>
        {
            var buf = new float[960]; var sw = System.Diagnostics.Stopwatch.StartNew(); long done = 0;
            while (run)
            {
                long target = sw.ElapsedMilliseconds * 48;
                while (done < target && run) { bus.Read(buf, 0, 960); done += 480; }
                Thread.Sleep(8);
            }
        }) { IsBackground = true, Name = "NullOut" };
        t.Start();
    }
    public void Dispose() { run = false; }
}

public sealed class AudioEngine : IDisposable
{
    public readonly Bus VoiceBus = new(), MonitorBus = new();
    IOut voiceOut, monitorOut;
    public string VoiceDeviceName { get; private set; }
    public string MonitorDeviceName { get; private set; }
    public bool VoiceReady => voiceOut != null && !(voiceOut is NullOutput);
    public string LastError { get; private set; }
    public event Action StateChanged;

    // microphone
    WasapiCapture capture; readonly FloatRing micRing = new(48000), teeRing = new(48000);
    public readonly EffectChain MicChain = new();
    public volatile bool FxEnabled = true, HearMyself, MicMuted;
    public volatile float MicLevel, MicOutLevel;
    public bool MicRunning => capture != null;
    /// <summary>The microphone that was the system default before VoxDeck took over.</summary>
    public string RealMicHint;
    public string MicDeviceName { get; private set; }
    float[] micMono = new float[4096], capTmp = new float[8192], teeTmp = new float[4096];
    double rsPos; float rsLast; bool micBuffering = true;

    public static readonly string[] VirtualHints = { "cable input", "vb-audio", "voicemeeter input", "voicemeeter aux", "virtual", "hi-fi cable", "vac", "line 1" };

    public AudioEngine()
    {
        VoiceBus.Extra = MicToVoice;
        MonitorBus.Extra = MicToMonitor;
    }

    public List<DeviceInfo> Outputs() => Enumerate(DataFlow.Render);
    public List<DeviceInfo> Inputs() => Enumerate(DataFlow.Capture);
    static List<DeviceInfo> Enumerate(DataFlow flow)
    {
        var list = new List<DeviceInfo>();
        if (!OperatingSystem.IsWindows()) return list;
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                try { list.Add(new DeviceInfo(d.ID, d.FriendlyName)); } catch { }
            }
        }
        catch { }
        return list;
    }
    public static bool LooksVirtual(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var n = name.ToLowerInvariant();
        return VirtualHints.Any(h => n.Contains(h));
    }
    public DeviceInfo FindVirtualOutput() => Outputs().FirstOrDefault(d => d.Name.ToLowerInvariant().Contains("cable input")) ?? Outputs().FirstOrDefault(d => LooksVirtual(d.Name));

    /// <summary>(Re)opens output devices. Null/empty monitor id = system default. Null voice id = auto-detect a virtual cable.</summary>
    public void Configure(string voiceId, string monitorId, bool monitorEnabled = true)
    {
        voiceOut?.Dispose(); monitorOut?.Dispose(); voiceOut = null; monitorOut = null; LastError = null;
        VoiceDeviceName = null; MonitorDeviceName = null;
        if (!OperatingSystem.IsWindows())
        {
            monitorOut = new NullOutput(MonitorBus); voiceOut = new NullOutput(VoiceBus); MonitorDeviceName = "Null output";
            StateChanged?.Invoke(); return;
        }
        try
        {
            using var en = new MMDeviceEnumerator();
            MMDevice mon = null, voice = null;
            var all = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
            if (!string.IsNullOrEmpty(voiceId) && voiceId != "auto" && voiceId != "none") voice = all.FirstOrDefault(d => d.ID == voiceId);
            if (voice == null && voiceId != "none")
                voice = all.FirstOrDefault(d => SafeName(d).ToLowerInvariant().Contains("cable input")) ?? all.FirstOrDefault(d => LooksVirtual(SafeName(d)));
            if (!string.IsNullOrEmpty(monitorId) && monitorId != "default") mon = all.FirstOrDefault(d => d.ID == monitorId);
            if (mon == null)
            {
                try { mon = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { }
                // never monitor into the virtual cable by default
                if (mon != null && (LooksVirtual(SafeName(mon)) || (voice != null && mon.ID == voice.ID))) mon = all.FirstOrDefault(d => d.ID != voice?.ID && !LooksVirtual(SafeName(d)));
            }
            if (voice != null && mon != null && voice.ID == mon.ID) voice = null;
            if (voice != null)
            {
                try { voiceOut = new WasapiOutput(voice, VoiceBus, 40); VoiceDeviceName = SafeName(voice); }
                catch (Exception ex) { LastError = "Virtual mic: " + ex.Message; voiceOut = null; }
            }
            if (mon != null && monitorEnabled)
            {
                try { monitorOut = new WasapiOutput(mon, MonitorBus, 50); MonitorDeviceName = SafeName(mon); }
                catch (Exception ex) { LastError = "Monitor: " + ex.Message; monitorOut = null; }
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
        monitorOut ??= new NullOutput(MonitorBus);
        voiceOut ??= new NullOutput(VoiceBus);
        StateChanged?.Invoke();
    }
    static string SafeName(MMDevice d) { try { return d.FriendlyName; } catch { return "Device"; } }

    public PlayHandle Play(AudioClip clip, bool toVoice, bool toMonitor, float volume = 1, double speed = 1, bool loop = false, string tag = null, object owner = null)
    {
        var h = new PlayHandle { Clip = clip, Volume = volume, Speed = speed, Loop = loop, Tag = tag, Owner = owner };
        if (clip == null || (clip.Frames < 2 && !clip.Open) || (!toVoice && !toMonitor)) { h.Active = 1; h.VoiceDone(); return h; }
        h.Active = (toVoice ? 1 : 0) + (toMonitor ? 1 : 0);
        if (toMonitor) MonitorBus.Add(new Voice { H = h, Master = true });
        if (toVoice) VoiceBus.Add(new Voice { H = h, Master = !toMonitor });
        return h;
    }
    public void StopAll(string tag = null) { VoiceBus.StopAll(tag); MonitorBus.StopAll(tag); }

    // ---------------- microphone ----------------
    /// <summary>True when Windows privacy settings refused the microphone (Settings → Privacy → Microphone).</summary>
    public bool MicDenied { get; private set; }
    public void StartMic(string micId)
    {
        StopMic(); MicDenied = false;
        if (!OperatingSystem.IsWindows()) { LastError = "Microphone capture requires Windows"; StateChanged?.Invoke(); return; }
        try
        {
            using var en = new MMDeviceEnumerator();
            MMDevice dev = null;
            if (!string.IsNullOrEmpty(micId) && micId != "default")
                dev = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).FirstOrDefault(d => d.ID == micId);
            dev ??= en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            // while VoxDeck is the system microphone the "default" device is our own cable: capturing it would feed back
            if (LooksVirtual(SafeName(dev)))
            {
                string real = Platform.VirtualMic.RealMic(RealMicHint);
                dev = real == null ? null : en.GetDevice(real);
                if (dev == null) throw new InvalidOperationException("No real microphone found");
            }
            MicDeviceName = SafeName(dev);
            var cap = new WasapiCapture(dev, true, 20);
            cap.DataAvailable += OnMicData;
            cap.RecordingStopped += (_, e) => { if (e.Exception != null) { LastError = "Mic: " + e.Exception.Message; StateChanged?.Invoke(); } };
            micRing.Clear(); teeRing.Clear(); micBuffering = true; rsPos = 0;
            cap.StartRecording();
            capture = cap; LastError = null;
        }
        catch (Exception ex) { MicDenied = ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005); LastError = "Mic: " + ex.Message; capture = null; }
        StateChanged?.Invoke();
    }
    public void StopMic()
    {
        var c = capture; capture = null;
        if (c != null) { try { c.StopRecording(); } catch { } try { c.Dispose(); } catch { } }
        micRing.Clear(); teeRing.Clear(); MicLevel = 0; MicOutLevel = 0;
    }

    void OnMicData(object s, WaveInEventArgs e)
    {
        var cap = (WasapiCapture)s; var wf = cap.WaveFormat;
        int ch = wf.Channels, bps = wf.BitsPerSample / 8; if (bps <= 0 || ch <= 0) return;
        int frames = e.BytesRecorded / (bps * ch);
        if (capTmp.Length < frames) capTmp = new float[frames];
        bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat || (wf is WaveFormatExtensible && bps == 4) || (wf.Encoding == WaveFormatEncoding.Extensible && bps == 4);
        var b = e.Buffer; float pk = 0;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < ch; c++)
            {
                int o = (i * ch + c) * bps; float v;
                if (bps == 4 && isFloat) v = BitConverter.ToSingle(b, o);
                else if (bps == 2) v = BitConverter.ToInt16(b, o) / 32768f;
                else if (bps == 3) v = ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608f;
                else if (bps == 4) v = BitConverter.ToInt32(b, o) / 2147483648f;
                else v = 0;
                sum += v;
            }
            float m = sum / ch; capTmp[i] = m; float a = Math.Abs(m); if (a > pk) pk = a;
        }
        MicLevel = Math.Max(pk, MicLevel * 0.85f);
        // linear resample to 48k
        int rate = wf.SampleRate;
        if (rate == 48000) { micRing.Write(capTmp, 0, frames); return; }
        double step = rate / 48000.0; int outN = 0;
        int need = (int)(frames / step) + 8; if (micMonoRs.Length < need) micMonoRs = new float[need];
        while (true)
        {
            int ip = (int)Math.Floor(rsPos); if (ip >= frames) break;
            float s0 = ip - 1 < 0 ? rsLast : capTmp[ip - 1], s1 = capTmp[ip];
            float fr = (float)(rsPos - ip);
            micMonoRs[outN++] = s0 + (s1 - s0) * fr; rsPos += step;
        }
        rsPos -= frames; if (frames > 0) rsLast = capTmp[frames - 1];
        micRing.Write(micMonoRs, 0, outN);
    }
    float[] micMonoRs = new float[8192];

    // The "primary" consumer of the microphone is the voice bus when a virtual device exists, otherwise the monitor bus.
    bool PrimaryIsVoice => VoiceReady;

    void PullMic(float[] buf, int off, int frames, bool audible, bool tee)
    {
        if (micMono.Length < frames) micMono = new float[frames];
        int avail = micRing.Count;
        if (micBuffering) { if (avail >= frames + 720) micBuffering = false; }
        if (avail > frames + 4800) { micRing.Skip(avail - frames - 1200); }
        int got = 0;
        if (!micBuffering) { got = micRing.Read(micMono, 0, frames); if (got < frames) micBuffering = true; }
        if (got < frames) Array.Clear(micMono, got, frames - got);
        if (MicMuted) Array.Clear(micMono, 0, frames);
        else if (FxEnabled) MicChain.Process(micMono, 0, frames);
        float pk = 0;
        for (int i = 0; i < frames; i++)
        {
            float m = micMono[i]; float a = m < 0 ? -m : m; if (a > pk) pk = a;
            if (audible) { buf[off + 2 * i] += m; buf[off + 2 * i + 1] += m; }
        }
        MicOutLevel = Math.Max(pk, MicOutLevel * 0.85f);
        if (tee) teeRing.Write(micMono, 0, frames);
    }
    void MicToVoice(float[] buf, int off, int frames)
    {
        if (capture == null || !PrimaryIsVoice) return;
        PullMic(buf, off, frames, true, HearMyself);
    }
    void MicToMonitor(float[] buf, int off, int frames)
    {
        if (capture == null) return;
        if (!PrimaryIsVoice) { PullMic(buf, off, frames, HearMyself, false); return; }
        if (!HearMyself) { teeRing.Clear(); return; }
        if (teeTmp.Length < frames) teeTmp = new float[frames];
        int avail = teeRing.Count;
        if (avail > frames + 4800) teeRing.Skip(avail - frames - 1200);
        int got = teeRing.Read(teeTmp, 0, frames);
        for (int i = 0; i < got; i++) { buf[off + 2 * i] += teeTmp[i]; buf[off + 2 * i + 1] += teeTmp[i]; }
    }

    public void Dispose() { StopMic(); voiceOut?.Dispose(); monitorOut?.Dispose(); }
}
