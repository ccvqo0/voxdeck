using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VoxDeck.Audio;
using VoxDeck.Core;
namespace VoxDeck.ViewModels;

public partial class ThemeItem : ObservableObject
{
    public ThemeDef Def { get; }
    public ThemeItem(ThemeDef d) { Def = d; }
    public string Name => Def.Name;
    [ObservableProperty] bool isSelected;
}
public record LangItem(string Code, string Name) { public override string ToString() => Name; }

public partial class SettingsViewModel : ObservableObject
{
    readonly AppSettings S = App.Settings; bool loading;
    public List<ThemeItem> Themes { get; } = Core.Themes.All.Select(t => new ThemeItem(t)).ToList();
    public List<LangItem> Langs { get; } = new() { new("en", "English"), new("ru", "Русский") };
    [ObservableProperty] LangItem lang;
    [ObservableProperty] double windowOpacity;
    [ObservableProperty] bool alwaysOnTop;
    [ObservableProperty] bool blur;
    [ObservableProperty] bool animations;
    [ObservableProperty] List<DeviceInfo> voiceDevices = new();
    [ObservableProperty] List<DeviceInfo> monitorDevices = new();
    [ObservableProperty] DeviceInfo voiceDevice;
    [ObservableProperty] DeviceInfo monitorDevice;
    [ObservableProperty] bool monitorEnabled;
    [ObservableProperty] double voiceVolume;
    [ObservableProperty] double monitorVolume;
    [ObservableProperty] double miniOpacity;
    [ObservableProperty] string stopHotkey;
    [ObservableProperty] string changerHotkey;
    public string OpacityText => WindowOpacity.ToString("0") + "%";
    public string MiniOpacityText => MiniOpacity.ToString("0") + "%";
    public double MiniOpacity01 => MiniOpacity / 100.0;
    public string VoiceVolumeText => VoiceVolume.ToString("0") + "%";
    public string MonitorVolumeText => MonitorVolume.ToString("0") + "%";
    public string ThemeCount => string.Format(Loc.T("set.themes.count"), Themes.Count);
    public string Version => "VoxDeck 2.0.0";
    public string DataDir => AppSettings.Dir;

    public SettingsViewModel()
    {
        loading = true;
        lang = Langs.FirstOrDefault(l => l.Code == Loc.I.Lang) ?? Langs[0];
        windowOpacity = S.WindowOpacity * 100; alwaysOnTop = S.AlwaysOnTop; blur = S.Blur; animations = S.Animations;
        monitorEnabled = S.MonitorEnabled; voiceVolume = S.VoiceVolume; monitorVolume = S.MonitorVolume; miniOpacity = S.MiniOpacity;
        stopHotkey = S.StopHotkey; changerHotkey = S.ChangerHotkey;
        foreach (var t in Themes) t.IsSelected = t.Name == Core.Themes.Current.Name;
        loading = false;
        Loc.I.Changed += () => { OnPropertyChanged(nameof(ThemeCount)); RefreshDevices(); };
    }

    public void RefreshDevices()
    {
        loading = true;
        var outs = App.Engine.Outputs();
        var v = new List<DeviceInfo> { new("auto", Loc.T("device.auto")), new("none", Loc.T("device.none")) }; v.AddRange(outs);
        var m = new List<DeviceInfo> { new("default", Loc.T("device.default")) }; m.AddRange(outs.Where(d => !AudioEngine.LooksVirtual(d.Name) || d.Id == S.MonitorDevice));
        VoiceDevices = v; MonitorDevices = m;
        VoiceDevice = v.FirstOrDefault(d => d.Id == S.VoiceDevice) ?? v[0];
        MonitorDevice = m.FirstOrDefault(d => d.Id == S.MonitorDevice) ?? m[0];
        loading = false;
    }

    [RelayCommand]
    void SelectTheme(ThemeItem t)
    {
        if (t == null) return;
        foreach (var x in Themes) x.IsSelected = x == t;
        S.Theme = t.Name; S.SaveSoon(); Core.Themes.Apply(t.Name, S.Blur); App.Main.RaiseWindowChanged();
    }
    [RelayCommand] void Refresh() { RefreshDevices(); App.Main.Reconfigure(); App.Main.Changer.RefreshDevices(); }
    [RelayCommand] void OpenData() => Ui.OpenUrl(AppSettings.Dir);
    public MainViewModel Main => App.Main;
    [ObservableProperty] string cacheText = "";
    public void RefreshCache()
    {
        Task.Run(() =>
        {
            var (files, bytes) = Library.SoundCache.Stats();
            string t = Loc.T("set.cache") + ": " + (bytes < 1 << 20 ? (bytes / 1024) + " KB" : (bytes / 1048576.0).ToString("0.#") + " MB");
            Ui.Post(() => CacheText = t);
        });
    }
    [RelayCommand] void ClearCache() { Task.Run(() => { try { Library.SoundCache.Clear(); } catch { } RefreshCache(); }); }

    partial void OnLangChanged(LangItem value) { if (loading || value == null) return; S.Language = value.Code; S.SaveSoon(); Loc.I.Set(value.Code); }
    partial void OnWindowOpacityChanged(double value) { OnPropertyChanged(nameof(OpacityText)); if (loading) return; S.WindowOpacity = value / 100; S.SaveSoon(); App.Main.RaiseWindowChanged(); }
    partial void OnAlwaysOnTopChanged(bool value) { if (loading) return; S.AlwaysOnTop = value; S.SaveSoon(); App.Main.RaiseWindowChanged(); }
    partial void OnBlurChanged(bool value) { if (loading) return; S.Blur = value; S.SaveSoon(); Core.Themes.Apply(S.Theme, value); App.Main.RaiseWindowChanged(); }
    partial void OnAnimationsChanged(bool value) { if (loading) return; S.Animations = value; S.SaveSoon(); App.Main.RaiseWindowChanged(); }
    partial void OnVoiceDeviceChanged(DeviceInfo value) { if (loading || value == null) return; S.VoiceDevice = value.Id; S.SaveSoon(); App.Main.Reconfigure(); }
    partial void OnMonitorDeviceChanged(DeviceInfo value) { if (loading || value == null) return; S.MonitorDevice = value.Id; S.SaveSoon(); App.Main.Reconfigure(); }
    partial void OnMonitorEnabledChanged(bool value) { if (loading) return; S.MonitorEnabled = value; S.SaveSoon(); }
    partial void OnVoiceVolumeChanged(double value) { OnPropertyChanged(nameof(VoiceVolumeText)); S.VoiceVolume = value; App.Engine.VoiceBus.Volume = (float)(value / 100); if (!loading) S.SaveSoon(); }
    partial void OnMonitorVolumeChanged(double value) { OnPropertyChanged(nameof(MonitorVolumeText)); S.MonitorVolume = value; App.Engine.MonitorBus.Volume = (float)(value / 100); if (!loading) S.SaveSoon(); }
    partial void OnMiniOpacityChanged(double value) { OnPropertyChanged(nameof(MiniOpacityText)); OnPropertyChanged(nameof(MiniOpacity01)); if (loading) return; S.MiniOpacity = value; S.SaveSoon(); }
    partial void OnStopHotkeyChanged(string value) { if (loading) return; S.StopHotkey = value ?? ""; S.SaveSoon(); App.Main.ApplyHotkeys(); }
    partial void OnChangerHotkeyChanged(string value) { if (loading) return; S.ChangerHotkey = value ?? ""; S.SaveSoon(); App.Main.ApplyHotkeys(); }
}

public partial class MainViewModel : ObservableObject
{
    readonly AppSettings S = App.Settings;
    public TtsViewModel Tts { get; private set; }
    public SoundpadViewModel Pad { get; private set; }
    public ChangerViewModel Changer { get; private set; }
    public SettingsViewModel Set { get; private set; }

    [ObservableProperty] int page;
    [ObservableProperty] string islandText = "";
    [ObservableProperty] string islandIcon = Ic.AudioLines;
    [ObservableProperty] bool islandActive;
    [ObservableProperty] bool islandError;
    [ObservableProperty] bool voiceReady;
    [ObservableProperty] string voiceTip;
    [ObservableProperty] string voiceText = "";
    [ObservableProperty] string voiceHint = "";
    [ObservableProperty] string monitorText = "";
    public bool IsPage0 => Page == 0; public bool IsPage1 => Page == 1; public bool IsPage2 => Page == 2; public bool IsPage3 => Page == 3;
    public bool NoVoice => !VoiceReady;
    public event Action WindowChanged;
    public event Action HotkeysChanged;
    public void RaiseWindowChanged() => WindowChanged?.Invoke();
    public void ApplyHotkeys() => HotkeysChanged?.Invoke();

    PlayHandle now; string nowText, nowIcon; DateTime toastUntil; string toastText, toastIcon; bool toastErr;

    public void Init()
    {
        Set = new SettingsViewModel(); Tts = new TtsViewModel(); Pad = new SoundpadViewModel(); Changer = new ChangerViewModel();
        App.Engine.VoiceBus.Volume = (float)(S.VoiceVolume / 100); App.Engine.MonitorBus.Volume = (float)(S.MonitorVolume / 100);
        Reconfigure();
        Set.RefreshDevices(); Changer.RefreshDevices(); InitSystemMic(); Changer.RestoreMic();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        t.Tick += (_, _) => RefreshStatus(); t.Start();
        Loc.I.Changed += () => { UpdateDeviceTexts(); RefreshStatus(); };
        RefreshStatus();
    }

    public void Reconfigure()
    {
        bool mic = App.Engine.MicRunning;
        App.Engine.Configure(S.VoiceDevice, S.MonitorDevice, true);
        UpdateDeviceTexts();
        if (!string.IsNullOrEmpty(App.Engine.LastError)) Toast(App.Engine.LastError, Ic.Info, true);
    }
    void UpdateDeviceTexts()
    {
        VoiceReady = App.Engine.VoiceReady;
        VoiceText = VoiceReady ? Loc.T("voice.on") : Loc.T("voice.missing"); VoiceTip = VoiceReady ? App.Engine.VoiceDeviceName : null;
        CanSystemMic = VoiceReady && Platform.VirtualMic.Installed;
        VoiceHint = !VoiceReady ? Loc.T("voice.missing.hint") : SystemMic && CanSystemMic ? Loc.T("voice.system.hint") : Loc.T("voice.ready.hint");
        OnPropertyChanged(nameof(CanInstall)); OnPropertyChanged(nameof(CanRemove));
        MonitorText = App.Engine.MonitorDeviceName ?? "—";
    }
    partial void OnVoiceReadyChanged(bool value) => OnPropertyChanged(nameof(NoVoice));
    partial void OnPageChanged(int value) { OnPropertyChanged(nameof(IsPage0)); OnPropertyChanged(nameof(IsPage1)); OnPropertyChanged(nameof(IsPage2)); OnPropertyChanged(nameof(IsPage3)); if (value == 3) Set.RefreshCache(); }

    public void Toast(string text, string icon = null, bool error = false)
    {
        Ui.Post(() => { toastText = text; toastIcon = icon ?? Ic.Info; toastErr = error; toastUntil = DateTime.UtcNow.AddSeconds(error ? 5 : 2.2); RefreshStatus(); });
    }
    public void SetNowPlaying(PlayHandle h, string text, string icon)
    {
        now = h; nowText = text; nowIcon = icon; toastUntil = DateTime.MinValue;
        h.Ended += hh => Ui.Post(() => { if (now == hh) { now = null; RefreshStatus(); } });
        RefreshStatus();
    }
    public void RefreshStatus()
    {
        if (DateTime.UtcNow < toastUntil) { IslandText = toastText; IslandIcon = toastIcon; IslandActive = true; IslandError = toastErr; return; }
        IslandError = false;
        if (now != null && !now.Stopped) { IslandText = nowText; IslandIcon = nowIcon; IslandActive = true; return; }
        if (App.Engine.MicRunning)
        {
            IslandText = S.ChangerOn ? Loc.T("status.changer") + " · " + (Changer?.CurrentName ?? "") : Loc.T("status.mic");
            IslandIcon = S.ChangerOn ? Ic.WandSparkles : Ic.Mic; IslandActive = true; return;
        }
        IslandText = Loc.T("status.ready"); IslandIcon = Ic.AudioLines; IslandActive = false;
    }

    [RelayCommand] void Go(string p) { if (int.TryParse(p, out var i)) Page = i; }
    [RelayCommand] public void StopAll() { App.Engine.StopAll(); Tts.StopCommand.Execute(null); now = null; Toast(Loc.T("stopped"), Ic.CircleStop); }
    [RelayCommand] void GetCable() => Ui.OpenUrl("https://vb-audio.com/Cable/");

    // ---------- virtual microphone ----------
    [ObservableProperty] bool isInstalling;
    [ObservableProperty] string installText = "";
    [ObservableProperty] bool systemMic;
    [ObservableProperty] bool canSystemMic;
    public bool CanInstall => !VoiceReady && !IsInstalling;
    public bool CanRemove => OperatingSystem.IsWindows() && Platform.VirtualMic.Installed && !IsInstalling;
    bool micLoading;
    partial void OnIsInstallingChanged(bool value) { OnPropertyChanged(nameof(CanInstall)); OnPropertyChanged(nameof(CanRemove)); }

    [RelayCommand]
    async Task InstallDriver()
    {
        if (IsInstalling) return;
        if (!OperatingSystem.IsWindows()) { Toast("Windows only", Ic.Info, true); return; }
        IsInstalling = true;
        try
        {
            var res = await Platform.VirtualMic.Install(step => Ui.Post(() => InstallText = Loc.T("drv.step." + step.ToString().ToLowerInvariant())));
            Set.RefreshDevices(); Reconfigure(); Changer.RefreshDevices();
            if (res.Ok && VoiceReady) { SystemMic = true; Toast(Loc.T("drv.ok"), Ic.Check); }
            else if (res.Error == "cancelled") Toast(Loc.T("drv.cancelled"), Ic.Info, true);
            else if (res.NeedsRestart) Toast(Loc.T("drv.restart"), Ic.Info, true);
            else Toast(Loc.T("drv.fail") + " " + res.Error, Ic.Info, true);
        }
        finally { IsInstalling = false; InstallText = ""; }
    }
    [RelayCommand]
    async Task RemoveDriver()
    {
        if (IsInstalling || !OperatingSystem.IsWindows()) return;
        IsInstalling = true;
        try
        {
            SystemMic = false;
            var res = await Platform.VirtualMic.Install(step => Ui.Post(() => InstallText = Loc.T("drv.step." + step.ToString().ToLowerInvariant())), uninstall: true);
            Set.RefreshDevices(); Reconfigure(); Changer.RefreshDevices();
            Toast(res.Ok ? Loc.T("drv.removed") : Loc.T("drv.fail") + " " + res.Error, res.Ok ? Ic.Check : Ic.Info, !res.Ok);
        }
        finally { IsInstalling = false; InstallText = ""; }
    }

    partial void OnSystemMicChanged(bool value)
    {
        if (micLoading) return;
        S.SystemMic = value; S.SaveSoon();
        if (value) ApplySystemMic(true); else ReleaseSystemMic();
        UpdateDeviceTexts();
    }
    /// <summary>Makes the cable the Windows default microphone and keeps the user's real microphone flowing through it.</summary>
    void ApplySystemMic(bool announce)
    {
        if (!Platform.VirtualMic.Installed) return;
        var prev = Platform.VirtualMic.TakeOver();
        if (!string.IsNullOrEmpty(prev)) { S.PrevDefaultMic = prev; S.Save(); }
        App.Engine.RealMicHint = S.PrevDefaultMic;
        if (Platform.VirtualMic.RealMic(S.PrevDefaultMic) != null)
        {
            if (!Changer.MicEnabled) { if (announce) Changer.MicEnabled = true; }          // your own voice keeps going through (only when you switch it on, never behind your back at start-up)
            else if (App.Engine.MicRunning && Audio.AudioEngine.LooksVirtual(App.Engine.MicDeviceName)) App.Engine.StartMic(S.MicDevice);
        }
        if (announce) Toast(Loc.T("sysmic.on"), Ic.Mic);
    }
    /// <summary>Gives the default-microphone role back to the device that had it.</summary>
    public void ReleaseSystemMic()
    {
        if (!OperatingSystem.IsWindows() || !Platform.VirtualMic.IsDefaultMic) return;
        var real = Platform.VirtualMic.RealMic(S.PrevDefaultMic);
        if (real != null) Platform.VirtualMic.SetDefault(real);
    }
    void InitSystemMic()
    {
        App.Engine.RealMicHint = S.PrevDefaultMic;
        micLoading = true; SystemMic = S.SystemMic && Platform.VirtualMic.Installed; micLoading = false;
        if (SystemMic) ApplySystemMic(false);
    }
    [RelayCommand] void Recheck() { Set.RefreshDevices(); Reconfigure(); Changer.RefreshDevices(); Toast(VoiceReady ? Loc.T("voice.found") : Loc.T("voice.missing"), VoiceReady ? Ic.Check : Ic.Cable, !VoiceReady); }
}
