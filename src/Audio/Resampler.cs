namespace VoxDeck.Audio;

/// <summary>Band-limited (Kaiser-windowed sinc) sample-rate converter for interleaved float audio.</summary>
public static class Resampler
{
    const int Half = 16, Taps = Half * 2, Phases = 512;
    static readonly Dictionary<long, float[]> tables = new();

    static double Bessel0(double x)
    {
        double sum = 1, term = 1, h = x * x / 4;
        for (int k = 1; k < 40; k++) { term *= h / (k * k); sum += term; if (term < 1e-12 * sum) break; }
        return sum;
    }

    static float[] Table(int src, int dst)
    {
        long key = ((long)src << 32) | (uint)dst;
        lock (tables)
        {
            if (tables.TryGetValue(key, out var t)) return t;
            double cut = Math.Min(1.0, (double)dst / src) * 0.955, beta = 8.6, i0 = Bessel0(beta);
            t = new float[Phases * Taps];
            for (int p = 0; p < Phases; p++)
            {
                double frac = (double)p / Phases, sum = 0;
                for (int k = 0; k < Taps; k++)
                {
                    double x = k - Half + 1 - frac, a = Math.PI * x * cut;
                    double s = Math.Abs(a) < 1e-9 ? 1 : Math.Sin(a) / a;
                    double r = x / Half, w = Math.Abs(r) >= 1 ? 0 : Bessel0(beta * Math.Sqrt(1 - r * r)) / i0;
                    double v = s * w * cut; t[p * Taps + k] = (float)v; sum += v;
                }
                if (cut > 0.9) for (int k = 0; k < Taps; k++) t[p * Taps + k] = (float)(t[p * Taps + k] / sum);   // exact unity gain at DC
            }
            tables[key] = t; return t;
        }
    }

    public static float[] Convert(float[] data, int channels, int srcRate, int dstRate)
    {
        if (srcRate == dstRate || data.Length == 0) return data;
        var tab = Table(srcRate, dstRate);
        int inFrames = data.Length / channels;
        int outFrames = (int)((long)inFrames * dstRate / srcRate);
        var res = new float[outFrames * channels];
        double step = (double)srcRate / dstRate;
        for (int i = 0; i < outFrames; i++)
        {
            double pos = i * step; int ip = (int)pos; int ph = (int)((pos - ip) * Phases); if (ph >= Phases) ph = Phases - 1;
            int t0 = ph * Taps, start = ip - Half + 1;
            int k0 = Math.Max(0, -start), k1 = Math.Min(Taps, inFrames - start);
            for (int c = 0; c < channels; c++)
            {
                float acc = 0; int idx = (start + k0) * channels + c;
                for (int k = k0; k < k1; k++, idx += channels) acc += data[idx] * tab[t0 + k];
                res[i * channels + c] = acc;
            }
        }
        return res;
    }
}

/// <summary>Speech polish applied locally to synthesized voices: rumble cut, presence, restored "air" above 12 kHz, even loudness.</summary>
public static class VoiceEnhancer
{
    /// <summary>mono 48 kHz in, new array out.</summary>
    public static float[] Process(float[] x)
    {
        var y = new float[x.Length];
        Biquad hp = new(), pres = new(), shelf = new(), bandA = new(), bandB = new(), airA = new(), airB = new(), deMud = new();
        hp.HighPass(75, 0.707); deMud.Peak(300, 1.0, -1.5); pres.Peak(3200, 0.8, 2.2); shelf.HighShelf(7000, 3.0);
        bandA.HighPass(4500, 0.707); bandB.LowPass(11000, 0.707); airA.HighPass(11500, 0.707); airB.HighPass(11500, 0.707);
        for (int i = 0; i < x.Length; i++)
        {
            float s = hp.Run(x[i]);
            // exciter: harmonics of the 4.5–11 kHz band fill the octave the 24 kHz source cannot carry
            float b = bandB.Run(bandA.Run(s));
            float h = airB.Run(airA.Run(MathF.Abs(b) * b * 14f + b * b * 6f));
            s = shelf.Run(pres.Run(deMud.Run(s)));
            y[i] = s + h * 0.55f;
        }
        return y;
    }

    /// <summary>Gain that brings speech to a consistent loudness (RMS of the voiced parts ≈ -19 dBFS).</summary>
    public static float LoudnessGain(float[] x, float target = 0.112f)
    {
        const int win = 480; double sum = 0; int n = 0; float peak = 0;
        for (int i = 0; i < x.Length; i++) { float a = Math.Abs(x[i]); if (a > peak) peak = a; }
        if (peak < 1e-4f) return 1;
        float gate = peak * 0.03f;
        for (int o = 0; o + win <= x.Length; o += win)
        {
            double e = 0; for (int i = 0; i < win; i++) e += x[o + i] * x[o + i];
            double rms = Math.Sqrt(e / win);
            if (rms > gate) { sum += e; n += win; }
        }
        if (n == 0) return 1;
        float g = target / (float)Math.Sqrt(sum / n);
        return Math.Clamp(g, 0.4f, Math.Min(6f, 1.6f / peak));
    }

    public static void GainLimit(float[] x, float g)
    {
        for (int i = 0; i < x.Length; i++)
        {
            float v = x[i] * g;
            if (v > 0.8f) v = 0.8f + 0.18f * MathF.Tanh((v - 0.8f) / 0.18f);
            else if (v < -0.8f) v = -0.8f + 0.18f * MathF.Tanh((v + 0.8f) / 0.18f);
            x[i] = v;
        }
    }

    /// <summary>Removes leading silence and shortens trailing silence so joined sentences flow naturally.</summary>
    public static float[] TrimSilence(float[] x, double keepLeadSec = 0.03, double keepTailSec = 0.22)
    {
        float peak = 0; for (int i = 0; i < x.Length; i++) { float a = Math.Abs(x[i]); if (a > peak) peak = a; }
        if (peak < 1e-4f) return x;
        float thr = Math.Max(peak * 0.012f, 0.0015f);
        int first = 0; while (first < x.Length && Math.Abs(x[first]) < thr) first++;
        int last = x.Length - 1; while (last > first && Math.Abs(x[last]) < thr) last--;
        int a0 = Math.Max(0, first - (int)(keepLeadSec * 48000)), a1 = Math.Min(x.Length, last + 1 + (int)(keepTailSec * 48000));
        if (a0 == 0 && a1 == x.Length) return x;
        var r = new float[a1 - a0]; Array.Copy(x, a0, r, 0, r.Length);
        int f = Math.Min(480, r.Length / 4);
        for (int i = 0; i < f; i++) { float k = (float)i / f; if (a0 > 0) r[i] *= k; }
        return r;
    }
}
