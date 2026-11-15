using Avalonia.Media;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VoxDeck.Core;
using VoxDeck.Platform;
namespace VoxDeck.Views;

public partial class TtsView : UserControl
{
    public TtsView()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("PinBtn").Click += (_, _) => App.ToggleMiniTts();
    }
}
public partial class SoundpadView : UserControl
{
    public SoundpadView()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("PinBtn").Click += (_, _) => App.ToggleMiniPad();
    }
}
public partial class ChangerView : UserControl { public ChangerView() { AvaloniaXamlLoader.Load(this); } }
public partial class SettingsView : UserControl { public SettingsView() { AvaloniaXamlLoader.Load(this); } }

public partial class MainWindow : Window
{
    readonly Control[] pages; readonly Panel root, blobs; readonly HotkeyManager hotkeys = new();
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        var S = App.Settings;
        Width = Math.Max(1000, S.Width); Height = Math.Max(640, S.Height);
        DataContext = App.Main;
        root = this.FindControl<Panel>("Root"); blobs = this.FindControl<Panel>("Blobs");
        pages = new Control[] { this.FindControl<Control>("P0"), this.FindControl<Control>("P1"), this.FindControl<Control>("P2"), this.FindControl<Control>("P3") };
        foreach (var n in new[] { "TitleBar", "LogoBar" })
            this.FindControl<Control>(n).PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else BeginMoveDrag(e); } };
        App.Main.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(App.Main.Page)) ShowPage(App.Main.Page); };
        App.Main.WindowChanged += ApplyWindow;
        App.Main.HotkeysChanged += ApplyHotkeys;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        Opened += (_, _) => { ApplyWindow(); ShowPage(App.Main.Page); hotkeys.Attach(this); ApplyHotkeys(); };
        Closing += (_, _) =>
        {
            if (WindowState == WindowState.Normal) { S.Width = Width; S.Height = Height; }
            S.Save(); App.CloseMinis(); hotkeys.Clear();
            if (S.SystemMic) { try { App.Main.ReleaseSystemMic(); } catch { } }      // your real microphone is the default again once VoxDeck is closed
            App.Engine.Dispose();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) App.Main.StopAllCommand.Execute(null); };
    }

    void OnDrop(object s, DragEventArgs e)
    {
        var files = e.Data.GetFiles(); if (files == null) return;
        var paths = files.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToList();
        if (paths.Count > 0) { App.Main.Page = 1; App.Main.Pad.AddPaths(paths); }
    }

    void ShowPage(int idx)
    {
        bool anim = App.Settings.Animations;
        for (int i = 0; i < pages.Length; i++)
        {
            var p = pages[i];
            p.Classes.Set("noanim", !anim);
            if (i == idx)
            {
                if (p.IsVisible && p.Classes.Contains("active")) continue;
                p.Classes.Remove("active"); p.IsVisible = true;
                Dispatcher.UIThread.Post(() => p.Classes.Add("active"), DispatcherPriority.Background);
            }
            else { p.IsVisible = false; p.Classes.Remove("active"); }
        }
    }

    void ApplyWindow()
    {
        var S = App.Settings; var t = Themes.Current;
        Topmost = S.AlwaysOnTop;
        bool glass = t.Glass && S.Blur; double op = Math.Clamp(S.WindowOpacity, 0.3, 1);
        if (glass) TransparencyLevelHint = new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent };
        else if (op < 0.995) TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        else TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        root.Opacity = op;
        blobs.IsVisible = Themes.Decorated;
        blobs.Classes.Set("anim", S.Animations && Themes.Decorated);
        // sharp text: LCD anti-aliasing needs an opaque surface, so it is used whenever the window is not see-through
        bool opaque = !glass && op >= 0.995;
        RenderOptions.SetTextRenderingMode(this, opaque ? TextRenderingMode.SubpixelAntialias : TextRenderingMode.Antialias);
        try { Icon = LogoMark.MakeIcon(Themes.Accent, Themes.OnAccent); } catch { }
        foreach (var p in pages) p.Classes.Set("noanim", !S.Animations);
        if (glass)
            Dispatcher.UIThread.Post(() =>
            {
                // no compositor transparency available -> fall back to an opaque rendition of the glass theme
                if (ActualTransparencyLevel == WindowTransparencyLevel.None && Themes.Current.Glass) Themes.Apply(S.Theme, false);
            }, DispatcherPriority.Background);
    }

    void ApplyHotkeys()
    {
        var S = App.Settings; hotkeys.Clear();
        hotkeys.Register(S.StopHotkey, () => App.Main.StopAllCommand.Execute(null));
        hotkeys.Register(S.ChangerHotkey, () => App.Main.Changer.ToggleChanger());
        foreach (var kv in S.Hotkeys) { var id = kv.Key; hotkeys.Register(kv.Value, () => App.Main.Pad.PlayById(id)); }
    }
}

public partial class MiniTtsWindow : Window
{
    public MiniTtsWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Main;
        this.FindControl<Control>("Header").PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        this.FindControl<Button>("CloseBtn").Click += (_, _) => Close();
        var input = this.FindControl<TextBox>("Input");
        input.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; App.Main.Tts.SpeakCommand.Execute(null); }
            else if (e.Key == Key.Escape) { e.Handled = true; App.Main.Tts.StopCommand.Execute(null); }
        }, RoutingStrategies.Tunnel);
        Opened += (_, _) => input.Focus();
    }
}

public partial class MiniPadWindow : Window
{
    public MiniPadWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = App.Main;
        this.FindControl<Control>("Header").PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        this.FindControl<Button>("CloseBtn").Click += (_, _) => Close();
        Opened += (_, _) => WinStyles.SetNoActivate(this, true);
    }
}

public partial class HotkeyDialog : Window
{
    string combo;
    public HotkeyDialog() : this("", null) { }
    public HotkeyDialog(string soundName, string current)
    {
        AvaloniaXamlLoader.Load(this);
        combo = current;
        var label = this.FindControl<TextBlock>("Combo"); var name = this.FindControl<TextBlock>("SoundName");
        name.Text = soundName; label.Text = string.IsNullOrEmpty(current) ? "—" : current;
        this.FindControl<Button>("OkBtn").Click += (_, _) => Close(combo ?? "");
        this.FindControl<Button>("ClearBtn").Click += (_, _) => Close("");
        this.FindControl<Button>("CancelBtn").Click += (_, _) => Close(null);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            e.Handled = true;
            if (e.Key == Key.Escape) { Close(null); return; }
            var c = Keys.Combo(e); if (c == null) return;
            combo = c; label.Text = c;
        }, RoutingStrategies.Tunnel);
    }
}
