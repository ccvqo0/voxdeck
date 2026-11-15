using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoxDeck.Audio;
namespace VoxDeck.Tts;

public class TtsVoice
{
    public string Id { get; set; }        // edge:en-US-AriaNeural / sapi:Microsoft David Desktop
    public string Name { get; set; }      // Aria
    public string Locale { get; set; }    // en-US
    public string LocaleName { get; set; }// English (United States)
    public string Gender { get; set; }    // Female / Male
    public string Engine { get; set; }    // Online / System
    public string Tags { get; set; }
    public string Lang => Locale?.Split('-')[0] ?? "";
}

public static class EdgeTts
{
    const string Token = EdgeDrm.TrustedToken, GecVer = EdgeDrm.Version, UA = EdgeDrm.UserAgent;
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static string Gec() => EdgeDrm.Generate();

    /// <summary>The service rejects tokens made with a wrong clock — learn the offset from the server's Date header.</summary>
    static async Task SyncClock()
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Head, "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list");
            req.Headers.TryAddWithoutValidation("User-Agent", UA);
            using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (res.Headers.Date is { } d) EdgeDrm.ClockSkew = (d - DateTimeOffset.UtcNow).TotalSeconds;
        }
        catch { }
    }

    public static List<TtsVoice> Parse(string json)
    {
        var list = new List<TtsVoice>();
        using var doc = JsonDocument.Parse(json);
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            string sn = v.GetProperty("ShortName").GetString(), loc = v.GetProperty("Locale").GetString();
            string name = sn.Substring(sn.LastIndexOf('-') + 1).Replace("Neural", "").Replace("Multilingual", " Multilingual").Trim();
            string locName = v.TryGetProperty("LocaleName", out var ln) ? ln.GetString() : null;
            if (string.IsNullOrEmpty(locName))
            {
                var fn = v.TryGetProperty("FriendlyName", out var f) ? f.GetString() : loc; int i = fn.LastIndexOf(" - "); locName = i > 0 ? fn[(i + 3)..] : loc;
            }
            string tags = "";
            if (v.TryGetProperty("VoiceTag", out var vt) && vt.TryGetProperty("VoicePersonalities", out var vp)) tags = string.Join(", ", vp.EnumerateArray().Select(x => x.GetString()));
            list.Add(new TtsVoice { Id = "edge:" + sn, Name = name, Locale = loc, LocaleName = locName, Gender = v.GetProperty("Gender").GetString(), Engine = "Online", Tags = tags });
        }
        return list;
    }

    public static async Task<List<TtsVoice>> FetchVoices()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list?trustedclienttoken={Token}&Sec-MS-GEC={Gec()}&Sec-MS-GEC-Version={GecVer}");
        req.Headers.TryAddWithoutValidation("User-Agent", UA);
        var res = await http.SendAsync(req); res.EnsureSuccessStatusCode();
        return Parse(await res.Content.ReadAsStringAsync());
    }

    static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? "";

    // ---- pre-opened connections: the TLS + websocket handshake is the slowest part of a short request ----
    sealed class Conn { public ClientWebSocket Ws; public DateTime At; }
    static readonly System.Collections.Concurrent.ConcurrentQueue<Conn> pool = new();
    static int opening;
    const double MaxIdleSec = 20;

    static async Task<ClientWebSocket> Open(CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        try
        {
            ws.Options.SetRequestHeader("Pragma", "no-cache");
            ws.Options.SetRequestHeader("Cache-Control", "no-cache");
            ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            ws.Options.SetRequestHeader("User-Agent", UA);
            ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
            ws.Options.SetRequestHeader("Cookie", $"muid={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))};");
            var uri = new Uri($"wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken={Token}&Sec-MS-GEC={Gec()}&Sec-MS-GEC-Version={GecVer}&ConnectionId={Guid.NewGuid():N}");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(12));
            await ws.ConnectAsync(uri, cts.Token);
            string cfg = $"X-Timestamp:{Stamp()}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
                         "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-96kbitrate-mono-mp3\"}}}}";
            await ws.SendAsync(Encoding.UTF8.GetBytes(cfg), WebSocketMessageType.Text, true, cts.Token);
            return ws;
        }
        catch { ws.Dispose(); throw; }
    }
    static string Stamp() => DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " GMT+0000 (Coordinated Universal Time)";

    /// <summary>Opens connections ahead of time so the next request starts instantly. Cheap to call often.</summary>
    public static void Warm(int want = 2)
    {
        while (pool.TryPeek(out var c) && (DateTime.UtcNow - c.At).TotalSeconds > MaxIdleSec) { if (pool.TryDequeue(out var old)) old.Ws.Dispose(); }
        int need = want - pool.Count - Volatile.Read(ref opening);
        for (int i = 0; i < need; i++)
        {
            Interlocked.Increment(ref opening);
            _ = Task.Run(async () =>
            {
                try { var ws = await Open(CancellationToken.None); pool.Enqueue(new Conn { Ws = ws, At = DateTime.UtcNow }); }
                catch { }
                finally { Interlocked.Decrement(ref opening); }
            });
        }
    }
    static async Task<(ClientWebSocket ws, bool pooled)> Take(CancellationToken ct)
    {
        while (pool.TryDequeue(out var c))
        {
            if (c.Ws.State == WebSocketState.Open && (DateTime.UtcNow - c.At).TotalSeconds <= MaxIdleSec) return (c.Ws, true);
            c.Ws.Dispose();
        }
        return (await Open(ct), false);
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> primed = new();
    /// <summary>
    /// A voice nobody used for a while takes the service 5–20 s to start. Asking for one syllable in the background
    /// as soon as the voice is picked moves that wait out of the user's way.
    /// </summary>
    public static void Prime(string shortName)
    {
        if (primed.TryGetValue(shortName, out var t) && (DateTime.UtcNow - t).TotalSeconds < 120) return;
        primed[shortName] = DateTime.UtcNow;
        _ = Task.Run(async () => { try { await Synthesize("1", shortName, 0, 0); } catch { primed.TryRemove(shortName, out _); } });
    }

    /// <summary>One request = one piece of text (keep it under ~2000 chars). rate in %, pitch in Hz. Returns MP3 bytes (24 kHz mono, 96 kbit/s).</summary>
    public static async Task<byte[]> Synthesize(string text, string shortName, double rate, double pitch, CancellationToken ct = default)
    {
        // the service cold-starts rarely used voice/prosody combinations: the first tries may fail or be slow
        int[] wait = { 0, 400, 1000, 2000, 3000 };
        Exception last = null;
        for (int attempt = 0; attempt < wait.Length; attempt++)
        {
            if (wait[attempt] > 0) await Task.Delay(wait[attempt], ct);
            bool pooled = false;
            try
            {
                var (ws, wasPooled) = await Take(ct); pooled = wasPooled;
                using (ws) { var audio = await Request(ws, text, shortName, rate, pitch, ct); primed[shortName] = DateTime.UtcNow; return audio; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                last = ex; Log?.Invoke($"attempt {attempt} ({(pooled ? "pooled" : "fresh")}) failed: {ex.GetType().Name}: {ex.Message}");
                if (pooled) attempt--;                       // a stale pooled socket does not count as a real failure
                else if (attempt == 0) await SyncClock();
            }
        }
        throw last ?? new InvalidOperationException("The speech service returned no audio");
    }

    public static Action<string> Log;
    static async Task<byte[]> Request(ClientWebSocket ws, string text, string shortName, double rate, double pitch, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(30));
        string loc = shortName.Length >= 5 ? shortName[..shortName.IndexOf('-', shortName.IndexOf('-') + 1)] : "en-US";
        string r = (rate >= 0 ? "+" : "") + ((int)Math.Round(rate)).ToString() + "%", p = (pitch >= 0 ? "+" : "") + ((int)Math.Round(pitch)).ToString() + "Hz";
        string ssml = $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{loc}'><voice name='{shortName}'><prosody pitch='{p}' rate='{r}' volume='+0%'>{Esc(text)}</prosody></voice></speak>";
        string msg = $"X-RequestId:{Guid.NewGuid():N}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{Stamp()}Z\r\nPath:ssml\r\n\r\n{ssml}";
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, cts.Token);

        using var audio = new MemoryStream(); var buf = new byte[32768]; using var frame = new MemoryStream();
        while (ws.State == WebSocketState.Open)
        {
            frame.SetLength(0); WebSocketReceiveResult res;
            do { res = await ws.ReceiveAsync(buf, cts.Token); frame.Write(buf, 0, res.Count); } while (!res.EndOfMessage);
            if (res.MessageType == WebSocketMessageType.Close) break;
            var data = frame.GetBuffer(); int len = (int)frame.Length;
            if (res.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(data, 0, len).Contains("Path:turn.end")) break;
            }
            else if (len >= 2)
            {
                int hl = (data[0] << 8) | data[1];
                if (hl + 2 <= len && Encoding.ASCII.GetString(data, 2, hl).Contains("Path:audio")) audio.Write(data, hl + 2, len - hl - 2);
            }
        }
        if (audio.Length == 0) throw new InvalidOperationException("The speech service returned no audio");
        return audio.ToArray();
    }
}

/// <summary>Offline voices installed in Windows (SAPI 5).</summary>
public static class SapiTts
{
    public static List<TtsVoice> Voices()
    {
        var list = new List<TtsVoice>();
        if (!OperatingSystem.IsWindows()) return list;
        try { AddVoices(list); } catch { }
        return list;
    }
    static void AddVoices(List<TtsVoice> list)
    {
        using var s = new System.Speech.Synthesis.SpeechSynthesizer();
        foreach (var v in s.GetInstalledVoices())
        {
            if (!v.Enabled) continue; var i = v.VoiceInfo;
            string loc = i.Culture?.Name ?? "en-US", ln = i.Culture?.EnglishName ?? loc;
            list.Add(new TtsVoice { Id = "sapi:" + i.Name, Name = i.Name.Replace("Microsoft ", "").Replace(" Desktop", ""), Locale = loc, LocaleName = ln, Gender = i.Gender.ToString(), Engine = "System", Tags = "Offline" });
        }
    }
    /// <summary>rate -100..200 (%) mapped to SAPI -10..10. Returns WAV bytes.</summary>
    public static byte[] Synthesize(string text, string voiceName, double rate)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("System voices are available on Windows only");
        return Run(text, voiceName, rate);
    }
    static byte[] Run(string text, string voiceName, double rate)
    {
        using var s = new System.Speech.Synthesis.SpeechSynthesizer();
        s.SelectVoice(voiceName);
        s.Rate = (int)Math.Clamp(Math.Round(rate / 10.0), -10, 10); s.Volume = 100;
        using var ms = new MemoryStream();
        s.SetOutputToWaveStream(ms); s.Speak(text); s.SetOutputToNull();
        return ms.ToArray();
    }
}

