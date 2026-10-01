namespace TinyEXR.Viewer.Services;

internal static class PreviewPixelMapper
{
    // The preview uses a centered Uniform stretch at 96 DPI.
    public static PreviewImageBounds GetImageBounds(double viewportWidth, double viewportHeight, int width, int height)
    {
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            viewportWidth <= 0 || viewportHeight <= 0 || width <= 0 || height <= 0)
        {
            return default;
        }

        double scale = Math.Min(viewportWidth / width, viewportHeight / height);
        double renderedWidth = width * scale;
        double renderedHeight = height * scale;
        return new PreviewImageBounds(
            (viewportWidth - renderedWidth) / 2,
            (viewportHeight - renderedHeight) / 2,
            renderedWidth,
            renderedHeight);
    }

    public static bool TryGetPixel(
        double viewportWidth, double viewportHeight, int width, int height,
        double pointerX, double pointerY, out int x, out int y)
    {
        x = 0;
        y = 0;
        PreviewImageBounds bounds = GetImageBounds(viewportWidth, viewportHeight, width, height);
        double localX = pointerX - bounds.X;
        double localY = pointerY - bounds.Y;
        if (!double.IsFinite(localX) || !double.IsFinite(localY) ||
            bounds.Width <= 0 || bounds.Height <= 0 ||
            localX < 0 || localY < 0 || localX >= bounds.Width || localY >= bounds.Height)
        {
            return false;
        }

        x = Math.Min((int)(localX / bounds.Width * width), width - 1);
        y = Math.Min((int)(localY / bounds.Height * height), height - 1);
        return true;
    }
}

internal readonly record struct PreviewImageBounds(double X, double Y, double Width, double Height);
