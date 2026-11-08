using StbImageSharp;
using StbImageWriteSharp;

namespace DiscImageStudio.Gnome.Imaging;

/// <summary>Managed raster helpers behind the WPF imaging stand-ins: decoding to straight
/// BGRA32, resampling and PNG encoding. Everything here is thread-safe and allocation-only,
/// so view models may call it from worker threads exactly as they call WIC on Windows.</summary>
internal static class RasterOps
{
    /// <summary>Reads width and height without decoding pixels. Returns null when the format
    /// is not recognised by the managed decoder.</summary>
    internal static (int Width, int Height)? TryReadHeader(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        StbImageSharp.ImageInfo? info = StbImageSharp.ImageInfo.FromStream(stream);
        if (info is { } header && header.Width > 0 && header.Height > 0)
            return (header.Width, header.Height);
        return TryDecodeWithGdk(data) is { } decoded ? (decoded.Width, decoded.Height) : null;
    }

    /// <summary>Decodes the first frame to straight-alpha BGRA32.</summary>
    internal static (int Width, int Height, byte[] Pixels) DecodeBgra32(byte[] data)
    {
        try
        {
            using MemoryStream stream = new(data, writable: false);
            if (StbImageSharp.ImageInfo.FromStream(stream) is { Width: > 0, Height: > 0 })
            {
                stream.Position = 0;
                ImageResult image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                SwapRedAndBlue(image.Data);
                return (image.Width, image.Height, image.Data);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
                                              or IndexOutOfRangeException or InvalidDataException)
        {
            // Fall through to GTK's loaders (TIFF and anything the managed decoder rejects).
        }

        return TryDecodeWithGdk(data)
               ?? throw new NotSupportedException("Unsupported or damaged image file.");
    }

    /// <summary>GTK ships its own PNG/JPEG/TIFF loaders and falls back to gdk-pixbuf; both
    /// are documented as thread-safe for texture creation and download.</summary>
    private static (int Width, int Height, byte[] Pixels)? TryDecodeWithGdk(byte[] data)
    {
        try
        {
            using GLib.Bytes bytes = GLib.Bytes.New(data);
            Gdk.Texture texture = Gdk.Texture.NewFromBytes(bytes);
            int width = texture.GetWidth(), height = texture.GetHeight();
            Gdk.TextureDownloader downloader = Gdk.TextureDownloader.New(texture);
            downloader.SetFormat(Gdk.MemoryFormat.B8g8r8a8);
            using GLib.Bytes downloaded = downloader.DownloadBytes(out nuint stride);
            ReadOnlySpan<byte> source = downloaded.GetRegionSpan<byte>(0, downloaded.GetSize());
            byte[] pixels = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++)
                source.Slice(checked((int)((nuint)y * stride)), width * 4).CopyTo(pixels.AsSpan(y * width * 4, width * 4));
            return (width, height, pixels);
        }
        catch (Exception exception) when (exception is GLib.GException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>WPF's DecodePixelWidth/Height rules: one side given keeps the aspect ratio,
    /// both given stretch, neither keeps the source size.</summary>
    internal static (int Width, int Height) DecodeSize(int width, int height, int decodeWidth, int decodeHeight)
    {
        if (decodeWidth > 0 && decodeHeight > 0) return (decodeWidth, decodeHeight);
        if (decodeWidth > 0)
            return (decodeWidth, Math.Max(1, (int)Math.Round(height * (double)decodeWidth / width)));
        if (decodeHeight > 0)
            return (Math.Max(1, (int)Math.Round(width * (double)decodeHeight / height)), decodeHeight);
        return (width, height);
    }

    /// <summary>Separable triangle-filter resample of straight-alpha BGRA32. The filter widens
    /// with the reduction factor, giving area-averaged downscaling (like WIC's Fant mode) and
    /// bilinear upscaling. Colour is weighted by alpha so transparent edges do not bleed.</summary>
    internal static byte[] ResizeBgra32(byte[] source, int width, int height, int newWidth, int newHeight)
    {
        if (newWidth == width && newHeight == height) return source;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(newHeight);

        // Premultiply into floats once; both passes then work on premultiplied values.
        float[] premultiplied = new float[checked(width * height * 4)];
        for (int i = 0; i < width * height; i++)
        {
            int p = i * 4;
            float alpha = source[p + 3] / 255f;
            premultiplied[p] = source[p] * alpha;
            premultiplied[p + 1] = source[p + 1] * alpha;
            premultiplied[p + 2] = source[p + 2] * alpha;
            premultiplied[p + 3] = source[p + 3];
        }

        float[] horizontal = ResampleAxis(premultiplied, width, height, newWidth, horizontalPass: true);
        float[] both = ResampleAxis(horizontal, newWidth, height, newHeight, horizontalPass: false);

        byte[] result = new byte[checked(newWidth * newHeight * 4)];
        for (int i = 0; i < newWidth * newHeight; i++)
        {
            int p = i * 4;
            float alpha = both[p + 3];
            if (alpha <= 0.5f) continue;
            float unpremultiply = 255f / alpha;
            result[p] = ToByte(both[p] * unpremultiply);
            result[p + 1] = ToByte(both[p + 1] * unpremultiply);
            result[p + 2] = ToByte(both[p + 2] * unpremultiply);
            result[p + 3] = ToByte(alpha);
        }

        return result;
    }

    private static float[] ResampleAxis(float[] source, int width, int height, int newLength, bool horizontalPass)
    {
        int oldLength = horizontalPass ? width : height;
        int outWidth = horizontalPass ? newLength : width;
        int outHeight = horizontalPass ? height : newLength;
        float[] result = new float[checked(outWidth * outHeight * 4)];
        if (oldLength == newLength)
        {
            Array.Copy(source, result, result.Length);
            return result;
        }

        double scale = (double)newLength / oldLength;
        double support = scale < 1 ? 1 / scale : 1;
        (int Start, float[] Weights)[] taps = new (int, float[])[newLength];
        for (int i = 0; i < newLength; i++)
        {
            double center = (i + 0.5) / scale - 0.5;
            int start = (int)Math.Floor(center - support) + 1;
            int end = (int)Math.Ceiling(center + support) - 1;
            start = Math.Max(start, 0);
            end = Math.Min(end, oldLength - 1);
            if (end < start) end = start = Math.Clamp((int)Math.Round(center), 0, oldLength - 1);
            float[] weights = new float[end - start + 1];
            float sum = 0;
            for (int j = start; j <= end; j++)
            {
                float weight = (float)Math.Max(0, 1 - Math.Abs(j - center) / support);
                weights[j - start] = weight;
                sum += weight;
            }

            if (sum <= 0)
            {
                Array.Clear(weights);
                weights[Math.Clamp((int)Math.Round(center) - start, 0, weights.Length - 1)] = 1;
            }
            else
            {
                for (int k = 0; k < weights.Length; k++) weights[k] /= sum;
            }

            taps[i] = (start, weights);
        }

        Parallel.For(0, horizontalPass ? outHeight : outWidth, line =>
        {
            for (int i = 0; i < newLength; i++)
            {
                (int start, float[] weights) = taps[i];
                float b = 0, g = 0, r = 0, a = 0;
                for (int k = 0; k < weights.Length; k++)
                {
                    int index = horizontalPass
                        ? (line * width + start + k) * 4
                        : ((start + k) * width + line) * 4;
                    float weight = weights[k];
                    b += source[index] * weight;
                    g += source[index + 1] * weight;
                    r += source[index + 2] * weight;
                    a += source[index + 3] * weight;
                }

                int output = horizontalPass ? (line * outWidth + i) * 4 : (i * outWidth + line) * 4;
                result[output] = b;
                result[output + 1] = g;
                result[output + 2] = r;
                result[output + 3] = a;
            }
        });
        return result;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)(value + 0.5f), 0, 255);

    internal static void SwapRedAndBlue(byte[] pixels)
    {
        for (int i = 0; i + 3 < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
    }

    /// <summary>Writes a Gray8 or straight BGRA32 buffer as PNG.</summary>
    internal static void WritePng(Stream output, int width, int height, byte[] pixels, bool grayscale)
    {
        byte[] data = pixels;
        if (!grayscale)
        {
            data = (byte[])pixels.Clone();
            SwapRedAndBlue(data);
        }

        new ImageWriter().WritePng(data, width, height,
            grayscale ? StbImageWriteSharp.ColorComponents.Grey : StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,
            output);
    }
}
