// Managed stand-ins for the WPF imaging types used by the shared view models and services.
// Pixels live in plain byte arrays (straight BGRA32, premultiplied PBGRA32, BGR24/32 or
// Gray8); GTK views turn them into Gdk textures through Gnome.Imaging.TextureFactory.

using System.Collections.ObjectModel;
using DiscImageStudio.Gnome.Imaging;

namespace System.Windows.Media
{
    public readonly struct PixelFormat : IEquatable<PixelFormat>
    {
        internal PixelFormat(string name, int bitsPerPixel)
        {
            Name = name;
            BitsPerPixel = bitsPerPixel;
        }

        internal string Name { get; }

        public int BitsPerPixel { get; }

        internal int BytesPerPixel => BitsPerPixel / 8;

        public bool Equals(PixelFormat other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PixelFormat other && Equals(other);

        public override int GetHashCode() => Name?.GetHashCode(StringComparison.Ordinal) ?? 0;

        public static bool operator ==(PixelFormat left, PixelFormat right) => left.Equals(right);

        public static bool operator !=(PixelFormat left, PixelFormat right) => !left.Equals(right);

        public override string ToString() => Name ?? "Default";
    }

    public static class PixelFormats
    {
        public static PixelFormat Default { get; } = new("Default", 32);
        public static PixelFormat Bgra32 { get; } = new("Bgra32", 32);
        public static PixelFormat Pbgra32 { get; } = new("Pbgra32", 32);
        public static PixelFormat Bgr32 { get; } = new("Bgr32", 32);
        public static PixelFormat Bgr24 { get; } = new("Bgr24", 24);
        public static PixelFormat Rgb24 { get; } = new("Rgb24", 24);
        public static PixelFormat Gray8 { get; } = new("Gray8", 8);
    }
}

namespace System.Windows.Media.Imaging
{
    public enum BitmapCacheOption
    {
        Default = 0,
        OnDemand = 0,
        OnLoad = 1,
        None = 2,
    }

    [Flags]
    public enum BitmapCreateOptions
    {
        None = 0,
        PreservePixelFormat = 1,
        DelayCreation = 2,
        IgnoreColorProfile = 4,
        IgnoreImageCache = 8,
    }

    /// <summary>Palettes are never used; the type exists for the <c>null</c> argument.</summary>
    public sealed class BitmapPalette
    {
        private BitmapPalette()
        {
        }
    }

    /// <summary>An immutable-once-frozen raster. Thread-safe to read, like a frozen WPF bitmap.</summary>
    public class BitmapSource
    {
        private byte[]? _pixels;
        private int _width;
        private int _height;
        private PixelFormat _format = PixelFormats.Bgra32;
        private double _dpiX = 96;
        private double _dpiY = 96;

        protected BitmapSource()
        {
        }

        private BitmapSource(int width, int height, double dpiX, double dpiY, PixelFormat format, byte[] pixels)
        {
            Initialize(width, height, format, pixels, dpiX, dpiY);
        }

        public virtual int PixelWidth { get { EnsureLoaded(); return _width; } }

        public virtual int PixelHeight { get { EnsureLoaded(); return _height; } }

        public virtual PixelFormat Format { get { EnsureLoaded(); return _format; } }

        public double DpiX { get { EnsureLoaded(); return _dpiX; } }

        public double DpiY { get { EnsureLoaded(); return _dpiY; } }

        /// <summary>Size in device-independent units (1/96 inch).</summary>
        public double Width => PixelWidth * 96.0 / DpiX;

        public double Height => PixelHeight * 96.0 / DpiY;

        public bool IsFrozen { get; private set; }

        public bool CanFreeze => true;

        public void Freeze() => IsFrozen = true;

        /// <summary>Tightly packed pixels in <see cref="Format"/>; stride = width × bytes per pixel.</summary>
        internal byte[] Pixels { get { EnsureLoaded(); return _pixels!; } }

        internal int Stride => PixelWidth * Format.BytesPerPixel;

        public static BitmapSource Create(int pixelWidth, int pixelHeight, double dpiX, double dpiY,
            PixelFormat pixelFormat, BitmapPalette? palette, Array pixels, int stride)
        {
            ArgumentNullException.ThrowIfNull(pixels);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelHeight);
            if (pixels is not byte[] bytes)
                throw new NotSupportedException("Only byte pixel buffers are supported.");
            int rowBytes = checked(pixelWidth * pixelFormat.BytesPerPixel);
            if (stride < rowBytes || bytes.Length < checked((long)stride * (pixelHeight - 1) + rowBytes))
                throw new ArgumentException("The pixel buffer is too small for the requested size.", nameof(pixels));
            byte[] packed = new byte[checked(rowBytes * pixelHeight)];
            for (int y = 0; y < pixelHeight; y++)
                Buffer.BlockCopy(bytes, y * stride, packed, y * rowBytes, rowBytes);
            return new BitmapSource(pixelWidth, pixelHeight, dpiX, dpiY, pixelFormat, packed);
        }

        public void CopyPixels(Array pixels, int stride, int offset) =>
            CopyPixels(new Int32Rect(0, 0, PixelWidth, PixelHeight), pixels, stride, offset);

        public void CopyPixels(Int32Rect sourceRect, Array pixels, int stride, int offset)
        {
            if (pixels is not byte[] destination)
                throw new NotSupportedException("Only byte pixel buffers are supported.");
            if (sourceRect.IsEmpty) sourceRect = new Int32Rect(0, 0, PixelWidth, PixelHeight);
            if (sourceRect.X < 0 || sourceRect.Y < 0
                || sourceRect.X + sourceRect.Width > PixelWidth || sourceRect.Y + sourceRect.Height > PixelHeight)
                throw new ArgumentOutOfRangeException(nameof(sourceRect));
            int bytesPerPixel = Format.BytesPerPixel;
            int rowBytes = sourceRect.Width * bytesPerPixel;
            if (stride < rowBytes)
                throw new ArgumentOutOfRangeException(nameof(stride));
            byte[] source = Pixels;
            for (int row = 0; row < sourceRect.Height; row++)
            {
                int from = ((sourceRect.Y + row) * PixelWidth + sourceRect.X) * bytesPerPixel;
                Buffer.BlockCopy(source, from, destination, offset + row * stride, rowBytes);
            }
        }

        protected void Initialize(int width, int height, PixelFormat format, byte[] pixels, double dpiX = 96, double dpiY = 96)
        {
            _width = width;
            _height = height;
            _format = format;
            _pixels = pixels;
            _dpiX = dpiX > 0 && double.IsFinite(dpiX) ? dpiX : 96;
            _dpiY = dpiY > 0 && double.IsFinite(dpiY) ? dpiY : 96;
        }

        protected bool IsLoaded => _pixels is not null;

        /// <summary>Lazily decoded sources (delay-created frames) load here.</summary>
        protected virtual void EnsureLoaded()
        {
            if (_pixels is null)
                throw new InvalidOperationException("The bitmap has not been initialized.");
        }

        /// <summary>Straight-alpha BGRA32 copy of any supported format.</summary>
        internal byte[] ToBgra32()
        {
            byte[] source = Pixels;
            int count = PixelWidth * PixelHeight;
            if (Format == PixelFormats.Bgra32) return source;
            byte[] result = new byte[checked(count * 4)];
            if (Format == PixelFormats.Gray8)
            {
                for (int i = 0; i < count; i++)
                {
                    byte value = source[i];
                    int p = i * 4;
                    result[p] = result[p + 1] = result[p + 2] = value;
                    result[p + 3] = 255;
                }
            }
            else if (Format == PixelFormats.Pbgra32)
            {
                for (int i = 0; i < count; i++)
                {
                    int p = i * 4;
                    byte alpha = source[p + 3];
                    result[p + 3] = alpha;
                    if (alpha == 0) continue;
                    result[p] = (byte)Math.Min(255, (source[p] * 255 + alpha / 2) / alpha);
                    result[p + 1] = (byte)Math.Min(255, (source[p + 1] * 255 + alpha / 2) / alpha);
                    result[p + 2] = (byte)Math.Min(255, (source[p + 2] * 255 + alpha / 2) / alpha);
                }
            }
            else if (Format == PixelFormats.Bgr32)
            {
                Buffer.BlockCopy(source, 0, result, 0, result.Length);
                for (int i = 3; i < result.Length; i += 4) result[i] = 255;
            }
            else if (Format == PixelFormats.Bgr24 || Format == PixelFormats.Rgb24)
            {
                bool rgb = Format == PixelFormats.Rgb24;
                for (int i = 0; i < count; i++)
                {
                    int s = i * 3, p = i * 4;
                    result[p] = source[rgb ? s + 2 : s];
                    result[p + 1] = source[s + 1];
                    result[p + 2] = source[rgb ? s : s + 2];
                    result[p + 3] = 255;
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported pixel format {Format}.");
            }

            return result;
        }

        /// <summary>Converts to the requested format (BGRA32, PBGRA32, BGR32, BGR24 or Gray8).</summary>
        internal byte[] ConvertTo(PixelFormat format)
        {
            if (format == Format) return Pixels;
            byte[] bgra = ToBgra32();
            int count = PixelWidth * PixelHeight;
            if (format == PixelFormats.Bgra32) return bgra;
            if (format == PixelFormats.Pbgra32)
            {
                byte[] result = new byte[bgra.Length];
                for (int p = 0; p < bgra.Length; p += 4)
                {
                    int alpha = bgra[p + 3];
                    result[p] = (byte)((bgra[p] * alpha + 127) / 255);
                    result[p + 1] = (byte)((bgra[p + 1] * alpha + 127) / 255);
                    result[p + 2] = (byte)((bgra[p + 2] * alpha + 127) / 255);
                    result[p + 3] = (byte)alpha;
                }

                return result;
            }

            if (format == PixelFormats.Bgr32)
            {
                byte[] result = (byte[])bgra.Clone();
                for (int p = 3; p < result.Length; p += 4) result[p] = 255;
                return result;
            }

            if (format == PixelFormats.Bgr24)
            {
                byte[] result = new byte[checked(count * 3)];
                for (int i = 0; i < count; i++)
                {
                    result[i * 3] = bgra[i * 4];
                    result[i * 3 + 1] = bgra[i * 4 + 1];
                    result[i * 3 + 2] = bgra[i * 4 + 2];
                }

                return result;
            }

            if (format == PixelFormats.Gray8)
            {
                // WIC's grey conversion: Rec. 601 luma, alpha ignored.
                byte[] result = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    int p = i * 4;
                    result[i] = (byte)Math.Clamp((int)Math.Round(
                        0.114 * bgra[p] + 0.587 * bgra[p + 1] + 0.299 * bgra[p + 2]), 0, 255);
                }

                return result;
            }

            throw new NotSupportedException($"Unsupported pixel format {format}.");
        }
    }

    public sealed class FormatConvertedBitmap : BitmapSource
    {
        public FormatConvertedBitmap(BitmapSource source, PixelFormat destinationFormat,
            BitmapPalette? destinationPalette, double alphaThreshold)
        {
            ArgumentNullException.ThrowIfNull(source);
            Initialize(source.PixelWidth, source.PixelHeight, destinationFormat,
                source.ConvertTo(destinationFormat), source.DpiX, source.DpiY);
        }
    }

    /// <summary>Decodes a file or stream once EndInit runs, honouring the WPF
    /// DecodePixelWidth/DecodePixelHeight scaling rules.</summary>
    public sealed class BitmapImage : BitmapSource
    {
        private bool _initializing;

        public BitmapImage()
        {
        }

        public BitmapImage(Uri uriSource)
        {
            UriSource = uriSource;
            EndInit();
        }

        public Uri? UriSource { get; set; }

        public Stream? StreamSource { get; set; }

        public BitmapCacheOption CacheOption { get; set; }

        public BitmapCreateOptions CreateOptions { get; set; }

        public int DecodePixelWidth { get; set; }

        public int DecodePixelHeight { get; set; }

        public void BeginInit() => _initializing = true;

        public void EndInit()
        {
            _initializing = false;
            byte[] data = BitmapData.Read(StreamSource, UriSource);
            (int width, int height, byte[] pixels) = RasterOps.DecodeBgra32(data);
            (int targetWidth, int targetHeight) = RasterOps.DecodeSize(width, height, DecodePixelWidth, DecodePixelHeight);
            pixels = RasterOps.ResizeBgra32(pixels, width, height, targetWidth, targetHeight);
            Initialize(targetWidth, targetHeight, PixelFormats.Bgra32, pixels);
        }

        protected override void EnsureLoaded()
        {
            if (_initializing && !IsLoaded)
                throw new InvalidOperationException("BitmapImage initialization is not complete.");
            base.EnsureLoaded();
        }
    }

    public sealed class BitmapFrame : BitmapSource
    {
        private byte[]? _encoded;
        private int _headerWidth;
        private int _headerHeight;

        private BitmapFrame()
        {
        }

        public override int PixelWidth => IsLoaded || _encoded is null ? base.PixelWidth : _headerWidth;

        public override int PixelHeight => IsLoaded || _encoded is null ? base.PixelHeight : _headerHeight;

        public static BitmapFrame Create(BitmapSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            BitmapFrame frame = new();
            frame.Initialize(source.PixelWidth, source.PixelHeight, source.Format, source.Pixels, source.DpiX, source.DpiY);
            return frame;
        }

        public static BitmapFrame Create(Stream stream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
            FromData(BitmapData.Read(stream, null), createOptions);

        public static BitmapFrame Create(Uri uri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
            FromData(BitmapData.Read(null, uri), createOptions);

        internal static BitmapFrame FromData(byte[] data, BitmapCreateOptions createOptions)
        {
            BitmapFrame frame = new();
            if ((createOptions & BitmapCreateOptions.DelayCreation) != 0
                && RasterOps.TryReadHeader(data) is { } header)
            {
                frame._encoded = data;
                frame._headerWidth = header.Width;
                frame._headerHeight = header.Height;
                return frame;
            }

            (int width, int height, byte[] pixels) = RasterOps.DecodeBgra32(data);
            frame.Initialize(width, height, PixelFormats.Bgra32, pixels);
            return frame;
        }

        protected override void EnsureLoaded()
        {
            if (!IsLoaded && _encoded is not null)
            {
                (int width, int height, byte[] pixels) = RasterOps.DecodeBgra32(_encoded);
                Initialize(width, height, PixelFormats.Bgra32, pixels);
                _encoded = null;
            }

            base.EnsureLoaded();
        }
    }

    public sealed class BitmapDecoder
    {
        private BitmapDecoder(BitmapFrame frame)
        {
            Frames = new ReadOnlyCollection<BitmapFrame>([frame]);
        }

        public ReadOnlyCollection<BitmapFrame> Frames { get; }

        public static BitmapDecoder Create(Stream bitmapStream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
            new(BitmapFrame.FromData(BitmapData.Read(bitmapStream, null), createOptions));

        public static BitmapDecoder Create(Uri bitmapUri, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
            new(BitmapFrame.FromData(BitmapData.Read(null, bitmapUri), createOptions));
    }

    public sealed class PngBitmapEncoder
    {
        public IList<BitmapFrame> Frames { get; } = new List<BitmapFrame>();

        public void Save(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (Frames.Count == 0) throw new InvalidOperationException("No frames to encode.");
            BitmapFrame frame = Frames[0];
            bool gray = frame.Format == PixelFormats.Gray8;
            RasterOps.WritePng(stream, frame.PixelWidth, frame.PixelHeight,
                gray ? frame.Pixels : frame.ToBgra32(), gray);
        }
    }

    internal static class BitmapData
    {
        internal static byte[] Read(Stream? stream, Uri? uri)
        {
            if (stream is not null)
            {
                if (stream.CanSeek) stream.Position = 0;
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                return copy.ToArray();
            }

            if (uri is null) throw new InvalidOperationException("Either StreamSource or UriSource must be set.");
            string path = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
            return File.ReadAllBytes(path);
        }
    }
}
