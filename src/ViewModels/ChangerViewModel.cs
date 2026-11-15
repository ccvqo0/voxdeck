using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxDeck.Audio;
using VoxDeck.Core;
namespace VoxDeck.ViewModels;

public partial class ChangerViewModel : ObservableObject
{
    readonly AppSettings S = App.Settings;
    List<PresetItem> allPresets = new();
    public List<FxGroup> Groups { get; } = new();
    public List<Chip> Cats { get; } = new();
    readonly Dictionary<string, FxSlider> sliders = new();

    [ObservableProperty] List<PresetItem> presets = new();
    [ObservableProperty] string search = "";
    [ObservableProperty] bool micEnabled;
    [ObservableProperty] bool changerOn;
    [ObservableProperty] bool hearMyself;
    [ObservableProperty] List<DeviceInfo> mics = new();
    [ObservableProperty] DeviceInfo selectedMic;
    [ObservableProperty] double micLevel;
    [ObservableProperty] double outLevel;
    [ObservableProperty] string newPresetName = "";
    [ObservableProperty] string currentName = "";
    [ObservableProperty] string presetCount = "";
    [ObservableProperty] bool isModified;
    string cat = "all"; bool loading;

    public ChangerViewModel()
    {
        changerOn = S.ChangerOn; hearMyself = S.HearMyself;
        var icons = new Dictionary<string, string> { ["tone"] = Ic.SlidersHorizontal, ["mod"] = Ic.AudioWaveform, ["dirt"] = Ic.Flame, ["space"] = Ic.Waves, ["dyn"] = Ic.Zap };
        foreach (var g in new[] { "tone", "mod", "dirt", "space", "dyn" })
        {
            var grp = new FxGroup { Key = g, Icon = icons[g] };
            foreach (var d in FxDefs.All.Where(d => d.Group == g))
            {
                var sl = new FxSlider(d, S.ChangerFx[d.Key], OnSlider); sliders[d.Key] = sl; grp.Sliders.Add(sl);
            }
            Groups.Add(grp);
        }
        foreach (var c in FxPresets.Cats) Cats.Add(new Chip { Key = c, LocKey = "cat." + c, IsSelected = c == "all" });
        RebuildPresets();
        App.Engine.FxEnabled = changerOn; App.Engine.HearMyself = hearMyself;
        App.Engine.MicChain.Set(S.ChangerFx);
        UpdateName();
        Loc.I.Changed += () => { foreach (var p in allPresets) p.Refresh(); foreach (var c in Cats) c.Refresh(); foreach (var s in sliders.Values) s.RefreshName(); UpdateName(); OnPropertyChanged(nameof(Groups)); Filter(); };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            double m = Math.Min(1, Math.Sqrt(App.Engine.MicLevel) * 1.15), o = Math.Min(1, Math.Sqrt(App.Engine.MicOutLevel) * 1.15);
            if (Math.Abs(m - MicLevel) > 0.004) MicLevel = m; if (Math.Abs(o - OutLevel) > 0.004) OutLevel = o;
        };
        timer.Start();
    }

    public void RefreshDevices()
    {
        var list = new List<DeviceInfo> { new("default", Loc.T("device.default")) };
        list.AddRange(App.Engine.Inputs().Where(d => !d.Name.ToLowerInvariant().Contains("cable output")));
        loading = true; Mics = list; SelectedMic = list.FirstOrDefault(d => d.Id == S.MicDevice) ?? list[0]; loading = false;
    }

    public void RebuildPresets()
    {
        allPresets = App.AllPresets().Select(p => new PresetItem(p)).ToList();
        Filter();
    }
    void Filter()
    {
        IEnumerable<PresetItem> q = allPresets;
        if (cat != "all") q = q.Where(p => p.Preset.Cat == cat);
        var s = Search?.Trim();
        if (!string.IsNullOrEmpty(s)) q = q.Where(p => p.Preset.En.Contains(s, StringComparison.OrdinalIgnoreCase) || p.Preset.Ru.Contains(s, StringComparison.OrdinalIgnoreCase));
        var res = q.ToList();
        foreach (var p in res) p.IsSelected = p.Preset.Id == S.ChangerPreset && !IsModified;
        Presets = res;
        PresetCount = string.Format(Loc.T("presets.count"), res.Count, allPresets.Count);
    }
    partial void OnSearchChanged(string value) => Filter();

    void UpdateName()
    {
        var p = allPresets.FirstOrDefault(x => x.Preset.Id == S.ChangerPreset);
        CurrentName = (p?.Name ?? Loc.T("custom")) + (IsModified ? " *" : "");
    }

    void OnSlider(FxSlider s)
    {
        if (loading) return;
        S.ChangerFx[s.Def.Key] = s.Value; S.SaveSoon();
        App.Engine.MicChain.Set(S.ChangerFx);
        if (!IsModified) { IsModified = true; foreach (var p in allPresets) p.IsSelected = false; UpdateName(); }
    }

    void Load(FxParams fx)
    {
        loading = true;
        S.ChangerFx = fx.Clone();
        foreach (var s in sliders.Values) s.SetSilently(S.ChangerFx[s.Def.Key]);
        loading = false;
        App.Engine.MicChain.Set(S.ChangerFx); S.SaveSoon();
    }

    [RelayCommand]
    void SelectCat(Chip c) { if (c == null) return; cat = c.Key; foreach (var x in Cats) x.IsSelected = x == c; Filter(); }

    [RelayCommand]
    public void SelectPreset(PresetItem p)
    {
        if (p == null) return;
        S.ChangerPreset = p.Preset.Id; IsModified = false; Load(p.Preset.P);
        foreach (var x in allPresets) x.IsSelected = x == p;
        UpdateName();
        App.Main.Toast(p.Name, p.Icon);
    }
    [RelayCommand] void Reset() { var p = allPresets.FirstOrDefault(x => x.Preset.Id == "none"); if (p != null) SelectPreset(p); }
    [RelayCommand]
    void SavePreset()
    {
        var name = NewPresetName?.Trim(); if (string.IsNullOrEmpty(name)) { App.Main.Toast(Loc.T("preset.needname"), Ic.Info); return; }
        S.CustomPresets.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        S.CustomPresets.Add(new SavedPreset { Name = name, Fx = S.ChangerFx.Clone() });
        S.ChangerPreset = "custom:" + name; IsModified = false; S.Save(); NewPresetName = "";
        RebuildPresets(); UpdateName(); App.Main.Tts.RebuildFx();
        App.Main.Toast(Loc.T("preset.saved"), Ic.Check);
    }
    [RelayCommand]
    void DeletePreset(PresetItem p)
    {
        if (p == null || !p.IsCustom) return;
        S.CustomPresets.RemoveAll(c => "custom:" + c.Name == p.Preset.Id); S.Save();
        RebuildPresets(); UpdateName(); App.Main.Tts.RebuildFx();
    }

    partial void OnMicEnabledChanged(bool value)
    {
        if (loading) return;
        S.MicEnabled = value; S.SaveSoon();
        if (value)
        {
            App.Engine.StartMic(S.MicDevice);
            if (!App.Engine.MicRunning) { loading = true; MicEnabled = false; loading = false; bool denied = App.Engine.MicDenied; App.Main.Toast(denied ? Loc.T("mic.denied") : App.Engine.LastError ?? Loc.T("mic.error"), Ic.MicOff, true); if (denied) Ui.OpenUrl("ms-settings:privacy-microphone"); }
        }
        else App.Engine.StopMic();
        App.Main.RefreshStatus();
    }
    partial void OnChangerOnChanged(bool value) { S.ChangerOn = value; S.SaveSoon(); App.Engine.FxEnabled = value; App.Main?.RefreshStatus(); }
    partial void OnHearMyselfChanged(bool value) { S.HearMyself = value; S.SaveSoon(); App.Engine.HearMyself = value; }
    partial void OnSelectedMicChanged(DeviceInfo value)
    {
        if (loading || value == null) return;
        S.MicDevice = value.Id; S.SaveSoon();
        if (MicEnabled) App.Engine.StartMic(S.MicDevice);
    }
    public void ToggleChanger() { ChangerOn = !ChangerOn; App.Main.Toast(Loc.T(ChangerOn ? "changer.on" : "changer.off"), Ic.WandSparkles); }
    public void RestoreMic() { if (S.MicEnabled) MicEnabled = true; }
}
