using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TinyEXR.Viewer.Models;
using TinyEXR.Viewer.Services;

namespace TinyEXR.Viewer.Controls;

public sealed class PixelSelectionOverlay : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<PixelSelectionOverlay, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<PixelInspection?> SelectionProperty =
        AvaloniaProperty.Register<PixelSelectionOverlay, PixelInspection?>(nameof(Selection));

    private static readonly Pen OutlinePen = new(Brushes.Black, 3);
    private static readonly Pen MarkerPen = new(Brushes.Cyan, 1);

    static PixelSelectionOverlay()
    {
        AffectsRender<PixelSelectionOverlay>(SourceProperty, SelectionProperty);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public PixelInspection? Selection
    {
        get => GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not Bitmap bitmap || Selection is not PixelInspection selection)
        {
            return;
        }

        PreviewImageBounds bounds = PreviewPixelMapper.GetImageBounds(
            Bounds.Width, Bounds.Height, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        double pixelSize = bounds.Width / bitmap.PixelSize.Width;
        Point center = new(bounds.X + (selection.X + 0.5) * pixelSize,
            bounds.Y + (selection.Y + 0.5) * pixelSize);
        double radius = Math.Max(4, pixelSize / 2);
        Rect marker = new(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using (context.PushClip(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)))
        {
            context.DrawRectangle(null, OutlinePen, marker);
            context.DrawRectangle(null, MarkerPen, marker);
            DrawArm(context, new Point(center.X - radius - 8, center.Y), new Point(center.X - radius - 2, center.Y));
            DrawArm(context, new Point(center.X + radius + 2, center.Y), new Point(center.X + radius + 8, center.Y));
            DrawArm(context, new Point(center.X, center.Y - radius - 8), new Point(center.X, center.Y - radius - 2));
            DrawArm(context, new Point(center.X, center.Y + radius + 2), new Point(center.X, center.Y + radius + 8));
        }
    }

    private static void DrawArm(DrawingContext context, Point start, Point end)
    {
        context.DrawLine(OutlinePen, start, end);
        context.DrawLine(MarkerPen, start, end);
    }
}
