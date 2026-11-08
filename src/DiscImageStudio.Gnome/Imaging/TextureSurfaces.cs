namespace DiscImageStudio.Gnome.Imaging;

/// <summary>Copies a GDK texture into a Cairo image surface for custom drawing.</summary>
internal static class TextureSurfaces
{
    public static Cairo.ImageSurface ToSurface(Gdk.Texture texture)
    {
        int width = texture.GetWidth(), height = texture.GetHeight();
        Gdk.TextureDownloader downloader = Gdk.TextureDownloader.New(texture);
        downloader.SetFormat(Gdk.MemoryFormat.B8g8r8a8Premultiplied);
        using GLib.Bytes bytes = downloader.DownloadBytes(out nuint stride);
        ReadOnlySpan<byte> source = bytes.GetRegionSpan<byte>(0, bytes.GetSize());
        Cairo.ImageSurface surface = new(Cairo.Format.Argb32, width, height);
        surface.Flush();
        Span<byte> data = surface.GetData();
        int targetStride = surface.Stride;
        for (int y = 0; y < height; y++)
            source.Slice((int)((nuint)y * stride), width * 4).CopyTo(data.Slice(y * targetStride, width * 4));
        surface.MarkDirty();
        return surface;
    }
}
