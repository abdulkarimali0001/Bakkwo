using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Bakkwo.App.Controls;

/// <summary>A slice: <see cref="Label"/> in bold, a small <see cref="Caption"/> under it, and a <see cref="Hint"/> shown in the middle on hover.</summary>
public sealed record DonutItem(string Id, string Label, string? Caption = null, string? Hint = null, bool Special = false);

/// <summary>
/// The ring of target formats that opens where you drop files. Drawn by hand: each slice is an
/// annular sector built from two arcs, and hit-testing is done with polar coordinates.
/// </summary>
public sealed class DonutMenu : Control
{
    public const double OuterRadius = 140;
    public const double InnerRadius = 56;

    /// <summary>Returned by <see cref="HitTest"/> for the middle of the ring (cancel).</summary>
    public const int CenterIndex = -2;

    /// <summary>Returned by <see cref="HitTest"/> outside the ring.</summary>
    public const int NoIndex = -1;

    private static readonly Color Accent = Color.FromRgb(0x6C, 0x5C, 0xE7);

    private IReadOnlyList<DonutItem> _items = [];
    private int _highlighted = NoIndex;
    private string _centerText = "";
    private string _cancelText = "Cancel";

    public IReadOnlyList<DonutItem> Items
    {
        get => _items;
        set
        {
            _items = value;
            _highlighted = NoIndex;
            InvalidateVisual();
        }
    }

    public int Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value) return;
            _highlighted = value;
            InvalidateVisual();
        }
    }

    public string CenterText
    {
        get => _centerText;
        set
        {
            _centerText = value;
            InvalidateVisual();
        }
    }

    public string CancelText
    {
        get => _cancelText;
        set
        {
            _cancelText = value;
            InvalidateVisual();
        }
    }

    public double Diameter => (OuterRadius + 14) * 2;

    protected override Size MeasureOverride(Size availableSize) => new(Diameter, Diameter);

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    /// <summary>Which slice a point (in this control's coordinates) falls on.</summary>
    public int HitTest(Point point)
    {
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);

        if (radius < InnerRadius) return CenterIndex;
        // A little slack outside the ring so a fast drag still lands on the slice.
        if (radius > OuterRadius + 30 || _items.Count == 0) return NoIndex;

        // Screen y points down, so atan2 runs clockwise; shift so 0° is at 12 o'clock.
        var degrees = Math.Atan2(dy, dx) * 180 / Math.PI;
        var fromTop = (degrees + 90 + 360) % 360;
        var index = (int)(fromTop / (360.0 / _items.Count));
        return Math.Min(index, _items.Count - 1);
    }

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var center = Center;

        IBrush slice = new SolidColorBrush(dark ? Color.FromRgb(38, 40, 52) : Color.FromRgb(250, 250, 253));
        IBrush highlight = new SolidColorBrush(Accent);
        IBrush text = new SolidColorBrush(dark ? Color.FromRgb(235, 235, 245) : Color.FromRgb(30, 30, 40));
        IBrush shadow = new SolidColorBrush(Color.FromArgb(dark ? (byte)90 : (byte)45, 0, 0, 0));
        IPen outline = new Pen(new SolidColorBrush(dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0)), 1);

        context.DrawEllipse(shadow, null, new Point(center.X, center.Y + 3), OuterRadius + 6, OuterRadius + 6);
        // Solid base so the gaps between slices read as divider lines, not holes.
        IBrush divider = new SolidColorBrush(dark ? Color.FromRgb(70, 72, 86) : Color.FromRgb(214, 214, 222));
        context.DrawEllipse(divider, null, center, OuterRadius + 0.5, OuterRadius + 0.5);

        var count = _items.Count;
        if (count > 0)
        {
            var sweep = 360.0 / count;
            var gap = count > 1 ? 1.4 : 0;
            var fontSize = count > 10 ? 11.5 : count > 8 ? 12.5 : 14;
            var captionSize = count > 10 ? 9 : 10;
            var bold = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);
            IBrush special = new SolidColorBrush(Color.FromArgb(dark ? (byte)70 : (byte)36, Accent.R, Accent.G, Accent.B));
            IBrush caption = new SolidColorBrush(dark ? Color.FromArgb(170, 235, 235, 245) : Color.FromArgb(150, 30, 30, 40));

            for (var i = 0; i < count; i++)
            {
                var start = -90 + i * sweep + gap / 2;
                var end = Math.Min(-90 + (i + 1) * sweep - gap / 2, start + 359.9);
                var isHot = i == _highlighted;

                var item = _items[i];
                // The slice under the pointer grows a little, so it's obvious what a release will pick.
                var outer = isHot ? OuterRadius + 8 : OuterRadius;
                var fill = isHot ? highlight : item.Special ? special : slice;
                if (item.Special && !isHot) context.DrawGeometry(slice, null, Slice(center, InnerRadius, outer, start, end));
                context.DrawGeometry(fill, outline, Slice(center, InnerRadius, outer, start, end));

                var label = new FormattedText(item.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    bold, fontSize, isHot ? Brushes.White : text);
                var anchor = PointAt(center, (InnerRadius + outer) / 2, (start + end) / 2);
                if (item.Caption is { Length: > 0 } captionText)
                {
                    var small = new FormattedText(captionText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        Typeface.Default, captionSize, isHot ? Brushes.White : caption);
                    var top = anchor.Y - (label.Height + small.Height) / 2;
                    context.DrawText(label, new Point(anchor.X - label.Width / 2, top));
                    context.DrawText(small, new Point(anchor.X - small.Width / 2, top + label.Height - 1));
                }
                else
                {
                    context.DrawText(label, new Point(anchor.X - label.Width / 2, anchor.Y - label.Height / 2));
                }
            }
        }

        // Middle: what was dropped, or "Cancel" while hovered.
        var cancelHot = _highlighted == CenterIndex;
        context.DrawEllipse(cancelHot ? new SolidColorBrush(Color.FromArgb(60, 220, 60, 60)) : slice, outline,
            center, InnerRadius - 6, InnerRadius - 6);

        // The middle explains the slice under the pointer ("Convert to MP3 audio"), otherwise shows the file.
        var hint = _highlighted >= 0 && _highlighted < _items.Count ? _items[_highlighted].Hint : null;
        var middleText = cancelHot ? _cancelText : hint ?? _centerText;
        var middle = new FormattedText(middleText, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, hint is null ? Typeface.Default : new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold),
            11, hint is null ? text : new SolidColorBrush(dark ? Color.FromRgb(0xB8, 0xAD, 0xFF) : Accent))
        {
            MaxTextWidth = (InnerRadius - 10) * 2,
            TextAlignment = TextAlignment.Center,
            MaxLineCount = 3,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(middle, new Point(center.X - middle.MaxTextWidth / 2, center.Y - middle.Height / 2));
    }

    private static StreamGeometry Slice(Point c, double inner, double outer, double startDeg, double endDeg)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var largeArc = endDeg - startDeg > 180;
            g.BeginFigure(PointAt(c, outer, startDeg), isFilled: true);
            g.ArcTo(PointAt(c, outer, endDeg), new Size(outer, outer), 0, largeArc, SweepDirection.Clockwise);
            g.LineTo(PointAt(c, inner, endDeg));
            g.ArcTo(PointAt(c, inner, startDeg), new Size(inner, inner), 0, largeArc, SweepDirection.CounterClockwise);
            g.EndFigure(isClosed: true);
        }
        return geometry;
    }

    private static Point PointAt(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
