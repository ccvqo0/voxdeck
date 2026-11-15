using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace VoxDeck.Audio;

/// <summary>Decoded audio: interleaved stereo float, 48 kHz.</summary>
public sealed class AudioClip
{
    public const int SR = 48000;
    public volatile float[] Data = Array.Empty<float>();
    /// <summary>True while more audio is still being appended (streaming speech); players wait instead of finishing.</summary>
    public volatile bool Open;
    public int Frames => Data.Length / 2;
    readonly object appendLock = new();
    public void AppendMono(float[] mono)
    {
        lock (appendLock)
        {
            var old = Data; var d = new float[old.Length + mono.Length * 2];
            Array.Copy(old, d, old.Length);
            for (int i = 0, o = old.Length; i < mono.Length; i++) { d[o++] = mono[i]; d[o++] = mono[i]; }
            Data = d;
        }
    }
    public double Seconds => Frames / (double)SR;

    public static AudioClip FromMono(float[] mono)
    {
        var d = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) { d[2 * i] = mono[i]; d[2 * i + 1] = mono[i]; }
        return new AudioClip { Data = d };
    }
    public float[] ToMono()
    {
        var m = new float[Frames];
        for (int i = 0; i < m.Length; i++) m[i] = (Data[2 * i] + Data[2 * i + 1]) * 0.5f;
        return m;
    }
    public AudioClip WithFx(FxParams fx) => fx == null || fx.IsNeutral() ? this : FromMono(EffectChain.Render(ToMono(), fx));

    public byte[] ToWav()
    {
        int n = Data.Length; var ms = new MemoryStream(44 + n * 2); var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(SR); w.Write(SR * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(n * 2);
        for (int i = 0; i < n; i++) w.Write((short)Math.Clamp((int)(Data[i] * 32767f), -32768, 32767));
        w.Flush(); return ms.ToArray();
    }
}

sealed class ArraySampleProvider : ISampleProvider
{
    readonly float[] d; int pos;
    public WaveFormat WaveFormat { get; }
    public ArraySampleProvider(float[] data, int rate, int ch) { d = data; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, ch); }
    public int Read(float[] buffer, int offset, int count)
    {
        int n = Math.Min(count, d.Length - pos); if (n <= 0) return 0;
        Array.Copy(d, pos, buffer, offset, n); pos += n; return n;
    }
}

public static class Decoder
{
    public const int MaxSeconds = 600;

    public static AudioClip FromInterleaved(float[] data, int rate, int ch)
    {
        if (ch < 1) ch = 1;
        // to stereo
        float[] st;
        if (ch == 2) st = data;
        else
        {
            int frames = data.Length / ch; st = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                if (ch == 1) { st[2 * i] = st[2 * i + 1] = data[i]; }
                else { st[2 * i] = data[i * ch]; st[2 * i + 1] = data[i * ch + 1]; }
            }
        }
        return new AudioClip { Data = Resampler.Convert(st, 2, rate, AudioClip.SR) };
    }

    /// <summary>Decodes to mono 48 kHz (used for speech).</summary>
    public static float[] DecodeMono(byte[] bytes)
    {
        if (IsMp3(bytes) && !IsWav(bytes))
        {
            using var mp = new NLayer.MpegFile(new MemoryStream(bytes));
            int ch = mp.Channels, rate = mp.SampleRate;
            var all = new List<float>(1 << 18); var buf = new float[16384]; int r;
            while ((r = mp.ReadSamples(buf, 0, buf.Length)) > 0) for (int i = 0; i < r; i++) all.Add(buf[i]);
            if (all.Count == 0) throw new InvalidDataException("empty mp3");
            float[] m;
            if (ch == 1) m = all.ToArray();
            else { m = new float[all.Count / ch]; for (int i = 0; i < m.Length; i++) { float s = 0; for (int c = 0; c < ch; c++) s += all[i * ch + c]; m[i] = s / ch; } }
            return Resampler.Convert(m, 1, rate, AudioClip.SR);
        }
        return Decode(bytes).ToMono();
    }

    static bool IsMp3(byte[] b) => b.Length > 3 && ((b[0] == 'I' && b[1] == 'D' && b[2] == '3') || (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0));
    static bool IsWav(byte[] b) => b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F';
    static bool IsOgg(byte[] b) => b.Length > 4 && b[0] == 'O' && b[1] == 'g' && b[2] == 'g' && b[3] == 'S';

    public static AudioClip Decode(byte[] bytes, string pathForFallback = null)
    {
        Exception first = null;
        try
        {
            if (IsWav(bytes)) return DecodeWav(bytes);
            if (IsOgg(bytes)) return DecodeOgg(bytes);
            if (IsMp3(bytes)) return DecodeMp3(bytes);
        }
        catch (Exception ex) { first = ex; }
        if (OperatingSystem.IsWindows())
        {
            try { return DecodeMediaFoundation(bytes, pathForFallback); } catch (Exception ex) { first ??= ex; }
        }
        try { return DecodeMp3(bytes); } catch (Exception ex) { first ??= ex; }
        throw new InvalidDataException("Unsupported or damaged audio file", first);
    }

    public static AudioClip DecodeFile(string path) => Decode(File.ReadAllBytes(path), path);

    static AudioClip DecodeMp3(byte[] bytes)
    {
        using var mp = new NLayer.MpegFile(new MemoryStream(bytes));
        int ch = mp.Channels, rate = mp.SampleRate; long max = (long)MaxSeconds * rate * ch;
        var all = new List<float>(1 << 20); var buf = new float[16384]; int r;
        while ((r = mp.ReadSamples(buf, 0, buf.Length)) > 0 && all.Count < max) for (int i = 0; i < r; i++) all.Add(buf[i]);
        if (all.Count == 0) throw new InvalidDataException("empty mp3");
        return FromInterleaved(all.ToArray(), rate, ch);
    }
    static AudioClip DecodeOgg(byte[] bytes)
    {
        using var vr = new NVorbis.VorbisReader(new MemoryStream(bytes), true);
        int ch = vr.Channels, rate = vr.SampleRate; long max = (long)MaxSeconds * rate * ch;
        var all = new List<float>(1 << 20); var buf = new float[16384]; int r;
        while ((r = vr.ReadSamples(buf, 0, buf.Length)) > 0 && all.Count < max) for (int i = 0; i < r; i++) all.Add(buf[i]);
        return FromInterleaved(all.ToArray(), rate, ch);
    }
    static AudioClip DecodeWav(byte[] bytes)
    {
        using var rd = new WaveFileReader(new MemoryStream(bytes));
        return ReadAll(rd.ToSampleProvider());
    }
    static AudioClip DecodeMediaFoundation(byte[] bytes, string path)
    {
        string tmp = null;
        try
        {
            if (path == null || !File.Exists(path)) { tmp = Path.Combine(Path.GetTempPath(), "voxdeck_" + Guid.NewGuid().ToString("N") + ".bin"); File.WriteAllBytes(tmp, bytes); path = tmp; }
            using var rd = new MediaFoundationReader(path);
            return ReadAll(rd.ToSampleProvider());
        }
        finally { if (tmp != null) try { File.Delete(tmp); } catch { } }
    }
    static AudioClip ReadAll(ISampleProvider sp)
    {
        int ch = sp.WaveFormat.Channels, rate = sp.WaveFormat.SampleRate; long max = (long)MaxSeconds * rate * ch;
        var all = new List<float>(1 << 20); var buf = new float[16384 - 16384 % ch]; int r;
        while ((r = sp.Read(buf, 0, buf.Length)) > 0 && all.Count < max) for (int i = 0; i < r; i++) all.Add(buf[i]);
        return FromInterleaved(all.ToArray(), rate, ch);
    }
}
