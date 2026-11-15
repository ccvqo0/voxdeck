using System.Text.Json;
namespace VoxDeck.Library;

public class SoundRef
{
    public string Id { get; set; }        // builtin:..., local:path, deezer:123, ov:uuid
    public string Name { get; set; }
    public string Sub { get; set; }
    public string Category { get; set; }
    public string Url { get; set; }
    public string Image { get; set; }
    public string Alt { get; set; }       // fallback address
    public string Color { get; set; }     // tile colour given by the site
    public double Dur { get; set; }
    public long Rank { get; set; }
    public long Added { get; set; }
}

public static class Online
{
    public static readonly HttpClient Http = Make();
    static HttpClient Make()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(25) };
        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "VoxDeck/1.0 (desktop soundboard)");
        return h;
    }

    public static readonly (int id, string name)[] Genres =
    {
        (0,"All"),(132,"Pop"),(116,"Rap/Hip Hop"),(152,"Rock"),(113,"Dance"),(165,"R&B"),(85,"Alternative"),(106,"Electro"),(466,"Folk"),(144,"Reggae"),(129,"Jazz"),
        (84,"Country"),(98,"Classical"),(173,"Films/Games"),(464,"Metal"),(169,"Soul & Funk"),(153,"Blues"),(197,"Latin Music"),(122,"Reggaeton"),(16,"Asian Music"),(2,"African Music"),(75,"Brazilian Music"),(81,"Indian Music"),(95,"Kids")
    };

    static List<SoundRef> ParseDeezer(string json)
    {
        var list = new List<SoundRef>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data)) return list;
        foreach (var t in data.EnumerateArray())
        {
            try
            {
                string prev = t.TryGetProperty("preview", out var pv) ? pv.GetString() : null; if (string.IsNullOrEmpty(prev)) continue;
                string artist = t.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var an) ? an.GetString() : "";
                string img = t.TryGetProperty("album", out var al) && al.TryGetProperty("cover_medium", out var cm) ? cm.GetString() : null;
                list.Add(new SoundRef { Id = "deezer:" + t.GetProperty("id").GetInt64(), Name = t.GetProperty("title").GetString(), Sub = artist, Category = "Songs", Url = prev, Image = img, Dur = 30, Rank = t.TryGetProperty("rank", out var rk) && rk.ValueKind == JsonValueKind.Number ? rk.GetInt64() : 0 });
            }
            catch { }
        }
        return list;
    }
    public static async Task<List<SoundRef>> DeezerChart(int genre, int limit = 100) => ParseDeezer(await Http.GetStringAsync($"https://api.deezer.com/chart/{genre}/tracks?limit={limit}"));
    public static async Task<List<SoundRef>> DeezerSearch(string q, int limit = 60) => ParseDeezer(await Http.GetStringAsync($"https://api.deezer.com/search?q={Uri.EscapeDataString(q)}&limit={limit}"));
    public static async Task<string> DeezerFreshPreview(string id)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://api.deezer.com/track/" + id));
        return doc.RootElement.TryGetProperty("preview", out var p) ? p.GetString() : null;
    }

    /// <summary>Openverse: Creative-Commons licensed sounds (Freesound, Wikimedia, Jamendo...).</summary>
    public static async Task<List<SoundRef>> OpenverseSearch(string q, int page = 1)
    {
        var list = new List<SoundRef>();
        var json = await Http.GetStringAsync($"https://api.openverse.org/v1/audio/?q={Uri.EscapeDataString(q)}&page_size=20&page={page}&filter_dead=true&mature=false");
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var res)) return list;
        foreach (var t in res.EnumerateArray())
        {
            try
            {
                string url = t.GetProperty("url").GetString(); if (string.IsNullOrEmpty(url)) continue;
                string ft = t.TryGetProperty("filetype", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : "";
                if (ft != "" && ft != "mp3" && ft != "ogg" && ft != "wav" && ft != "oga") continue;
                double dur = t.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() / 1000.0 : 0;
                if (dur > 600) continue;
                string creator = t.TryGetProperty("creator", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "";
                string lic = t.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString().ToUpperInvariant() : "";
                string img = t.TryGetProperty("thumbnail", out var th) && th.ValueKind == JsonValueKind.String ? th.GetString() : null;
                list.Add(new SoundRef { Id = "ov:" + t.GetProperty("id").GetString(), Name = t.GetProperty("title").GetString(), Sub = (creator + (lic != "" ? " · " + (lic == "CC0" || lic == "PDM" ? lic : "CC " + lic) : "")).Trim(' ', '·'), Category = "Sounds", Url = url, Image = img, Dur = dur });
            }
            catch { }
        }
        return list;
    }
}
