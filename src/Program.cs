using Avalonia;
using VoxDeck.Audio;
namespace VoxDeck;

static class Program
{
    public static string[] Args = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        if (args.Length > 1 && args[0] == "--voicescan") return VoiceScan.Run(args[1], args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null).GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--install-driver")
        {
            // the same code path as the "Install virtual mic" button
            var r = Platform.VirtualMic.Install(st => Console.WriteLine("step: " + st), args.Length > 1 && args[1] == "remove").GetAwaiter().GetResult();
            Console.WriteLine($"ok={r.Ok} publisher=\"{r.Publisher}\" restart={r.NeedsRestart} error={r.Error}");
            Console.WriteLine("render: " + Platform.VirtualMic.FindRender().name + " | capture: " + Platform.VirtualMic.FindCapture().name);
            return r.Ok ? 0 : 1;
        }
        if (args.Length > 0 && args[0] == "--ttsbench") return VoiceScan.Bench(args.Length > 1 && args[1] == "nowarm").GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--selftest") return SelfTest.Run(args.Length > 1 ? args[1] : ".", args.Length > 2 && args[2] == "fast").GetAwaiter().GetResult();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Core.AppSettings.Dir, "crash.log"), DateTime.Now + "\n" + ex); } catch { }
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}

/// <summary>Headless checks of everything that does not need a window (run with --selftest dir).</summary>
static class SelfTest
{
    public static async Task<int> Run(string dir, bool fast)
    {
        Directory.CreateDirectory(dir); int fails = 0;
        void Check(string name, bool ok, string info = "") { Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (info != "" ? "  — " + info : "")); if (!ok) fails++; }
        // 1. synth library
        int bad = 0; double total = 0; var sw = System.Diagnostics.Stopwatch.StartNew(); string worst = ""; double worstMs = 0;
        foreach (var s in Synth.All)
        {
            try
            {
                var t0 = sw.Elapsed.TotalMilliseconds; var d = s.Render(); var ms = sw.Elapsed.TotalMilliseconds - t0; if (ms > worstMs) { worstMs = ms; worst = s.Name; }
                float pk = 0; bool nan = false; foreach (var v in d) { if (float.IsNaN(v) || float.IsInfinity(v)) nan = true; pk = Math.Max(pk, Math.Abs(v)); }
                double sec = d.Length / 48000.0; total += sec;
                if (nan || pk < 0.05 || pk > 1.01 || d.Length < 100) { bad++; Console.WriteLine($"   bad sound: {s.Id} peak={pk} nan={nan} len={d.Length}"); }
                if (Math.Abs(sec - s.Dur) > Math.Max(0.6, s.Dur * 0.5)) Console.WriteLine($"   note: {s.Name} declared {s.Dur}s actual {sec:0.00}s");
            }
            catch (Exception ex) { bad++; Console.WriteLine($"   EXC {s.Id}: {ex.Message}"); }
        }
        Check("synth library", bad == 0, $"{Synth.All.Count} sounds, {total:0}s audio, rendered in {sw.Elapsed.TotalSeconds:0.0}s (slowest {worst} {worstMs:0}ms), ids unique={Synth.All.Select(s => s.Id).Distinct().Count() == Synth.All.Count}");
        foreach (var n in new[] { "Air Horn", "Sad Trombone", "Korobeiniki", "Beat: Trap 140", "Applause" })
            File.WriteAllBytes(Path.Combine(dir, n.Replace(":", "").Replace(" ", "_") + ".wav"), AudioClip.FromMono(Synth.All.First(s => s.Name == n).Render()).ToWav());
        // 2. presets on a test signal
        var voice = AudioClip.FromMono(Synth.All.First(s => s.Name == "Happy Birthday").Render()).ToMono();
        int pbad = 0; sw.Restart();
        foreach (var p in FxPresets.Builtin)
        {
            var o = EffectChain.Render(voice, p.P); float pk = 0; bool nan = false; double e = 0;
            foreach (var v in o) { if (float.IsNaN(v) || float.IsInfinity(v)) nan = true; pk = Math.Max(pk, Math.Abs(v)); e += v * v; }
            double rms = Math.Sqrt(e / o.Length);
            if (nan || pk > 1.0001 || rms < 0.003) { pbad++; Console.WriteLine($"   bad preset {p.Id}: peak={pk} rms={rms} nan={nan}"); }
        }
        double rt = voice.Length / 48000.0 * FxPresets.Builtin.Count / sw.Elapsed.TotalSeconds;
        Check("fx presets", pbad == 0, $"{FxPresets.Builtin.Count} presets, {FxDefs.All.Length} params, {rt:0}x realtime avg");
        Check("preset ids unique", FxPresets.Builtin.Select(p => p.Id).Distinct().Count() == FxPresets.Builtin.Count);
        // live-chain block processing (small blocks like a real device)
        {
            var ch = new EffectChain(FxPresets.Builtin.First(p => p.Id == "demon").P); var b = (float[])voice.Clone(); sw.Restart();
            for (int o = 0; o + 480 <= b.Length; o += 480) ch.Process(b, o, 480);
            Check("live chain 10ms blocks", !b.Any(float.IsNaN), $"{voice.Length / 48000.0 / sw.Elapsed.TotalSeconds:0}x realtime");
        }
        // resampler: a 1 kHz tone must survive 44.1k -> 48k and 24k -> 48k at unity gain, 15 kHz must not alias
        {
            float[] Tone(int rate, double f, int n) { var t = new float[n]; for (int i = 0; i < n; i++) t[i] = (float)Math.Sin(2 * Math.PI * f * i / rate) * 0.5f; return t; }
            double Rms(float[] x) { double e = 0; for (int i = x.Length / 4; i < x.Length * 3 / 4; i++) e += x[i] * x[i]; return Math.Sqrt(e / (x.Length / 2)); }
            double a = Rms(Resampler.Convert(Tone(44100, 1000, 44100), 1, 44100, 48000)), b = Rms(Resampler.Convert(Tone(24000, 1000, 24000), 1, 24000, 48000)), c = Rms(Resampler.Convert(Tone(24000, 10000, 24000), 1, 24000, 48000)), d2 = Rms(Resampler.Convert(Tone(96000, 30000, 96000), 1, 96000, 48000));
            Check("resampler", Math.Abs(a - 0.3536) < 0.004 && Math.Abs(b - 0.3536) < 0.004 && c > 0.33 && d2 < 0.002, $"1k@44.1 {a:0.0000}, 1k@24 {b:0.0000}, 10k@24 {c:0.0000}, 30k@96 (must vanish) {d2:0.0000}");
        }
        // 3. localization completeness
        var used = new HashSet<string>();
        foreach (var d in FxDefs.All) used.Add("fx." + d.Key);
        foreach (var c in FxPresets.Cats) used.Add("cat." + c);
        var missing = used.Where(k => Core.Loc.T(k) == k).ToList();
        Check("loc keys", missing.Count == 0, string.Join(",", missing));
        Check("edge token", Tts.EdgeDrm.Generate(1700000000) == "2CD23A2CA0B2595A-F3C846C370E05F9CC83ACC5F995C5F5D87A4D0F14CE8936A0F902E64099E38A2" && Tts.EdgeDrm.Generate(1794754321.5) == "FFD847141FFCFE6C-68B9B76481E7CAE15D86E441A570931A5DF495F62B605B778DD440D6F7B1DCD0" && Tts.EdgeDrm.Generate(1800000299) == "99E54519B490BE5C-E162DB0977FBDCB9B86DD8148BF1BCDC3C0303176CE39852A42B415935C20654", Tts.EdgeDrm.Generate(1700000000));
        // 4. online: Edge TTS
        if (!fast) try
        {
            var voices = await Tts.EdgeTts.FetchVoices();
            Check("edge voices list", voices.Count > 100, $"{voices.Count} voices, {voices.Select(v => v.Locale).Distinct().Count()} locales");
            Tts.TtsVoice V(string n) => voices.First(x => x.Id == "edge:" + n);
            // cold request, then a warm one through a pre-opened connection
            var t0 = System.Diagnostics.Stopwatch.StartNew();
            var j0 = Tts.TtsService.Start("Hello! This is VoxDeck speaking. Testing one, two, three.", V("en-US-AriaNeural"), 0, 0, 100); await j0.Completion;
            double cold = t0.Elapsed.TotalMilliseconds;
            Tts.EdgeTts.Warm(); await Task.Delay(1500);
            t0.Restart();
            var j1 = Tts.TtsService.Start("Привет! Это проверка синтеза речи в приложении.", V("ru-RU-SvetlanaNeural"), 0, 0, 100); await j1.Completion;
            double warm = t0.Elapsed.TotalMilliseconds;
            Check("edge tts short phrase", j0.Clip.Seconds > 1.5 && j1.Clip.Seconds > 1.5, $"cold {cold:0} ms, warm {warm:0} ms ({j0.Clip.Seconds:0.0}s / {j1.Clip.Seconds:0.0}s audio)");
            File.WriteAllBytes(Path.Combine(dir, "tts_en.wav"), j0.Clip.ToWav()); File.WriteAllBytes(Path.Combine(dir, "tts_ru.wav"), j1.Clip.ToWav());
            // clarity processing: energy above 12 kHz must exist after enhancement, loudness must be even
            Tts.TtsService.Clarity = false; var raw = await Tts.TtsService.Speak("Hello! This is VoxDeck speaking. Testing one, two, three.", V("en-US-AriaNeural"), 0, 0, 100); Tts.TtsService.Clarity = true;
            (double rms, double hi) Stat(AudioClip c) { var m = c.ToMono(); double e = 0, eh = 0; float prev = 0, prev2 = 0; var hp = new float[m.Length]; for (int i = 0; i < m.Length; i++) { e += m[i] * m[i]; float d = m[i] - 2 * prev + prev2; prev2 = prev; prev = m[i]; eh += d * d; } return (Math.Sqrt(e / m.Length), Math.Sqrt(eh / m.Length)); }
            var sr = Stat(raw); var se2 = Stat(j0.Clip);
            Check("clarity processing", se2.hi / se2.rms > sr.hi / sr.rms * 1.15 && j0.Clip.Data.Max(Math.Abs) <= 1.0f, $"brightness x{(se2.hi / se2.rms) / (sr.hi / sr.rms):0.00}, rms {sr.rms:0.000} -> {se2.rms:0.000}, peak {j0.Clip.Data.Max(Math.Abs):0.00}");
            // effect on speech + per-voice rate
            var fxj = Tts.TtsService.Start("Robots speak like this.", V("en-US-GuyNeural"), -20, 2, 100, FxPresets.Builtin.First(p => p.Id == "robot").P); await fxj.Completion;
            File.WriteAllBytes(Path.Combine(dir, "tts_robot.wav"), fxj.Clip.ToWav());
            Check("edge tts with effect + prosody", fxj.Clip.Seconds > 1, $"{fxj.Clip.Seconds:0.0}s");
            // long text: sentences are requested in parallel, playback could start after the first one
            var longText = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"This is sentence number {i} of a long text used to verify that streaming works."));
            t0.Restart();
            var lj = Tts.TtsService.Start(longText, V("en-US-GuyNeural"), 0, 0, 100); await lj.First; double first = t0.Elapsed.TotalMilliseconds; await lj.Completion;
            Check("edge tts long text", lj.Clip.Seconds > 120, $"{longText.Length} chars in {lj.Pieces} pieces -> {lj.Clip.Seconds:0}s audio; first sound after {first:0} ms, all done in {t0.Elapsed.TotalSeconds:0.0}s ({lj.Clip.Seconds / t0.Elapsed.TotalSeconds:0}x realtime)");
            // the phrase prepared while the user was typing must be the very one that "Play" gets
            var preA = Tts.TtsService.Start("Prepared while typing.", V("en-US-AriaNeural"), 0, 0, 100);
            await preA.Completion; t0.Restart();
            var preB = Tts.TtsService.Start("Prepared while typing.", V("en-US-AriaNeural"), 0, 0, 100); await preB.First;
            double reuseMs = t0.Elapsed.TotalMilliseconds;
            var preC = Tts.TtsService.Start("Prepared while typing.", V("en-US-AriaNeural"), 10, 0, 100); await preC.Completion;
            using var cancelSrc = new System.Threading.CancellationTokenSource();
            var preD = Tts.TtsService.Start("This one is cancelled half way and must not be served again.", V("en-US-AriaNeural"), 0, 0, 100, null, cancelSrc.Token);
            cancelSrc.Cancel(); try { await preD.Completion; } catch { }
            var preE = Tts.TtsService.Start("This one is cancelled half way and must not be served again.", V("en-US-AriaNeural"), 0, 0, 100); await preE.Completion;
            Check("prepared phrase is reused", ReferenceEquals(preA, preB) && !ReferenceEquals(preA, preC) && reuseMs < 30 && preE.Clip.Seconds > 1.5, $"play after typing answers in {reuseMs:0.0} ms; other speed -> new audio; cancelled job replaced={!ReferenceEquals(preD, preE)} ({preE.Clip.Seconds:0.0}s)");
        }
        catch (Exception ex) { Check("edge tts", false, ex.ToString()); }
        // 5a. sound websites
        if (!fast)
        {
            async Task Site(string name, Func<Task<List<Library.SoundRef>>> f)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew(); var l = await f(); long ms = sw.ElapsedMilliseconds;
                    if (l.Count == 0) { Check(name, false, "no results"); return; }
                    var bytes = await Library.Sources.Fetch(l[0]); var clip = Audio.Decoder.Decode(bytes);
                    float peak = clip.Data.Length > 0 ? clip.Data.Max(Math.Abs) : 0;
                    Check(name, clip.Seconds > 0.05 && peak > 0.01, $"{l.Count} results in {ms} ms; \"{l[0].Name}\" -> {bytes.Length / 1024} KB, {clip.Seconds:0.0}s, peak {peak:0.00}");
                }
                catch (Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); }
            }
            await Site("myinstants trending (ru)", () => Library.Sources.MyinstantsList("trending", "ru"));
            await Site("myinstants best (us)", () => Library.Sources.MyinstantsList("best", "us"));
            await Site("myinstants category", () => Library.Sources.MyinstantsCategory("games"));
            await Site("myinstants search", () => Library.Sources.MyinstantsSearch("bruh"));
            await Site("bbc sound effects", () => Library.Sources.BbcSearch("dog bark"));
            await Site("freesound", () => Library.Sources.FreesoundSearch("door knock"));
            await Site("mixkit", () => Library.Sources.MixkitSearch("explosion"));
            await Site("wikimedia commons", () => Library.Sources.CommonsSearch("applause"));
            try
            {
                var sw2 = System.Diagnostics.Stopwatch.StartNew(); var (all, failed) = await Library.Sources.SearchAll("laugh", 1);
                var per = all.GroupBy(r => r.Id[..r.Id.IndexOf(':')]).Select(g => g.Key + ":" + g.Count());
                Check("search all sites", all.Count >= 40 && per.Count() >= 4, $"{all.Count} results in {sw2.ElapsedMilliseconds} ms ({string.Join(", ", per)}){(failed.Count > 0 ? "; no answer: " + string.Join(", ", failed) : "")}");
            }
            catch (Exception ex) { Check("search all sites", false, ex.Message); }
            Library.SoundCache.Put("selftest:x", new byte[2000]); Check("disk cache", Library.SoundCache.Get("selftest:x")?.Length == 2000, "");
        }
        // 5. online library
        if (!fast) try
        {
            var ch = await Library.Online.DeezerChart(0); Check("deezer chart", ch.Count > 20, ch.Count + " tracks, #1 " + ch[0].Sub + " - " + ch[0].Name);
            var rock = await Library.Online.DeezerChart(152); Check("deezer genre chart", rock.Count > 5, rock.Count + " tracks");
            var se = await Library.Online.DeezerSearch("never gonna give you up"); Check("deezer search", se.Count > 3, se.Count + " results");
            var bytes = await Library.Online.Http.GetByteArrayAsync(se[0].Url); var clip = Decoder.Decode(bytes);
            Check("deezer preview decode", clip.Seconds > 20, $"{bytes.Length / 1024} KB -> {clip.Seconds:0.0}s");
            var fresh = await Library.Online.DeezerFreshPreview(se[0].Id[7..]); Check("deezer fresh preview", !string.IsNullOrEmpty(fresh));
        }
        catch (Exception ex) { Check("deezer", false, ex.Message); }
        if (!fast) try
        {
            var ov = await Library.Online.OpenverseSearch("applause"); Check("openverse search", ov.Count > 3, ov.Count + " results, first: " + ov[0].Name + " / " + ov[0].Sub);
            var bytes = await Library.Online.Http.GetByteArrayAsync(ov[0].Url); var clip = Decoder.Decode(bytes);
            Check("openverse decode", clip.Seconds > 0.2, $"{clip.Seconds:0.0}s");
        }
        catch (Exception ex) { Check("openverse", false, ex.Message); }
        // 6. engine (null output on non-Windows): play + stop + ended callback
        {
            var eng = new AudioEngine(); eng.Configure("auto", "default");
            var clip = AudioClip.FromMono(Synth.All.First(s => s.Name == "Coin 1").Render()); bool ended = false;
            var h = eng.Play(clip, true, true, 1, 1, false, "t"); h.Ended += _ => ended = true;
            await Task.Delay(1200);
            Check("engine playback end", ended, $"voiceReady={eng.VoiceReady} monitor={eng.MonitorDeviceName} err={eng.LastError}");
            var h2 = eng.Play(AudioClip.FromMono(Synth.All.First(s => s.Name == "Rain").Render()), true, true, 1, 1, true, "t"); bool e2 = false; h2.Ended += _ => e2 = true;
            await Task.Delay(300); var bars = new float[16]; eng.MonitorBus.Spectrum(bars);
            eng.StopAll(); await Task.Delay(200);
            Check("engine stop + spectrum", e2 && bars.Max() > 0.1f, "max bar " + bars.Max().ToString("0.00"));
            Console.WriteLine("   outputs: " + string.Join(" | ", eng.Outputs().Select(d => d.Name)) + "   inputs: " + string.Join(" | ", eng.Inputs().Select(d => d.Name)));
            if (OperatingSystem.IsWindows())
            {
                eng.MicChain.Set(FxPresets.Builtin.First(p => p.Id == "robot").P); eng.FxEnabled = true; eng.HearMyself = true;
                eng.StartMic("default"); await Task.Delay(1500);
                Console.WriteLine($"   mic: running={eng.MicRunning} level={eng.MicLevel:0.000} out={eng.MicOutLevel:0.000} err={eng.LastError}");
                eng.StopMic();
            }
            eng.Dispose();
        }
        // 6b. real Windows only: the virtual microphone end to end (what VoxDeck plays must come out of the "CABLE Output" recording device)
        if (OperatingSystem.IsWindows() && Platform.VirtualMic.Installed)
        {
            try
            {
                var eng = new AudioEngine(); eng.Configure("auto", "default");
                Check("virtual mic detected", eng.VoiceReady && (eng.VoiceDeviceName ?? "").Contains("CABLE", StringComparison.OrdinalIgnoreCase), $"plays into \"{eng.VoiceDeviceName}\" err={eng.LastError}");
                var (capId, capName) = Platform.VirtualMic.FindCapture();
                using var en = new NAudio.CoreAudioApi.MMDeviceEnumerator(); using var dev = en.GetDevice(capId);
                using var cap = new NAudio.CoreAudioApi.WasapiCapture(dev, true, 20);
                var rec = new List<float>(); int ch = cap.WaveFormat.Channels, rate = cap.WaveFormat.SampleRate; bool isFloat = cap.WaveFormat.BitsPerSample == 32;
                cap.DataAvailable += (_, e) =>
                {
                    lock (rec)
                    {
                        if (isFloat) for (int i = 0; i + 4 * ch <= e.BytesRecorded; i += 4 * ch) rec.Add(BitConverter.ToSingle(e.Buffer, i));
                        else for (int i = 0; i + 2 * ch <= e.BytesRecorded; i += 2 * ch) rec.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                    }
                };
                cap.StartRecording(); await Task.Delay(300);
                // 1 kHz tone, 1.5 s
                var tone = new float[AudioClip.SR * 3 / 2]; for (int i = 0; i < tone.Length; i++) tone[i] = 0.5f * MathF.Sin(2 * MathF.PI * 1000 * i / AudioClip.SR);
                eng.Play(AudioClip.FromMono(tone), true, false, 1, 1, false, "t");
                await Task.Delay(2200); cap.StopRecording(); await Task.Delay(200);
                float[] r; lock (rec) r = rec.ToArray();
                double Power(double f) { double re = 0, im = 0; for (int i = 0; i < r.Length; i++) { re += r[i] * Math.Cos(2 * Math.PI * f * i / rate); im += r[i] * Math.Sin(2 * Math.PI * f * i / rate); } return Math.Sqrt(re * re + im * im) / Math.Max(1, r.Length); }
                double p1k = Power(1000), p3k = Power(3100), peak = r.Length > 0 ? r.Max(Math.Abs) : 0;
                Check("virtual mic carries the sound", peak > 0.05 && p1k > 10 * p3k, $"recorded {r.Length / (double)rate:0.0}s from \"{capName}\" ({rate} Hz): peak {peak:0.00}, 1 kHz level {p1k:0.0000}, off-tone level {p3k:0.0000}");
                // speech through the cable
                if (!fast)
                {
                    lock (rec) rec.Clear(); cap.StartRecording(); await Task.Delay(200);
                    var voices = await Tts.EdgeTts.FetchVoices();
                    var job = Tts.TtsService.Start("Привет! Это проверка виртуального микрофона.", voices.First(v => v.Id == "edge:ru-RU-DmitryNeural"), 0, 0, 100); await job.First;
                    eng.Play(job.Clip, true, false, 1, 1, false, "t"); await job.Completion; await Task.Delay((int)(job.Clip.Seconds * 1000) + 500); cap.StopRecording(); await Task.Delay(200);
                    lock (rec) r = rec.ToArray();
                    double e2 = 0; foreach (var v in r) e2 += v * v;
                    Check("speech reaches the virtual mic", r.Length > rate && Math.Sqrt(e2 / r.Length) > 0.01, $"{job.Clip.Seconds:0.0}s of speech played, recorded rms {Math.Sqrt(e2 / Math.Max(1, r.Length)):0.000}");
                    var wav = new AudioClip { Data = Resampler.Convert(r.SelectMany(v => new[] { v, v }).ToArray(), 2, rate, AudioClip.SR) };
                    File.WriteAllBytes(Path.Combine(dir, "captured_from_virtual_mic.wav"), wav.ToWav());
                }
                // default-microphone takeover and hand-back
                string before = Platform.VirtualMic.DefaultId(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications);
                var prev = Platform.VirtualMic.TakeOver(); bool isDef = Platform.VirtualMic.IsDefaultMic;
                Check("becomes the default microphone", isDef, $"default before: \"{Platform.VirtualMic.NameOf(before)}\", now: \"{Platform.VirtualMic.NameOf(Platform.VirtualMic.DefaultId(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications))}\"");
                if (!string.IsNullOrEmpty(prev)) { Platform.VirtualMic.SetDefault(prev); Check("default microphone restored", Platform.VirtualMic.DefaultId(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Communications) == prev, Platform.VirtualMic.NameOf(prev)); }
                eng.Dispose();
            }
            catch (Exception ex) { Check("virtual mic", false, ex.ToString()); }
        }
        else if (OperatingSystem.IsWindows()) Console.WriteLine("   virtual mic driver is not installed — loopback checks skipped");
        // 7. hotkey parser
        Check("hotkey parse", Platform.Keys.TryParse("Ctrl+Alt+S", out var m, out var vk) && m == 3 && vk == 0x53 && Platform.Keys.TryParse("Shift+F5", out m, out vk) && m == 4 && vk == 0x74 && Platform.Keys.TryParse("Num1", out m, out vk) && vk == 0x61 && Platform.Keys.TryParse("Ctrl+Num+", out m, out vk) && vk == 0x6B && !Platform.Keys.TryParse("Ctrl+Foo", out m, out vk));
        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}

/// <summary>Synthesizes the same counting text with every online voice and records how fast each one speaks (developer tool).</summary>
static class VoiceScan
{
    public static async Task<int> Run(string outFile, string filter, string customText)
    {
        var voices = await Tts.EdgeTts.FetchVoices();
        if (!string.IsNullOrEmpty(filter)) voices = voices.Where(v => System.Text.RegularExpressions.Regex.IsMatch(v.Id, filter)).ToList();
        Tts.TtsService.AutoBalance = Environment.GetEnvironmentVariable("VOX_BALANCE") == "1"; Tts.TtsService.Clarity = false;
        string text = customText ?? "1, 2, 3, 4, 5, 6, 7, 8, 9, 10. 25, 48, 73, 100, 365, 1998.";
        var rows = new System.Collections.Concurrent.ConcurrentBag<string>(); int done = 0, bad = 0;
        using var gate = new SemaphoreSlim(3);
        await Task.WhenAll(voices.Select(async v =>
        {
            await gate.WaitAsync();
            try
            {
                var clip = await Tts.TtsService.Speak(text, v, 0, 0, 100);
                var m = clip.ToMono(); float peak = m.Max(Math.Abs); int win = 480, active = 0;
                for (int o = 0; o + win <= m.Length; o += win) { double e = 0; for (int i = 0; i < win; i++) e += m[o + i] * m[o + i]; if (Math.Sqrt(e / win) > peak * 0.04) active++; }
                rows.Add($"{{\"id\":\"{v.Id[5..]}\",\"locale\":\"{v.Locale}\",\"sec\":{clip.Seconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},\"active\":{(active * 0.01).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)},\"peak\":{peak.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}}}");
            }
            catch (Exception ex) { Interlocked.Increment(ref bad); rows.Add($"{{\"id\":\"{v.Id[5..]}\",\"locale\":\"{v.Locale}\",\"error\":\"{ex.Message.Replace("\"", "'")}\"}}"); }
            finally { gate.Release(); int d = Interlocked.Increment(ref done); if (d % 20 == 0) Console.WriteLine($"{d}/{voices.Count} (failed {bad})"); }
        }));
        File.WriteAllText(outFile, "[\n" + string.Join(",\n", rows.OrderBy(r => r)) + "\n]\n");
        Console.WriteLine($"done: {voices.Count} voices, {bad} failed");
        return 0;
    }

    public static async Task<int> Bench(bool noWarm)
    {
        var voices = await Tts.EdgeTts.FetchVoices();
        string[] phrases = { "Привет всем, как дела?", "Го в катку, я готов.", "Подождите минуту, сейчас вернусь.", "Hello there, how are you doing today?", "Отличная игра, всем спасибо!", "Let's go, one more round.", "Кто последний, тот моет посуду.", "Я сейчас не могу говорить в микрофон, поэтому пишу текстом. Надеюсь, меня хорошо слышно. Если что, напишите в чат." };
        string[] ids = { "ru-RU-SvetlanaNeural", "ru-RU-DmitryNeural", "ru-RU-SvetlanaNeural", "en-US-AriaNeural", "ru-RU-DmitryNeural", "en-US-GuyNeural", "ru-RU-SvetlanaNeural", "ru-RU-DmitryNeural" };
        var times = new List<double>(); var t0 = System.Diagnostics.Stopwatch.StartNew();
        Tts.EdgeTts.Log = m => Console.WriteLine($"   [{t0.ElapsedMilliseconds}] {m}");
        for (int i = 0; i < phrases.Length; i++)
        {
            if (!noWarm) { Tts.EdgeTts.Warm(); await Task.Delay(1300); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var job = Tts.TtsService.Start(phrases[i], voices.First(v => v.Id == "edge:" + ids[i]), 0, 0, 100);
            await job.First; double first = sw.Elapsed.TotalMilliseconds; await job.Completion;
            times.Add(first);
            Console.WriteLine($"{ids[i],-24} {phrases[i].Length,3} chars  first sound {first,5:0} ms  complete {sw.Elapsed.TotalMilliseconds,5:0} ms  audio {job.Clip.Seconds:0.0}s");
        }
        times.Sort(); Console.WriteLine($"median {times[times.Count / 2]:0} ms, best {times[0]:0} ms, worst {times[^1]:0} ms ({(noWarm ? "new connection each time" : "pre-opened connections")})");
        return 0;
    }
}
