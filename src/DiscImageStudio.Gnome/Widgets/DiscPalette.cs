namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Disc canvas and calibration colours for the dark and light styles (the WPF
/// Tokens / Palette.Light disc brushes). Chosen from libadwaita's current style.</summary>
internal readonly record struct Rgba(double R, double G, double B, double A = 1)
{
    public static Rgba FromHex(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255.0,
        ((argb >> 8) & 0xFF) / 255.0,
        (argb & 0xFF) / 255.0,
        ((argb >> 24) & 0xFF) / 255.0);

    public void Apply(Cairo.Context cr) => cr.SetSourceRgba(R, G, B, A);
}

internal sealed record DiscPalette(
    Rgba Backdrop,
    Rgba DiscBody,
    Rgba Hub,
    Rgba Guide,
    Rgba Mask,
    Rgba Selection,
    Rgba DataRegion,
    Rgba MissingImage,
    Rgba PhotoBackdrop,
    Rgba Edge,
    Rgba HubOutline,
    Rgba Trace,
    Rgba Prediction,
    Rgba Handle,
    Rgba Text)
{
    public static DiscPalette Dark { get; } = new(
        Rgba.FromHex(0xFF0D0D0F), Rgba.FromHex(0xFF1C1C20), Rgba.FromHex(0xFF0D0D0F), Rgba.FromHex(0x55A1A1AA),
        Rgba.FromHex(0x88000000), Rgba.FromHex(0xFF3385FF), Rgba.FromHex(0x663385FF), Rgba.FromHex(0xFFF31260),
        Rgba.FromHex(0xFF13151A), Rgba.FromHex(0xFF74C0FF), Rgba.FromHex(0xFFF5A524), Rgba.FromHex(0xFF22D3EE),
        Rgba.FromHex(0xFFFFA94D), Rgba.FromHex(0xFFD4D4D8), Rgba.FromHex(0xFFA1A1AA));

    public static DiscPalette Light { get; } = new(
        Rgba.FromHex(0xFFE6E6EA), Rgba.FromHex(0xFFFFFFFF), Rgba.FromHex(0xFFE2E2E6), Rgba.FromHex(0x5571717A),
        Rgba.FromHex(0x66000000), Rgba.FromHex(0xFF2569CC), Rgba.FromHex(0x662569CC), Rgba.FromHex(0xFFF31260),
        Rgba.FromHex(0xFFD9DADE), Rgba.FromHex(0xFF0B66D0), Rgba.FromHex(0xFFC4841D), Rgba.FromHex(0xFF0891B2),
        Rgba.FromHex(0xFFC2410C), Rgba.FromHex(0xFF3F3F46), Rgba.FromHex(0xFF52525B));

    public static DiscPalette Current => Adw.StyleManager.GetDefault().GetDark() ? Dark : Light;
}
