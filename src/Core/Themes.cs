using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
namespace VoxDeck.Core;

public record ThemeDef(string Name, bool Dark, string Bg1, string Bg2, string Surface, string Surface2, string Stroke, string Text, string Sub, string Accent, string Accent2, bool Glass = false, double Radius = 16, bool Grad = false)
{
    bool Decorated => Glass || Grad;
    static IBrush Lin(string a, string b) => new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) } };
    public IBrush Preview => Decorated ? Lin(Bg1, Bg2) : new SolidColorBrush(Color.Parse(Bg1));
    public IBrush AccentPreview => Decorated ? Lin(Accent, Accent2) : new SolidColorBrush(Color.Parse(Accent));
    public IBrush TextPreview => new SolidColorBrush(Color.Parse(Text));
    public IBrush SurfacePreview => new SolidColorBrush(Color.Parse(Surface));
    public IBrush StrokePreview => new SolidColorBrush(Color.Parse(Stroke));
}

public static class Themes
{
    public static readonly List<ThemeDef> All = new()
    {
        //   name                dark   bg1        bg2        surface      surface2     stroke       text       sub        accent     accent2
        new("Carbon",            true,  "#101113","#101113","#17181B","#202226","#2C2E34","#EDEEF0","#9DA1AA","#C6F135","#C6F135", false, 10),
        new("Daylight",          false, "#F2F2EF","#F2F2EF","#FFFFFF","#ECECE8","#D5D5CF","#161616","#62625C","#1F5FE0","#1F5FE0", false, 10),
        new("Signal",            true,  "#121212","#121212","#1A1A1A","#232323","#303030","#F1F1F1","#A0A0A0","#FF6A1A","#FF6A1A", false, 6),
        new("Liquid Glass",      true,  "#99101426","#99221a3d","#26FFFFFF","#3AFFFFFF","#40FFFFFF","#FFFFFF","#B8C0D8","#7C9CFF","#C084FC", true, 22),
        new("Liquid Glass Light",false, "#B3EEF3FF","#B3FBEFFF","#8CFFFFFF","#C8FFFFFF","#B0FFFFFF","#10142A","#566080","#3B6CFF","#B14DFF", true, 22),
        new("Frosted Aqua",      true,  "#99062030","#990B3A4A","#22FFFFFF","#38FFFFFF","#3CFFFFFF","#F2FDFF","#A9CBD6","#2DD4BF","#38BDF8", true, 22),
        new("Paper",             false, "#F6F1E7","#EFE7D6","#FFFDF8","#F3EBDC","#D9CDB8","#2B2620","#7A6F60","#C2552D","#E08A3C", false, 6),
        new("Ink & Paper",       false, "#FAFAF7","#F0EFEA","#FFFFFF","#ECEBE5","#1A1A1A","#111111","#666660","#111111","#444444", false, 2),
        new("Midnight",          true,  "#0B0F1E","#131A33","#161D36","#1E2747","#2A3558","#EAF0FF","#8C98BA","#6C8CFF","#9B6CFF"),
        new("Obsidian",          true,  "#000000","#0A0A0A","#111111","#1A1A1A","#262626","#FAFAFA","#8A8A8A","#FFFFFF","#A3A3A3", false, 12),
        new("Graphite",          true,  "#18191C","#202225","#26282C","#303338","#3A3D43","#F2F3F5","#9AA0A8","#5865F2","#8EA1FF"),
        new("Nord",              true,  "#2E3440","#3B4252","#3B4252","#434C5E","#4C566A","#ECEFF4","#A7B1C2","#88C0D0","#81A1C1"),
        new("Dracula",           true,  "#21222C","#282A36","#2F3141","#3A3C4E","#44475A","#F8F8F2","#9EA3C0","#BD93F9","#FF79C6"),
        new("Tokyo Night",       true,  "#16161E","#1A1B26","#1F2335","#292E42","#343A55","#C0CAF5","#7A85B5","#7AA2F7","#BB9AF7"),
        new("Catppuccin Mocha",  true,  "#11111B","#1E1E2E","#252538","#313244","#45475A","#CDD6F4","#9399B2","#CBA6F7","#F5C2E7"),
        new("Catppuccin Latte",  false, "#EFF1F5","#E6E9EF","#FFFFFF","#DCE0E8","#BCC0CC","#4C4F69","#7C7F93","#8839EF","#EA76CB"),
        new("Gruvbox",           true,  "#1D2021","#282828","#32302F","#3C3836","#504945","#EBDBB2","#A89984","#FABD2F","#FE8019", false, 8),
        new("Solarized Dark",    true,  "#002B36","#073642","#0A3F4C","#104B5A","#1B5C6C","#EEE8D5","#93A1A1","#2AA198","#268BD2", false, 10),
        new("Solarized Light",   false, "#FDF6E3","#EEE8D5","#FFFBF0","#EEE8D5","#D3CBB7","#3B4A52","#839496","#268BD2","#2AA198", false, 10),
        new("Rose Pine",         true,  "#191724","#1F1D2E","#26233A","#2F2B45","#403D52","#E0DEF4","#908CAA","#EBBCBA","#C4A7E7"),
        new("Synthwave '84",     true,  "#1A1033","#2B1055","#34206A","#432A85","#6A3FB8","#FDF4FF","#C7A6F0","#FF4FD8","#36E2F4", false, 16, true),
        new("Cyberpunk",         true,  "#0A0A12","#14101F","#1B1730","#26203F","#FCEE0A","#F7F7F0","#A9A6C0","#FCEE0A","#00F0FF", false, 2),
        new("Aurora",            true,  "#061A1F","#12143A","#14273A","#1C3550","#2B4A66","#E9FFFA","#93B8B8","#34F5C5","#7B6CFF", false, 20, true),
        new("Forest",            true,  "#0E1A14","#14261C","#1A2F23","#223B2C","#2F4F3B","#E8F5EC","#8FB09C","#4ADE80","#A3E635"),
        new("Ocean",             true,  "#06141F","#0A2540","#0F2E4D","#153B61","#1F4E7D","#E6F4FF","#89AECF","#38BDF8","#22D3EE"),
        new("Sunset",            true,  "#1F1020","#3A1530","#3D1B36","#512346","#6E2F5A","#FFF1E8","#D4A5A5","#FF7A59","#FFB347", false, 16, true),
        new("Volcano",           true,  "#140A0A","#241010","#2C1414","#3A1A1A","#522222","#FFECE6","#C29A92","#FF4D2E","#FF9F1C", false, 10),
        new("Sakura",            false, "#FFF5F8","#FFE8F0","#FFFFFF","#FFE1EB","#F7C2D4","#4A2A3A","#9C7084","#F0528F","#FF8FB8", false, 20),
        new("Mint",              false, "#F1FBF6","#E0F5EA","#FFFFFF","#D9F0E3","#B5DEC8","#16382A","#5C8572","#10B981","#34D399", false, 18),
        new("Lavender",          false, "#F6F4FF","#ECE8FF","#FFFFFF","#E4DEFF","#CFC6F5","#2B2550","#7A73A6","#7C5CFF","#B197FC", false, 18),
        new("Sky",               false, "#F2F8FF","#E3F0FF","#FFFFFF","#D8E9FF","#B9D4F5","#12294A","#5A7699","#2F7BFF","#38BDF8", false, 16),
        new("Clean Light",       false, "#F7F7F8","#EFEFF1","#FFFFFF","#EBEBEF","#DADAE0","#18181B","#71717A","#18181B","#52525B", false, 12),
        new("Coffee",            true,  "#1B1512","#2A201A","#32261F","#3F3027","#584436","#F3E7DA","#B39B86","#D9A066","#E6C29A", false, 10),
        new("Terminal",          true,  "#020A04","#04140A","#07200F","#0B2D16","#145A2A","#B6FFC8","#4FAE6A","#39FF6A","#00D1A0", false, 0),
        new("Bubblegum",         true,  "#241038","#3A1558","#421C64","#55257F","#7A3BB0","#FFF0FB","#D5A8E0","#FF6AD5","#FFD166", false, 24, true),
    };

    public static ThemeDef Current { get; private set; } = All[0];
    public static event Action Changed;
    public static Color Accent { get; private set; } = Color.Parse("#C6F135");
    public static Color OnAccent { get; private set; } = Color.Parse("#111111");
    public static bool Decorated => Current.Glass || Current.Grad;
    public static ThemeDef Find(string name) => All.FirstOrDefault(t => t.Name == name) ?? All[0];

    static Color Mixc(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static double Lum(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    public static void Apply(string name, bool allowGlass = true)
    {
        var t = Find(name); Current = t;
        var app = Application.Current; if (app == null) return;
        var r = app.Resources;
        Color bg1 = Color.Parse(t.Bg1), bg2 = Color.Parse(t.Bg2), surf = Color.Parse(t.Surface), surf2 = Color.Parse(t.Surface2), stroke = Color.Parse(t.Stroke),
              text = Color.Parse(t.Text), sub = Color.Parse(t.Sub), acc = Color.Parse(t.Accent), acc2 = Color.Parse(t.Accent2);
        bool glass = t.Glass && allowGlass, grad = t.Glass || t.Grad;
        if (!grad) { bg2 = bg1; }
        if (t.Glass && !allowGlass) { bg1 = A(bg1, 255); bg2 = A(bg2, 255); }
        Color onAcc = Lum(t.Glass || t.Grad ? Mixc(acc, acc2, 0.5) : acc) > 0.62 ? Color.Parse("#111111") : Colors.White;
        Color white = Colors.White, black = Colors.Black;

        app.RequestedThemeVariant = t.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        r["IsGlass"] = glass;
        r["AccentHoverBrush"] = new SolidColorBrush(Lum(acc) > 0.5 ? Mixc(acc, black, 0.10) : Mixc(acc, white, 0.14));
        r["WindowBgBrush"] = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(bg1, 0), new GradientStop(bg2, 1) } };
        r["SolidBgBrush"] = new SolidColorBrush(A(Mixc(bg1, bg2, 0.5), 255));
        r["PopupBgBrush"] = new SolidColorBrush(t.Glass ? (t.Dark ? Color.Parse("#F01A1D33") : Color.Parse("#F5F4F6FF")) : A(Mixc(surf, surf2, 0.5), 255));
        r["SurfaceBrush"] = new SolidColorBrush(surf);
        r["Surface2Brush"] = new SolidColorBrush(surf2);
        r["SurfaceHoverBrush"] = new SolidColorBrush(Mixc(surf2, text, 0.08));
        r["StrokeBrush"] = new SolidColorBrush(t.Glass ? stroke : A(stroke, 255));
        r["StrokeSoftBrush"] = new SolidColorBrush(A(stroke, (byte)(t.Glass ? 46 : 130)));
        r["TextBrush"] = new SolidColorBrush(text);
        r["SubTextBrush"] = new SolidColorBrush(sub);
        r["AccentBrush"] = new SolidColorBrush(acc);
        r["Accent2Brush"] = new SolidColorBrush(acc2);
        r["AccentSoftBrush"] = new SolidColorBrush(A(acc, 46));
        r["AccentGradBrush"] = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(acc, 0), new GradientStop(grad ? acc2 : acc, 1) } };
        r["OnAccentBrush"] = new SolidColorBrush(onAcc);
        r["Blob1Brush"] = new RadialGradientBrush { GradientStops = { new GradientStop(A(acc, (byte)(t.Dark ? 110 : 70)), 0), new GradientStop(A(acc, 0), 1) } };
        r["Blob2Brush"] = new RadialGradientBrush { GradientStops = { new GradientStop(A(acc2, (byte)(t.Dark ? 100 : 64)), 0), new GradientStop(A(acc2, 0), 1) } };
        r["GlassHighlightBrush"] = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(A(white, (byte)(t.Glass ? 46 : 0)), 0), new GradientStop(A(white, 0), 0.5) } };
        r["ShadowColor"] = A(black, (byte)(t.Dark ? 110 : 40));
        r["CardRadius"] = new CornerRadius(t.Radius);
        r["CtlRadius"] = new CornerRadius(Math.Min(t.Radius, 12) * 0.75);
        r["PillRadius"] = new CornerRadius(t.Radius < 4 ? t.Radius : 999);
        r["TileRadius"] = new CornerRadius(Math.Min(t.Radius, 16) * 0.85);
        r["DangerBrush"] = new SolidColorBrush(Color.Parse(t.Dark ? "#FF6B6B" : "#D92D20"));
        r["OkBrush"] = new SolidColorBrush(Color.Parse(t.Dark ? "#4ADE80" : "#16A34A"));
        r["WarnBrush"] = new SolidColorBrush(Color.Parse(t.Dark ? "#FBBF24" : "#B45309"));

        // ---- Fluent overrides ----
        r["SystemAccentColor"] = acc;
        r["SystemAccentColorDark1"] = Mixc(acc, black, 0.12); r["SystemAccentColorDark2"] = Mixc(acc, black, 0.24); r["SystemAccentColorDark3"] = Mixc(acc, black, 0.36);
        r["SystemAccentColorLight1"] = Mixc(acc, white, 0.15); r["SystemAccentColorLight2"] = Mixc(acc, white, 0.30); r["SystemAccentColorLight3"] = Mixc(acc, white, 0.45);
        var fieldBg = new SolidColorBrush(t.Glass ? A(t.Dark ? black : white, (byte)(t.Dark ? 50 : 120)) : Mixc(surf, bg1, 0.55));
        var fieldBgHover = new SolidColorBrush(t.Glass ? A(t.Dark ? black : white, (byte)(t.Dark ? 70 : 150)) : Mixc(surf, bg1, 0.35));
        var strokeB = new SolidColorBrush(t.Glass ? stroke : A(stroke, 255)); var textB = new SolidColorBrush(text); var subB = new SolidColorBrush(sub); var accB = new SolidColorBrush(acc);
        foreach (var k in new[] { "TextControlBackground", "TextControlBackgroundFocused", "ComboBoxBackground", "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused" }) r[k] = fieldBg;
        foreach (var k in new[] { "TextControlBackgroundPointerOver", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed" }) r[k] = fieldBgHover;
        foreach (var k in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed", "ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush", "ToolTipBorderBrush", "FlyoutBorderThemeBrush" }) r[k] = strokeB;
        r["TextControlBorderBrushFocused"] = accB;
        foreach (var k in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "ComboBoxForeground", "ComboBoxForegroundPointerOver", "ComboBoxForegroundFocused", "ComboBoxForegroundPressed", "ToolTipForeground", "MenuFlyoutItemForeground", "ComboBoxItemForeground", "ToggleSwitchContentForeground", "ComboBoxDropDownGlyphForeground" }) r[k] = textB;
        foreach (var k in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused", "ComboBoxPlaceHolderForeground" }) r[k] = subB;
        var popup = (IBrush)r["PopupBgBrush"];
        foreach (var k in new[] { "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "ToolTipBackground", "FlyoutPresenterBackground" }) r[k] = popup;
        var track = new SolidColorBrush(A(text, 46));
        foreach (var k in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" }) r[k] = track;
        foreach (var k in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" }) r[k] = accB;
        foreach (var k in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed", "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed" }) r[k] = accB;
        foreach (var k in new[] { "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed" }) r[k] = new SolidColorBrush(onAcc);
        foreach (var k in new[] { "ToggleSwitchStrokeOff", "ToggleSwitchStrokeOffPointerOver", "ToggleSwitchKnobFillOff", "ToggleSwitchKnobFillOffPointerOver" }) r[k] = subB;
        foreach (var k in new[] { "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedUnfocused", "MenuFlyoutItemBackgroundPointerOver", "ComboBoxItemBackgroundPointerOver" }) r[k] = new SolidColorBrush(A(acc, 56));
        foreach (var k in new[] { "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemForegroundPointerOver", "MenuFlyoutItemForegroundPointerOver", "ComboBoxItemForegroundSelectedUnfocused" }) r[k] = textB;
        r["ControlCornerRadius"] = new CornerRadius(Math.Min(t.Radius, 12) * 0.75);
        r["OverlayCornerRadius"] = new CornerRadius(Math.Min(t.Radius, 14));
        r["ScrollBarThumbFill"] = new SolidColorBrush(A(text, 60));
        r["TileTextBrush"] = new SolidColorBrush(text);
        OnAccent = onAcc; Accent = acc;
        Changed?.Invoke();
    }
}
