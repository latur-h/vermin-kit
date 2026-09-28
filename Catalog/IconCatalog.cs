using System.Drawing.Drawing2D;

namespace VerminKit;

sealed class IconCatalog
{
    readonly string root = Path.Combine(AppContext.BaseDirectory, "Catalog", "icons");
    readonly Dictionary<string, Image?> images = new(StringComparer.OrdinalIgnoreCase);

    public Image? Hero(string id, int size = 48) => Load(Path.Combine("heroes", id + ".png"), size);

    public Image? Career(string id, int size = 56) => Load(Path.Combine("careers", id + ".png"), size);

    public Image? Talent(string careerId, int row, int column, int size = 56) =>
        Load(Path.Combine("talents", careerId, (row * 3 + column + 1).ToString("00") + ".png"), size);

    public Image? Weapon(string? id) =>
        string.IsNullOrEmpty(id) ? null : Load(Path.Combine("weapons", id + ".png"), 22);

    public Image? Trait(string? id) =>
        string.IsNullOrEmpty(id) ? null : Load(Path.Combine("traits", id + ".png"), 22);

    public Image? Slot(string id) => Load(Path.Combine("slots", id + ".png"), 18);

    Image? Load(string relative, int size)
    {
        var key = relative + "\n" + size;
        if (images.TryGetValue(key, out var cached))
            return cached;

        var path = Path.Combine(root, relative);
        if (!File.Exists(path))
        {
            images[key] = null;
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var source = new Bitmap(stream);
            var bitmap = new Bitmap(size, size);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(source, Fit(source.Size, size));
            images[key] = bitmap;
            return bitmap;
        }
        catch (Exception)
        {
            images[key] = null;
            return null;
        }
    }

    static Rectangle Fit(Size source, int box)
    {
        if (source.Width < 1 || source.Height < 1)
            return new Rectangle(0, 0, box, box);

        var scale = Math.Min(box / (float)source.Width, box / (float)source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return new Rectangle((box - width) / 2, (box - height) / 2, width, height);
    }
}
