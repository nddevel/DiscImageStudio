using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiscImageStudio.Gnome.Imaging;

/// <summary>Conversions from the managed bitmaps to Cairo surfaces and GDK textures.</summary>
internal static class CairoImages
{
    /// <summary>Copies a bitmap into a new ARGB32 surface. Cairo's ARGB32 is premultiplied
    /// native-endian, i.e. PBGRA32 byte order on little-endian machines.</summary>
    internal static Cairo.ImageSurface FromBitmap(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        byte[] premultiplied = bitmap.ConvertTo(PixelFormats.Pbgra32);
        Cairo.ImageSurface surface = new(Cairo.Format.Argb32, width, height);
        surface.Flush();
        Span<byte> data = surface.GetData();
        int stride = surface.Stride;
        for (int y = 0; y < height; y++)
            premultiplied.AsSpan(y * width * 4, width * 4).CopyTo(data.Slice(y * stride, width * 4));
        surface.MarkDirty();
        return surface;
    }
}

/// <summary>Builds GDK textures for display. Textures are immutable and safe to create on
/// any thread; widgets must still be touched on the main thread only.</summary>
internal static class TextureFactory
{
    internal static Gdk.Texture FromBitmap(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        (byte[] pixels, Gdk.MemoryFormat format, int bytesPerPixel) = bitmap.Format == PixelFormats.Gray8
            ? (bitmap.Pixels, Gdk.MemoryFormat.G8, 1)
            : bitmap.Format == PixelFormats.Pbgra32
                ? (bitmap.Pixels, Gdk.MemoryFormat.B8g8r8a8Premultiplied, 4)
                : (bitmap.ToBgra32(), Gdk.MemoryFormat.B8g8r8a8, 4);
        using GLib.Bytes bytes = GLib.Bytes.New(pixels);
        return Gdk.MemoryTexture.New(width, height, format, bytes, (nuint)(width * bytesPerPixel));
    }

    /// <summary>Loads an image file for display, or null when it cannot be read.</summary>
    internal static Gdk.Texture? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            return Gdk.Texture.NewFromFilename(path);
        }
        catch (GLib.GException)
        {
            try
            {
                (int width, int height, byte[] pixels) = RasterOps.DecodeBgra32(File.ReadAllBytes(path));
                using GLib.Bytes bytes = GLib.Bytes.New(pixels);
                return Gdk.MemoryTexture.New(width, height, Gdk.MemoryFormat.B8g8r8a8, bytes, (nuint)(width * 4));
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
