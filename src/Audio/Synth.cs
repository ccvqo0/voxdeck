namespace VoxDeck.Audio;

public sealed class BuiltinSound
{
    public string Id, Name, Category; public double Dur; public Func<float[]> Render;
}

/// <summary>Procedurally synthesized built-in sound library (no samples shipped, everything is generated on demand).</summary>
public static class Synth
{
    const int SR = 48000; const double TAU = Math.PI * 2;
    public enum W { Sin, Sq, Saw, Tri, P25 }

    // ---------- primitives ----------
    static float[] Osc(double dur, W w, Func<double, double> f, Func<double, double> a)
    {
        int n = (int)(dur * SR); var o = new float[n]; double ph = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SR; ph += f(t) / SR; ph -= Math.Floor(ph);
            double v = w switch { W.Sin => Math.Sin(TAU * ph), W.Sq => ph < 0.5 ? 1 : -1, W.P25 => ph < 0.25 ? 1 : -1, W.Saw => 2 * ph - 1, _ => 4 * Math.Abs(ph - 0.5) - 1 };
            o[i] = (float)(v * a(t));
        }
        return o;
    }
    static float[] Noise(double dur, Func<double, double> a, int seed = 1)
    {
        int n = (int)(dur * SR); var o = new float[n]; var r = new Random(seed);
        for (int i = 0; i < n; i++) o[i] = (float)((r.NextDouble() * 2 - 1) * a(i / (double)SR));
        return o;
    }
    static float[] Brown(double dur, Func<double, double> a, int seed = 1)
    {
        int n = (int)(dur * SR); var o = new float[n]; var r = new Random(seed); double y = 0;
        for (int i = 0; i < n; i++) { y = (y + (r.NextDouble() * 2 - 1) * 0.04) * 0.998; o[i] = (float)(y * 6 * a(i / (double)SR)); }
        return o;
    }
    static float[] LP(float[] x, double f, double q = 0.707) { var b = new Biquad(); b.LowPass(f, q); var o = new float[x.Length]; for (int i = 0; i < x.Length; i++) o[i] = b.Run(x[i]); return o; }
    static float[] HP(float[] x, double f, double q = 0.707) { var b = new Biquad(); b.HighPass(f, q); var o = new float[x.Length]; for (int i = 0; i < x.Length; i++) o[i] = b.Run(x[i]); return o; }
    static float[] BP(float[] x, double lo, double hi) => LP(HP(x, lo), hi);
    /// <summary>State variable filter with time-varying cutoff. mode: 0 LP, 1 BP, 2 HP.</summary>
    static float[] Svf(float[] x, Func<double, double> fc, double q, int mode)
    {
        var o = new float[x.Length]; double low = 0, band = 0;
        for (int i = 0; i < x.Length; i++)
        {
            double f = 2 * Math.Sin(Math.PI * Math.Min(fc(i / (double)SR), 7500) / SR);
            low += f * band; double high = x[i] - low - q * band; band += f * high;
            o[i] = (float)(mode == 0 ? low : mode == 1 ? band : high);
        }
        return o;
    }
    static float[] Mix(params float[][] xs)
    {
        var o = new float[xs.Max(x => x.Length)];
        foreach (var x in xs) for (int i = 0; i < x.Length; i++) o[i] += x[i];
        return o;
    }
    static float[] Cat(params float[][] xs)
    {
        var o = new float[xs.Sum(x => x.Length)]; int p = 0;
        foreach (var x in xs) { Array.Copy(x, 0, o, p, x.Length); p += x.Length; }
        return o;
    }
    static float[] Sil(double d) => new float[(int)(d * SR)];
    static float[] At(double total, params (double t, float[] x)[] parts)
    {
        int n = Math.Max((int)(total * SR), parts.Max(p => (int)(p.t * SR) + p.x.Length)); var o = new float[n];
        foreach (var (t, x) in parts) { int s = (int)(t * SR); for (int i = 0; i < x.Length; i++) o[s + i] += x[i]; }
        return o;
    }
    static float[] G(float[] x, double g) { var o = new float[x.Length]; for (int i = 0; i < x.Length; i++) o[i] = (float)(x[i] * g); return o; }
    static float[] Drive(float[] x, double g) { var o = new float[x.Length]; for (int i = 0; i < x.Length; i++) o[i] = (float)Math.Tanh(x[i] * g); return o; }
    static float[] Crush(float[] x, int hold, double levels)
    {
        var o = new float[x.Length]; float h = 0;
        for (int i = 0; i < x.Length; i++) { if (i % hold == 0) h = (float)(Math.Round(x[i] * levels) / levels); o[i] = h; }
        return o;
    }
    static float[] Rv(float[] x, double mix, double room, double tail = 1.5)
    {
        var r = new Reverb(); var o = new float[x.Length + (int)(tail * SR)]; float fb = (float)(0.7 + room * 0.28);
        for (int i = 0; i < o.Length; i++) { float s = i < x.Length ? x[i] : 0; o[i] = (float)(s * (1 - 0.4 * mix) + r.Run(s, fb, 0.25f) * mix * 1.3); }
        return o;
    }
    static float[] Ech(float[] x, double time, double fb, double mix, double tail = 1.5)
    {
        int d = (int)(time * SR); var o = new float[x.Length + (int)(tail * SR)]; var buf = new float[o.Length];
        for (int i = 0; i < o.Length; i++) { float s = i < x.Length ? x[i] : 0; float dl = i >= d ? buf[i - d] : 0; buf[i] = (float)(s + dl * fb); o[i] = (float)(s + dl * mix); }
        return o;
    }
    static float[] Rep(float[] x, int times, double gap = 0) { var parts = new List<float[]>(); for (int i = 0; i < times; i++) { parts.Add(x); if (gap > 0) parts.Add(Sil(gap)); } return Cat(parts.ToArray()); }
    static float[] Fin(float[] x, double peak = 0.8)
    {
        float m = 1e-6f; foreach (var v in x) { float a = Math.Abs(v); if (a > m) m = a; }
        var o = new float[x.Length]; float g = (float)(peak / m); int f = Math.Min(144, x.Length / 2);
        for (int i = 0; i < x.Length; i++) { float e = 1; if (i < f) e = i / (float)f; else if (i > x.Length - f) e = (x.Length - i) / (float)f; o[i] = x[i] * g * e; }
        return o;
    }
    // envelopes / curves
    static Func<double, double> E(double decay) => t => Math.Exp(-t * decay);
    static Func<double, double> K(double v) => _ => v;
    static Func<double, double> AD(double a, double total) => t => t < a ? t / a : Math.Max(0, 1 - (t - a) / Math.Max(1e-6, total - a));
    static Func<double, double> ASR(double a, double r, double total) => t => t < a ? t / a : t > total - r ? Math.Max(0, (total - t) / r) : 1;
    static Func<double, double> Sw(double f0, double f1, double dur) => t => f0 * Math.Pow(f1 / f0, Math.Min(1, t / dur));
    static Func<double, double> Vib(double f, double depth, double rate) => t => f * (1 + depth * Math.Sin(TAU * rate * t));
    static double M(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);
    static readonly string[] NN = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    static string NoteName(int midi) => NN[midi % 12] + (midi / 12 - 1);
    static int Midi(string n)
    {
        int oct = n[^1] - '0'; string pc = n[..^1];
        return Array.IndexOf(NN, pc) + (oct + 1) * 12;
    }

    // ---------- instruments ----------
    static float[] Piano(double f, double dur = 1.8)
    {
        var parts = new float[8][];
        for (int k = 1; k <= 8; k++)
        {
            int kk = k; double fk = f * kk * Math.Sqrt(1 + 0.0005 * kk * kk); if (fk > 16000) { parts[k - 1] = new float[1]; continue; }
            parts[k - 1] = Osc(dur, W.Sin, K(fk), t => Math.Min(1, t / 0.004) * Math.Exp(-t * (1.3 + 0.8 * kk)) / Math.Pow(kk, 1.15));
        }
        return Mix(parts);
    }
    static float[] Chip(double f, double dur = 0.45) => Osc(dur, W.P25, K(f), t => Math.Min(1, t / 0.003) * Math.Exp(-t * 5));
    static float[] BellI(double f, double dur = 2.5)
    {
        double[] r = { 1, 2.76, 5.4, 8.93 }, a = { 1, .6, .4, .25 }, d = { 1.3, 2, 3, 4.5 };
        var parts = new float[4][];
        for (int k = 0; k < 4; k++) { int kk = k; parts[k] = Osc(dur, W.Sin, K(Math.Min(f * r[kk], 18000)), t => a[kk] * Math.Exp(-t * d[kk] * (2.5 / dur))); }
        return Mix(parts);
    }
    static float[] Bass(double f, double dur = 0.9) => LP(Mix(Osc(dur, W.Saw, K(f), t => Math.Min(1, t / 0.005) * Math.Exp(-t * 3)), Osc(dur, W.Sin, K(f), t => 0.8 * Math.Exp(-t * 2.5))), Math.Min(1200, f * 8));
    static float[] Brass(double f, double dur) => LP(Mix(Osc(dur, W.Saw, Vib(f, 0.004, 5.5), ASR(0.03, 0.08, dur)), G(Osc(dur, W.Saw, Vib(f * 1.006, 0.004, 5.2), ASR(0.03, 0.08, dur)), 0.7)), 2600);
    static float[] Pad(double[] fs, double dur = 3)
    {
        var parts = new List<float[]>();
        foreach (var f in fs) { var ff = f; parts.Add(Osc(dur, W.Saw, K(ff * 0.996), ASR(0.35, 0.9, dur))); parts.Add(Osc(dur, W.Saw, K(ff * 1.004), ASR(0.35, 0.9, dur))); }
        return LP(Mix(parts.ToArray()), 2000);
    }
    static float[] Arp(W w, double step, double decay, params int[] midis)
    {
        var parts = new (double, float[])[midis.Length];
        for (int i = 0; i < midis.Length; i++) { double f = M(midis[i]); parts[i] = (i * step, Osc(i == midis.Length - 1 ? step * 3.5 : step * 1.05, w, K(f), i == midis.Length - 1 ? E(decay) : K(1))); }
        return At(0, parts);
    }
    static float[] Tune(int bpm, string notes, W lead = W.P25)
    {
        double beat = 60.0 / bpm, t = 0; var parts = new List<(double, float[])>();
        foreach (var tok in notes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var sp = tok.Split(':'); double len = sp.Length > 1 ? double.Parse(sp[1], System.Globalization.CultureInfo.InvariantCulture) : 1; double d = len * beat;
            if (sp[0] != "R")
            {
                double f = M(Midi(sp[0])); double dd = d;
                parts.Add((t, Osc(dd * 0.97, lead, K(f), tt => Math.Min(1, tt / 0.004) * (0.35 + 0.65 * Math.Exp(-tt * 4)) * Math.Min(1, (dd * 0.97 - tt) / 0.02))));
                parts.Add((t, Osc(dd * 0.97, W.Tri, K(f / 2), tt => 0.45 * Math.Min(1, tt / 0.004) * Math.Min(1, (dd * 0.97 - tt) / 0.02))));
            }
            t += d;
        }
        return At(t + 0.1, parts.ToArray());
    }
    // drum kit
    static float[] Kick(double f0 = 150, double f1 = 45, double dec = 9, double dur = 0.4) => Mix(Osc(dur, W.Sin, Sw(f0, f1, 0.08), E(dec)), G(HP(Noise(0.01, E(300), 3), 2000), 0.3));
    static float[] Snare(double tone = 200, double dec = 14, double dur = 0.25) => Mix(Osc(dur, W.Tri, Sw(tone * 1.4, tone, 0.04), E(dec * 1.3)), G(HP(Noise(dur, E(dec), 5), 1500), 0.9));
    static float[] Clap() => BP(At(0.3, (0, Noise(0.012, E(200), 11)), (0.012, Noise(0.012, E(200), 12)), (0.025, Noise(0.012, E(200), 13)), (0.036, Noise(0.22, E(22), 14))), 900, 3800);
    static float[] HatC() => HP(Noise(0.06, E(70), 21), 7000);
    static float[] HatO(double dur = 0.4) => HP(Noise(dur, E(3.2 / dur), 22), 7000);
    static float[] Tom(double f) => Osc(0.45, W.Sin, Sw(f * 1.5, f, 0.06), E(7));
    static float[] Crash(double dur = 1.8) => Mix(HP(Noise(dur, E(4.5 / dur), 31), 4500), G(HP(Mix(Osc(dur, W.Sq, K(3170), E(6)), Osc(dur, W.Sq, K(4513), E(6)), Osc(dur, W.Sq, K(5861), E(6))), 3000), 0.12));
    static float[] Beat(int bpm, int bars, string kick, string snare, string hat, string open = null, string k808 = null)
    {
        double s16 = 60.0 / bpm / 4; var parts = new List<(double, float[])>(); var k = Kick(); var sn = Snare(); var hc = G(HatC(), 0.45); var ho = G(HatO(0.25), 0.4); var sub = G(Drive(Osc(0.5, W.Sin, Sw(110, 45, 0.08), E(4)), 2), 0.9);
        for (int b = 0; b < bars; b++)
            for (int i = 0; i < 16; i++)
            {
                double t = (b * 16 + i) * s16;
                if (kick[i % kick.Length] == 'x') parts.Add((t, k));
                if (snare[i % snare.Length] == 'x') parts.Add((t, sn));
                if (hat[i % hat.Length] == 'x') parts.Add((t, hc));
                if (open != null && open[i % open.Length] == 'x') parts.Add((t, ho));
                if (k808 != null && k808[i % k808.Length] == 'x') parts.Add((t, sub));
            }
        return At(bars * 16 * s16, parts.ToArray());
    }
    static float[] Dtmf(double a, double b, double dur = 0.25) => Mix(Osc(dur, W.Sin, K(a), ASR(0.005, 0.005, dur)), Osc(dur, W.Sin, K(b), ASR(0.005, 0.005, dur)));
    static float[] Beep(double f, double dur, W w = W.Sin) => Osc(dur, w, K(f), ASR(0.004, 0.006, dur));
    static float[] RobotTalk(int seed)
    {
        var r = new Random(seed); var parts = new List<float[]>(); int n = 7 + r.Next(6);
        for (int i = 0; i < n; i++)
        {
            double f0 = 300 + r.NextDouble() * 2200, f1 = r.Next(3) == 0 ? f0 : 300 + r.NextDouble() * 2200, d = 0.04 + r.NextDouble() * 0.1;
            parts.Add(Osc(d, r.Next(2) == 0 ? W.Sq : W.Sin, Sw(f0, f1, d), ASR(0.003, 0.004, d)));
            if (r.Next(3) == 0) parts.Add(Sil(0.03));
        }
        return Cat(parts.ToArray());
    }
    static float[] Glitch(int seed)
    {
        var r = new Random(seed); var parts = new List<float[]>();
        for (int i = 0; i < 14; i++)
        {
            double d = 0.015 + r.NextDouble() * 0.06;
            float[] p = r.Next(3) switch { 0 => Noise(d, K(0.8), seed * 31 + i), 1 => Osc(d, W.Sq, K(80 + r.NextDouble() * 3000), K(0.8)), _ => Osc(d, W.Saw, Sw(3000 * r.NextDouble() + 100, 3000 * r.NextDouble() + 100, d), K(0.8)) };
            parts.Add(Crush(p, 1 + r.Next(12), 6)); if (r.Next(4) == 0) parts.Add(Sil(0.02));
        }
        return Cat(parts.ToArray());
    }
    static float[] Grains(double dur, int count, int seed, Func<Random, float[]> grain, Func<double, double> density = null)
    {
        var r = new Random(seed); var o = new float[(int)(dur * SR)];
        for (int i = 0; i < count; i++)
        {
            double t = r.NextDouble() * dur; if (density != null && r.NextDouble() > density(t)) continue;
            var g = grain(r); int s = (int)(t * SR); for (int k = 0; k < g.Length && s + k < o.Length; k++) o[s + k] += g[k];
        }
        return o;
    }

    // ---------- catalogue ----------
    public static readonly List<BuiltinSound> All = Build();
    public static readonly string[] Categories = { "Meme & Reactions", "8-Bit", "Drums & Beats", "Signals & Alarms", "Nature & Ambience", "Sci-Fi", "UI", "Risers & Impacts", "Tunes", "Notes: Piano", "Notes: Chiptune", "Notes: Bell", "Notes: Bass", "Chords" };

    static List<BuiltinSound> Build()
    {
        var L = new List<BuiltinSound>();
        void A(string cat, string name, double dur, Func<float[]> f) => L.Add(new BuiltinSound { Id = "builtin:" + cat + "/" + name, Name = name, Category = cat, Dur = dur, Render = () => Fin(f()) });

        // ===== Meme & Reactions =====
        const string MR = "Meme & Reactions";
        Func<double, float[]> horn = d => Drive(LP(Mix(Osc(d, W.Saw, t => 370 * (1 - 0.03 * Math.Exp(-t * 30)), ASR(0.01, 0.06, d)), Osc(d, W.Saw, t => 466 * (1 - 0.03 * Math.Exp(-t * 30)), ASR(0.01, 0.06, d)), Osc(d, W.Saw, t => 554.4 * (1 - 0.03 * Math.Exp(-t * 30)), ASR(0.01, 0.06, d)), Osc(d, W.Sq, K(185), ASR(0.01, 0.06, d))), 3200), 1.6);
        A(MR, "Air Horn", 0.8, () => Rv(horn(0.7), 0.12, 0.4, 0.3));
        A(MR, "Air Horn x3", 1.5, () => Rv(Cat(horn(0.16), Sil(0.07), horn(0.16), Sil(0.07), horn(0.75)), 0.12, 0.4, 0.3));
        A(MR, "Air Horn Long", 2.2, () => Rv(horn(2.0), 0.12, 0.4, 0.3));
        Func<double, double, float[]> tbn = (f, d) => LP(Osc(d, W.Saw, t => f * (1 - 0.02 * t / d) * (1 + 0.012 * Math.Sin(TAU * 5.5 * t) * Math.Min(1, t * 3)), ASR(0.04, 0.1, d)), 1100);
        A(MR, "Sad Trombone", 2.6, () => Cat(tbn(233, 0.45), tbn(220, 0.45), tbn(207.6, 0.45), LP(Osc(1.3, W.Saw, t => 196 * (1 - 0.06 * t) * (1 + 0.03 * Math.Sin(TAU * 5.5 * t)), ASR(0.04, 0.5, 1.3)), 1000)));
        A(MR, "Ta-Da!", 1.6, () => Rv(Cat(Mix(Brass(M(60), 0.14), Brass(M(64), 0.14), Brass(M(67), 0.14)), Sil(0.03), Mix(Brass(M(72), 1.1), Brass(M(76), 1.1), Brass(M(79), 1.1), Brass(M(60), 1.1))), 0.2, 0.6, 0.6));
        A(MR, "Deep Boom", 2.2, () => Rv(Mix(Drive(Osc(1.4, W.Sin, Sw(80, 32, 0.5), E(2.6)), 2.5), G(LP(Noise(0.25, E(18), 9), 500), 0.8)), 0.35, 0.85, 1.2));
        A(MR, "Thud", 0.5, () => Mix(Osc(0.4, W.Sin, Sw(110, 40, 0.1), E(11)), G(LP(Noise(0.08, E(40), 4), 700), 0.6)));
        A(MR, "Censor Beep", 0.6, () => Beep(1000, 0.6));
        A(MR, "Censor Beep Long", 1.5, () => Beep(1000, 1.5));
        A(MR, "Wrong Buzzer", 0.7, () => LP(Mix(Osc(0.7, W.Sq, K(118), ASR(0.005, 0.03, 0.7)), Osc(0.7, W.Saw, K(121), ASR(0.005, 0.03, 0.7))), 2500));
        A(MR, "Correct Ding", 1.5, () => At(0, (0, BellI(1568, 1.2)), (0.11, BellI(2093, 1.4))));
        A(MR, "Ding", 1.4, () => BellI(1760, 1.4));
        A(MR, "Crickets", 3, () => Rep(Cat(Rep(Osc(0.045, W.Sin, K(4600), t => 0.5 + 0.5 * Math.Sin(TAU * 60 * t)), 3, 0.02), Sil(0.45)), 5));
        A(MR, "Dun Dun Dunnn", 3.6, () => Rv(At(0, (0, Mix(Brass(M(48), 0.35), Brass(M(36), 0.35))), (0.5, Mix(Brass(M(48), 0.35), Brass(M(36), 0.35))), (1.0, Mix(Brass(M(42), 1.8), Brass(M(30), 1.8), Brass(M(51), 1.8), Brass(M(57), 1.8), G(Kick(120, 35, 4, 1), 1.5)))), 0.3, 0.8, 0.8));
        A(MR, "Record Scratch", 0.5, () => Svf(Mix(Noise(0.45, AD(0.02, 0.45), 8), Osc(0.45, W.Saw, t => 900 - 700 * Math.Abs(Math.Sin(TAU * 4.5 * t)), K(0.5))), t => 800 + 2500 * Math.Abs(Math.Sin(TAU * 4.5 * t)) * (1 - t), 0.6, 1));
        for (int i = 1; i <= 3; i++) { int s = i; A(MR, "Whoopee " + i, 0.5 + 0.25 * i, () => { var r = new Random(s * 7); double d = 0.4 + 0.25 * s, f = 70 + 15 * s; double w1 = r.NextDouble() * 6 + 3, w2 = r.NextDouble() * 20 + 9; return LP(Osc(d, W.Saw, t => f * (1 + 0.25 * Math.Sin(TAU * w1 * t) + 0.1 * Math.Sin(TAU * w2 * t)) * (1 - 0.3 * t / d), t => ASR(0.01, 0.08, d)(t) * (0.6 + 0.4 * Math.Sin(TAU * (25 + 5 * s) * t))), 700); }); }
        A(MR, "Boing", 1.0, () => Osc(1.0, W.Sin, t => 260 + 240 * Math.Exp(-t * 3) * Math.Sin(TAU * 11 * t) + 120 * Math.Exp(-t * 3), E(3.2)));
        A(MR, "Spring", 0.9, () => Osc(0.9, W.Tri, t => 500 + 400 * Math.Exp(-t * 4) * Math.Sin(TAU * 18 * t), E(4)));
        A(MR, "Slide Whistle Up", 0.9, () => Mix(Osc(0.9, W.Sin, Sw(450, 2300, 0.85), ASR(0.03, 0.05, 0.9)), G(HP(Noise(0.9, ASR(0.03, 0.05, 0.9), 2), 5000), 0.05)));
        A(MR, "Slide Whistle Down", 0.9, () => Mix(Osc(0.9, W.Sin, Sw(2300, 400, 0.85), ASR(0.03, 0.05, 0.9)), G(HP(Noise(0.9, ASR(0.03, 0.05, 0.9), 2), 5000), 0.05)));
        A(MR, "Cartoon Fall", 1.6, () => Cat(Osc(1.3, W.Sin, t => 1900 * Math.Pow(0.2, t / 1.3) * (1 + 0.02 * Math.Sin(TAU * 9 * t)), ASR(0.02, 0.02, 1.3)), Mix(Osc(0.3, W.Sin, Sw(120, 40, 0.1), E(10)), LP(Noise(0.2, E(20), 6), 900))));
        A(MR, "Applause", 4, () => BP(Grains(4, 2600, 17, r => Noise(0.012 + r.NextDouble() * 0.01, E(160), r.Next()), t => t < 0.4 ? t / 0.4 : t > 2.8 ? Math.Max(0, (4 - t) / 1.2) : 1), 700, 5500));
        A(MR, "Small Clap", 1.6, () => BP(Grains(1.6, 60, 5, r => G(Clap(), 0.5 + r.NextDouble() * 0.5), t => Math.Max(0, 1 - t / 1.4)), 700, 5000));
        A(MR, "Heartbeat", 2.6, () => Rep(Cat(Kick(90, 45, 14, 0.18), Sil(0.05), G(Kick(80, 40, 16, 0.2), 0.75), Sil(0.45)), 3));
        A(MR, "Tick Tock", 2, () => Rep(Cat(Beep(2200, 0.012), Sil(0.488), Beep(1700, 0.012), Sil(0.488)), 2));
        A(MR, "Bonk", 0.3, () => Mix(Osc(0.25, W.Sin, K(820), E(28)), Osc(0.25, W.Sin, K(410), E(22)), G(Noise(0.01, E(300)), 0.4)));
        A(MR, "Pop", 0.15, () => Osc(0.12, W.Sin, Sw(300, 1500, 0.04), E(35)));
        A(MR, "Bubble", 0.2, () => Osc(0.18, W.Sin, Sw(350, 1400, 0.16), AD(0.02, 0.18)));
        A(MR, "Bubbles", 2.5, () => Grains(2.5, 28, 3, r => { double f = 300 + r.NextDouble() * 900, d = 0.08 + r.NextDouble() * 0.1; return G(Osc(d, W.Sin, Sw(f, f * 3, d), AD(0.02, d)), 0.4); }));
        A(MR, "Zip", 0.3, () => Crush(Osc(0.28, W.Saw, Sw(200, 3200, 0.25), AD(0.02, 0.28)), 6, 8));
        A(MR, "Splat", 0.4, () => LP(Mix(Noise(0.35, E(14), 44), Osc(0.2, W.Sin, Sw(200, 60, 0.1), E(15))), 1400));
        A(MR, "Twinkle", 2, () => Rv(Grains(1.4, 22, 9, r => G(BellI(2000 + r.Next(8) * 330, 0.6), 0.3)), 0.25, 0.7, 0.6));
        A(MR, "Magic Wand", 2, () => Rv(Mix(Arp(W.Sin, 0.06, 2.5, 84, 88, 91, 96, 100, 103, 108), G(Grains(1.2, 30, 2, r => G(BellI(3000 + r.NextDouble() * 4000, 0.3), 0.12)), 1)), 0.3, 0.7, 0.8));
        A(MR, "Angel Choir", 4, () => Rv(Svf(Pad(new[] { M(60), M(64), M(67), M(72), M(76) }, 3.2), K(1400), 0.5, 1), 0.6, 0.95, 1.5));
        A(MR, "Horror Sting", 3, () => Rv(Mix(Pad(new[] { M(60), M(61), M(66), M(71), M(72) }, 1.6), G(Kick(100, 30, 3, 1.2), 1.2)), 0.5, 0.95, 1.4));
        A(MR, "Jump Scare", 1.6, () => Rv(Drive(Mix(Osc(1, W.Saw, K(M(73)), E(2)), Osc(1, W.Saw, K(M(74)), E(2)), Osc(1, W.Saw, K(M(79)), E(2)), Osc(1, W.Saw, K(M(80)), E(2)), HP(Noise(1, E(3), 5), 2000)), 2), 0.3, 0.8, 0.6));
        A(MR, "Suspense", 4, () => Rv(Mix(Osc(3.5, W.Saw, K(M(38)), t => ASR(1.5, 0.5, 3.5)(t) * (0.6 + 0.4 * Math.Sin(TAU * 13 * t))), Osc(3.5, W.Saw, K(M(39)), t => 0.6 * ASR(2.5, 0.5, 3.5)(t) * (0.6 + 0.4 * Math.Sin(TAU * 14 * t)))), 0.4, 0.9, 0.8));
        A(MR, "Party Horn", 1.1, () => LP(Osc(1.0, W.Saw, t => (t < 0.5 ? 320 + 300 * t : 470 - 500 * (t - 0.5)) * (1 + 0.03 * Math.Sin(TAU * 40 * t)), ASR(0.02, 0.1, 1.0)), 3000));
        A(MR, "Victory Fanfare", 2.6, () => Rv(At(0, (0, Brass(M(67), 0.16)), (0.18, Brass(M(72), 0.16)), (0.36, Brass(M(76), 0.16)), (0.54, Mix(Brass(M(79), 0.5), Brass(M(72), 0.5))), (1.1, Brass(M(76), 0.2)), (1.32, Mix(Brass(M(79), 1.2), Brass(M(72), 1.2), Brass(M(64), 1.2), Brass(M(48), 1.2)))), 0.25, 0.7, 0.7));
        A(MR, "Fail", 1.2, () => Cat(Osc(0.25, W.Sq, K(M(58)), K(0.6)), Osc(0.25, W.Sq, K(M(57)), K(0.6)), Osc(0.6, W.Sq, Vib(M(56), 0.02, 6), E(2))));
        A(MR, "Rimshot Joke", 1.6, () => At(1.6, (0, Tom(110)), (0.17, Tom(110)), (0.42, Mix(Kick(), Crash(1.1)))));
        A(MR, "Drum Roll", 3.2, () => { var parts = new List<(double, float[])>(); double t = 0; int i = 0; while (t < 2.3) { parts.Add((t, G(Snare(210, 22, 0.12), 0.5 + 0.5 * t / 2.3 + (i % 2) * 0.1))); t += 0.055 - 0.012 * t / 2.3; i++; } parts.Add((2.32, Mix(Kick(), Crash(0.9)))); return At(0, parts.ToArray()); });
        A(MR, "Whip Crack", 0.4, () => Mix(Svf(Noise(0.25, AD(0.15, 0.25), 3), Sw(400, 6000, 0.16), 0.4, 1), At(0, (0.16, HP(Noise(0.2, E(40), 9), 2500)))));
        A(MR, "Bang", 0.9, () => Rv(Mix(LP(Noise(0.4, E(16), 2), 3000), Osc(0.3, W.Sin, Sw(160, 45, 0.1), E(10))), 0.25, 0.7, 0.5));
        A(MR, "Punch", 0.3, () => Mix(LP(Noise(0.12, E(35), 12), 1800), Osc(0.2, W.Sin, Sw(180, 60, 0.07), E(18))));
        A(MR, "Slap", 0.25, () => BP(Noise(0.2, E(30), 15), 1200, 6000));
        A(MR, "Kiss Pop", 0.2, () => Osc(0.14, W.Sin, Sw(1800, 3600, 0.1), AD(0.06, 0.14)));
        A(MR, "Squeak", 0.3, () => Osc(0.28, W.Sin, t => 1800 + 1200 * Math.Sin(Math.PI * t / 0.28), AD(0.05, 0.28)));
        A(MR, "Womp Womp", 1.2, () => Cat(tbn(180, 0.4), Sil(0.05), LP(Osc(0.7, W.Saw, Sw(170, 120, 0.6), ASR(0.03, 0.2, 0.7)), 800)));
        A(MR, "Error Chord", 0.9, () => Mix(Osc(0.9, W.Saw, K(M(55)), E(4)), Osc(0.9, W.Saw, K(M(56)), E(4)), Osc(0.9, W.Saw, K(M(62)), E(4))));
        A(MR, "Trumpet Fail", 1.4, () => LP(Osc(1.3, W.Saw, t => 440 * Math.Pow(0.55, t * t) * (1 + 0.04 * Math.Sin(TAU * (6 + 8 * t) * t)), ASR(0.02, 0.2, 1.3)), 2200));
        A(MR, "Cash Register", 1.2, () => At(0, (0, BellI(2400, 0.5)), (0.09, BellI(3100, 0.9)), (0.02, G(HP(Noise(0.15, E(25), 7), 3000), 0.5))));
        A(MR, "Countdown 3-2-1-Go", 3.8, () => Cat(Beep(440, 0.25), Sil(0.75), Beep(440, 0.25), Sil(0.75), Beep(440, 0.25), Sil(0.75), Beep(880, 0.8)));

        // ===== 8-Bit =====
        const string B8 = "8-Bit";
        for (int i = 1; i <= 4; i++) { double k = Math.Pow(2, (i - 1) * 3 / 12.0); A(B8, "Coin " + i, 0.45, () => Cat(Osc(0.07, W.Sq, K(988 * k), K(0.6)), Osc(0.36, W.Sq, K(1319 * k), E(7)))); }
        for (int i = 1; i <= 4; i++) { double k = 0.7 + 0.25 * i; int n = i; A(B8, "Jump " + i, 0.28, () => Osc(0.26, n % 2 == 0 ? W.P25 : W.Sq, Sw(180 * k, 720 * k, 0.22), AD(0.01, 0.26))); }
        for (int i = 1; i <= 6; i++) { int n = i; A(B8, "Laser " + i, 0.16 + 0.04 * i, () => Osc(0.14 + 0.04 * n, n % 2 == 0 ? W.Saw : W.Sq, Sw(2200 - 250 * n, 120 + 40 * n, 0.14 + 0.04 * n), E(9 - n))); }
        int[][] ups = { new[] { 60, 64, 67, 72, 76, 79, 84 }, new[] { 57, 60, 64, 69, 72, 76, 81, 84 }, new[] { 60, 62, 64, 65, 67, 69, 71, 72, 74, 76 }, new[] { 48, 60, 55, 67, 60, 72, 67, 79 } };
        for (int i = 1; i <= 4; i++) { var u = ups[i - 1]; A(B8, "Power Up " + i, u.Length * 0.055 + 0.2, () => Arp(W.Sq, 0.055, 9, u)); }
        for (int i = 1; i <= 3; i++) { var u = Enumerable.Reverse(ups[i - 1]).ToArray(); A(B8, "Power Down " + i, u.Length * 0.06 + 0.2, () => Arp(W.P25, 0.06, 9, u)); }
        for (int i = 1; i <= 4; i++) { int n = i; A(B8, "Explosion " + i, 0.35 + 0.3 * i, () => Crush(Svf(Noise(0.35 + 0.3 * n, E(5.0 / n), n), Sw(4000, 100, 0.3 + 0.3 * n), 0.7, 0), 4 + 2 * n, 8)); }
        for (int i = 1; i <= 4; i++) { int n = i; A(B8, "Hit " + i, 0.18, () => Mix(Crush(Noise(0.06 + 0.02 * n, E(40), n * 3), 6, 6), Osc(0.16, W.Sq, Sw(260 + 60 * n, 55, 0.1), E(16)))); }
        for (int i = 1; i <= 6; i++) { double f = 330 * Math.Pow(2, (i - 1) / 3.0); A(B8, "Blip " + i, 0.07, () => Osc(0.06, W.Sq, K(f), K(0.6))); }
        A(B8, "Game Over", 2.2, () => Cat(Osc(0.3, W.Sq, K(M(72)), K(.6)), Osc(0.3, W.Sq, K(M(67)), K(.6)), Osc(0.3, W.Sq, K(M(64)), K(.6)), Osc(1.2, W.Sq, Vib(M(60), 0.015, 6), E(1.6))));
        A(B8, "Level Up", 0.9, () => Arp(W.Sq, 0.07, 5, 72, 76, 79, 84, 88, 91));
        A(B8, "Extra Life", 0.8, () => Arp(W.P25, 0.085, 6, 84, 88, 91, 88, 96));
        A(B8, "Level Complete", 1.8, () => Arp(W.Sq, 0.11, 2.2, 60, 64, 67, 72, 76, 79, 84, 88));
        A(B8, "Pause", 0.4, () => Cat(Osc(0.08, W.Sq, K(M(76)), K(.6)), Osc(0.08, W.Sq, K(M(72)), K(.6)), Osc(0.08, W.Sq, K(M(76)), K(.6)), Osc(0.14, W.Sq, K(M(72)), E(8))));
        A(B8, "Select", 0.12, () => Cat(Osc(0.04, W.Sq, K(660), K(.6)), Osc(0.07, W.Sq, K(990), E(12))));
        A(B8, "Start", 0.6, () => Arp(W.Sq, 0.06, 6, 67, 72, 76, 79, 84));
        A(B8, "Menu Move", 0.06, () => Osc(0.05, W.Tri, K(880), K(.8)));
        A(B8, "Text Blip", 0.04, () => Osc(0.035, W.Sq, K(520), K(.5)));
        A(B8, "Error", 0.35, () => Cat(Osc(0.12, W.Sq, K(160), K(.6)), Sil(0.04), Osc(0.16, W.Sq, K(120), K(.6))));
        A(B8, "Shoot", 0.2, () => Mix(Osc(0.18, W.Sq, Sw(900, 200, 0.15), E(14)), Crush(Noise(0.05, E(50), 2), 5, 6)));
        A(B8, "Pickup", 0.2, () => Cat(Osc(0.05, W.Sq, K(M(79)), K(.6)), Osc(0.05, W.Sq, K(M(84)), K(.6)), Osc(0.1, W.Sq, K(M(91)), E(12))));
        A(B8, "Door", 0.5, () => Crush(Noise(0.45, AD(0.2, 0.45), 6), 30, 5));
        A(B8, "Stairs", 0.7, () => Rep(Cat(Osc(0.05, W.Sq, K(220), K(.6)), Sil(0.03), Osc(0.05, W.Sq, K(330), K(.6)), Sil(0.03)), 4));
        A(B8, "Teleport", 0.7, () => Osc(0.7, W.Sq, t => 300 + 2000 * Math.Abs(Math.Sin(TAU * (3 + 14 * t) * t)), AD(0.05, 0.7)));
        A(B8, "Low Health", 1.2, () => Rep(Cat(Osc(0.1, W.Sq, K(880), K(.6)), Sil(0.2)), 4));
        A(B8, "Victory Jingle", 1.9, () => Tune(200, "C5:.5 E5:.5 G5:.5 C6:1 G5:.5 C6:3", W.Sq));
        A(B8, "Defeat Jingle", 1.9, () => Tune(140, "E5:.5 D#5:.5 D5:.5 C#5:.5 C5:2", W.Sq));
        A(B8, "Secret Found", 1.2, () => Arp(W.Sq, 0.1, 4, 79, 78, 75, 69, 68, 76, 80, 84));
        A(B8, "Charge Up", 1.2, () => Osc(1.2, W.Sq, t => 200 * Math.Pow(8, t / 1.2) * (1 + 0.1 * Math.Sign(Math.Sin(TAU * 30 * t))), AD(1.1, 1.2)));
        A(B8, "Bounce", 0.2, () => Osc(0.18, W.Sq, t => 200 + 600 * Math.Sin(Math.PI * t / 0.18), K(.6)));
        A(B8, "Splash", 0.5, () => Crush(Svf(Noise(0.5, E(7), 4), Sw(800, 5000, 0.3), 0.5, 1), 8, 7));
        A(B8, "Engine Loop", 2, () => Crush(Osc(2, W.Saw, t => 60 + 20 * Math.Sin(TAU * 1.5 * t), ASR(0.05, 0.05, 2)), 10, 6));

        // ===== Drums & Beats =====
        const string DR = "Drums & Beats";
        A(DR, "Kick", 0.4, () => Kick());
        A(DR, "Kick Tight", 0.25, () => Kick(170, 55, 16, 0.25));
        A(DR, "Kick Deep", 0.6, () => Kick(120, 36, 6, 0.6));
        A(DR, "Kick Punchy", 0.35, () => Drive(Kick(220, 50, 10, 0.35), 2.5));
        A(DR, "Kick Distorted", 0.5, () => Drive(Kick(160, 42, 7, 0.5), 6));
        A(DR, "808 Sub", 1.4, () => Drive(Osc(1.4, W.Sin, Sw(120, 42, 0.09), E(2.3)), 2.2));
        foreach (var (nm, mi) in new[] { ("C", 36), ("D", 38), ("E", 40), ("F", 41), ("G", 43), ("A", 45) }) A(DR, "808 Bass " + nm, 1.2, () => Drive(Osc(1.2, W.Sin, t => M(mi) * (1 + 1.2 * Math.Exp(-t * 40)), E(2.4)), 2.5));
        A(DR, "Snare", 0.25, () => Snare());
        A(DR, "Snare Tight", 0.16, () => Snare(240, 26, 0.16));
        A(DR, "Snare Fat", 0.35, () => Drive(Snare(170, 10, 0.35), 2));
        A(DR, "Snare Electro", 0.3, () => Mix(Snare(220, 16, 0.3), Osc(0.1, W.Sq, Sw(900, 300, 0.05), E(40))));
        A(DR, "Clap", 0.3, () => Clap());
        A(DR, "Clap Reverb", 1.2, () => Rv(Clap(), 0.4, 0.6, 0.9));
        A(DR, "Hi-Hat Closed", 0.07, () => HatC());
        A(DR, "Hi-Hat Open", 0.4, () => HatO());
        A(DR, "Tom High", 0.45, () => Tom(220));
        A(DR, "Tom Mid", 0.45, () => Tom(160));
        A(DR, "Tom Low", 0.45, () => Tom(105));
        A(DR, "Rim", 0.08, () => Mix(Osc(0.03, W.Sq, K(1700), E(120)), HP(Noise(0.03, E(150)), 3000)));
        A(DR, "Cowbell", 0.4, () => BP(Mix(Osc(0.4, W.Sq, K(540), E(9)), Osc(0.4, W.Sq, K(800), E(9))), 400, 4000));
        A(DR, "Crash", 1.8, () => Crash());
        A(DR, "Ride", 1.0, () => Mix(G(HP(Noise(1, E(4), 33), 6000), 0.5), G(Osc(1, W.Sq, K(5200), E(5)), 0.1), G(Osc(1, W.Sq, K(7300), E(5)), 0.1)));
        A(DR, "Shaker", 0.15, () => HP(Noise(0.14, AD(0.05, 0.14), 6), 5000));
        A(DR, "Tambourine", 0.35, () => Mix(HP(Noise(0.3, E(12), 8), 6000), G(Osc(0.3, W.Sq, K(8200), E(14)), 0.1)));
        A(DR, "Woodblock", 0.15, () => Mix(Osc(0.13, W.Sin, K(1100), E(40)), Osc(0.13, W.Sin, K(1650), E(55))));
        A(DR, "Conga", 0.35, () => Osc(0.35, W.Sin, Sw(330, 260, 0.03), E(11)));
        A(DR, "Sub Drop", 1.6, () => Osc(1.6, W.Sin, Sw(95, 26, 1.3), E(1.4)));
        A(DR, "Reverse Cymbal", 1.5, () => { var c = Crash(1.5); Array.Reverse(c); return c; });
        A(DR, "Beat: Boom Bap 90", 5.4, () => Beat(90, 2, "x.....x...x.....", "....x.......x...", "x.x.x.x.x.x.x.x."));
        A(DR, "Beat: House 124", 3.9, () => Beat(124, 2, "x...x...x...x...", "....x.......x...", "..x...x...x...x.", "..x...x...x...x."));
        A(DR, "Beat: Trap 140", 6.9, () => Beat(140, 4, "x.........x.....", "........x.......", "x.x.x.x.x.xxx.x.", null, "x.........x....."));
        A(DR, "Beat: Breakbeat 130", 3.7, () => Beat(130, 2, "x.x.......x.....", "....x..x.x..x...", "x.x.x.x.x.x.x.x."));
        A(DR, "Beat: Rock 110", 4.4, () => Beat(110, 2, "x.......x.x.....", "....x.......x...", "x.x.x.x.x.x.x.x.", "x..............."));
        A(DR, "Beat: Drum & Bass 174", 2.8, () => Beat(174, 2, "x.........x.....", "....x.......x...", "x.x.x.x.x.x.x.x."));
        A(DR, "Fill: Toms", 0.9, () => At(0, (0, Tom(220)), (0.11, Tom(220)), (0.22, Tom(160)), (0.33, Tom(160)), (0.44, Tom(105)), (0.55, Mix(Kick(), Crash(0.8)))));

        // ===== Signals & Alarms =====
        const string SG = "Signals & Alarms";
        A(SG, "Alarm Clock", 2.4, () => Rep(Cat(Rep(Cat(Beep(2050, 0.07, W.Sq), Sil(0.07)), 4), Sil(0.25)), 3));
        A(SG, "Police Siren", 3.2, () => LP(Osc(3.2, W.Saw, t => 950 + 450 * Math.Sin(TAU * 0.62 * t - 1.57), ASR(0.05, 0.1, 3.2)), 3500));
        A(SG, "Ambulance Siren", 3.2, () => LP(Osc(3.2, W.Sq, t => ((int)(t / 0.4) % 2 == 0) ? 650 : 870, ASR(0.02, 0.05, 3.2)), 3000));
        A(SG, "Yelp Siren", 2.4, () => LP(Osc(2.4, W.Saw, t => 700 + 800 * ((t * 3.3) % 1.0), ASR(0.02, 0.05, 2.4)), 3500));
        A(SG, "Air Raid Siren", 5, () => LP(Mix(Osc(5, W.Saw, t => 180 + 320 * Math.Sin(Math.PI * Math.Min(1, t / 4.6)), ASR(0.3, 0.6, 5)), Osc(5, W.Saw, t => 1.2 * (180 + 320 * Math.Sin(Math.PI * Math.Min(1, t / 4.6))), ASR(0.3, 0.6, 5))), 2500));
        A(SG, "Red Alert", 2.7, () => Rep(Cat(LP(Osc(0.6, W.Saw, Sw(420, 900, 0.55), ASR(0.02, 0.05, 0.6)), 3000), Sil(0.3)), 3));
        A(SG, "Ship Horn", 2.4, () => LP(Mix(Osc(2.4, W.Saw, K(87), ASR(0.15, 0.4, 2.4)), Osc(2.4, W.Saw, K(130.5), ASR(0.15, 0.4, 2.4)), Osc(2.4, W.Sq, K(43.6), ASR(0.15, 0.4, 2.4))), 900));
        A(SG, "Train Horn", 2.2, () => LP(Mix(Osc(2.2, W.Saw, K(311), ASR(0.05, 0.3, 2.2)), Osc(2.2, W.Saw, K(370), ASR(0.05, 0.3, 2.2)), Osc(2.2, W.Saw, K(466), ASR(0.05, 0.3, 2.2)), Osc(2.2, W.Saw, K(622), ASR(0.05, 0.3, 2.2))), 2600));
        A(SG, "Car Horn", 0.6, () => LP(Mix(Osc(0.55, W.Sq, K(420), ASR(0.01, 0.03, 0.55)), Osc(0.55, W.Sq, K(505), ASR(0.01, 0.03, 0.55))), 3500));
        A(SG, "Car Horn x2", 0.9, () => Rep(Cat(LP(Mix(Osc(0.25, W.Sq, K(420), ASR(0.01, 0.03, 0.25)), Osc(0.25, W.Sq, K(505), ASR(0.01, 0.03, 0.25))), 3500), Sil(0.12)), 2));
        A(SG, "Clown Horn", 0.5, () => LP(Osc(0.4, W.Saw, t => 330 * (1 + 0.5 * Math.Exp(-t * 25)) * (t > 0.2 ? 0.8 : 1), ASR(0.01, 0.1, 0.4)), 2500));
        A(SG, "Bike Bell", 1.2, () => At(0, (0, BellI(2600, 0.5)), (0.16, BellI(2600, 1.0))));
        A(SG, "Doorbell", 2, () => At(0, (0, BellI(M(76), 1.2)), (0.5, BellI(M(72), 1.5))));
        A(SG, "Church Bell", 4.5, () => Rv(Mix(BellI(196, 4), G(BellI(98, 4), 0.6)), 0.25, 0.85, 0.5));
        A(SG, "Gong", 6, () => Rv(Mix(Osc(5, W.Sin, K(82), E(0.7)), Osc(5, W.Sin, K(131), E(0.9)), Osc(5, W.Sin, K(203), E(1.2)), Osc(5, W.Sin, K(317), E(1.6)), Osc(5, W.Sin, K(489), E(2.2)), G(LP(Noise(0.4, E(10)), 2000), 0.4)), 0.3, 0.9, 1));
        A(SG, "School Bell", 2, () => Mix(Osc(2, W.Sq, K(2100), t => ASR(0.01, 0.1, 2)(t) * (0.5 + 0.5 * Math.Sign(Math.Sin(TAU * 28 * t)))), G(BellI(2100, 2), 0.5)));
        A(SG, "Phone Ring", 3, () => Rep(Cat(Osc(1, W.Sin, K(440), t => ASR(0.01, 0.01, 1)(t) * (0.5 + 0.5 * Math.Sin(TAU * 20 * t))), Sil(0.5)), 2));
        A(SG, "Retro Phone Ring", 2.6, () => Rep(Cat(Mix(Osc(0.9, W.Sq, K(1400), t => 0.5 + 0.5 * Math.Sign(Math.Sin(TAU * 25 * t))), G(BellI(2800, 0.9), 0.8)), Sil(0.4)), 2));
        A(SG, "Dial Tone", 2, () => Dtmf(350, 440, 2));
        A(SG, "Busy Tone", 2, () => Rep(Cat(Dtmf(480, 620, 0.5), Sil(0.5)), 2));
        A(SG, "Ringback Tone", 3, () => Cat(Dtmf(440, 480, 2), Sil(1)));
        double[] rowF = { 697, 770, 852, 941 }, colF = { 1209, 1336, 1477 }; string keys = "123456789*0#";
        for (int i = 0; i < 12; i++) { int ii = i; A(SG, "DTMF " + keys[i], 0.25, () => Dtmf(rowF[ii / 3], colF[ii % 3])); }
        A(SG, "Dial-Up Handshake", 4, () => { var r = new Random(56); var p = new List<float[]> { Dtmf(350, 440, 0.4), Beep(2100, 0.5), Sil(0.05) }; for (int i = 0; i < 28; i++) { double f = 600 + r.Next(10) * 220; p.Add(Osc(0.03 + r.NextDouble() * 0.05, W.Sin, K(f), K(.7))); } p.Add(G(Mix(BP(Noise(1.4, K(1), 3), 900, 2600), Osc(1.4, W.Sin, K(1800), K(.3))), 0.8)); return Cat(p.ToArray()); });
        A(SG, "Morse SOS", 3, () => { float[] dot = Cat(Beep(700, 0.08), Sil(0.08)), dash = Cat(Beep(700, 0.24), Sil(0.08)); return Cat(dot, dot, dot, Sil(0.16), dash, dash, dash, Sil(0.16), dot, dot, dot); });
        A(SG, "Sonar Ping", 2.6, () => Ech(Osc(0.6, W.Sin, Sw(1500, 1380, 0.5), E(6)), 0.33, 0.5, 0.5, 2));
        A(SG, "Radar Blip", 1, () => Ech(Osc(0.07, W.Sin, K(2400), E(30)), 0.12, 0.4, 0.5, 0.9));
        A(SG, "Timer Beep", 1.3, () => Rep(Cat(Beep(2700, 0.09), Sil(0.09)), 3));
        A(SG, "Microwave Done", 2.6, () => Rep(Cat(Beep(2000, 0.5), Sil(0.35)), 3));
        A(SG, "Truck Reverse", 3, () => Rep(Cat(Beep(1250, 0.4, W.Sq), Sil(0.4)), 4));
        A(SG, "Smoke Alarm", 2.2, () => Rep(Cat(Beep(3200, 0.5, W.Sq), Sil(0.2)), 3));
        A(SG, "Heart Monitor", 3, () => Rep(Cat(Beep(1000, 0.09), Sil(0.66)), 4));
        A(SG, "Flatline", 3, () => Beep(1000, 3));
        A(SG, "Elevator Ding", 1.6, () => BellI(1320, 1.6));
        A(SG, "Boxing Bell", 2.4, () => At(0, (0, BellI(1100, 1)), (0.22, BellI(1100, 1)), (0.44, BellI(1100, 1.9))));
        A(SG, "Game Show Buzzer", 1.2, () => LP(Mix(Osc(1.2, W.Saw, K(98), ASR(0.01, 0.05, 1.2)), Osc(1.2, W.Sq, K(147), ASR(0.01, 0.05, 1.2))), 2000));
        A(SG, "Whistle", 0.9, () => Mix(Osc(0.9, W.Sin, t => 2700 * (1 + 0.012 * Math.Sin(TAU * 38 * t)), ASR(0.02, 0.05, 0.9)), G(HP(Noise(0.9, ASR(0.02, 0.05, 0.9), 2), 4000), 0.1)));
        A(SG, "Referee Whistle x2", 1.1, () => Rep(Cat(Osc(0.35, W.Sin, t => 2700 * (1 + 0.012 * Math.Sin(TAU * 38 * t)), ASR(0.02, 0.05, 0.35)), Sil(0.15)), 2));

        // ===== Nature & Ambience =====
        const string NA = "Nature & Ambience";
        A(NA, "White Noise", 5, () => Noise(5, ASR(0.2, 0.3, 5), 1));
        A(NA, "Pink Noise", 5, () => LP(Noise(5, ASR(0.2, 0.3, 5), 2), 2500, 0.5));
        A(NA, "Brown Noise", 5, () => Brown(5, ASR(0.2, 0.3, 5), 3));
        A(NA, "Rain", 6, () => Mix(G(HP(Noise(6, ASR(0.5, 0.6, 6), 4), 2500), 0.35), Grains(6, 900, 5, r => G(HP(Noise(0.006, E(400), r.Next()), 1500), 0.3 + r.NextDouble() * 0.5))));
        A(NA, "Heavy Rain", 6, () => Mix(G(HP(Noise(6, ASR(0.5, 0.6, 6), 6), 1200), 0.6), G(Brown(6, ASR(0.5, 0.6, 6), 7), 0.4)));
        A(NA, "Wind", 6, () => Svf(Noise(6, t => ASR(0.8, 1, 6)(t) * (0.6 + 0.4 * Math.Sin(TAU * 0.21 * t)), 8), t => 500 + 350 * Math.Sin(TAU * 0.17 * t) + 200 * Math.Sin(TAU * 0.41 * t), 0.25, 1));
        A(NA, "Storm Wind", 6, () => Svf(Noise(6, t => ASR(0.8, 1, 6)(t) * (0.6 + 0.4 * Math.Sin(TAU * 0.35 * t)), 18), t => 900 + 600 * Math.Sin(TAU * 0.3 * t) + 300 * Math.Sin(TAU * 0.83 * t), 0.15, 1));
        A(NA, "Ocean Waves", 8, () => LP(Noise(8, t => 0.15 + 0.85 * Math.Pow(0.5 + 0.5 * Math.Sin(TAU * t / 4 - 1.2), 2), 9), 1400));
        A(NA, "Thunder", 5, () => Rv(Mix(Brown(4, t => Math.Exp(-t * 0.9) * (0.5 + 0.5 * Math.Abs(Math.Sin(TAU * 1.7 * t) * Math.Sin(TAU * 0.6 * t + 1))), 10), G(LP(Noise(0.3, E(12), 11), 1800), 0.9)), 0.4, 0.9, 1));
        A(NA, "Fire Crackle", 5, () => Mix(G(Brown(5, ASR(0.3, 0.5, 5), 12), 0.25), Grains(5, 220, 13, r => G(HP(Noise(0.004 + r.NextDouble() * 0.01, E(300), r.Next()), 1000), 0.2 + r.NextDouble() * 0.8))));
        A(NA, "Stream", 6, () => Mix(G(BP(Noise(6, ASR(0.5, 0.5, 6), 14), 800, 4000), 0.3), Grains(6, 500, 15, r => { double f = 500 + r.NextDouble() * 1800; return G(Osc(0.04, W.Sin, Sw(f, f * 1.6, 0.04), AD(0.01, 0.04)), 0.25); })));
        A(NA, "Birds", 5, () => Grains(5, 26, 16, r => { double f = 2200 + r.NextDouble() * 2500, d = 0.08 + r.NextDouble() * 0.12, w = 20 + r.NextDouble() * 30; return G(Osc(d, W.Sin, t => f * (1 + 0.15 * Math.Sin(TAU * w * t)), AD(0.02, d)), 0.5); }));
        A(NA, "Night Crickets", 6, () => Mix(Rep(Cat(Rep(Osc(0.04, W.Sin, K(4300), t => 0.5 + 0.5 * Math.Sin(TAU * 55 * t)), 3, 0.02), Sil(0.3)), 12), G(Brown(6, K(1), 17), 0.08)));
        A(NA, "Frog", 0.6, () => LP(Osc(0.5, W.Saw, Sw(180, 320, 0.4), t => ASR(0.02, 0.1, 0.5)(t) * (0.5 + 0.5 * Math.Sign(Math.Sin(TAU * 38 * t)))), 1500));
        A(NA, "Owl", 1.8, () => Cat(Osc(0.35, W.Sin, K(380), ASR(0.05, 0.1, 0.35)), Sil(0.15), Osc(1.1, W.Sin, t => 370 - 30 * t, t => ASR(0.05, 0.4, 1.1)(t) * (0.8 + 0.2 * Math.Sin(TAU * 7 * t)))));
        A(NA, "Wolf Howl", 3, () => Rv(LP(Osc(2.6, W.Saw, t => 300 + 320 * Math.Sin(Math.PI * Math.Min(1, t / 2.6)) * (1 + 0.01 * Math.Sin(TAU * 5 * t)), ASR(0.3, 0.8, 2.6)), 1100), 0.4, 0.9, 0.6));
        A(NA, "Mosquito", 2.5, () => Osc(2.5, W.Saw, t => 620 + 60 * Math.Sin(TAU * 0.9 * t) + 25 * Math.Sin(TAU * 7 * t), t => ASR(0.3, 0.5, 2.5)(t) * (0.6 + 0.4 * Math.Sin(TAU * 0.7 * t))));
        A(NA, "Bee Buzz", 2.5, () => LP(Osc(2.5, W.Saw, t => 210 + 25 * Math.Sin(TAU * 1.3 * t) + 12 * Math.Sin(TAU * 11 * t), ASR(0.2, 0.4, 2.5)), 2200));
        A(NA, "Water Drop", 0.4, () => Osc(0.3, W.Sin, t => 700 + 900 * Math.Exp(-t * 30), E(14)));
        A(NA, "Dripping", 3, () => Rv(Grains(2.4, 7, 21, r => Osc(0.3, W.Sin, t => 600 + r.Next(300) + 900 * Math.Exp(-t * 30), E(14))), 0.35, 0.8, 0.8));
        A(NA, "Cave Ambience", 6, () => Rv(Mix(G(Brown(5, ASR(1, 1, 5), 22), 0.6), Grains(5, 5, 23, r => G(Osc(0.3, W.Sin, t => 800 + 900 * Math.Exp(-t * 30), E(14)), 0.4))), 0.6, 0.95, 1));
        A(NA, "Crowd Murmur", 6, () => BP(Grains(6, 700, 24, r => { double f = 110 + r.NextDouble() * 180, d = 0.08 + r.NextDouble() * 0.2; return G(Osc(d, W.Saw, t => f * (1 + 0.1 * Math.Sin(TAU * 4 * t)), AD(d / 2, d)), 0.2); }), 250, 2400));
        A(NA, "City Hum", 6, () => Mix(G(Brown(6, ASR(0.6, 0.6, 6), 25), 0.7), G(LP(Osc(6, W.Saw, K(55), ASR(0.6, 0.6, 6)), 200), 0.3)));
        A(NA, "Spaceship Interior", 6, () => Mix(LP(Osc(6, W.Saw, K(48), ASR(0.6, 0.6, 6)), 180), G(Brown(6, ASR(0.6, 0.6, 6), 26), 0.3), G(Osc(6, W.Sin, K(1900), t => 0.03 * ASR(0.6, 0.6, 6)(t)), 1)));
        A(NA, "Vinyl Crackle", 5, () => Mix(G(HP(Noise(5, ASR(0.2, 0.3, 5), 27), 6000), 0.06), Grains(5, 120, 28, r => G(Noise(0.002, K(1), r.Next()), 0.3 + r.NextDouble() * 0.7))));
        A(NA, "Static", 3, () => Crush(Noise(3, t => ASR(0.05, 0.1, 3)(t) * (0.7 + 0.3 * Math.Sign(Math.Sin(TAU * 9 * t))), 29), 3, 16));

        // ===== Sci-Fi =====
        const string SF = "Sci-Fi";
        A(SF, "Warp Drive", 2.6, () => Rv(Mix(Osc(2.2, W.Saw, Sw(40, 2400, 2.0), AD(2.0, 2.2)), Svf(Noise(2.2, AD(2.0, 2.2), 2), Sw(200, 7000, 2.0), 0.3, 1)), 0.3, 0.8, 0.6));
        A(SF, "Hyperspace Jump", 2.2, () => Rv(Cat(Osc(1.1, W.Saw, Sw(60, 3000, 1.05), AD(1.0, 1.1)), Mix(Noise(0.5, E(7), 3), Osc(0.6, W.Sin, Sw(200, 30, 0.4), E(5)))), 0.35, 0.85, 0.6));
        A(SF, "Teleport", 1.4, () => Rv(Mix(Osc(1.1, W.Sin, t => 500 + 1800 * t + 300 * Math.Sin(TAU * 22 * t), AD(0.5, 1.1)), G(Grains(1.1, 40, 4, r => G(BellI(2500 + r.NextDouble() * 4000, 0.2), 0.2)), 1)), 0.3, 0.7, 0.5));
        for (int i = 1; i <= 4; i++) { int n = i; A(SF, "Phaser " + i, 0.5, () => Mix(Osc(0.45, n % 2 == 0 ? W.Saw : W.Sin, t => (1600 - 250 * n) * Math.Pow(0.2, t / 0.45) * (1 + 0.3 * Math.Sin(TAU * (30 + 15 * n) * t)), E(5)), G(HP(Noise(0.1, E(30), n), 3000), 0.3))); }
        for (int i = 1; i <= 3; i++) { int n = i; A(SF, "Plasma Shot " + i, 0.6, () => Rv(Drive(Osc(0.4, W.Saw, Sw(900 + 300 * n, 70, 0.3), E(7)), 3), 0.2, 0.6, 0.3)); }
        A(SF, "Force Field", 3, () => Mix(Osc(3, W.Saw, K(110), t => ASR(0.3, 0.4, 3)(t) * (0.7 + 0.3 * Math.Sin(TAU * 6 * t))), Osc(3, W.Saw, K(110.7), ASR(0.3, 0.4, 3)), G(Osc(3, W.Sin, K(880), t => ASR(0.3, 0.4, 3)(t) * (0.5 + 0.5 * Math.Sin(TAU * 3 * t))), 0.2)));
        for (int i = 1; i <= 6; i++) { int n = i; A(SF, "Robot Talk " + i, 1, () => RobotTalk(n * 13)); }
        A(SF, "Alien Signal", 3, () => Ech(Osc(2.2, W.Sin, t => 900 + 600 * Math.Sin(TAU * 3 * t) * Math.Sin(TAU * 0.7 * t), t => ASR(0.1, 0.3, 2.2)(t) * (0.5 + 0.5 * Math.Sin(TAU * 13 * t))), 0.19, 0.5, 0.4, 0.8));
        A(SF, "UFO", 3.5, () => Osc(3.5, W.Sin, t => 420 + 120 * Math.Sin(TAU * 7 * t) + 60 * Math.Sin(TAU * 0.4 * t), t => ASR(0.5, 0.8, 3.5)(t) * (0.7 + 0.3 * Math.Sin(TAU * 14 * t))));
        A(SF, "Reactor Power Up", 2.5, () => Mix(Osc(2.5, W.Saw, Sw(30, 420, 2.3), AD(2.3, 2.5)), Osc(2.5, W.Sq, Sw(60, 840, 2.3), t => 0.3 * AD(2.3, 2.5)(t))));
        A(SF, "Reactor Power Down", 2.5, () => Mix(Osc(2.5, W.Saw, Sw(420, 25, 2.4), E(0.9)), Osc(2.5, W.Sq, Sw(840, 50, 2.4), t => 0.3 * Math.Exp(-t * 0.9))));
        A(SF, "Shield Hit", 0.9, () => Rv(Mix(Osc(0.5, W.Sin, t => 300 + 200 * Math.Sin(TAU * 40 * t), E(6)), G(BellI(900, 0.6), 0.6)), 0.3, 0.7, 0.4));
        A(SF, "Scanner", 2.4, () => Rep(Osc(0.6, W.Sin, t => 800 + 1400 * (t / 0.6), t => ASR(0.02, 0.05, 0.6)(t) * (0.6 + 0.4 * Math.Sin(TAU * 30 * t))), 4));
        for (int i = 1; i <= 4; i++) { int n = i; A(SF, "Glitch " + i, 0.6, () => Glitch(n * 5)); }
        A(SF, "Computer Thinking", 2.5, () => { var r = new Random(77); var p = new List<float[]>(); for (int i = 0; i < 34; i++) { p.Add(Beep(500 + r.Next(14) * 180, 0.045, W.Sq)); p.Add(Sil(0.025)); } return Cat(p.ToArray()); });
        A(SF, "Data Burst", 0.8, () => { var r = new Random(78); var p = new List<float[]>(); for (int i = 0; i < 40; i++) p.Add(Osc(0.018, W.Sq, K(800 + r.Next(20) * 260), K(.6))); return Cat(p.ToArray()); });
        A(SF, "Energy Blade On", 1.4, () => Mix(Osc(1.4, W.Saw, Sw(40, 96, 0.35), ASR(0.05, 0.3, 1.4)), Osc(1.4, W.Saw, Sw(40.5, 97.5, 0.35), ASR(0.05, 0.3, 1.4)), G(Svf(Noise(0.5, E(6), 5), Sw(300, 4000, 0.3), 0.3, 1), 0.6)));
        A(SF, "Energy Blade Hum", 3, () => LP(Mix(Osc(3, W.Saw, K(96), t => ASR(0.2, 0.3, 3)(t) * (0.8 + 0.2 * Math.Sin(TAU * 9 * t))), Osc(3, W.Saw, K(97.3), ASR(0.2, 0.3, 3)), Osc(3, W.Sq, K(48), t => 0.5 * ASR(0.2, 0.3, 3)(t))), 1500));
        A(SF, "Energy Blade Swing", 0.6, () => LP(Mix(Osc(0.6, W.Saw, t => 96 + 80 * Math.Sin(Math.PI * t / 0.6), AD(0.3, 0.6)), Osc(0.6, W.Saw, t => 97.3 + 82 * Math.Sin(Math.PI * t / 0.6), AD(0.3, 0.6))), 2400));
        A(SF, "Tractor Beam", 3, () => Mix(Osc(3, W.Sin, t => 200 + 30 * Math.Sin(TAU * 5 * t), ASR(0.3, 0.4, 3)), Osc(3, W.Sin, t => 600 + 300 * ((t * 2) % 1.0), t => 0.4 * ASR(0.3, 0.4, 3)(t))));
        for (int i = 1; i <= 3; i++) { int n = i; A(SF, "Comms Beep " + i, 0.3, () => Cat(Beep(1200 + 300 * n, 0.07), Sil(0.03), Beep(1600 + 300 * n, 0.12))); }
        A(SF, "Airlock Hiss", 1.8, () => Svf(Noise(1.8, t => Math.Min(1, t / 0.05) * Math.Exp(-t * 2), 6), Sw(7000, 1500, 1.5), 0.5, 2));
        A(SF, "Cryo Freeze", 2, () => Rv(Mix(HP(Noise(1.5, E(2.5), 7), 4000), Grains(1.5, 50, 8, r => G(BellI(3000 + r.NextDouble() * 5000, 0.2), 0.15))), 0.3, 0.7, 0.5));
        A(SF, "Stun Ray", 0.8, () => Osc(0.8, W.Sq, t => 1200 + 800 * Math.Sign(Math.Sin(TAU * 25 * t)), E(3.5)));
        A(SF, "Black Hole", 4, () => Rv(Mix(Osc(3.5, W.Sin, Sw(160, 22, 3.3), ASR(0.3, 0.6, 3.5)), G(Brown(3.5, ASR(1, 0.6, 3.5), 9), 0.5)), 0.5, 0.95, 0.5));
        A(SF, "Time Rewind", 1.6, () => Osc(1.6, W.Saw, t => 300 + 900 * Math.Abs(Math.Sin(TAU * (2 + 5 * t) * t)) , t => ASR(0.05, 0.2, 1.6)(t)));
        A(SF, "Drone Flyby", 2.4, () => LP(Osc(2.4, W.Saw, t => 180 * (1 + 0.12 * Math.Tanh((1.2 - t) * 4)), t => Math.Exp(-Math.Pow((t - 1.2) / 0.55, 2))), 2600));

        // ===== UI =====
        const string UI = "UI";
        for (int i = 1; i <= 4; i++) { int n = i; A(UI, "Click " + i, 0.03, () => Mix(Osc(0.02, W.Sin, K(1500 + 500 * n), E(220)), G(HP(Noise(0.006, E(600), n), 4000), 0.5))); }
        A(UI, "Tap", 0.05, () => Osc(0.04, W.Sin, Sw(700, 300, 0.03), E(90)));
        for (int i = 1; i <= 3; i++) { int n = i; A(UI, "Pop " + i, 0.1, () => Osc(0.09, W.Sin, Sw(250 + 150 * n, 1000 + 400 * n, 0.03), E(40))); }
        A(UI, "Toggle On", 0.16, () => Cat(Osc(0.05, W.Sin, K(700), E(30)), Osc(0.1, W.Sin, K(1050), E(25))));
        A(UI, "Toggle Off", 0.16, () => Cat(Osc(0.05, W.Sin, K(1050), E(30)), Osc(0.1, W.Sin, K(700), E(25))));
        A(UI, "Success", 0.8, () => At(0, (0, BellI(M(84), 0.4)), (0.1, BellI(M(88), 0.4)), (0.2, BellI(M(91), 0.6))));
        A(UI, "Error", 0.4, () => Cat(Osc(0.13, W.Tri, K(330), ASR(0.005, 0.02, 0.13)), Sil(0.04), Osc(0.2, W.Tri, K(247), ASR(0.005, 0.05, 0.2))));
        A(UI, "Warning", 0.5, () => Rep(Cat(Osc(0.12, W.Tri, K(880), ASR(0.005, 0.02, 0.12)), Sil(0.07)), 2));
        A(UI, "Message In", 0.4, () => At(0, (0, Osc(0.15, W.Sin, K(880), E(18))), (0.09, Osc(0.3, W.Sin, K(1320), E(12)))));
        A(UI, "Message Out", 0.3, () => Osc(0.25, W.Sin, Sw(500, 1300, 0.2), AD(0.02, 0.25)));
        int[][] nts = { new[] { 84, 91 }, new[] { 79, 84, 88 }, new[] { 88, 84 }, new[] { 76, 83, 88 }, new[] { 81, 85, 88, 93 }, new[] { 91, 86, 91 } };
        for (int i = 1; i <= 6; i++) { var u = nts[i - 1]; A(UI, "Notification " + i, 0.9, () => At(0, u.Select((m, k) => (k * 0.11, BellI(M(m), 0.7))).ToArray())); }
        A(UI, "Camera Shutter", 0.25, () => At(0, (0, HP(Noise(0.03, E(120), 4), 2000)), (0.07, HP(Noise(0.05, E(80), 5), 1500)), (0.07, Osc(0.03, W.Sin, K(180), E(80)))));
        A(UI, "Key Press", 0.06, () => Mix(HP(Noise(0.02, E(250), 6), 2500), Osc(0.04, W.Sin, K(260), E(120))));
        A(UI, "Typing", 1.6, () => Grains(1.5, 14, 7, r => Mix(HP(Noise(0.02, E(250), r.Next()), 2500), Osc(0.04, W.Sin, K(220 + r.Next(100)), E(120)))));
        A(UI, "Mouse Click", 0.08, () => At(0, (0, HP(Noise(0.008, E(500), 8), 3000)), (0.05, G(HP(Noise(0.008, E(500), 9), 3500), 0.7))));
        A(UI, "Swipe", 0.3, () => Svf(Noise(0.28, AD(0.14, 0.28), 10), Sw(800, 5000, 0.28), 0.4, 1));
        A(UI, "Hover", 0.05, () => Osc(0.04, W.Sin, K(1800), E(90)));
        A(UI, "Open", 0.3, () => Osc(0.28, W.Sin, Sw(400, 1100, 0.2), AD(0.03, 0.28)));
        A(UI, "Close", 0.3, () => Osc(0.28, W.Sin, Sw(1100, 400, 0.2), AD(0.03, 0.28)));
        A(UI, "Delete", 0.35, () => Mix(Osc(0.3, W.Sin, Sw(500, 120, 0.25), E(9)), G(LP(Noise(0.1, E(30), 11), 1500), 0.4)));
        A(UI, "Unlock", 0.5, () => At(0, (0, Osc(0.03, W.Sin, K(1800), E(120))), (0.12, BellI(1568, 0.3))));
        A(UI, "Lock", 0.3, () => At(0, (0, Osc(0.04, W.Sin, K(900), E(100))), (0.07, Osc(0.05, W.Sin, K(500), E(80)))));
        A(UI, "Level Fill", 0.9, () => Osc(0.9, W.Tri, Sw(300, 1400, 0.85), ASR(0.02, 0.05, 0.9)));
        A(UI, "Achievement", 1.4, () => Rv(At(0, (0, BellI(M(79), 0.4)), (0.09, BellI(M(84), 0.4)), (0.18, BellI(M(88), 0.4)), (0.27, BellI(M(91), 0.9))), 0.2, 0.6, 0.4));
        A(UI, "Call Join", 0.5, () => At(0, (0, Osc(0.14, W.Sin, K(M(72)), E(14))), (0.12, Osc(0.3, W.Sin, K(M(79)), E(9)))));
        A(UI, "Call Leave", 0.5, () => At(0, (0, Osc(0.14, W.Sin, K(M(79)), E(14))), (0.12, Osc(0.3, W.Sin, K(M(72)), E(9)))));
        A(UI, "Mute", 0.2, () => Osc(0.16, W.Sin, Sw(600, 380, 0.12), E(16)));
        A(UI, "Unmute", 0.2, () => Osc(0.16, W.Sin, Sw(380, 600, 0.12), E(16)));

        // ===== Risers & Impacts =====
        const string RI = "Risers & Impacts";
        foreach (var (nm, d) in new[] { ("Short", 1.0), ("Medium", 2.5), ("Long", 5.0) })
        {
            A(RI, "Riser " + nm, d, () => Mix(Svf(Noise(d, AD(d * 0.97, d), 3), Sw(300, 7000, d), 0.3, 1), G(Osc(d, W.Saw, Sw(110, 880, d), AD(d * 0.97, d)), 0.4)));
            A(RI, "Downer " + nm, d, () => Mix(Svf(Noise(d, E(2.5 / d), 3), Sw(7000, 200, d), 0.3, 1), G(Osc(d, W.Saw, Sw(880, 55, d), E(2.5 / d)), 0.4)));
        }
        A(RI, "Impact Low", 2.4, () => Rv(Mix(Drive(Osc(1.2, W.Sin, Sw(90, 30, 0.3), E(3)), 2), LP(Noise(0.3, E(14), 5), 600)), 0.4, 0.9, 1.2));
        A(RI, "Impact Mid", 2, () => Rv(Mix(Osc(0.8, W.Sin, Sw(200, 60, 0.2), E(5)), LP(Noise(0.3, E(14), 6), 2500)), 0.4, 0.85, 1.2));
        A(RI, "Impact High", 1.8, () => Rv(Mix(HP(Noise(0.4, E(10), 7), 2000), Osc(0.3, W.Sin, Sw(600, 150, 0.1), E(10))), 0.4, 0.8, 1.2));
        A(RI, "Cinematic Hit", 3.5, () => Rv(Mix(Drive(Osc(2, W.Sin, Sw(70, 28, 0.6), E(1.6)), 3), LP(Noise(0.5, E(8), 8), 1500), G(Brass(M(36), 1.5), 0.5), G(Brass(M(43), 1.5), 0.4)), 0.45, 0.95, 1.5));
        A(RI, "Braam", 3.5, () => Rv(Drive(Mix(Brass(M(34), 2.5), Brass(M(41), 2.5), Brass(M(22), 2.5), G(Osc(2.5, W.Sq, K(M(22)), ASR(0.05, 0.6, 2.5)), 0.6)), 2.2), 0.4, 0.9, 1));
        A(RI, "Sub Boom", 2.5, () => Osc(2.5, W.Sin, Sw(70, 24, 1.0), E(1.6)));
        for (int i = 1; i <= 4; i++) { int n = i; A(RI, "Whoosh " + i, 0.3 + 0.2 * i, () => { double d = 0.3 + 0.2 * n; return Svf(Noise(d, t => Math.Exp(-Math.Pow((t - d / 2) / (d / 4.5), 2)), n), t => 500 + 3500 * Math.Exp(-Math.Pow((t - d / 2) / (d / 3), 2)), 0.35, 1); }); }
        A(RI, "Stinger", 1.5, () => Rv(Mix(Brass(M(60), 0.5), Brass(M(66), 0.5), Brass(M(49), 0.5), HP(Noise(0.2, E(20), 9), 3000)), 0.35, 0.8, 1));
        A(RI, "Tension Drone", 6, () => Mix(LP(Osc(6, W.Saw, K(M(26)), ASR(1.5, 1.5, 6)), 400), LP(Osc(6, W.Saw, K(M(26) * 1.01), ASR(1.5, 1.5, 6)), 400), G(Osc(6, W.Sin, K(M(74)), t => ASR(3, 1.5, 6)(t) * (0.6 + 0.4 * Math.Sin(TAU * 5 * t))), 0.15)));
        A(RI, "Swell", 3, () => Rv(Pad(new[] { M(48), M(55), M(60), M(64) }, 2.6), 0.4, 0.85, 0.8));
        A(RI, "Bass Drop", 3, () => Cat(Mix(Svf(Noise(1.2, AD(1.15, 1.2), 3), Sw(300, 7000, 1.2), 0.3, 1), G(Osc(1.2, W.Saw, Sw(110, 1760, 1.2), AD(1.15, 1.2)), 0.4)), Sil(0.08), G(Drive(Osc(1.6, W.Sin, Sw(160, 32, 0.25), E(2)), 3), 1.6)));
        A(RI, "Tape Stop", 1.2, () => Osc(1.2, W.Saw, t => 440 * Math.Pow(Math.Max(0.02, 1 - t / 1.15), 1.5), t => Math.Max(0, 1 - t / 1.2)));
        A(RI, "Tape Start", 1, () => Osc(1.0, W.Saw, t => 440 * Math.Pow(Math.Min(1, t / 0.9 + 0.02), 1.5), ASR(0.1, 0.1, 1.0)));

        // ===== Tunes (8-bit renditions of public-domain melodies) =====
        const string TU = "Tunes";
        A(TU, "Ode to Joy", 16, () => Tune(120, "E4 E4 F4 G4 G4 F4 E4 D4 C4 C4 D4 E4 E4:1.5 D4:.5 D4:2 E4 E4 F4 G4 G4 F4 E4 D4 C4 C4 D4 E4 D4:1.5 C4:.5 C4:2"));
        A(TU, "Twinkle Twinkle", 8.8, () => Tune(110, "C4 C4 G4 G4 A4 A4 G4:2 F4 F4 E4 E4 D4 D4 C4:2"));
        A(TU, "Happy Birthday", 14, () => Tune(110, "G4:.75 G4:.25 A4 G4 C5 B4:2 G4:.75 G4:.25 A4 G4 D5 C5:2 G4:.75 G4:.25 G5 E5 C5 B4 A4 F5:.75 F5:.25 E5 C5 D5 C5:2"));
        A(TU, "Jingle Bells", 13.8, () => Tune(140, "E4 E4 E4:2 E4 E4 E4:2 E4 G4 C4:1.5 D4:.5 E4:4 F4 F4 F4:1.5 F4:.5 F4 E4 E4 E4:.5 E4:.5 E4 D4 D4 E4 D4:2 G4:2"));
        A(TU, "Fur Elise", 10, () => Tune(140, "E5:.5 D#5:.5 E5:.5 D#5:.5 E5:.5 B4:.5 D5:.5 C5:.5 A4:1 R:.5 C4:.5 E4:.5 A4:.5 B4:1 R:.5 E4:.5 G#4:.5 B4:.5 C5:1 R:.5 E4:.5 E5:.5 D#5:.5 E5:.5 D#5:.5 E5:.5 B4:.5 D5:.5 C5:.5 A4:1 R:.5 C4:.5 E4:.5 A4:.5 B4:1 R:.5 E4:.5 C5:.5 B4:.5 A4:2", W.Tri));
        A(TU, "Symphony No. 5", 6.3, () => Tune(108, "R:.5 G4:.5 G4:.5 G4:.5 D#4:3.5 R:.5 F4:.5 F4:.5 F4:.5 D4:3.5", W.Saw));
        A(TU, "Bridal Chorus", 5.4, () => Tune(90, "C4:1 F4:.75 F4:.25 F4:2 C4:1 G4:.75 E4:.25 F4:2"));
        A(TU, "Funeral March", 8, () => Tune(60, "A#3:1 A#3:.75 A#3:.25 A#3:1 C#4:.75 C4:.25 C4:.75 A#3:.25 A#3:.75 A3:.25 A#3:2", W.Saw));
        A(TU, "Korobeiniki", 13, () => Tune(150, "E5:1 B4:.5 C5:.5 D5:1 C5:.5 B4:.5 A4:1 A4:.5 C5:.5 E5:1 D5:.5 C5:.5 B4:1.5 C5:.5 D5:1 E5:1 C5:1 A4:1 A4:2 R:.5 D5:1 F5:.5 A5:1 G5:.5 F5:.5 E5:1.5 C5:.5 E5:1 D5:.5 C5:.5 B4:1 B4:.5 C5:.5 D5:1 E5:1 C5:1 A4:1 A4:2", W.Sq));
        A(TU, "Shave and a Haircut", 3, () => Tune(150, "C5:1 G4:.5 G4:.5 A4:1 G4:1 R:1 B4:1 C5:1"));
        A(TU, "Charge!", 1.9, () => Tune(180, "G4:.5 C5:.5 E5:.5 G5:1 E5:.5 G5:2", W.Saw));
        A(TU, "Hall of the Mountain King", 7, () => Tune(130, "B3:.5 C#4:.5 D4:.5 E4:.5 F#4:.5 D4:.5 F#4:1 F4:.5 C#4:.5 F4:1 E4:.5 C4:.5 E4:1 B3:.5 C#4:.5 D4:.5 E4:.5 F#4:.5 D4:.5 F#4:.5 B4:.5 A4:.5 F#4:.5 D4:.5 F#4:.5 A4:2", W.Sq));
        A(TU, "Toccata in D minor", 6.5, () => Rv(Tune(70, "A5:.25 G5:.25 A5:2 R:.5 G5:.25 F5:.25 E5:.25 D5:.25 C#5:1 D5:2", W.Saw), 0.4, 0.9, 1));
        A(TU, "Greensleeves", 14.5, () => Tune(100, "A4:1 C5:2 D5:1 E5:1.5 F5:.5 E5:1 D5:2 B4:1 G4:1.5 A4:.5 B4:1 C5:2 A4:1 A4:1.5 G#4:.5 A4:1 B4:2 G#4:1 E4:2", W.Tri));
        A(TU, "Taps", 20, () => Tune(60, "G4:.75 G4:.25 C5:3 G4:.75 C5:.25 E5:3 G4:.5 C5:.5 E5:1 G4:.5 C5:.5 E5:1 G4:.5 C5:.5 E5:3", W.Saw));
        A(TU, "Canon in D", 9.7, () => Tune(100, "F#5:2 E5:2 D5:2 C#5:2 B4:2 A4:2 B4:2 C#5:2", W.Tri));
        A(TU, "La Cucaracha", 7, () => Tune(140, "C4:.5 C4:.5 C4:.5 F4:1.5 A4:1 C4:.5 C4:.5 C4:.5 F4:1.5 A4:2 F4:1 F4:.5 E4:.5 E4:.5 D4:.5 D4:.5 C4:2"));
        A(TU, "Scale Up", 2.6, () => Tune(200, "C4:.5 D4:.5 E4:.5 F4:.5 G4:.5 A4:.5 B4:.5 C5:2"));
        A(TU, "Scale Down", 2.6, () => Tune(200, "C5:.5 B4:.5 A4:.5 G4:.5 F4:.5 E4:.5 D4:.5 C4:2"));

        // ===== Notes =====
        for (int m = 48; m <= 84; m++) { int mm = m; A("Notes: Piano", "Piano " + NoteName(m), 1.8, () => Piano(M(mm))); }
        for (int m = 48; m <= 84; m++) { int mm = m; A("Notes: Chiptune", "Chip " + NoteName(m), 0.45, () => Chip(M(mm))); }
        for (int m = 60; m <= 96; m++) { int mm = m; A("Notes: Bell", "Bell " + NoteName(m), 2.5, () => BellI(M(mm))); }
        for (int m = 24; m <= 48; m++) { int mm = m; A("Notes: Bass", "Bass " + NoteName(m), 0.9, () => Bass(M(mm))); }
        for (int r = 0; r < 12; r++)
        {
            int root = 48 + r;
            A("Chords", NN[r] + " Major", 3, () => Pad(new[] { M(root), M(root + 4), M(root + 7), M(root + 12) }));
            A("Chords", NN[r] + " Minor", 3, () => Pad(new[] { M(root), M(root + 3), M(root + 7), M(root + 12) }));
            A("Chords", NN[r] + " Seventh", 3, () => Pad(new[] { M(root), M(root + 4), M(root + 7), M(root + 10) }));
        }
        return L;
    }
}
