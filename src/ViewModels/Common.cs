using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VoxDeck.Audio;
using VoxDeck.Core;
using VoxDeck.Library;
namespace VoxDeck.ViewModels;

public static class Ui
{
    public static void Post(Action a) => Dispatcher.UIThread.Post(a);
    public static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
    public static string SafeFile(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim(); if (name.Length > 80) name = name[..80];
        return name.Length == 0 ? "sound" : name;
    }
    public static async Task<bool> SaveBytes(Window owner, string suggested, string ext, Func<byte[]> bytes)
    {
        if (owner == null) return false;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = SafeFile(suggested) + "." + ext, DefaultExtension = ext,
            FileTypeChoices = new[] { new FilePickerFileType(ext.ToUpperInvariant() + " audio") { Patterns = new[] { "*." + ext } } }
        });
        if (file == null) return false;
        var data = await Task.Run(bytes);
        await using var s = await file.OpenWriteAsync();
        await s.WriteAsync(data);
        return true;
    }
    public static string Dur(double sec)
    {
        if (sec <= 0) return "";
        if (sec < 10) return sec.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        int s = (int)Math.Round(sec); return s < 60 ? s + "s" : $"{s / 60}:{s % 60:00}";
    }
    public static Color Hsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360; double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2; double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; } else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
    public static int StableHash(string s) { unchecked { int h = 23; foreach (var ch in s ?? "") h = h * 31 + ch; return h & 0x7fffffff; } }
    /// <summary>One flat, stable colour per key (used to tell tiles apart at a glance).</summary>
    public static IBrush GradFor(string key) => new SolidColorBrush(Hsl(StableHash(key) % 360, 0.62, 0.60));
    public static IBrush Tint(string hex, string fallbackKey)
    {
        if (!string.IsNullOrEmpty(hex) && Color.TryParse(hex, out var c))
        {
            // very dark site colours would vanish on dark themes
            double l = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            if (l < 0.28) c = Color.FromRgb((byte)(c.R + (255 - c.R) * 0.45), (byte)(c.G + (255 - c.G) * 0.45), (byte)(c.B + (255 - c.B) * 0.45));
            return new SolidColorBrush(c);
        }
        return GradFor(fallbackKey);
    }
}

/// <summary>Attached property: asynchronously loads a remote image (album covers).</summary>
public class Img
{
    public static readonly AttachedProperty<string> UrlProperty = AvaloniaProperty.RegisterAttached<Img, Image, string>("Url");
    public static string GetUrl(Image i) => i.GetValue(UrlProperty);
    public static void SetUrl(Image i, string v) => i.SetValue(UrlProperty, v);
    static readonly Dictionary<string, Bitmap> cache = new();
    static readonly SemaphoreSlim gate = new(6);
    static Img()
    {
        UrlProperty.Changed.AddClassHandler<Image>(async (img, e) =>
        {
            var url = e.NewValue as string; img.Source = null;
            if (string.IsNullOrEmpty(url)) return;
            if (cache.TryGetValue(url, out var bm)) { img.Source = bm; return; }
            try
            {
                await gate.WaitAsync();
                byte[] data;
                try { data = await Online.Http.GetByteArrayAsync(url); } finally { gate.Release(); }
                var b = await Task.Run(() => Bitmap.DecodeToWidth(new MemoryStream(data), 220));
                if (cache.Count > 400) cache.Clear();
                cache[url] = b;
                if (GetUrl(img) == url) img.Source = b;
            }
            catch { }
        });
    }
}

public partial class SoundItem : ObservableObject
{
    public SoundRef Ref { get; }
    public SoundItem(SoundRef r) { Ref = r; Brush = Ui.Tint(r.Color, r.Category ?? r.Name); }
    public void RefreshDur() => OnPropertyChanged(nameof(DurText));
    public string Id => Ref.Id;
    public SoundpadViewModel Pad => App.Main.Pad;
    public string Name => Ref.Name;
    public string Sub => string.IsNullOrEmpty(Ref.Sub) ? Ref.Category : Ref.Sub;
    public string DurText => Ui.Dur(Ref.Dur);
    public string Image => Ref.Image;
    public bool HasImage => !string.IsNullOrEmpty(Ref.Image);
    public IBrush Brush { get; }
    public string Icon => Ic.Get(SoundpadViewModel.IconFor(Ref));
    public bool IsLocal => Ref.Id.StartsWith("local:");
    public bool IsOnline => Sources.IsOnline(Ref.Id);
    [ObservableProperty] bool isFav;
    [ObservableProperty] bool isPlaying;
    [ObservableProperty] bool isLoading;
    [ObservableProperty] string hotkey;
    [ObservableProperty] bool hasHotkey;
    internal int Active;
    partial void OnHotkeyChanged(string value) => HasHotkey = !string.IsNullOrEmpty(value);
}

public partial class FxSlider : ObservableObject
{
    public FxDef Def { get; }
    readonly Action<FxSlider> changed;
    public FxSlider(FxDef d, double v, Action<FxSlider> onChanged) { Def = d; value = v; changed = onChanged; }
    public string Name => Loc.T("fx." + Def.Key);
    public double Min => Def.Min; public double Max => Def.Max; public double Step => Def.Step;
    [ObservableProperty] double value;
    public string ValueText
    {
        get
        {
            double v = Math.Round(Value / Def.Step) * Def.Step;
            if (Def.Key == "lowcut" && v <= 25) return Loc.T("off");
            if (Def.Key == "highcut" && v >= 19500) return Loc.T("off");
            if (Def.Key == "gate" && v <= -79.5) return Loc.T("off");
            string s = Math.Abs(v) >= 100 || Def.Step >= 1 ? v.ToString("0") : v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            if ((Def.Unit == "st" || Def.Unit == "dB") && v > 0) s = "+" + s;
            return s + " " + Def.Unit;
        }
    }
    public bool IsModified => Math.Abs(Value - Def.Def) > 1e-6;
    partial void OnValueChanged(double value) { OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(IsModified)); changed?.Invoke(this); }
    public void SetSilently(double v) { value = v; OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(ValueText)); OnPropertyChanged(nameof(IsModified)); }
    public void RefreshName() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(ValueText)); }
}

public class FxGroup
{
    public string Key { get; set; }
    public string Title => Loc.T("fxgroup." + Key);
    public string Icon { get; set; }
    public List<FxSlider> Sliders { get; set; } = new();
}

public partial class PresetItem : ObservableObject
{
    public FxPreset Preset { get; }
    public PresetItem(FxPreset p) { Preset = p; Brush = Ui.GradFor(p.Cat + p.Id); }
    public string Name => Preset.Custom ? Preset.En : (Loc.I.Lang == "ru" ? Preset.Ru : Preset.En);
    public string Icon => Ic.Get(Preset.Icon);
    public string CatName => Loc.T("cat." + Preset.Cat);
    public IBrush Brush { get; }
    public bool IsCustom => Preset.Custom;
    public ChangerViewModel Owner => App.Main.Changer;
    [ObservableProperty] bool isSelected;
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(CatName)); }
    public override string ToString() => Name;
}

public partial class Chip : ObservableObject
{
    public string Key { get; set; }
    public string LocKey { get; set; }
    public string Raw { get; set; }
    public string RawRu { get; set; }
    public string Title => Raw != null ? (RawRu != null && Loc.I.Lang == "ru" ? RawRu : Raw) : Loc.T(LocKey);
    public string Icon { get; set; }
    public bool IsHeader { get; set; }
    public bool IsItem => !IsHeader;
    public bool HasIcon => !string.IsNullOrEmpty(Icon);
    [ObservableProperty] bool isSelected;
    [ObservableProperty] string count;
    public void Refresh() => OnPropertyChanged(nameof(Title));
    public override string ToString() => Title;
}
