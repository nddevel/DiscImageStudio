using System.Windows.Media;
using System.Windows.Media.Imaging;
using DiscImageStudio.Gnome.Imaging;

namespace DiscImageStudio.Services;

/// <summary>One layer flattened for rendering: physical placement in millimetres relative to
/// the disc centre plus the decoded bitmap. <paramref name="BaseWidthMm"/>/
/// <paramref name="BaseHeightMm"/> are the layer's physical size at scale 1.</summary>
public sealed record DesignLayerRenderItem(
    BitmapSource Image,
    double BaseWidthMm,
    double BaseHeightMm,
    double TranslateXMm,
    double TranslateYMm,
    double RotationDeg,
    double Scale);

/// <summary>Cairo implementation of the layer compositor: renders the layer stack into the
/// square source picture the generation pipeline consumes. The square's centre is the disc
/// centre and its side spans 2 × <c>canvasOuterRadiusMm</c>. Thread-safe; the view models
/// call it from worker threads.</summary>
internal static class DesignComposer
{
    /// <summary>Upper bound on the decoded side of a layer image; anything larger is
    /// downscaled once at load, which stays invisible at the 8192 export size.</summary>
    internal const int MaxDecodeWidth = 4096;

    /// <summary>Width of the decode used for the layer panel's thumbnails.</summary>
    internal const int ThumbnailDecodeWidth = 96;

    public static void Render(
        IReadOnlyList<DesignLayerRenderItem> layers,
        double canvasOuterRadiusMm,
        int outputSize,
        string outputPath)
    {
        if (canvasOuterRadiusMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(canvasOuterRadiusMm));
        }

        double pixelsPerMm = outputSize / (2.0 * canvasOuterRadiusMm);
        double center = outputSize / 2.0;

        using Cairo.ImageSurface target = new(Cairo.Format.Argb32, outputSize, outputSize);
        using (Cairo.Context context = new(target))
        {
            // The layer list is ordered topmost-first, so painting runs bottom-up.
            for (int index = layers.Count - 1; index >= 0; index--)
            {
                DesignLayerRenderItem item = layers[index];
                double widthPixels = item.BaseWidthMm * item.Scale * pixelsPerMm;
                double heightPixels = item.BaseHeightMm * item.Scale * pixelsPerMm;
                if (widthPixels <= 0 || heightPixels <= 0)
                {
                    continue;
                }

                using Cairo.ImageSurface image = CairoImages.FromBitmap(item.Image);
                context.Save();
                // Same transform as the WPF composer: rotate about the layer centre, then place
                // the layer's top-left corner relative to the disc centre.
                context.Translate(
                    center + item.TranslateXMm * pixelsPerMm,
                    center + item.TranslateYMm * pixelsPerMm);
                context.Rotate(item.RotationDeg * Math.PI / 180.0);
                context.Translate(-widthPixels / 2, -heightPixels / 2);
                context.Scale(widthPixels / image.Width, heightPixels / image.Height);
                context.SetSourceSurface(image, 0, 0);
                using (Cairo.Pattern pattern = context.GetSource())
                {
                    pattern.Filter = Cairo.Filter.Good;
                }

                context.Rectangle(0, 0, image.Width, image.Height);
                context.Fill();
                context.Restore();
            }
        }

        target.Flush();
        Cairo.Status status = target.WriteToPng(outputPath);
        if (status != Cairo.Status.Success)
        {
            throw new IOException($"Could not write {outputPath}: {status}.");
        }
    }

    /// <summary>Decodes an image for cross-thread reads, capped at <see cref="MaxDecodeWidth"/>
    /// on its longer side. Any decode failure returns null.</summary>
    public static BitmapSource? DecodeFrozenImage(string path)
    {
        try
        {
            return Decode(path, MaxDecodeWidth);
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException
                                              or OutOfMemoryException
                                              or ArgumentException
                                              or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Small decode for the layer panel's list row.</summary>
    public static BitmapSource? DecodeThumbnail(string path)
    {
        try
        {
            return Decode(path, ThumbnailDecodeWidth);
        }
        catch (Exception)
        {
            // Thumbnails are cosmetic: a damaged file must not fail the whole project open.
            return null;
        }
    }

    private static BitmapSource Decode(string path, int maxSide)
    {
        byte[] data = File.ReadAllBytes(path);
        (int width, int height, byte[] pixels) = RasterOps.DecodeBgra32(data);
        if (Math.Max(width, height) > maxSide)
        {
            double scale = (double)maxSide / Math.Max(width, height);
            int newWidth = Math.Max(1, (int)Math.Round(width * scale));
            int newHeight = Math.Max(1, (int)Math.Round(height * scale));
            pixels = RasterOps.ResizeBgra32(pixels, width, height, newWidth, newHeight);
            (width, height) = (newWidth, newHeight);
        }

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
