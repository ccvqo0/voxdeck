using System.Text.Json;
using VoxDeck.Audio;
using VoxDeck.Library;
namespace VoxDeck.Core;

public class AppSettings
{
    public string Language { get; set; } = "auto";
    public string Theme { get; set; } = "Carbon";
    public int Rev { get; set; }
    public double WindowOpacity { get; set; } = 1.0;
    public bool AlwaysOnTop { get; set; }
    public bool Blur { get; set; } = true;
    public bool Animations { get; set; } = true;
    public double Width { get; set; } = 1240;
    public double Height { get; set; } = 800;

    public string VoiceDevice { get; set; } = "auto";
    public string MonitorDevice { get; set; } = "default";
    public string MicDevice { get; set; } = "default";
    public double VoiceVolume { get; set; } = 100;
    public double MonitorVolume { get; set; } = 80;
    public bool MonitorEnabled { get; set; } = true;

    public string TtsVoice { get; set; } = "";
    public double TtsRate { get; set; } = 0;
    public double TtsPitch { get; set; } = 0;
    public double TtsVolume { get; set; } = 100;
    public string TtsFx { get; set; } = "none";
    public bool TtsClearAfter { get; set; }
    public bool TtsClarity { get; set; } = true;
    public bool TtsAutoBalance { get; set; } = true;
    /// <summary>voice id -> [rate %, pitch st, volume %] — every voice remembers its own tuning.</summary>
    public Dictionary<string, double[]> VoiceTweaks { get; set; } = new();
    public List<string> FavVoices { get; set; } = new();
    public List<string> TtsHistory { get; set; } = new();

    public double PadVolume { get; set; } = 80;
    public double PadSpeed { get; set; } = 100;
    public bool PadOverlap { get; set; } = true;
    public bool PadLoop { get; set; }
    public List<SoundRef> MySounds { get; set; } = new();
    public List<SoundRef> Favorites { get; set; } = new();
    public List<SoundRef> Recent { get; set; } = new();
    public Dictionary<string, int> PlayCounts { get; set; } = new();
    public Dictionary<string, string> Hotkeys { get; set; } = new();   // soundId -> "Ctrl+Alt+1"
    public string StopHotkey { get; set; } = "Ctrl+Alt+S";
    public string ChangerHotkey { get; set; } = "Ctrl+Alt+V";

    public bool MicEnabled { get; set; }
    /// <summary>Become the Windows default microphone while the app runs (so Discord & games need no setup).</summary>
    public bool SystemMic { get; set; }
    /// <summary>Default microphone to give back on exit.</summary>
    public string PrevDefaultMic { get; set; }
    public bool ChangerOn { get; set; } = true;
    public bool HearMyself { get; set; }
    public string ChangerPreset { get; set; } = "none";
    public FxParams ChangerFx { get; set; } = new();
    public List<SavedPreset> CustomPresets { get; set; } = new();

    public double MiniOpacity { get; set; } = 100;
    public bool MiniClickThroughFocus { get; set; } = true;

    // ---------- persistence ----------
    public static string Dir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxDeck");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }
    }
    const int CurrentRev = 2;
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public static AppSettings Load()
    {
        AppSettings s = null;
        try { if (File.Exists(FilePath)) s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opt); } catch { }
        s ??= new AppSettings { Rev = CurrentRev };
        if (s.Rev < 2)
        {
            // 2.0 switched to the flat look; settings written by 1.x still carry the old default theme
            if (s.Theme == "Liquid Glass") s.Theme = "Carbon";
            if (s.MiniOpacity == 92) s.MiniOpacity = 100;
            s.Rev = CurrentRev;
        }
        return s;
    }
    readonly object saveLock = new(); CancellationTokenSource saveCts;
    public void Save()
    {
        try { lock (saveLock) File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opt)); } catch { }
    }
    /// <summary>Debounced save (settings change very often while dragging sliders).</summary>
    public void SaveSoon()
    {
        saveCts?.Cancel(); var cts = saveCts = new CancellationTokenSource();
        Task.Delay(600, cts.Token).ContinueWith(t => { if (!t.IsCanceled) Save(); });
    }
}

public class SavedPreset
{
    public string Name { get; set; }
    public FxParams Fx { get; set; } = new();
}
