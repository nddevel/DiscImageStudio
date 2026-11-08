using MahApps.Metro.IconPacks;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Maps the Lucide icon names used by the shared view models to GNOME symbolic icons.</summary>
internal static class Icons
{
    public static string ForLucide(PackIconLucideKind kind) => kind switch
    {
        PackIconLucideKind.SquarePen => "document-edit-symbolic",
        PackIconLucideKind.Disc => "media-optical-dvd-symbolic",
        PackIconLucideKind.Disc3 => "media-optical-cd-audio-symbolic",
        PackIconLucideKind.DiscAlbum => "media-optical-bd-symbolic",
        _ => "media-optical-symbolic",
    };

    public static string ForLucide(string? name) => name switch
    {
        "SquarePen" => "document-edit-symbolic",
        "Disc" => "media-optical-dvd-symbolic",
        "Disc3" => "media-optical-cd-audio-symbolic",
        "DiscAlbum" => "media-optical-bd-symbolic",
        _ => "media-optical-symbolic",
    };
}
