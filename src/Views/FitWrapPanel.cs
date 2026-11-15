using Avalonia;
using Avalonia.Controls;
namespace VoxDeck.Views;

/// <summary>Wrap panel whose columns stretch to fill the row: as many columns as fit MinItemWidth, all equal width.</summary>
public class FitWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<FitWrapPanel, double>(nameof(MinItemWidth), 150);
    public static readonly StyledProperty<double> ItemHeightProperty = AvaloniaProperty.Register<FitWrapPanel, double>(nameof(ItemHeight), double.NaN);
    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<FitWrapPanel, int>(nameof(MaxColumns), 12);
    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ItemHeight { get => GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    static FitWrapPanel() { AffectsMeasure<FitWrapPanel>(MinItemWidthProperty, ItemHeightProperty, MaxColumnsProperty); }

    int Cols(double w) => double.IsInfinity(w) || w <= 0 ? Math.Min(MaxColumns, Math.Max(1, Children.Count)) : Math.Clamp((int)Math.Floor(w / MinItemWidth), 1, MaxColumns);

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width; int cols = Cols(w);
        double iw = double.IsInfinity(w) ? MinItemWidth : Math.Floor(w / cols);
        double y = 0, rowH = 0; int c = 0, n = 0;
        foreach (var ch in Children)
        {
            if (!ch.IsVisible) continue;
            ch.Measure(new Size(iw, double.IsNaN(ItemHeight) ? double.PositiveInfinity : ItemHeight));
            rowH = Math.Max(rowH, double.IsNaN(ItemHeight) ? ch.DesiredSize.Height : ItemHeight);
            n++; if (++c == cols) { y += rowH; rowH = 0; c = 0; }
        }
        y += rowH;
        return new Size(double.IsInfinity(w) ? iw * Math.Min(cols, Math.Max(n, 1)) : w, y);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int cols = Cols(finalSize.Width); double iw = Math.Floor(finalSize.Width / cols);
        var vis = Children.Where(ch => ch.IsVisible).ToList();
        double y = 0;
        for (int i = 0; i < vis.Count; i += cols)
        {
            double rowH = 0;
            for (int j = i; j < Math.Min(i + cols, vis.Count); j++) rowH = Math.Max(rowH, double.IsNaN(ItemHeight) ? vis[j].DesiredSize.Height : ItemHeight);
            for (int j = i; j < Math.Min(i + cols, vis.Count); j++) vis[j].Arrange(new Rect((j - i) * iw, y, iw, rowH));
            y += rowH;
        }
        return new Size(finalSize.Width, y);
    }
}
