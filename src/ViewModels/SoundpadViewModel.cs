using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxDeck.Audio;
using VoxDeck.Core;
using VoxDeck.Library;
namespace VoxDeck.ViewModels;

public partial class SoundpadViewModel : ObservableObject
{
    readonly AppSettings S = App.Settings;
    readonly Dictionary<string, SoundItem> items = new();            // id -> shared item instance
    readonly Dictionary<string, AudioClip> clips = new(); readonly LinkedList<string> clipOrder = new(); long clipBytes;
    readonly Dictionary<string, byte[]> rawBytes = new();
    readonly Dictionary<int, List<SoundRef>> chartCache = new();
    public List<Chip> Sections { get; } = new();
    public List<Chip> Genres { get; } = new();
    readonly List<Chip> memeChips = new(), siteChips = new();
    [ObservableProperty] List<Chip> chips = new();
    [ObservableProperty] bool canLoadMore;
    [ObservableProperty] bool isLoadingMore;
    string memeKey = "trending", siteKey = "all"; int page = 1; List<SoundItem> onlineList; Func<int, Task<List<SoundRef>>> pager;
    public List<Chip> Suggestions { get; } = new();
    public List<string> SortNames => new() { Loc.T("sort.default"), Loc.T("sort.name"), Loc.T("sort.popular"), Loc.T("sort.short"), Loc.T("sort.long"), Loc.T("sort.new") };

    [ObservableProperty] List<SoundItem> shown = new();
    [ObservableProperty] System.Collections.ObjectModel.ObservableCollection<SoundItem> visible = new();
    [ObservableProperty] List<SoundItem> miniItems = new();
    [ObservableProperty] Chip section;
    [ObservableProperty] string search = "";
    [ObservableProperty] int sortIndex;
    [ObservableProperty] bool isLoading;
    [ObservableProperty] bool isEmpty;
    [ObservableProperty] string emptyText = "";
    [ObservableProperty] string title = "";
    [ObservableProperty] string countText = "";
    [ObservableProperty] bool showGenres;
    [ObservableProperty] bool showSuggestions;
    [ObservableProperty] bool canAdd;
    [ObservableProperty] double volume;
    [ObservableProperty] double speed;
    [ObservableProperty] bool overlap;
    [ObservableProperty] bool loop;
    [ObservableProperty] string searchHint = "";
    public string VolumeText => Volume.ToString("0") + "%";
    public string SpeedText => (Speed / 100).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×";
    int genre; int reqId; CancellationTokenSource searchCts;

    public static string IconFor(SoundRef r)
    {
        if (r.Id.StartsWith("deezer:")) return "Disc3";
        if (r.Id.StartsWith("ov:")) return "Cloud";
        if (r.Id.StartsWith("mi:")) return "Laugh";
        if (r.Id.StartsWith("bbc:")) return "Radio";
        if (r.Id.StartsWith("fs:")) return "AudioLines";
        if (r.Id.StartsWith("mk:")) return "Sparkles";
        if (r.Id.StartsWith("wm:")) return "Globe";
        if (r.Id.StartsWith("local:")) return "HardDrive";
        return CatIcon(r.Category);
    }
    static string CatIcon(string c) => c switch
    {
        "Meme & Reactions" => "Sparkles", "8-Bit" => "Gamepad2", "Drums & Beats" => "Drum", "Signals & Alarms" => "Bell", "Nature & Ambience" => "Waves", "Sci-Fi" => "Rocket",
        "UI" => "MousePointerClick", "Risers & Impacts" => "Zap", "Tunes" => "ListMusic", "Chords" => "Music", _ => c != null && c.StartsWith("Notes") ? "Piano" : "Music"
    };

    public SoundpadViewModel()
    {
        volume = S.PadVolume; speed = S.PadSpeed; overlap = S.PadOverlap; loop = S.PadLoop;
        Sections.Add(new Chip { IsHeader = true, LocKey = "pad.library" });
        Sections.Add(new Chip { Key = "all", LocKey = "pad.all", Icon = Ic.Library });
        Sections.Add(new Chip { Key = "fav", LocKey = "pad.fav", Icon = Ic.Star });
        Sections.Add(new Chip { Key = "recent", LocKey = "pad.recent", Icon = Ic.History });
        Sections.Add(new Chip { Key = "top", LocKey = "pad.top", Icon = Ic.Flame });
        Sections.Add(new Chip { Key = "mine", LocKey = "pad.mine", Icon = Ic.FolderOpen });
        Sections.Add(new Chip { IsHeader = true, LocKey = "pad.online" });
        Sections.Add(new Chip { Key = "memes", LocKey = "pad.memes", Icon = Ic.Laugh });
        Sections.Add(new Chip { Key = "sounds", LocKey = "pad.sounds", Icon = Ic.Globe });
        Sections.Add(new Chip { Key = "charts", LocKey = "pad.charts", Icon = Ic.TrendingUp });
        Sections.Add(new Chip { Key = "songs", LocKey = "pad.songs", Icon = Ic.Disc3 });
        Sections.Add(new Chip { Key = "saved", LocKey = "pad.saved", Icon = Ic.CloudDownload });
        Sections.Add(new Chip { IsHeader = true, LocKey = "pad.builtin" });
        foreach (var c in Synth.Categories) Sections.Add(new Chip { Key = "cat:" + c, Raw = c, Icon = Ic.Get(CatIcon(c)), Count = Synth.All.Count(s => s.Category == c).ToString() });
        foreach (var (id, name) in Online.Genres) Genres.Add(new Chip { Key = id.ToString(), Raw = name, LocKey = id == 0 ? "genre.all" : null, IsSelected = id == 0 });
        Genres[0].Raw = null;
        memeChips.Add(new Chip { Key = "trending", LocKey = "meme.trending", IsSelected = true });
        memeChips.Add(new Chip { Key = "best", LocKey = "meme.best" });
        memeChips.Add(new Chip { Key = "recent", LocKey = "meme.recent" });
        foreach (var (slug, en, ru) in Sources.MemeCategories) memeChips.Add(new Chip { Key = "cat:" + slug, Raw = en, RawRu = ru });
        siteChips.Add(new Chip { Key = "all", LocKey = "site.all", IsSelected = true });
        foreach (var site in Sources.Sites) siteChips.Add(new Chip { Key = site.Key, Raw = site.Name });
        foreach (var q in new[] { "laugh", "applause", "explosion", "scream", "dog bark", "cat meow", "door", "car", "rain", "whoosh", "bell", "fart", "crowd", "siren", "piano", "guitar riff", "drum loop", "jingle", "cartoon", "horror", "game", "robot", "water", "footsteps" })
            Suggestions.Add(new Chip { Key = q, Raw = q });
        foreach (var r in Synth.All) Item(new SoundRef { Id = r.Id, Name = r.Name, Category = r.Category, Dur = r.Dur });
        foreach (var r in S.MySounds) Item(r);
        foreach (var r in S.Favorites) Item(r).IsFav = true;
        foreach (var kv in S.Hotkeys) if (items.TryGetValue(kv.Key, out var it)) it.Hotkey = kv.Value;
        Sections.First(s => s.Key == "all").Count = (Synth.All.Count + S.MySounds.Count).ToString();
        Loc.I.Changed += () => { foreach (var s in Sections) s.Refresh(); foreach (var g in Genres) g.Refresh(); foreach (var g in memeChips) g.Refresh(); foreach (var g in siteChips) g.Refresh(); OnPropertyChanged(nameof(SortNames)); SortIndex = Math.Max(0, SortIndex); Refresh(); };
        Section = Sections.First(s => s.Key == "cat:Meme & Reactions");
        RefreshMini();
    }

    SoundItem Item(SoundRef r)
    {
        if (items.TryGetValue(r.Id, out var it)) return it;
        it = new SoundItem(r); items[r.Id] = it;
        if (S.Hotkeys.TryGetValue(r.Id, out var hk)) it.Hotkey = hk;
        return it;
    }
    public SoundItem Find(string id) => items.TryGetValue(id, out var it) ? it : null;

    partial void OnSectionChanged(Chip value)
    {
        if (value == null || value.IsHeader) return;
        foreach (var s in Sections) s.IsSelected = s == value;
        Refresh();
    }
    partial void OnSearchChanged(string value)
    {
        var k = Section?.Key;
        if (k == "songs" || k == "sounds" || k == "charts" || k == "memes")
        {
            searchCts?.Cancel(); var cts = searchCts = new CancellationTokenSource();
            Task.Delay(450, cts.Token).ContinueWith(t => { if (!t.IsCanceled) Ui.Post(Refresh); });
        }
        else Refresh();
    }
    partial void OnSortIndexChanged(int value) { if (value >= 0) Refresh(); }
    partial void OnVolumeChanged(double value) { S.PadVolume = value; S.SaveSoon(); OnPropertyChanged(nameof(VolumeText)); foreach (var h in live) h.Volume = (float)(value / 100); }
    partial void OnSpeedChanged(double value) { S.PadSpeed = value; S.SaveSoon(); OnPropertyChanged(nameof(SpeedText)); foreach (var h in live) h.Speed = value / 100; }
    partial void OnOverlapChanged(bool value) { S.PadOverlap = value; S.SaveSoon(); }
    partial void OnLoopChanged(bool value) { S.PadLoop = value; S.SaveSoon(); foreach (var h in live) h.Loop = value; }
    readonly List<PlayHandle> live = new();

    public void Refresh()
    {
        var sec = Section; if (sec == null) return;
        int my = ++reqId; string k = sec.Key, q = Search?.Trim() ?? "";
        Title = sec.Title; CanAdd = true; ShowSuggestions = false; CanLoadMore = false; pager = null; onlineList = null; page = 1;
        Chips = k == "charts" ? Genres : k == "memes" ? memeChips : k == "sounds" ? siteChips : null; ShowGenres = Chips != null;
        SearchHint = Loc.T(k == "songs" ? "pad.search.songs" : k == "sounds" ? "pad.search.sounds" : k == "memes" ? "pad.search.memes" : "pad.search");
        IEnumerable<SoundItem> list = null;
        switch (k)
        {
            case "all": list = Synth.All.Select(r => items[r.Id]).Concat(S.MySounds.Select(Item)); break;
            case "fav": list = S.Favorites.Select(Item); break;
            case "recent": list = S.Recent.Select(Item); break;
            case "top": list = S.PlayCounts.OrderByDescending(p => p.Value).Select(p => Find(p.Key)).Where(i => i != null).Take(100); break;
            case "mine": list = S.MySounds.Select(Item); break;
            case "saved": list = S.Favorites.Concat(S.Recent).Where(r => Sources.IsOnline(r.Id) && SoundCache.Has(r.Id)).GroupBy(r => r.Id).Select(g => Item(g.First())); break;
            case "charts": _ = LoadOnline(my, () => Chart(genre), q, true); return;
            case "songs": _ = LoadOnline(my, () => q.Length == 0 ? Chart(0) : Online.DeezerSearch(q), null, false); return;
            case "memes":
            {
                string mk = memeKey, region = MemeRegion();
                pager = q.Length > 0 ? pg => Sources.MyinstantsSearch(q, pg)
                      : mk.StartsWith("cat:") ? pg => Sources.MyinstantsCategory(mk[4..], pg)
                      : pg => Sources.MyinstantsList(mk, region, pg);
                _ = LoadOnline(my, () => pager(1), null, false); return;
            }
            case "sounds":
            {
                if (q.Length == 0) { ShowSuggestions = true; Present(my, new List<SoundItem>(), Loc.T("pad.sounds.hint")); return; }
                string sk = siteKey;
                pager = async pg =>
                {
                    var (found, failed) = await Sources.SearchAll(q, pg, sk == "all" ? null : new[] { sk });
                    if (found.Count == 0 && failed.Count > 0 && pg == 1) throw new HttpRequestException("offline");
                    return found;
                };
                _ = LoadOnline(my, () => pager(1), null, false); return;
            }
            default:
                if (k.StartsWith("cat:")) { var c = k[4..]; list = Synth.All.Where(r => r.Category == c).Select(r => items[r.Id]); }
                break;
        }
        list ??= Enumerable.Empty<SoundItem>();
        if (q.Length > 0) list = list.Where(i => Match(i, q));
        Present(my, list.ToList(), k switch { "fav" => Loc.T("pad.empty.fav"), "recent" => Loc.T("pad.empty.recent"), "top" => Loc.T("pad.empty.recent"), "mine" => Loc.T("pad.empty.mine"), _ => Loc.T("pad.empty") });
    }
    static bool Match(SoundItem i, string q) => q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(p => (i.Name + " " + i.Sub + " " + i.Ref.Category).Contains(p, StringComparison.OrdinalIgnoreCase));

    async Task<List<SoundRef>> Chart(int g)
    {
        if (chartCache.TryGetValue(g, out var c)) return c;
        var r = await Online.DeezerChart(g); if (r.Count > 0) chartCache[g] = r; return r;
    }
    async Task LoadOnline(int my, Func<Task<List<SoundRef>>> loader, string localFilter, bool ranked)
    {
        IsLoading = true; IsEmpty = false;
        try
        {
            var refs = await loader();
            if (my != reqId) return;
            var list = refs.Select(Item).ToList();
            if (!string.IsNullOrEmpty(localFilter)) list = list.Where(i => Match(i, localFilter)).ToList();
            if (pager != null) { onlineList = list; CanLoadMore = list.Count >= 12; }
            Present(my, list, Loc.T("pad.empty.online"));
        }
        catch (Exception)
        {
            if (my == reqId) Present(my, new List<SoundItem>(), Loc.T("pad.offline"));
        }
        finally { if (my == reqId) IsLoading = false; }
    }
    void Present(int my, List<SoundItem> list, string emptyMsg)
    {
        if (my != reqId) return;
        IsLoading = false;
        list = SortIndex switch
        {
            1 => list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            2 => list.OrderByDescending(i => i.Ref.Rank + (S.PlayCounts.TryGetValue(i.Id, out var c) ? c * 1_000_000L : 0)).ToList(),
            3 => list.OrderBy(i => i.Ref.Dur).ToList(),
            4 => list.OrderByDescending(i => i.Ref.Dur).ToList(),
            5 => list.OrderByDescending(i => i.Ref.Added).ToList(),
            _ => list
        };
        Shown = list; IsEmpty = list.Count == 0; EmptyText = emptyMsg;
        // show the first rows immediately, then stream the rest in so big lists never freeze the window
        const int first = 48, batch = 40;
        var vis = new System.Collections.ObjectModel.ObservableCollection<SoundItem>(list.Take(first));
        Visible = vis; int pos = Math.Min(first, list.Count);
        void More()
        {
            if (!ReferenceEquals(Visible, vis) || pos >= list.Count) return;
            int end = Math.Min(pos + batch, list.Count);
            for (; pos < end; pos++) vis.Add(list[pos]);
            Avalonia.Threading.Dispatcher.UIThread.Post(More, Avalonia.Threading.DispatcherPriority.Background);
        }
        if (pos < list.Count) Avalonia.Threading.Dispatcher.UIThread.Post(More, Avalonia.Threading.DispatcherPriority.Background);
        CountText = list.Count == 0 ? "" : string.Format(Loc.T("pad.count"), list.Count);
    }
    void RefreshMini() => MiniItems = S.Favorites.Count > 0 ? S.Favorites.Select(Item).ToList() : Synth.All.Where(r => r.Category == "Meme & Reactions").Take(24).Select(r => items[r.Id]).ToList();

    string MemeRegion()
    {
        try
        {
            var c = System.Globalization.CultureInfo.CurrentUICulture.Name; string cc = c.Contains('-') ? c[(c.LastIndexOf('-') + 1)..].ToLowerInvariant() : "";
            if (Array.IndexOf(Sources.MemeRegions, cc) >= 0) return cc;
        }
        catch { }
        return Loc.I.Lang == "ru" ? "ru" : "us";
    }
    [RelayCommand]
    void SelectGenre(Chip g)
    {
        if (g == null) return;
        var k = Section?.Key;
        if (k == "memes") { memeKey = g.Key; foreach (var x in memeChips) x.IsSelected = x == g; if (!string.IsNullOrEmpty(Search)) { Search = ""; return; } }
        else if (k == "sounds") { siteKey = g.Key; foreach (var x in siteChips) x.IsSelected = x == g; }
        else { genre = int.Parse(g.Key); foreach (var x in Genres) x.IsSelected = x == g; }
        Refresh();
    }
    [RelayCommand]
    async Task LoadMore()
    {
        if (pager == null || onlineList == null || IsLoadingMore) return;
        int my = reqId; var pg = pager; var vis = Visible; IsLoadingMore = true;
        try
        {
            var more = await pg(page + 1);
            if (my != reqId) return;
            page++;
            var have = new HashSet<string>(onlineList.Select(i => i.Id)); int added = 0;
            foreach (var r in more) if (have.Add(r.Id)) { var it = Item(r); onlineList.Add(it); vis.Add(it); added++; }
            if (added == 0) CanLoadMore = false;
            CountText = string.Format(Loc.T("pad.count"), onlineList.Count);
        }
        catch { if (my == reqId) CanLoadMore = false; }
        finally { IsLoadingMore = false; }
    }
    [RelayCommand] void Suggest(Chip c) { if (c != null) Search = c.Key; }

    // ---------- loading & playback ----------
    async Task<AudioClip> LoadClip(SoundItem it)
    {
        if (clips.TryGetValue(it.Id, out var c)) return c;
        var r = it.Ref; AudioClip clip;
        if (r.Id.StartsWith("builtin:")) { var b = Synth.All.First(x => x.Id == r.Id); clip = await Task.Run(() => AudioClip.FromMono(b.Render())); }
        else if (r.Id.StartsWith("local:")) { clip = await Task.Run(() => Decoder.DecodeFile(r.Url)); }
        else
        {
            var bytes = await Raw(it);
            try { clip = await Task.Run(() => Decoder.Decode(bytes)); }
            catch when (!string.IsNullOrEmpty(r.Alt) && r.Url != r.Alt)      // the main file was not decodable: try the original upload
            {
                rawBytes.Remove(it.Id); bytes = await Sources.Web.GetByteArrayAsync(r.Alt);
                clip = await Task.Run(() => Decoder.Decode(bytes)); rawBytes[it.Id] = bytes; SoundCache.Put(it.Id, bytes);
            }
        }
        if (Math.Abs(r.Dur - clip.Seconds) > 0.3 && !r.Id.StartsWith("deezer:")) { r.Dur = clip.Seconds; it.RefreshDur(); }
        clips[it.Id] = clip; clipOrder.AddLast(it.Id); clipBytes += clip.Data.Length * 4L;
        while (clipBytes > 320L * 1024 * 1024 && clipOrder.Count > 1)
        {
            var old = clipOrder.First.Value; clipOrder.RemoveFirst();
            if (clips.Remove(old, out var oc)) clipBytes -= oc.Data.Length * 4L;
            rawBytes.Remove(old);
        }
        return clip;
    }
    async Task<byte[]> Raw(SoundItem it)
    {
        if (rawBytes.TryGetValue(it.Id, out var b)) return b;
        var r = it.Ref;
        b = await Task.Run(() => SoundCache.Get(it.Id));
        if (b == null)
        {
            if (r.Id.StartsWith("deezer:"))
            {
                try { b = await Online.Http.GetByteArrayAsync(r.Url); }
                catch
                {
                    var fresh = await Online.DeezerFreshPreview(r.Id[7..]); if (string.IsNullOrEmpty(fresh)) throw;
                    r.Url = fresh; b = await Online.Http.GetByteArrayAsync(fresh);
                }
            }
            else if (r.Id.StartsWith("ov:")) b = await Online.Http.GetByteArrayAsync(r.Url);
            else b = await Sources.Fetch(r);
            var keep = b; _ = Task.Run(() => SoundCache.Put(it.Id, keep));
        }
        if (rawBytes.Count > 60) rawBytes.Clear();
        rawBytes[it.Id] = b; return b;
    }

    async Task Start(SoundItem it, bool toVoice, bool toMonitor)
    {
        if (it == null) return;
        if (it.IsPlaying && !Overlap) { StopItem(it); return; }
        if (it.IsLoading) return;
        try
        {
            it.IsLoading = true;
            var clip = await LoadClip(it);
            if (!Overlap) App.Engine.StopAll("pad");
            var h = App.Engine.Play(clip, toVoice, toMonitor, (float)(Volume / 100), Speed / 100, Loop, "pad", it);
            it.Active++; it.IsPlaying = true; live.Add(h);
            h.Ended += hh => Ui.Post(() => { live.Remove(hh); it.Active--; if (it.Active <= 0) { it.Active = 0; it.IsPlaying = false; } });
            App.Main.SetNowPlaying(h, it.Name, it.Icon);
            S.PlayCounts[it.Id] = (S.PlayCounts.TryGetValue(it.Id, out var n) ? n : 0) + 1;
            S.Recent.RemoveAll(x => x.Id == it.Id); S.Recent.Insert(0, it.Ref); if (S.Recent.Count > 60) S.Recent.RemoveAt(S.Recent.Count - 1);
            S.SaveSoon();
        }
        catch (Exception ex) { App.Main.Toast(Loc.T("pad.loaderror") + " " + ((ex.InnerException ?? ex).Message is var m && m.Length > 70 ? m[..70] + "…" : m), Ic.Info, true); }
        finally { it.IsLoading = false; }
    }
    void StopItem(SoundItem it) { foreach (var h in live.ToList()) if (h.Owner == it) h.Stop(); }

    [RelayCommand] public Task Play(SoundItem it) => Start(it, true, S.MonitorEnabled);
    [RelayCommand] Task Preview(SoundItem it) => Start(it, false, true);
    [RelayCommand] void StopOne(SoundItem it) { if (it != null) StopItem(it); }
    [RelayCommand] public void StopAll() => App.Engine.StopAll("pad");
    [RelayCommand]
    void ToggleFav(SoundItem it)
    {
        if (it == null) return; it.IsFav = !it.IsFav;
        S.Favorites.RemoveAll(x => x.Id == it.Id); if (it.IsFav) S.Favorites.Add(it.Ref);
        S.SaveSoon(); RefreshMini(); if (Section?.Key == "fav") Refresh();
    }
    [RelayCommand]
    async Task Download(SoundItem it)
    {
        if (it == null) return;
        try
        {
            it.IsLoading = true;
            if (it.IsOnline)
            {
                var raw = await Raw(it); string ext = raw.Length > 4 && raw[0] == 'O' && raw[1] == 'g' ? "ogg" : raw.Length > 4 && raw[0] == 'R' && raw[1] == 'I' ? "wav" : "mp3";
                it.IsLoading = false;
                if (await Ui.SaveBytes(App.MainWindow, (string.IsNullOrEmpty(it.Ref.Sub) ? "" : it.Ref.Sub.Split('·')[0].Trim() + " - ") + it.Name, ext, () => raw)) App.Main.Toast(Loc.T("saved"), Ic.Check);
            }
            else
            {
                var clip = await LoadClip(it); it.IsLoading = false;
                if (await Ui.SaveBytes(App.MainWindow, it.Name, "wav", clip.ToWav)) App.Main.Toast(Loc.T("saved"), Ic.Check);
            }
        }
        catch (Exception ex) { App.Main.Toast(Loc.T("pad.loaderror") + " " + ex.Message, Ic.Info, true); }
        finally { it.IsLoading = false; }
    }
    [RelayCommand]
    async Task AddFiles()
    {
        var w = App.MainWindow; if (w == null) return;
        var files = await w.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true, Title = Loc.T("pad.add"),
            FileTypeFilter = new[] { new FilePickerFileType("Audio") { Patterns = new[] { "*.mp3", "*.wav", "*.ogg", "*.flac", "*.m4a", "*.aac", "*.wma", "*.opus" } }, FilePickerFileTypes.All }
        });
        AddPaths(files.Select(f => f.TryGetLocalPath()).Where(p => p != null));
    }
    public void AddPaths(IEnumerable<string> paths)
    {
        int n = 0;
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) { try { AddPaths(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories).Where(f => AudioExt.Contains(Path.GetExtension(f).ToLowerInvariant())).Take(2000)); } catch { } continue; }
            if (!AudioExt.Contains(Path.GetExtension(p).ToLowerInvariant())) continue;
            string id = "local:" + p; if (S.MySounds.Any(x => x.Id == id)) continue;
            var r = new SoundRef { Id = id, Name = Path.GetFileNameWithoutExtension(p), Category = "My sounds", Sub = Path.GetExtension(p).TrimStart('.').ToUpperInvariant(), Url = p, Added = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            S.MySounds.Add(r); Item(r); n++;
        }
        if (n > 0)
        {
            S.Save(); Sections.First(s => s.Key == "all").Count = (Synth.All.Count + S.MySounds.Count).ToString();
            Section = Sections.First(s => s.Key == "mine"); Refresh();
            App.Main.Toast(string.Format(Loc.T("pad.added"), n), Ic.Check);
        }
    }
    static readonly HashSet<string> AudioExt = new() { ".mp3", ".wav", ".ogg", ".oga", ".flac", ".m4a", ".aac", ".wma", ".opus" };
    [RelayCommand]
    void Remove(SoundItem it)
    {
        if (it == null || !it.IsLocal) return;
        S.MySounds.RemoveAll(x => x.Id == it.Id); S.Favorites.RemoveAll(x => x.Id == it.Id); S.Recent.RemoveAll(x => x.Id == it.Id); S.PlayCounts.Remove(it.Id);
        S.Hotkeys.Remove(it.Id); items.Remove(it.Id); S.Save(); App.Main.ApplyHotkeys();
        Sections.First(s => s.Key == "all").Count = (Synth.All.Count + S.MySounds.Count).ToString();
        Refresh(); RefreshMini();
    }
    [RelayCommand]
    async Task AssignHotkey(SoundItem it)
    {
        if (it == null || App.MainWindow == null) return;
        var dlg = new Views.HotkeyDialog(it.Name, it.Hotkey);
        var res = await dlg.ShowDialog<string>(App.MainWindow);
        if (res == null) return;                       // cancelled
        if (res == "") { S.Hotkeys.Remove(it.Id); it.Hotkey = null; }
        else
        {
            foreach (var other in S.Hotkeys.Where(kv => kv.Value == res && kv.Key != it.Id).Select(kv => kv.Key).ToList()) { S.Hotkeys.Remove(other); if (Find(other) is { } o) o.Hotkey = null; }
            S.Hotkeys[it.Id] = res; it.Hotkey = res;
            if (it.IsOnline && !S.Favorites.Any(f => f.Id == it.Id)) { S.Favorites.Add(it.Ref); it.IsFav = true; RefreshMini(); }
        }
        S.Save(); App.Main.ApplyHotkeys();
    }
    public void PlayById(string id)
    {
        var it = Find(id) ?? (S.Favorites.FirstOrDefault(f => f.Id == id) is { } r ? Item(r) : null);
        if (it != null) _ = Play(it);
    }
}
