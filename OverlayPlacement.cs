namespace Vermintide_2;

readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

static class OverlayPlacement
{
    public const int Margin = 12;

    public static Point AtOffset(WindowBounds window, Size size, int offsetX, int offsetY) =>
        Clamp(window, new Point(window.Left + offsetX, window.Top + offsetY), size);

    public static Point TopCenter(WindowBounds window, Size size) =>
        Clamp(window, new Point(window.Left + (window.Width - size.Width) / 2, window.Top + Margin), size);

    public static bool TryPlaceLegacy(string? anchor, WindowBounds window, Size size, out Point point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(anchor))
            return false;

        int x = anchor switch
        {
            "TopLeft" or "MiddleLeft" or "BottomLeft" => window.Left + Margin,
            "TopRight" or "MiddleRight" or "BottomRight" => window.Right - Margin - size.Width,
            "TopCenter" or "MiddleCenter" or "BottomCenter" => window.Left + (window.Width - size.Width) / 2,
            _ => int.MinValue
        };
        int y = anchor switch
        {
            "TopLeft" or "TopCenter" or "TopRight" => window.Top + Margin,
            "BottomLeft" or "BottomCenter" or "BottomRight" => window.Bottom - Margin - size.Height,
            "MiddleLeft" or "MiddleCenter" or "MiddleRight" => window.Top + (window.Height - size.Height) / 2,
            _ => int.MinValue
        };
        if (x == int.MinValue || y == int.MinValue)
            return false;

        point = Clamp(window, new Point(x, y), size);
        return true;
    }

    public static Point PlaceEditor(WindowBounds window, Point status, Size statusSize, Size editorSize)
    {
        const int gap = 8;
        int below = status.Y + statusSize.Height + gap;
        int above = status.Y - gap - editorSize.Height;
        int y = below + editorSize.Height <= window.Bottom - Margin
            ? below
            : above >= window.Top + Margin
                ? above
                : window.Top + Margin;
        return Clamp(window, new Point(status.X, y), editorSize);
    }

    public static Point Clamp(WindowBounds window, Point point, Size size)
    {
        int minX = window.Left + Margin;
        int minY = window.Top + Margin;
        int maxX = window.Right - Margin - size.Width;
        int maxY = window.Bottom - Margin - size.Height;
        return new Point(ClampAxis(point.X, minX, maxX), ClampAxis(point.Y, minY, maxY));
    }

    static int ClampAxis(int value, int min, int max)
    {
        if (max < min)
            return min;
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
