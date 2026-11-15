using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxDeck.Audio;
using VoxDeck.Core;
using VoxDeck.Tts;
namespace VoxDeck.ViewModels;

public partial class VoiceItem : ObservableObject
{
    public TtsVoice V { get; }
    public VoiceItem(TtsVoice v) { V = v; Avatar = Ui.GradFor(v.Gender + v.Name); }
    public string Name => V.Name;
    public string Locale => V.LocaleName;
    public string Code { get { var p = V.Locale?.Split('-'); return p == null ? "" : p.Length > 1 ? p[1].ToUpperInvariant() : p[0].ToUpperInvariant(); } }
    public string Initial => string.IsNullOrEmpty(V.Name) ? "?" : V.Name[..1].ToUpperInvariant();
    public string GenderText => V.Gender == "Female" ? Loc.T("female") : V.Gender == "Male" ? Loc.T("male") : V.Gender;
    public string EngineText => V.Engine == "System" ? Loc.T("engine.system") : Loc.T("engine.online");
    public string Tags => V.Tags;
    public bool HasTags => !string.IsNullOrEmpty(V.Tags);
    public IBrush Avatar { get; }
    [ObservableProperty] bool isFav;
    [ObservableProperty] bool isBusy;
    public void Refresh() { OnPropertyChanged(nameof(GenderText)); OnPropertyChanged(nameof(EngineText)); }
}

public partial class TtsViewModel : ObservableObject
{
    readonly AppSettings S = App.Settings;
    List<VoiceItem> all = new();
    public ObservableCollection<string> History { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public List<PresetItem> FxList { get; private set; } = new();

    [ObservableProperty] string text = "";
    [ObservableProperty] List<VoiceItem> voices = new();
    [ObservableProperty] VoiceItem selectedVoice;
    [ObservableProperty] string search = "";
    [ObservableProperty] int languageIndex;
    [ObservableProperty] int genderIndex;
    [ObservableProperty] bool favOnly;
    [ObservableProperty] double rate;
    [ObservableProperty] double pitch;
    [ObservableProperty] double volume = 100;
    [ObservableProperty] PresetItem selectedFx;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool isSpeaking;
    [ObservableProperty] string voiceCount = "";
    [ObservableProperty] bool hasHistory;
    [ObservableProperty] bool clearAfter;
    [ObservableProperty] bool clarity;
    [ObservableProperty] bool autoBalance;

    public string CharCount => $"{Text?.Length ?? 0}";
    public string RateText => (Rate >= 0 ? "+" : "") + Rate.ToString("0") + "%";
    public string PitchText => (Pitch >= 0 ? "+" : "") + Pitch.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " st";
    public string VolumeText => Volume.ToString("0") + "%";
    public string SelectedVoiceText => SelectedVoice == null ? "—" : $"{SelectedVoice.Name} · {SelectedVoice.Locale}";

    CancellationTokenSource cts, warmCts, preCts; TtsJob preJob; PlayHandle current; bool loadingTweak;

    public TtsViewModel()
    {
        rate = S.TtsRate; pitch = S.TtsPitch; volume = S.TtsVolume; clearAfter = S.TtsClearAfter;
        clarity = TtsService.Clarity = S.TtsClarity; autoBalance = TtsService.AutoBalance = S.TtsAutoBalance;
        foreach (var h in S.TtsHistory) History.Add(h);
        hasHistory = History.Count > 0;
        RebuildFx();
        LoadVoices();
        Loc.I.Changed += () => { foreach (var v in all) v.Refresh(); RebuildLanguages(); RebuildFx(); OnPropertyChanged(nameof(SelectedVoiceText)); Filter(); };
    }

    public void RebuildFx()
    {
        string cur = SelectedFx?.Preset.Id ?? S.TtsFx;
        FxList = App.AllPresets().Select(p => new PresetItem(p)).ToList();
        OnPropertyChanged(nameof(FxList));
        SelectedFx = FxList.FirstOrDefault(p => p.Preset.Id == cur) ?? FxList[0];
    }

    void LoadVoices()
    {
        var list = new List<TtsVoice>();
        try
        {
            using var st = Avalonia.Platform.AssetLoader.Open(new Uri("avares://VoxDeck/Assets/edge_voices.json"));
            using var rd = new StreamReader(st); list.AddRange(EdgeTts.Parse(rd.ReadToEnd()));
        }
        catch { }
        SetVoices(list);
        Task.Run(async () =>
        {
            var sapi = SapiTts.Voices();
            List<TtsVoice> fresh = null;
            try { fresh = await EdgeTts.FetchVoices(); } catch { }
            if (sapi.Count > 0 || (fresh != null && fresh.Count > 0))
                Ui.Post(() => { var l = new List<TtsVoice>(sapi); l.AddRange(fresh != null && fresh.Count > 50 ? fresh : list); SetVoices(l); });
        });
    }

    void SetVoices(List<TtsVoice> list)
    {
        string keep = SelectedVoice?.V.Id ?? S.TtsVoice;
        all = list.OrderBy(v => v.LocaleName).ThenBy(v => v.Name).Select(v => new VoiceItem(v) { IsFav = S.FavVoices.Contains(v.Id) }).ToList();
        RebuildLanguages();
        Filter();
        var sel = all.FirstOrDefault(v => v.V.Id == keep);
        if (sel == null)
        {
            string pref = Loc.I.Lang == "ru" ? "edge:ru-RU-SvetlanaNeural" : "edge:en-US-AriaNeural";
            sel = all.FirstOrDefault(v => v.V.Id == pref) ?? all.FirstOrDefault(v => v.V.Locale.StartsWith(Loc.I.Lang)) ?? all.FirstOrDefault();
        }
        SelectedVoice = sel;
    }

    List<string> langKeys = new();
    void RebuildLanguages()
    {
        int keepIdx = LanguageIndex; string keepKey = keepIdx > 0 && keepIdx < langKeys.Count ? langKeys[keepIdx] : null;
        langKeys = new List<string> { null };
        langKeys.AddRange(all.Select(v => v.V.LocaleName).Distinct().OrderBy(x => x));
        Languages.Clear(); Languages.Add(Loc.T("all.languages"));
        foreach (var k in langKeys.Skip(1)) Languages.Add(k);
        LanguageIndex = keepKey != null ? Math.Max(0, langKeys.IndexOf(keepKey)) : 0;
    }

    void Filter()
    {
        IEnumerable<VoiceItem> q = all;
        if (LanguageIndex > 0 && LanguageIndex < langKeys.Count) { var k = langKeys[LanguageIndex]; q = q.Where(v => v.V.LocaleName == k); }
        if (GenderIndex == 1) q = q.Where(v => v.V.Gender == "Female"); else if (GenderIndex == 2) q = q.Where(v => v.V.Gender == "Male");
        if (FavOnly) q = q.Where(v => v.IsFav);
        var s = Search?.Trim();
        if (!string.IsNullOrEmpty(s))
        {
            var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            q = q.Where(v => parts.All(p => (v.V.Name + " " + v.V.LocaleName + " " + v.V.Locale + " " + v.V.Gender + " " + v.V.Tags + " " + v.V.Engine).Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
        var res = q.OrderByDescending(v => v.IsFav).ToList();
        var keep = SelectedVoice;
        Voices = res;
        if (keep != null && res.Contains(keep)) SelectedVoice = keep; else if (keep != null) { selectedVoice = keep; OnPropertyChanged(nameof(SelectedVoice)); }
        VoiceCount = string.Format(Loc.T("voices.count"), res.Count, all.Count);
    }

    partial void OnSearchChanged(string value) => Filter();
    partial void OnLanguageIndexChanged(int value) { if (value >= 0) Filter(); }
    partial void OnGenderIndexChanged(int value) => Filter();
    partial void OnFavOnlyChanged(bool value) => Filter();
    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(CharCount));
        Prefetch();
    }
    /// <summary>While the user is still typing, the phrase is already being prepared, so "Play" usually answers instantly.</summary>
    void Prefetch()
    {
        warmCts?.Cancel(); preCts?.Cancel(); preJob = null;
        var voice = SelectedVoice; if (voice == null || voice.V.Engine == "System") return;
        var c = warmCts = new CancellationTokenSource();
        Task.Delay(550, c.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Ui.Post(() =>
            {
                if (c.IsCancellationRequested) return;
                EdgeTts.Warm();
                var txt = Text?.Trim();
                if (string.IsNullOrEmpty(txt) || txt.Length < 2 || txt.Length > 1500 || IsBusy) return;
                preCts = new CancellationTokenSource();
                preJob = TtsService.Start(txt, voice.V, Rate, Pitch, Volume, SelectedFx?.Preset.P, preCts.Token);
            });
        });
    }
    void SaveTweak()
    {
        if (loadingTweak || SelectedVoice == null) return;
        if (Rate == 0 && Pitch == 0 && Volume == 100) S.VoiceTweaks.Remove(SelectedVoice.V.Id); else S.VoiceTweaks[SelectedVoice.V.Id] = new[] { Rate, Pitch, Volume };
    }
    partial void OnRateChanged(double value) { S.TtsRate = value; SaveTweak(); S.SaveSoon(); OnPropertyChanged(nameof(RateText)); if (!loadingTweak) Prefetch(); }
    partial void OnPitchChanged(double value) { S.TtsPitch = value; SaveTweak(); S.SaveSoon(); OnPropertyChanged(nameof(PitchText)); if (!loadingTweak) Prefetch(); }
    partial void OnVolumeChanged(double value) { S.TtsVolume = value; SaveTweak(); S.SaveSoon(); OnPropertyChanged(nameof(VolumeText)); if (!loadingTweak) Prefetch(); }
    partial void OnClearAfterChanged(bool value) { S.TtsClearAfter = value; S.SaveSoon(); }
    partial void OnClarityChanged(bool value) { S.TtsClarity = TtsService.Clarity = value; S.SaveSoon(); }
    partial void OnAutoBalanceChanged(bool value) { S.TtsAutoBalance = TtsService.AutoBalance = value; S.SaveSoon(); OnPropertyChanged(nameof(RateText)); }
    partial void OnSelectedVoiceChanged(VoiceItem value)
    {
        if (value != null)
        {
            S.TtsVoice = value.V.Id; S.SaveSoon();
            if (value.V.Engine != "System") { EdgeTts.Warm(); EdgeTts.Prime(value.V.Id[5..]); }
            // each voice keeps its own speed / pitch / volume
            loadingTweak = true;
            if (S.VoiceTweaks.TryGetValue(value.V.Id, out var t) && t.Length >= 3) { Rate = t[0]; Pitch = t[1]; Volume = t[2]; } else { Rate = 0; Pitch = 0; Volume = 100; }
            loadingTweak = false;
            Prefetch();
        }
        OnPropertyChanged(nameof(SelectedVoiceText));
    }
    partial void OnSelectedFxChanged(PresetItem value) { if (value != null) { S.TtsFx = value.Preset.Id; S.SaveSoon(); Prefetch(); } }
    [RelayCommand] void ResetVoice() { Rate = 0; Pitch = 0; Volume = 100; }

    TtsJob Begin(string txt, VoiceItem voice)
    {
        warmCts?.Cancel();
        var mine = new CancellationTokenSource();
        var job = TtsService.Start(txt, voice.V, Rate, Pitch, Volume, SelectedFx?.Preset.P, mine.Token);
        cts?.Cancel();
        if (ReferenceEquals(job, preJob)) { cts = preCts; preCts = null; preJob = null; mine.Dispose(); }   // the phrase prepared while typing is the one being played now
        else { cts = mine; preCts?.Cancel(); preCts = null; preJob = null; }
        return job;
    }

    async Task Run(bool toVoice, bool toMonitor)
    {
        var txt = Text?.Trim(); var voice = SelectedVoice;
        if (string.IsNullOrEmpty(txt)) { App.Main.Toast(Loc.T("tts.empty"), Ic.Info); return; }
        if (voice == null) { App.Main.Toast(Loc.T("tts.novoice"), Ic.Info); return; }
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var job = Begin(txt, voice);
            await job.First;                       // playback starts with the first sentence, the rest streams in behind it
            current?.Stop();
            var h = current = App.Engine.Play(job.Clip, toVoice, toMonitor, 1, 1, false, "tts");
            IsSpeaking = true; App.Main.SetNowPlaying(h, voice.Name + ": " + (txt.Length > 42 ? txt[..42] + "…" : txt), Ic.MessageSquareText);
            h.Ended += hh => Ui.Post(() => { if (current == hh) IsSpeaking = false; });
            _ = job.Completion.ContinueWith(t => { if (t.IsFaulted && !(t.Exception?.InnerException is OperationCanceledException)) Ui.Post(() => App.Main.Toast(Loc.T("tts.error") + " " + Short(t.Exception), Ic.Info, true)); });
            AddHistory(txt);
            if (toVoice && ClearAfter) Text = "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.Main.Toast(Loc.T("tts.error") + " " + Short(ex), Ic.Info, true); }
        finally { IsBusy = false; }
    }
    static string Short(Exception ex) { while (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException; var m = (ex.InnerException ?? ex).Message; return m.Length > 90 ? m[..90] + "…" : m; }

    void AddHistory(string txt)
    {
        History.Remove(txt); History.Insert(0, txt);
        while (History.Count > 30) History.RemoveAt(History.Count - 1);
        S.TtsHistory = History.ToList(); S.SaveSoon(); HasHistory = true;
    }

    [RelayCommand] Task Speak() => Run(true, S.MonitorEnabled);
    [RelayCommand] Task Preview() => Run(false, true);
    [RelayCommand] void Stop() { cts?.Cancel(); preCts?.Cancel(); App.Engine.StopAll("tts"); IsSpeaking = false; }
    [RelayCommand] void UseHistory(string t) { if (t != null) Text = t; }
    [RelayCommand] void ClearHistory() { History.Clear(); S.TtsHistory.Clear(); S.SaveSoon(); HasHistory = false; }
    [RelayCommand] void ClearText() => Text = "";
    [RelayCommand]
    void ToggleFav(VoiceItem v)
    {
        if (v == null) return; v.IsFav = !v.IsFav;
        if (v.IsFav) { if (!S.FavVoices.Contains(v.V.Id)) S.FavVoices.Add(v.V.Id); } else S.FavVoices.Remove(v.V.Id);
        S.SaveSoon(); if (FavOnly) Filter();
    }
    [RelayCommand]
    async Task Download()
    {
        var txt = Text?.Trim(); var voice = SelectedVoice;
        if (string.IsNullOrEmpty(txt) || voice == null) { App.Main.Toast(Loc.T("tts.empty"), Ic.Info); return; }
        if (IsBusy) return; IsBusy = true;
        try
        {
            var job = Begin(txt, voice); await job.Completion; var clip = job.Clip;
            if (await Ui.SaveBytes(App.MainWindow, voice.Name + " - " + (txt.Length > 30 ? txt[..30] : txt), "wav", clip.ToWav)) App.Main.Toast(Loc.T("saved"), Ic.Check);
        }
        catch (Exception ex) { App.Main.Toast(Loc.T("tts.error") + " " + Short(ex), Ic.Info, true); }
        finally { IsBusy = false; }
    }

    static readonly Dictionary<string, string> Samples = new()
    {
        ["en"] = "Hi! This is how my voice sounds.", ["ru"] = "Привет! Вот так звучит мой голос.", ["uk"] = "Привіт! Ось так звучить мій голос.", ["be"] = "Прывітанне! Вось так гучыць мой голас.",
        ["de"] = "Hallo! So klingt meine Stimme.", ["fr"] = "Bonjour ! Voici le son de ma voix.", ["es"] = "¡Hola! Así suena mi voz.", ["it"] = "Ciao! Ecco come suona la mia voce.",
        ["pt"] = "Olá! É assim que a minha voz soa.", ["pl"] = "Cześć! Tak brzmi mój głos.", ["tr"] = "Merhaba! Sesim böyle duyuluyor.", ["nl"] = "Hallo! Zo klinkt mijn stem.",
        ["ja"] = "こんにちは!これが私の声です。", ["zh"] = "你好!这就是我的声音。", ["ko"] = "안녕하세요! 제 목소리는 이렇습니다.", ["ar"] = "مرحبا! هكذا يبدو صوتي.", ["hi"] = "नमस्ते! मेरी आवाज़ ऐसी है।",
        ["cs"] = "Ahoj! Takhle zní můj hlas.", ["sv"] = "Hej! Så här låter min röst.", ["kk"] = "Сәлем! Менің дауысым осылай естіледі.", ["id"] = "Halo! Beginilah suara saya.", ["vi"] = "Xin chào! Đây là giọng nói của tôi.",
    };
    [RelayCommand]
    async Task PreviewVoice(VoiceItem v)
    {
        if (v == null || v.IsBusy) return;
        v.IsBusy = true;
        try
        {
            string sample = Samples.TryGetValue(v.V.Lang, out var s) ? s : v.V.Name;
            var clip = await TtsService.Speak(sample, v.V, 0, 0, 100);
            App.Engine.StopAll("ttsprev");
            var h = App.Engine.Play(clip, false, true, 1, 1, false, "ttsprev");
            App.Main.SetNowPlaying(h, v.Name + " · " + v.Locale, Ic.Headphones);
        }
        catch (Exception ex) { App.Main.Toast(Loc.T("tts.error") + " " + Short(ex), Ic.Info, true); }
        finally { v.IsBusy = false; }
    }
}
