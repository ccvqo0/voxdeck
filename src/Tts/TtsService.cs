using System.Text.RegularExpressions;
using VoxDeck.Audio;
namespace VoxDeck.Tts;

/// <summary>A speech request in progress. Clip grows sentence by sentence, so playback can start before the whole text is ready.</summary>
public sealed class TtsJob
{
    public readonly AudioClip Clip = new() { Open = true };
    internal readonly TaskCompletionSource FirstTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Completes when the first sentence is playable (or fails with the synthesis error).</summary>
    public Task First => FirstTcs.Task;
    public Task Completion { get; internal set; }
    public Exception Error { get; internal set; }
    public double FirstMs { get; internal set; }
    public double TotalMs { get; internal set; }
    public int Pieces { get; internal set; }
}

public static class TtsService
{
    /// <summary>Local clean-up of synthesized speech (presence, air, even loudness).</summary>
    public static bool Clarity = true;
    /// <summary>Slow down voices that speak faster than the others of the same language.</summary>
    public static bool AutoBalance = true;

    static readonly SemaphoreSlim net = new(4);
    static readonly List<(string key, TtsJob job)> cache = new();
    static readonly Regex sentenceEnd = new(@"(?<=[\.\!\?…。！？;])\s+|\n+", RegexOptions.Compiled);

    /// <summary>First piece is short so speech starts quickly; the rest are requested in parallel.</summary>
    public static List<string> Split(string text, int firstMax = 150, int max = 420)
    {
        var res = new List<string>();
        text = Regex.Replace(text.Replace("\r", ""), @"[ \t]+", " ").Trim();
        if (text.Length == 0) return res;
        var cur = new System.Text.StringBuilder();
        void Flush() { if (cur.Length > 0) { res.Add(cur.ToString().Trim()); cur.Clear(); } }
        foreach (var raw in sentenceEnd.Split(text))
        {
            var s = raw.Trim(); if (s.Length == 0) continue;
            int limit = res.Count == 0 ? firstMax : max;
            while (s.Length > max)          // a single huge sentence: cut at a comma or a space
            {
                Flush();
                int cut = s.LastIndexOfAny(new[] { ',', ':', '—', '-' }, max - 1, max / 2);
                if (cut < 0) cut = s.LastIndexOf(' ', max - 1, max / 2);
                if (cut < 0) cut = max - 1;
                res.Add(s[..(cut + 1)].Trim()); s = s[(cut + 1)..].Trim();
            }
            if (cur.Length > 0 && cur.Length + 1 + s.Length > limit) Flush();
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(s);
        }
        Flush();
        return res;
    }

    static async Task<float[]> Piece(string text, TtsVoice voice, double ratePct, double pitchSt, CancellationToken ct)
    {
        float[] mono;
        if (voice.Id.StartsWith("sapi:"))
        {
            var wav = await Task.Run(() => SapiTts.Synthesize(text, voice.Id[5..], ratePct), ct);
            mono = await Task.Run(() => Audio.Decoder.DecodeMono(wav), ct);
            if (Math.Abs(pitchSt) > 0.05) mono = await Task.Run(() => EffectChain.Render(mono, FxParams.Of(("pitch", pitchSt))), ct);
        }
        else
        {
            byte[] mp3;
            await net.WaitAsync(ct);
            try { mp3 = await EdgeTts.Synthesize(text, voice.Id[5..], ratePct, pitchSt * 8, ct); }
            finally { net.Release(); }
            mono = await Task.Run(() => Audio.Decoder.DecodeMono(mp3), ct);
        }
        return VoiceEnhancer.TrimSilence(mono);
    }

    public static TtsJob Start(string text, TtsVoice voice, double ratePct, double pitchSt, double volumePct, FxParams fx = null, CancellationToken ct = default)
    {
        if (AutoBalance) ratePct += VoiceBalance.RateOffset(voice.Id);
        ratePct = Math.Clamp(Math.Round(ratePct), -90, 200);
        bool hasFx = fx != null && !fx.IsNeutral();
        string key = $"{voice.Id}|{ratePct}|{pitchSt:0.##}|{volumePct:0}|{Clarity}|{(hasFx ? string.Join(",", fx.V.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value)) : "")}|{text}";
        lock (cache)
        {
            int i = cache.FindIndex(c => c.key == key);
            if (i >= 0) { var hit = cache[i].job; if (hit.Error == null && !(hit.Completion?.IsFaulted ?? false) && !(hit.Completion?.IsCanceled ?? false)) return hit; cache.RemoveAt(i); }
        }
        var job = new TtsJob();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool online = !voice.Id.StartsWith("sapi:");
        var pieces = online ? Split(text) : new List<string> { text.Trim() };
        job.Pieces = pieces.Count;
        var tasks = pieces.Select(p => Piece(p, voice, ratePct, pitchSt, ct)).ToList();
        float vol = (float)(volumePct / 100.0);
        job.Completion = Task.Run(async () =>
        {
            try
            {
                var fxs = hasFx ? new EffectChain.Stream(fx) : null; float gain = 0;
                for (int i = 0; i < tasks.Count; i++)
                {
                    var mono = await tasks[i];
                    if (Clarity) mono = VoiceEnhancer.Process(mono);
                    if (gain == 0) gain = Clarity ? VoiceEnhancer.LoudnessGain(mono) : 1;
                    if (fxs != null) { VoiceEnhancer.GainLimit(mono, gain); mono = fxs.Process(mono); VoiceEnhancer.GainLimit(mono, vol); }
                    else { if (ReferenceEquals(mono, null)) continue; mono = (float[])mono.Clone(); VoiceEnhancer.GainLimit(mono, gain * vol); }
                    job.Clip.AppendMono(mono);
                    if (i == 0) { job.FirstMs = sw.Elapsed.TotalMilliseconds; job.FirstTcs.TrySetResult(); }
                }
                if (fxs != null) { var tail = fxs.Finish(); VoiceEnhancer.GainLimit(tail, vol); job.Clip.AppendMono(tail); }
                if (job.Clip.Frames < 2) throw new InvalidOperationException("No audio received");
                job.TotalMs = sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                job.Error = ex; job.FirstTcs.TrySetException(ex);
                lock (cache) cache.RemoveAll(c => ReferenceEquals(c.job, job));
                throw;
            }
            finally { job.Clip.Open = false; if (online) EdgeTts.Warm(); }
        });
        _ = job.First.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        lock (cache) { cache.Add((key, job)); while (cache.Count > 12) cache.RemoveAt(0); }
        return job;
    }

    /// <summary>Whole text to a finished clip.</summary>
    public static async Task<AudioClip> Speak(string text, TtsVoice voice, double ratePct, double pitchSt, double volumePct, CancellationToken ct = default)
    {
        var job = Start(text, voice, ratePct, pitchSt, volumePct, null, ct);
        await job.Completion; return job.Clip;
    }
}

/// <summary>Per-voice speaking-rate corrections measured against the other voices of the same language (Assets/voice_balance.json).</summary>
public static class VoiceBalance
{
    static Dictionary<string, double> map;
    public static double RateOffset(string voiceId)
    {
        if (map == null)
        {
            var m = new Dictionary<string, double>();
            try
            {
                using var s = typeof(VoiceBalance).Assembly.GetManifestResourceStream("voice_balance.json");
                using var doc = System.Text.Json.JsonDocument.Parse(s);
                foreach (var p in doc.RootElement.EnumerateObject()) m[p.Name] = p.Value.GetDouble();
            }
            catch { }
            map = m;
        }
        return map.TryGetValue(voiceId.StartsWith("edge:") ? voiceId[5..] : voiceId, out var v) ? v : 0;
    }
}
