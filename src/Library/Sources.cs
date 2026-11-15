using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace VoxDeck.Library;

/// <summary>Sound websites VoxDeck can browse. None of them needs an account or an API key.</summary>
public static class Sources
{
    public static readonly HttpClient Web = Make();
    static HttpClient Make()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
        h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        h.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        return h;
    }
    static string Dec(string s) => WebUtility.HtmlDecode(s ?? "").Trim();
    static string Q(string s) => Uri.EscapeDataString(s ?? "");

    public sealed record Site(string Key, string Name, string Icon, Func<string, int, Task<List<SoundRef>>> Search);
    /// <summary>Sites used by the "everything" search, in the order results are interleaved.</summary>
    public static readonly Site[] Sites =
    {
        new("mi", "Myinstants", "Laugh", MyinstantsSearch),
        new("bbc", "BBC Sound Effects", "Radio", BbcSearch),
        new("fs", "Freesound", "AudioLines", FreesoundSearch),
        new("mk", "Mixkit", "Sparkles", MixkitSearch),
        new("wm", "Wikimedia Commons", "Globe", CommonsSearch),
        new("ov", "Openverse", "Cloud", (q, p) => Online.OpenverseSearch(q, p)),
    };
    public static string SiteOf(string id)
    {
        int i = id.IndexOf(':'); if (i <= 0) return null;
        string k = id[..i]; foreach (var s in Sites) if (s.Key == k) return s.Name;
        return k == "deezer" ? "Deezer" : null;
    }
    public static bool IsOnline(string id) { int i = id.IndexOf(':'); return i > 0 && id[..i] is "deezer" or "ov" or "mi" or "bbc" or "fs" or "mk" or "wm"; }

    /// <summary>Queries every site at once; slow or failing sites are skipped. Results are interleaved so each site is visible on the first screen.</summary>
    public static async Task<(List<SoundRef> items, List<string> failed)> SearchAll(string q, int page, IEnumerable<string> only = null, int timeoutMs = 9000)
    {
        var sites = Sites.Where(s => only == null || only.Contains(s.Key)).ToList();
        var tasks = sites.Select(async s =>
        {
            try
            {
                var t = s.Search(q, page);
                if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) return (s, list: (List<SoundRef>)null);
                return (s, list: await t);
            }
            catch { return (s, list: (List<SoundRef>)null); }
        }).ToList();
        var done = await Task.WhenAll(tasks);
        var res = new List<SoundRef>(); var seen = new HashSet<string>();
        int max = done.Max(d => d.list?.Count ?? 0);
        for (int i = 0; i < max; i++) foreach (var d in done) if (d.list != null && i < d.list.Count && seen.Add(d.list[i].Id)) res.Add(d.list[i]);
        return (res, done.Where(d => d.list == null).Select(d => d.s.Name).ToList());
    }

    // ---------------- Myinstants: meme buttons ----------------
    public static readonly (string slug, string en, string ru)[] MemeCategories =
    {
        ("memes","Memes","Мемы"), ("games","Games","Игры"), ("reactions","Reactions","Реакции"), ("sound effects","Sound effects","Эффекты"), ("tiktok trends","TikTok","TikTok"), ("viral","Viral","Вирусное"),
        ("anime & manga","Anime","Аниме"), ("movies","Movies","Кино"), ("television","TV","ТВ"), ("music","Music","Музыка"), ("pranks","Pranks","Пранки"), ("sports","Sports","Спорт"), ("politics","Politics","Политика"), ("whatsapp audios","Voice notes","Голосовые"),
    };
    public static readonly string[] MemeRegions = { "us", "ru", "ua", "by", "kz", "gb", "de", "fr", "es", "it", "pl", "tr", "br", "mx", "in", "id", "jp", "kr" };
    static readonly Regex miItem = new(@"background-color:\s*(#[0-9A-Fa-f]{3,8})[^<]*</div>\s*<button[^>]*onclick=""play\('([^']+)'[^""]*""[^>]*>.*?<a href=""/[a-z]{2}/instant/([^""/]+)/""[^>]*class=""instant-link[^""]*""[^>]*>([^<]*)</a>", RegexOptions.Singleline | RegexOptions.Compiled);
    static List<SoundRef> ParseMyinstants(string html)
    {
        var list = new List<SoundRef>();
        foreach (Match m in miItem.Matches(html))
        {
            string name = Dec(m.Groups[4].Value); if (name.Length == 0) continue;
            list.Add(new SoundRef { Id = "mi:" + m.Groups[3].Value, Name = name, Sub = "Myinstants", Category = "Memes", Url = "https://www.myinstants.com" + m.Groups[2].Value, Color = m.Groups[1].Value });
        }
        return list;
    }
    static async Task<List<SoundRef>> MyinstantsPages(string url, int page, int pages)
    {
        var tasks = Enumerable.Range(page, pages).Select(async p => { try { return ParseMyinstants(await Web.GetStringAsync(url + (url.Contains('?') ? "&" : "?") + "page=" + p)); } catch when (p > page) { return new List<SoundRef>(); } });
        var res = new List<SoundRef>(); var seen = new HashSet<string>();
        foreach (var l in await Task.WhenAll(tasks)) foreach (var r in l) if (seen.Add(r.Id)) res.Add(r);
        return res;
    }
    /// <summary>kind: "trending", "best" or "recent". Each call returns 2 site pages (72 buttons).</summary>
    public static Task<List<SoundRef>> MyinstantsList(string kind, string region, int page = 1) =>
        MyinstantsPages(kind == "recent" ? "https://www.myinstants.com/en/recent/" : kind == "best" ? $"https://www.myinstants.com/en/best_of_all_time/{region}/" : $"https://www.myinstants.com/en/index/{region}/", page * 2 - 1, 2);
    public static Task<List<SoundRef>> MyinstantsCategory(string slug, int page = 1) => MyinstantsPages($"https://www.myinstants.com/en/categories/{Q(slug)}/", page * 2 - 1, 2);
    public static Task<List<SoundRef>> MyinstantsSearch(string q, int page = 1) => MyinstantsPages($"https://www.myinstants.com/en/search/?name={Q(q)}", page, 1);

    // ---------------- BBC Sound Effects archive ----------------
    public static async Task<List<SoundRef>> BbcSearch(string q, int page = 1)
    {
        const int size = 24;
        var body = JsonSerializer.Serialize(new { criteria = new { from = (page - 1) * size, size, query = q } });
        using var res = await Web.PostAsync("https://sound-effects-api.bbcrewind.co.uk/api/sfx/search", new StringContent(body, Encoding.UTF8, "application/json"));
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var list = new List<SoundRef>();
        if (!doc.RootElement.TryGetProperty("results", out var arr)) return list;
        foreach (var t in arr.EnumerateArray())
        {
            try
            {
                string id = t.GetProperty("id").GetString(), desc = t.GetProperty("description").GetString() ?? "";
                double dur = t.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() / 1000.0 : 0;
                if (dur > 600) continue;
                desc = desc.Trim(); if (desc.Length > 0) desc = char.ToUpperInvariant(desc[0]) + desc[1..];
                list.Add(new SoundRef { Id = "bbc:" + id, Name = desc, Sub = "BBC Sound Effects", Category = "Sound effects", Url = $"https://sound-effects-media.bbcrewind.co.uk/mp3/{id}.mp3", Dur = dur });
            }
            catch { }
        }
        return list;
    }

    // ---------------- Freesound (web search, HQ previews) ----------------
    static readonly Regex fsItem = new(@"<div\s+class=""bw-player""(?<a>[^>]*?data-mp3=""[^""]+""[^>]*)>", RegexOptions.Singleline | RegexOptions.Compiled);
    static string Attr(string tag, string name) { var m = Regex.Match(tag, name + @"=""([^""]*)"""); return m.Success ? Dec(m.Groups[1].Value) : ""; }
    public static async Task<List<SoundRef>> FreesoundSearch(string q, int page = 1)
    {
        var html = await Web.GetStringAsync($"https://freesound.org/search/?q={Q(q)}&page={page}&f=duration%3A%5B0+TO+120%5D");
        var list = new List<SoundRef>(); var seen = new HashSet<string>();
        foreach (Match m in fsItem.Matches(html))
        {
            string a = m.Groups["a"].Value, id = Attr(a, "data-sound-id"), mp3 = Attr(a, "data-mp3"), title = Attr(a, "data-title"), user = Attr(a, "data-username");
            if (id.Length == 0 || mp3.Length == 0 || !seen.Add(id)) continue;
            double.TryParse(Attr(a, "data-duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dur);
            title = Regex.Replace(title, @"\.(wav|mp3|ogg|flac|aiff?|m4a)$", "", RegexOptions.IgnoreCase).Replace('_', ' ').Trim();
            list.Add(new SoundRef { Id = "fs:" + id, Name = title, Sub = (user.Length > 0 ? user + " · " : "") + "Freesound", Category = "Sound effects", Url = mp3.Replace("-lq.mp3", "-hq.mp3"), Dur = dur });
        }
        return list;
    }

    // ---------------- Mixkit ----------------
    static readonly Regex mkItem = new(@"data-audio-player-preview-url-value=""(?<u>[^""]+)""\s+data-audio-player-item-id-value=""(?<id>\d+)"".*?item-grid-card__title[^>]*>\s*(?:<a[^>]*>\s*)?(?<t>[^<]+).*?data-test-id=""duration""[^>]*>\s*(?<d>[\d:]+)", RegexOptions.Singleline | RegexOptions.Compiled);
    public static async Task<List<SoundRef>> MixkitSearch(string q, int page = 1)
    {
        string slug = Regex.Replace(q.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0) return new List<SoundRef>();      // the catalogue is English only
        var html = await Web.GetStringAsync($"https://mixkit.co/free-sound-effects/discover/{slug}/" + (page > 1 ? "?page=" + page : ""));
        var list = new List<SoundRef>(); var seen = new HashSet<string>();
        foreach (Match m in mkItem.Matches(html))
        {
            if (!seen.Add(m.Groups["id"].Value)) continue;
            double dur = 0; foreach (var part in m.Groups["d"].Value.Split(':')) dur = dur * 60 + (double.TryParse(part, out var v) ? v : 0);
            list.Add(new SoundRef { Id = "mk:" + m.Groups["id"].Value, Name = Dec(m.Groups["t"].Value), Sub = "Mixkit", Category = "Sound effects", Url = m.Groups["u"].Value, Dur = dur });
        }
        return list;
    }

    // ---------------- Wikimedia Commons ----------------
    public static async Task<List<SoundRef>> CommonsSearch(string q, int page = 1)
    {
        const int size = 20;
        var url = "https://commons.wikimedia.org/w/api.php?action=query&format=json&generator=search&gsrnamespace=6&gsrlimit=" + size + "&gsroffset=" + (page - 1) * size +
                  "&gsrsearch=" + Q("filetype:audio " + q) + "&prop=imageinfo&iiprop=url%7Csize%7Cmime%7Cextmetadata&iiextmetadatafilter=LicenseShortName%7CArtist";
        using var doc = JsonDocument.Parse(await Web.GetStringAsync(url));
        var list = new List<(int idx, SoundRef r)>();
        if (!doc.RootElement.TryGetProperty("query", out var qq) || !qq.TryGetProperty("pages", out var pages)) return new List<SoundRef>();
        foreach (var p in pages.EnumerateObject())
        {
            try
            {
                var v = p.Value; var ii = v.GetProperty("imageinfo")[0];
                string mime = ii.TryGetProperty("mime", out var mm) ? mm.GetString() : "", u = ii.GetProperty("url").GetString();
                if (!(mime.Contains("ogg") || mime.Contains("wav") || mime.Contains("mpeg") || mime.Contains("flac") || mime.Contains("mp3"))) continue;
                double dur = ii.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                long size2 = ii.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number ? sz.GetInt64() : 0;
                if (dur > 300 || size2 > 40_000_000) continue;
                string title = v.GetProperty("title").GetString(); if (title.StartsWith("File:")) title = title[5..];
                title = Regex.Replace(title, @"\.(wav|mp3|ogg|oga|flac|opus)$", "", RegexOptions.IgnoreCase).Replace('_', ' ');
                string lic = ii.TryGetProperty("extmetadata", out var em) && em.TryGetProperty("LicenseShortName", out var ls) && ls.TryGetProperty("value", out var lv) ? lv.GetString() : "";
                // Opus-in-Ogg cannot be decoded locally: use the MP3 transcode Commons generates for every audio file
                string name = u[(u.LastIndexOf('/') + 1)..];
                string play = mime.Contains("ogg") || mime.Contains("flac") ? u.Replace("/commons/", "/commons/transcoded/") + "/" + name + ".mp3" : u;
                list.Add((v.TryGetProperty("index", out var ix) ? ix.GetInt32() : 0, new SoundRef { Id = "wm:" + v.GetProperty("pageid").GetInt64(), Name = title, Sub = "Wikimedia" + (string.IsNullOrEmpty(lic) ? "" : " · " + lic), Category = "Sound effects", Url = play, Alt = u, Dur = dur }));
            }
            catch { }
        }
        return list.OrderBy(x => x.idx).Select(x => x.r).ToList();
    }

    /// <summary>Downloads the audio of a sound, trying its fallback address if the main one fails.</summary>
    public static async Task<byte[]> Fetch(SoundRef r)
    {
        try { return await Get(r.Url); }
        catch when (!string.IsNullOrEmpty(r.Alt)) { return await Get(r.Alt); }
    }
    static async Task<byte[]> Get(string url)
    {
        using var res = await Web.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        if (res.Content.Headers.ContentLength > 60_000_000) throw new InvalidDataException("File is too large");
        var b = await res.Content.ReadAsByteArrayAsync();
        if (b.Length < 64) throw new InvalidDataException("Empty file");
        return b;
    }
}

/// <summary>Downloaded sounds are kept on disk, so favourites keep working offline and replay instantly.</summary>
public static class SoundCache
{
    public static long MaxBytes = 600L * 1024 * 1024;
    static string dir;
    static string Dir { get { if (dir == null) { dir = Path.Combine(Core.AppSettings.Dir, "cache"); try { Directory.CreateDirectory(dir); } catch { } } return dir; } }
    static string PathFor(string id) => Path.Combine(Dir, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(id)))[..24] + ".bin");
    public static bool Has(string id) { try { return File.Exists(PathFor(id)); } catch { return false; } }
    public static byte[] Get(string id)
    {
        try { var p = PathFor(id); if (!File.Exists(p)) return null; var b = File.ReadAllBytes(p); try { File.SetLastWriteTimeUtc(p, DateTime.UtcNow); } catch { } return b.Length > 64 ? b : null; }
        catch { return null; }
    }
    public static void Put(string id, byte[] data)
    {
        try { File.WriteAllBytes(PathFor(id), data); if (Random.Shared.Next(12) == 0) Trim(); } catch { }
    }
    public static (int files, long bytes) Stats()
    {
        try { var f = new DirectoryInfo(Dir).GetFiles("*.bin"); return (f.Length, f.Sum(x => x.Length)); } catch { return (0, 0); }
    }
    public static void Trim(long? limit = null)
    {
        try
        {
            var files = new DirectoryInfo(Dir).GetFiles("*.bin").OrderByDescending(f => f.LastWriteTimeUtc).ToList(); long sum = 0;
            foreach (var f in files) { sum += f.Length; if (sum > (limit ?? MaxBytes)) try { f.Delete(); } catch { } }
        }
        catch { }
    }
    public static void Clear() => Trim(0);
}
