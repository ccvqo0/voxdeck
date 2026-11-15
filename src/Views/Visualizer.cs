using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
namespace VoxDeck.Views;

/// <summary>Small spectrum visualizer fed by the audio engine.</summary>
public class Visualizer : Control
{
    public static readonly StyledProperty<int> BarsProperty = AvaloniaProperty.Register<Visualizer, int>(nameof(Bars), 16);
    public int Bars { get => GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
    float[] raw = new float[16], smooth = new float[16];
    DispatcherTimer timer; bool idle; int idleFrames;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick());
        timer.Start();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { timer?.Stop(); timer = null; base.OnDetachedFromVisualTree(e); }

    void Tick()
    {
        int n = Math.Max(2, Bars);
        if (raw.Length != n) { raw = new float[n]; smooth = new float[n]; }
        var eng = App.Engine; if (eng == null) return;
        var bus = eng.VoiceBus.Peak > eng.MonitorBus.Peak ? eng.VoiceBus : eng.MonitorBus;
        bool silent = bus.Peak < 0.0008f;
        if (silent) Array.Clear(raw); else bus.Spectrum(raw);
        float mx = 0;
        for (int i = 0; i < n; i++)
        {
            float t = raw[i]; smooth[i] += (t - smooth[i]) * (t > smooth[i] ? 0.55f : 0.22f);
            if (smooth[i] > mx) mx = smooth[i];
        }
        if (mx < 0.01f) { idleFrames++; if (idleFrames > 3 && idle) return; idle = true; } else { idle = false; idleFrames = 0; }
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        int n = smooth.Length; double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        double gap = Math.Max(1.5, w / n * 0.32), bw = (w - gap * (n - 1)) / n;
        var brush = (this.TryFindResource("AccentGradBrush", out var b) ? b as IBrush : null) ?? Brushes.White;
        for (int i = 0; i < n; i++)
        {
            double v = Math.Clamp(smooth[i], 0, 1); double bh = Math.Max(bw < 3 ? 2 : 3, v * h);
            var r = new Rect(i * (bw + gap), (h - bh) / 2, bw, bh);
            ctx.DrawRectangle(brush, null, r, bw / 2, bw / 2);
        }
    }
}
