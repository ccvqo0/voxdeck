using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VoxDeck.Audio;
using VoxDeck.Core;
using VoxDeck.ViewModels;
using VoxDeck.Views;
namespace VoxDeck;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; }
    public static AudioEngine Engine { get; private set; }
    public static MainViewModel Main { get; private set; }
    public static MainWindow MainWindow { get; private set; }
    static MiniTtsWindow miniTts; static MiniPadWindow miniPad;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public static List<FxPreset> AllPresets()
    {
        var l = new List<FxPreset>(FxPresets.Builtin);
        foreach (var c in Settings.CustomPresets) l.Add(new FxPreset("custom:" + c.Name, c.Name, c.Name, "custom", "Star", c.Fx, true));
        return l;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Settings = AppSettings.Load();
        var args = Program.Args;
        string Arg(string name) => args.FirstOrDefault(a => a.StartsWith("--" + name + "="))?[(name.Length + 3)..];
        if (Arg("theme") is { } th) Settings.Theme = th;
        if (Arg("lang") is { } lg) Settings.Language = lg;
        string lang = Settings.Language;
        if (lang != "en" && lang != "ru")
        {
            var ui = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            lang = ui is "ru" or "be" or "uk" or "kk" ? "ru" : "en";
        }
        Loc.I.Set(lang);
        Themes.Apply(Settings.Theme, Settings.Blur);
        Engine = new AudioEngine();
        Main = new MainViewModel();
        Main.Init();
        if (int.TryParse(Arg("page"), out var pg)) Main.Page = pg;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow = new MainWindow();
            desktop.MainWindow = MainWindow;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (Arg("demo") is { } demo) Demo.Run(demo);
        }
        base.OnFrameworkInitializationCompleted();
    }

    static void Place(Window w, int dx, int dy)
    {
        try
        {
            var scr = MainWindow?.Screens.ScreenFromWindow(MainWindow) ?? MainWindow?.Screens.Primary; if (scr == null) return;
            var wa = scr.WorkingArea; double sc = scr.Scaling;
            w.Position = new PixelPoint(wa.Right - (int)((w.Width + dx) * sc), wa.Bottom - (int)(((double.IsNaN(w.Height) ? 210 : w.Height) + dy) * sc));
        }
        catch { }
    }
    public static void ToggleMiniTts()
    {
        if (miniTts != null) { miniTts.Close(); return; }
        miniTts = new MiniTtsWindow(); miniTts.Closed += (_, _) => miniTts = null; Place(miniTts, 24, 40); miniTts.Show();
    }
    public static void ToggleMiniPad()
    {
        if (miniPad != null) { miniPad.Close(); return; }
        miniPad = new MiniPadWindow(); miniPad.Closed += (_, _) => miniPad = null; Place(miniPad, 24, 270); miniPad.Show();
    }
    public static void CloseMinis() { miniTts?.Close(); miniPad?.Close(); }
}

/// <summary>Scripted states used only for automated UI screenshots (--demo=...).</summary>
static class Demo
{
    public static void Run(string what)
    {
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            foreach (var w in what.Split(','))
            {
                switch (w)
                {
                    case "text": App.Main.Tts.Text = App.Main.Tts.Text.Length > 0 ? App.Main.Tts.Text : (Loc.I.Lang == "ru" ? "Всем привет! Я говорю без микрофона — это VoxDeck." : "Hey everyone! I'm talking without a microphone — this is VoxDeck."); break;
                    case "speak": App.Main.Tts.PreviewCommand.Execute(null); break;
                    case "minitts": App.ToggleMiniTts(); break;
                    case "minipad": App.ToggleMiniPad(); break;
                    case "charts": App.Main.Pad.Section = App.Main.Pad.Sections.First(s => s.Key == "charts"); break;
                    case "memes": App.Main.Pad.Section = App.Main.Pad.Sections.First(s => s.Key == "memes"); break;
                    case "sites": App.Main.Pad.Section = App.Main.Pad.Sections.First(s => s.Key == "sounds"); App.Main.Pad.Search = "laugh"; break;
                    case "all": App.Main.Pad.Section = App.Main.Pad.Sections.First(s => s.Key == "all"); break;
                    case "playpad": App.Main.Pad.PlayCommand.Execute(App.Main.Pad.Shown.FirstOrDefault()); break;
                    case "preset": App.Main.Changer.SelectPreset(App.Main.Changer.Presets.First(p => p.Preset.Id == "robot")); break;
                    case "ru": App.Main.Set.Lang = App.Main.Set.Langs[1]; break;
                    case "hotkey": _ = new HotkeyDialog("Air Horn", "Ctrl+Alt+1").ShowDialog<string>(App.MainWindow); break;
                }
            }
        }, TimeSpan.FromSeconds(1.2));
    }
}
