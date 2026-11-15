using NAudio.Dsp;
namespace VoxDeck.Audio;

public record FxDef(string Key, string Group, double Min, double Max, double Def, string Unit, double Step);

public static class FxDefs
{
    public static readonly FxDef[] All =
    {
        new("pitch","tone",-12,12,0,"st",0.5),
        new("bass","tone",-18,18,0,"dB",0.5),
        new("mid","tone",-18,18,0,"dB",0.5),
        new("treble","tone",-18,18,0,"dB",0.5),
        new("lowcut","tone",20,2000,20,"Hz",10),
        new("highcut","tone",500,20000,20000,"Hz",100),
        new("robot","mod",0,100,0,"%",1),
        new("robotfreq","mod",20,600,90,"Hz",5),
        new("mono","mod",0,100,0,"%",1),
        new("whisper","mod",0,100,0,"%",1),
        new("tremolo","mod",0,100,0,"%",1),
        new("tremrate","mod",0.5,20,6,"Hz",0.5),
        new("vibrato","mod",0,100,0,"%",1),
        new("vibrate","mod",0.5,12,5,"Hz",0.5),
        new("chorus","mod",0,100,0,"%",1),
        new("flanger","mod",0,100,0,"%",1),
        new("drive","dirt",0,100,0,"%",1),
        new("crush","dirt",0,100,0,"%",1),
        new("echo","space",0,100,0,"%",1),
        new("echotime","space",30,1000,280,"ms",10),
        new("echofb","space",0,90,35,"%",1),
        new("reverb","space",0,100,0,"%",1),
        new("room","space",0,100,50,"%",1),
        new("gate","dyn",-80,-20,-80,"dB",1),
        new("comp","dyn",0,100,0,"%",1),
        new("gain","dyn",-24,24,0,"dB",0.5),
    };
    static readonly Dictionary<string, FxDef> Map = All.ToDictionary(d => d.Key);
    public static double Default(string k) => Map.TryGetValue(k, out var d) ? d.Def : 0;
}

public class FxParams
{
    public Dictionary<string, double> V { get; set; } = new();
    public double this[string k]
    {
        get => V.TryGetValue(k, out var v) ? v : FxDefs.Default(k);
        set => V[k] = value;
    }
    public FxParams Clone() => new() { V = new Dictionary<string, double>(V) };
    public bool IsNeutral() => FxDefs.All.All(d => Math.Abs(this[d.Key] - d.Def) < 1e-6);
    public static FxParams Of(params (string k, double v)[] kv)
    {
        var p = new FxParams();
        foreach (var (k, v) in kv) p.V[k] = v;
        return p;
    }
}

public record FxPreset(string Id, string En, string Ru, string Cat, string Icon, FxParams P, bool Custom = false);

public static class FxPresets
{
    public static readonly string[] Cats = { "all", "clean", "human", "creature", "robot", "device", "space", "fun", "custom" };
    static FxPreset N(string id, string en, string ru, string cat, string icon, params (string, double)[] kv) => new(id, en, ru, cat, icon, FxParams.Of(kv));
    public static readonly List<FxPreset> Builtin = new()
    {
        N("none","Original","Оригинал","clean","Mic"),
        N("studio","Studio","Студия","clean","Sparkles",("lowcut",80),("comp",45),("treble",3),("gate",-55)),
        N("broadcast","Broadcast","Диктор","clean","Radio",("lowcut",70),("bass",5),("mid",-2),("treble",4),("comp",65),("gate",-52)),
        N("bassboost","Bass Boost","Бас-буст","clean","Speaker",("bass",11),("comp",30)),
        N("crisp","Crisp","Чёткий","clean","Zap",("lowcut",120),("treble",8),("comp",35)),
        N("warm","Warm","Тёплый","clean","Flame",("bass",4),("treble",-4),("highcut",9000),("comp",25)),
        N("noisegate","Noise Gate","Шумодав","clean","MicOff",("gate",-45),("lowcut",90)),

        N("deep","Deep Voice","Низкий голос","human","User",("pitch",-3.5),("bass",4)),
        N("deeper","Very Deep","Очень низкий","human","User",("pitch",-6),("bass",6),("comp",30)),
        N("higher","Higher Voice","Высокий голос","human","User",("pitch",3.5),("treble",2)),
        N("feminine","Feminine","Женственный","human","Heart",("pitch",4.5),("bass",-4),("treble",3),("lowcut",150)),
        N("masculine","Masculine","Мужественный","human","User",("pitch",-4.5),("bass",5),("treble",-2)),
        N("kid","Kid","Ребёнок","human","Baby",("pitch",6.5),("bass",-6),("lowcut",220)),
        N("oldman","Old Man","Старик","human","User",("pitch",-2),("vibrato",35),("vibrate",6.5),("tremolo",25),("tremrate",7),("highcut",5000)),
        N("announcer","Movie Trailer","Трейлер","human","Megaphone",("pitch",-5),("bass",8),("comp",70),("reverb",18),("room",60)),
        N("asmr","Soft ASMR","Мягкий ASMR","human","Ear",("whisper",35),("bass",3),("treble",5),("comp",60)),

        N("chipmunk","Chipmunk","Бурундук","creature","Sparkles",("pitch",10),("lowcut",200)),
        N("helium","Helium","Гелий","creature","Sparkles",("pitch",7.5),("treble",4)),
        N("giant","Giant","Великан","creature","User",("pitch",-9),("bass",9),("reverb",22),("room",70)),
        N("demon","Demon","Демон","creature","Skull",("pitch",-8),("drive",30),("chorus",40),("reverb",35),("room",80),("bass",8)),
        N("monster","Monster","Монстр","creature","Skull",("pitch",-11),("drive",45),("bass",10),("flanger",20)),
        N("goblin","Goblin","Гоблин","creature","Ghost",("pitch",5),("drive",35),("robot",25),("robotfreq",140),("treble",5)),
        N("ghost","Ghost","Призрак","creature","Ghost",("pitch",2),("whisper",55),("reverb",70),("room",95),("echo",30),("echotime",420),("vibrato",30)),
        N("alien","Alien","Пришелец","creature","Rocket",("pitch",5),("robot",60),("robotfreq",310),("flanger",55),("echo",15),("echotime",90)),
        N("dragon","Dragon","Дракон","creature","Flame",("pitch",-10),("drive",55),("reverb",45),("room",90),("bass",12),("echo",20),("echotime",180)),
        N("orc","Orc","Орк","creature","Skull",("pitch",-6),("drive",40),("bass",7),("mid",4)),
        N("fairy","Fairy","Фея","creature","Sparkles",("pitch",9),("chorus",50),("reverb",35),("room",60),("treble",6)),

        N("robot","Robot","Робот","robot","Bot",("mono",100),("robot",30),("robotfreq",60)),
        N("android","Android","Андроид","robot","Bot",("robot",70),("robotfreq",110),("flanger",30),("highcut",7000)),
        N("cyborg","Cyborg","Киборг","robot","Cpu",("pitch",-3),("mono",55),("flanger",45),("drive",15)),
        N("dalek","Exterminator","Истребитель","robot","Bot",("robot",100),("robotfreq",32),("drive",35),("lowcut",200),("highcut",5000)),
        N("vocoder","Vocoder","Вокодер","robot","AudioWaveform",("mono",100),("chorus",60),("reverb",20)),
        N("8bit","8-Bit","8 бит","robot","Gamepad2",("crush",75),("lowcut",150)),
        N("glitch","Glitch","Глитч","robot","Zap",("crush",45),("tremolo",80),("tremrate",17),("robot",35),("robotfreq",480)),
        N("ai","AI Assistant","ИИ-ассистент","robot","Sparkles",("mono",35),("chorus",25),("treble",4),("comp",50),("reverb",8)),
        N("mech","Mech Pilot","Пилот меха","robot","Cpu",("pitch",-2),("robot",45),("robotfreq",75),("lowcut",300),("highcut",3800),("drive",25)),

        N("telephone","Telephone","Телефон","device","Phone",("lowcut",400),("highcut",3200),("drive",18),("comp",50)),
        N("radio","AM Radio","Радио AM","device","Radio",("lowcut",500),("highcut",2800),("drive",40),("crush",15),("comp",60)),
        N("walkie","Walkie-Talkie","Рация","device","Radio",("lowcut",600),("highcut",2600),("drive",60),("crush",25),("comp",80),("gate",-48)),
        N("megaphone","Megaphone","Мегафон","device","Megaphone",("lowcut",450),("highcut",4200),("drive",55),("mid",8),("echo",12),("echotime",110)),
        N("oldtv","Old TV","Старый ТВ","device","Tv",("lowcut",250),("highcut",4500),("crush",30),("drive",20)),
        N("intercom","Intercom","Домофон","device","Bell",("lowcut",700),("highcut",2400),("drive",45),("robot",12),("robotfreq",50)),
        N("gramophone","Gramophone","Граммофон","device","Disc3",("lowcut",350),("highcut",3000),("vibrato",18),("vibrate",1),("drive",25),("crush",12)),
        N("headset","Cheap Headset","Дешёвая гарнитура","device","Headphones",("lowcut",300),("highcut",5000),("crush",20),("mid",5)),
        N("pa","PA System","Громкая связь","device","Megaphone",("lowcut",300),("highcut",5000),("echo",35),("echotime",160),("echofb",40),("reverb",25),("room",75)),

        N("cave","Cave","Пещера","space","Waves",("reverb",55),("room",92),("echo",25),("echotime",350),("echofb",45)),
        N("cathedral","Cathedral","Собор","space","Church",("reverb",75),("room",98),("highcut",9000)),
        N("stadium","Stadium","Стадион","space","Megaphone",("echo",45),("echotime",330),("echofb",50),("reverb",35),("room",85)),
        N("bathroom","Bathroom","Ванная","space","Waves",("reverb",40),("room",30),("treble",3)),
        N("hall","Concert Hall","Концертный зал","space","Music",("reverb",45),("room",80)),
        N("canyon","Canyon Echo","Эхо каньона","space","Waves",("echo",60),("echotime",600),("echofb",60)),
        N("underwater","Underwater","Под водой","space","Waves",("highcut",700),("vibrato",40),("vibrate",3),("chorus",50),("reverb",30),("room",70),("bass",6)),
        N("space","Deep Space","Космос","space","Rocket",("flanger",60),("echo",40),("echotime",480),("echofb",65),("reverb",50),("room",95)),
        N("tunnel","Tunnel","Тоннель","space","Waves",("reverb",50),("room",65),("echo",30),("echotime",120),("echofb",55),("lowcut",150)),
        N("smallroom","Small Room","Комната","space","Waves",("reverb",22),("room",20)),

        N("drunk","Wobbly","Шатающийся","fun","Sparkles",("pitch",-1.5),("vibrato",75),("vibrate",2)),
        N("nervous","Nervous","Нервный","fun","Zap",("tremolo",70),("tremrate",11),("pitch",1.5)),
        N("whisper","Whisper","Шёпот","fun","Ear",("whisper",100),("treble",4)),
        N("scream","Overdrive","Перегруз","fun","Flame",("drive",90),("comp",70),("mid",6)),
        N("earrape","Blown Speaker","Хрип динамика","fun","Speaker",("drive",100),("crush",50),("bass",12),("gain",-6)),
        N("double","Doubler","Двойник","fun","Layers",("chorus",85),("echo",20),("echotime",45),("echofb",0)),
        N("choir","Choir","Хор","fun","Music",("chorus",100),("reverb",50),("room",85),("flanger",15)),
        N("jelly","Jelly","Желе","fun","Sparkles",("vibrato",100),("vibrate",9),("pitch",2)),
        N("slowmo","Slow-Mo","Слоу-мо","fun","Clock",("pitch",-7),("reverb",25),("room",60),("highcut",4000)),
        N("squeak","Squeaky Toy","Пищалка","fun","Sparkles",("pitch",12),("vibrato",25),("vibrate",8),("lowcut",400)),
        N("villain","Villain","Злодей","fun","Skull",("pitch",-5),("chorus",35),("reverb",30),("room",75),("echo",15),("echotime",140)),
        N("hero","Superhero","Супергерой","fun","Zap",("pitch",-2.5),("bass",6),("comp",60),("reverb",15),("treble",3)),
    };
}

sealed class Biquad
{
    const double SR = 48000;
    double b0 = 1, b1, b2, a1, a2, z1, z2;
    public float Run(float x)
    {
        double y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        if (Math.Abs(z1) < 1e-25) z1 = 0;
        if (Math.Abs(z2) < 1e-25) z2 = 0;
        return (float)y;
    }
    void Set(double B0, double B1, double B2, double A0, double A1, double A2) { b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0; a1 = A1 / A0; a2 = A2 / A0; }
    public void LowPass(double f, double q) { f = Math.Min(f, SR * 0.45); double w = 2 * Math.PI * f / SR, a = Math.Sin(w) / (2 * q), c = Math.Cos(w); Set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a); }
    public void HighPass(double f, double q) { double w = 2 * Math.PI * f / SR, a = Math.Sin(w) / (2 * q), c = Math.Cos(w); Set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a); }
    public void Peak(double f, double q, double db) { double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / SR, a = Math.Sin(w) / (2 * q), c = Math.Cos(w); Set(1 + a * A, -2 * c, 1 - a * A, 1 + a / A, -2 * c, 1 - a / A); }
    public void LowShelf(double f, double db) { double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / SR, c = Math.Cos(w), sq = 2 * Math.Sqrt(A) * Math.Sin(w) / 2 * Math.Sqrt(2); Set(A * ((A + 1) - (A - 1) * c + sq), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sq), (A + 1) + (A - 1) * c + sq, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sq); }
    public void HighShelf(double f, double db) { double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / SR, c = Math.Cos(w), sq = 2 * Math.Sqrt(A) * Math.Sin(w) / 2 * Math.Sqrt(2); Set(A * ((A + 1) + (A - 1) * c + sq), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sq), (A + 1) - (A - 1) * c + sq, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sq); }
}

sealed class ModDelay
{
    readonly float[] buf = new float[8192]; int w; double ph;
    public float Run(float x, double baseMs, double depthMs, double rateHz, double fb, double wet, double dry)
    {
        ph += 2 * Math.PI * rateHz / 48000; if (ph > 2 * Math.PI) ph -= 2 * Math.PI;
        double d = (baseMs + depthMs * Math.Sin(ph)) * 48.0; if (d < 1) d = 1; if (d > 8000) d = 8000;
        double rp = w - d; while (rp < 0) rp += 8192;
        int i0 = (int)rp; double fr = rp - i0; int i1 = (i0 + 1) & 8191; i0 &= 8191;
        float y = (float)(buf[i0] * (1 - fr) + buf[i1] * fr);
        buf[w] = (float)(x + y * fb); w = (w + 1) & 8191;
        return (float)(x * dry + y * wet);
    }
}

sealed class Reverb
{
    static readonly int[] CT = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
    static readonly int[] AT = { 556, 441, 341, 225 };
    readonly float[][] cb = new float[8][]; readonly int[] ci = new int[8]; readonly float[] cs = new float[8];
    readonly float[][] ab = new float[4][]; readonly int[] ai = new int[4];
    public Reverb()
    {
        for (int i = 0; i < 8; i++) cb[i] = new float[(int)(CT[i] * 48000.0 / 44100)];
        for (int i = 0; i < 4; i++) ab[i] = new float[(int)(AT[i] * 48000.0 / 44100)];
    }
    public float Run(float x, float feedback, float damp)
    {
        float inp = x * 0.015f, o = 0;
        for (int i = 0; i < 8; i++)
        {
            var b = cb[i]; int k = ci[i]; float y = b[k];
            cs[i] = y * (1 - damp) + cs[i] * damp;
            if (Math.Abs(cs[i]) < 1e-20f) cs[i] = 0;
            b[k] = inp + cs[i] * feedback; if (++k >= b.Length) k = 0; ci[i] = k; o += y;
        }
        for (int i = 0; i < 4; i++)
        {
            var b = ab[i]; int k = ai[i]; float bo = b[k]; float y = -o + bo;
            b[k] = o + bo * 0.5f; if (++k >= b.Length) k = 0; ai[i] = k; o = y;
        }
        return o * 3f;
    }
}

/// <summary>STFT based "monotone robot" (zero phase) and "whisper" (random phase).</summary>
sealed class SpectralFx
{
    const int N = 1024, H = 256, M = 10, Lat = N - H;
    readonly float[] inF = new float[N], outF = new float[N], acc = new float[2 * N], win = new float[N];
    readonly Complex[] fft = new Complex[N];
    readonly Random rnd = new(7);
    int rover = Lat;
    public SpectralFx() { for (int i = 0; i < N; i++) win[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N)); }
    public void Process(float[] b, int off, int n, float robot, float whisper)
    {
        for (int i = 0; i < n; i++)
        {
            float x = b[off + i];
            inF[rover] = x;
            float dry = inF[rover - Lat];
            float wet = outF[rover - Lat];
            float mix = Math.Min(1f, robot + whisper);
            b[off + i] = dry * (1 - mix) + wet * mix;
            rover++;
            if (rover >= N)
            {
                rover = Lat;
                for (int k = 0; k < N; k++) { fft[k].X = inF[k] * win[k]; fft[k].Y = 0; }
                FastFourierTransform.FFT(true, M, fft);
                float tot = robot + whisper; float wr = tot > 0 ? robot / tot : 1;
                for (int k = 0; k <= N / 2; k++)
                {
                    float mag = (float)Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y);
                    double ph = rnd.NextDouble() * 2 * Math.PI;
                    float re = mag * wr + (float)(mag * Math.Cos(ph)) * (1 - wr) * 1.6f;
                    float im = (float)(mag * Math.Sin(ph)) * (1 - wr) * 1.6f;
                    fft[k].X = re; fft[k].Y = im;
                    if (k > 0 && k < N / 2) { fft[N - k].X = re; fft[N - k].Y = -im; }
                }
                FastFourierTransform.FFT(false, M, fft);
                for (int k = 0; k < N; k++) acc[k] += fft[k].X * win[k] / 1.5f;
                Array.Copy(acc, 0, outF, 0, H);
                Array.Copy(acc, H, acc, 0, N);
                Array.Copy(inF, H, inF, 0, Lat);
            }
        }
    }
}

/// <summary>Mono 48 kHz effect chain used by the voice changer (live) and TTS (offline).</summary>
public sealed class EffectChain
{
    const int SR = 48000, FftSize = 2048, Osamp = 8;
    public const int PitchLatency = FftSize - FftSize / Osamp;
    FxParams _pending; FxParams p = new();
    readonly SmbPitchShifter smb = new();
    readonly SpectralFx spec = new();
    readonly ModDelay vib = new(), ch1 = new(), ch2 = new(), fl = new();
    readonly Biquad lc1 = new(), lc2 = new(), hc1 = new(), hc2 = new(), bs = new(), md = new(), tr = new();
    readonly Reverb rev = new();
    readonly float[] echo = new float[SR * 2]; int ew;
    float[] tmp = new float[4096];
    double ringPh, tremPh, gateEnv, gateGain = 1, compEnv, crushHold; int crushCnt;
    // derived
    float pitchRatio = 1, robot, mono, whisper, trem, vibr, chorus, flang, drive, crush, echoMix, echoFb, revMix, revFb, gain = 1, comp;
    double robotFreq, tremRate, vibRate, gateThr; int echoLen = 1; bool eq, lowcut, highcut, gate;

    public EffectChain() { Apply(new FxParams()); }
    public EffectChain(FxParams fx) { Apply(fx ?? new FxParams()); }
    public void Set(FxParams fx) { Interlocked.Exchange(ref _pending, fx.Clone()); }

    void Apply(FxParams q)
    {
        p = q;
        pitchRatio = (float)Math.Pow(2, q["pitch"] / 12.0);
        robot = (float)(q["robot"] / 100); robotFreq = q["robotfreq"];
        mono = (float)(q["mono"] / 100); whisper = (float)(q["whisper"] / 100);
        trem = (float)(q["tremolo"] / 100); tremRate = q["tremrate"];
        vibr = (float)(q["vibrato"] / 100); vibRate = q["vibrate"];
        chorus = (float)(q["chorus"] / 100); flang = (float)(q["flanger"] / 100);
        drive = (float)(q["drive"] / 100); crush = (float)(q["crush"] / 100);
        echoMix = (float)(q["echo"] / 100); echoFb = (float)(q["echofb"] / 100); echoLen = Math.Clamp((int)(q["echotime"] * 48), 1, echo.Length - 1);
        revMix = (float)(q["reverb"] / 100); revFb = (float)(0.70 + q["room"] / 100 * 0.28);
        gain = (float)Math.Pow(10, q["gain"] / 20); comp = (float)(q["comp"] / 100);
        gate = q["gate"] > -79.5; gateThr = Math.Pow(10, q["gate"] / 20);
        lowcut = q["lowcut"] > 25; highcut = q["highcut"] < 19500;
        if (lowcut) { lc1.HighPass(q["lowcut"], 0.707); lc2.HighPass(q["lowcut"], 0.707); }
        if (highcut) { hc1.LowPass(q["highcut"], 0.707); hc2.LowPass(q["highcut"], 0.707); }
        eq = Math.Abs(q["bass"]) > 0.01 || Math.Abs(q["mid"]) > 0.01 || Math.Abs(q["treble"]) > 0.01;
        bs.LowShelf(180, q["bass"]); md.Peak(1200, 0.8, q["mid"]); tr.HighShelf(4200, q["treble"]);
    }

    public bool PitchActive => Math.Abs(pitchRatio - 1) > 0.002f;

    public void Process(float[] b, int off, int n)
    {
        var pend = Interlocked.Exchange(ref _pending, null);
        if (pend != null) Apply(pend);
        if (n <= 0) return;

        if (gate)
        {
            for (int i = 0; i < n; i++)
            {
                double a = Math.Abs(b[off + i]);
                gateEnv = a > gateEnv ? a : gateEnv * 0.9995;
                double target = gateEnv > gateThr ? 1 : 0;
                gateGain += (target - gateGain) * (target > gateGain ? 0.02 : 0.0008);
                b[off + i] *= (float)gateGain;
            }
        }
        if (PitchActive)
        {
            if (tmp.Length < n) tmp = new float[n];
            Array.Copy(b, off, tmp, 0, n);
            smb.PitchShift(pitchRatio, n, FftSize, Osamp, SR, tmp);
            Array.Copy(tmp, 0, b, off, n);
        }
        if (mono > 0.001f || whisper > 0.001f) spec.Process(b, off, n, mono, whisper);

        for (int i = 0; i < n; i++)
        {
            float x = b[off + i];
            if (robot > 0.001f)
            {
                ringPh += 2 * Math.PI * robotFreq / SR; if (ringPh > 2 * Math.PI) ringPh -= 2 * Math.PI;
                x *= (1 - robot) + robot * (float)Math.Sin(ringPh) * 1.4f;
            }
            if (vibr > 0.001f) x = vib.Run(x, 6, 4.5 * vibr, vibRate, 0, 1, 0);
            if (chorus > 0.001f)
            {
                float c = ch1.Run(x, 18, 3.5, 0.7, 0, 1, 0) + ch2.Run(x, 27, 4.5, 1.1, 0, 1, 0);
                x = x * (1 - 0.3f * chorus) + c * 0.45f * chorus;
            }
            if (flang > 0.001f) x = fl.Run(x, 2.2, 1.9, 0.35, 0.65, 0.7 * flang, 1 - 0.2 * flang);
            if (drive > 0.001f)
            {
                float g = 1 + drive * drive * 60 + drive * 6;
                x = (float)(Math.Tanh(x * g) / Math.Pow(g, 0.55));
                x *= 1 + drive;
            }
            if (crush > 0.001f)
            {
                int hold = 1 + (int)(crush * crush * 28);
                if (++crushCnt >= hold) { crushCnt = 0; double levels = Math.Pow(2, 14 - crush * 11); crushHold = Math.Round(x * levels) / levels; }
                x = (float)crushHold;
            }
            if (lowcut) x = lc2.Run(lc1.Run(x));
            if (highcut) x = hc2.Run(hc1.Run(x));
            if (eq) x = tr.Run(md.Run(bs.Run(x)));
            if (trem > 0.001f)
            {
                tremPh += 2 * Math.PI * tremRate / SR; if (tremPh > 2 * Math.PI) tremPh -= 2 * Math.PI;
                x *= 1 - trem * (0.5f + 0.5f * (float)Math.Sin(tremPh));
            }
            if (comp > 0.001f)
            {
                double a = Math.Abs(x);
                compEnv += (a - compEnv) * (a > compEnv ? 0.004 : 0.0002);
                double thrDb = -8 - comp * 22, envDb = 20 * Math.Log10(compEnv + 1e-9), over = envDb - thrDb;
                double grDb = over > 0 ? -over * 0.75 : 0;
                double makeup = -thrDb * 0.45;
                x *= (float)Math.Pow(10, (grDb + makeup) / 20);
            }
            if (echoMix > 0.001f)
            {
                int rp = ew - echoLen; if (rp < 0) rp += echo.Length;
                float d = echo[rp];
                echo[ew] = x + d * echoFb; if (++ew >= echo.Length) ew = 0;
                x += d * echoMix;
            }
            else { echo[ew] = 0; if (++ew >= echo.Length) ew = 0; }
            if (revMix > 0.001f)
            {
                float r = rev.Run(x, revFb, 0.25f);
                x = x * (1 - 0.45f * revMix) + r * revMix * 1.3f;
            }
            x *= gain;
            // soft limiter
            if (x > 0.9f) x = 0.9f + 0.1f * (float)Math.Tanh((x - 0.9f) * 10);
            else if (x < -0.9f) x = -0.9f + 0.1f * (float)Math.Tanh((x + 0.9f) * 10);
            b[off + i] = x;
        }
    }

    /// <summary>Offline render: mono in -> mono out, with tails and latency compensation.</summary>
    public static float[] Render(float[] mono, FxParams fx)
    {
        if (fx == null || fx.IsNeutral()) return mono;
        var ch = new EffectChain(fx);
        int lat = (ch.PitchActive ? PitchLatency : 0) + ((fx["mono"] > 0 || fx["whisper"] > 0) ? 768 : 0);
        double tailSec = 0.1 + (fx["reverb"] > 0 ? 1.2 + fx["room"] / 100 * 2.5 : 0) + (fx["echo"] > 0 ? Math.Min(4, fx["echotime"] / 1000 * (2 + fx["echofb"] / 12)) : 0);
        int tail = (int)(tailSec * SR);
        var buf = new float[mono.Length + lat + tail];
        Array.Copy(mono, buf, mono.Length);
        for (int o = 0; o < buf.Length; o += 2048) ch.Process(buf, o, Math.Min(2048, buf.Length - o));
        if (lat == 0) return buf;
        var res = new float[buf.Length - lat];
        Array.Copy(buf, lat, res, 0, res.Length);
        return res;
    }

    /// <summary>Feeds consecutive pieces of one recording through the chain (latency removed, tail returned by Finish).</summary>
    public sealed class Stream
    {
        readonly EffectChain ch; readonly int tail; int skip;
        public Stream(FxParams fx)
        {
            ch = new EffectChain(fx);
            skip = (ch.PitchActive ? PitchLatency : 0) + ((fx["mono"] > 0 || fx["whisper"] > 0) ? 768 : 0);
            double tailSec = 0.1 + (fx["reverb"] > 0 ? 1.2 + fx["room"] / 100 * 2.5 : 0) + (fx["echo"] > 0 ? Math.Min(4, fx["echotime"] / 1000 * (2 + fx["echofb"] / 12)) : 0);
            tail = (int)(tailSec * SR) + skip;
        }
        public float[] Process(float[] mono)
        {
            var buf = (float[])mono.Clone();
            for (int o = 0; o < buf.Length; o += 2048) ch.Process(buf, o, Math.Min(2048, buf.Length - o));
            if (skip == 0) return buf;
            int drop = Math.Min(skip, buf.Length); skip -= drop;
            var res = new float[buf.Length - drop]; Array.Copy(buf, drop, res, 0, res.Length); return res;
        }
        public float[] Finish() => Process(new float[tail]);
    }
}
