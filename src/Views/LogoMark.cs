using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
namespace VoxDeck.Views;

/// <summary>The VoxDeck mark: five level-meter bars arranged as a "V". Drawn as vectors in the colours of the current theme.</summary>
public class LogoMark : Control
{
    public static readonly StyledProperty<IBrush> BackProperty = AvaloniaProperty.Register<LogoMark, IBrush>(nameof(Back));
    public static readonly StyledProperty<IBrush> BarsProperty = AvaloniaProperty.Register<LogoMark, IBrush>(nameof(Bars));
    public IBrush Back { get => GetValue(BackProperty); set => SetValue(BackProperty, value); }
    public IBrush Bars { get => GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
    static LogoMark() { AffectsRender<LogoMark>(BackProperty, BarsProperty); }

    public override void Render(DrawingContext ctx) => Draw(ctx, Math.Min(Bounds.Width, Bounds.Height), Back, Bars);

    // bar centres and tops, in units of the icon size
    static readonly double[] X = { 0.2, 0.35, 0.5, 0.65, 0.8 };
    static readonly double[] Top = { 0.215, 0.335, 0.455, 0.335, 0.215 };
    const double BarW = 0.104, BarH = 0.33, Corner = 0.225;

    public static void Draw(DrawingContext ctx, double s, IBrush back, IBrush bars)
    {
        if (s <= 0) return;
        if (back != null) ctx.DrawRectangle(back, null, new RoundedRect(new Rect(0, 0, s, s), s * Corner));
        if (bars == null) return;
        double w = s * BarW;
        for (int i = 0; i < X.Length; i++)
            ctx.DrawRectangle(bars, null, new RoundedRect(new Rect(s * X[i] - w / 2, s * Top[i], w, s * BarH), w / 2));
    }

    /// <summary>Window / taskbar icon in the given colours, so it follows the theme.</summary>
    public static WindowIcon MakeIcon(Color back, Color bars, int size = 128)
    {
        using var rtb = new RenderTargetBitmap(new PixelSize(size, size));
        using (var ctx = rtb.CreateDrawingContext()) Draw(ctx, size, new SolidColorBrush(back), new SolidColorBrush(bars));
        var ms = new MemoryStream(); rtb.Save(ms); ms.Position = 0;
        return new WindowIcon(ms);
    }
}
