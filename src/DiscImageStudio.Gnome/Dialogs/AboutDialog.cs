using System.Reflection;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Gnome.Dialogs;

/// <summary>The libadwaita About window: version, licence, upstream credit and links.</summary>
internal static class AboutDialog
{
    public const string RepositoryUrl = "https://github.com/nddevel/DiscImageStudio";
    public const string UpstreamUrl = "https://github.com/JiaFeiMiao-K-Cat/DiscImageStudio";

    public static string Version =>
        typeof(AboutDialog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0]
        ?? typeof(AboutDialog).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public static void Show(Gtk.Widget parent)
    {
        Adw.AboutDialog dialog = Adw.AboutDialog.New();
        dialog.SetApplicationName("Disc Image Studio");
        dialog.SetApplicationIcon(App.ApplicationId);
        dialog.SetVersion(Version);
        dialog.SetDeveloperName("JiaFeiMiao-K-Cat & contributors");
        dialog.SetComments(L.T("Xaml.DiscImageStudioIsAnAllInOneWorkspace"));
        dialog.SetWebsite(RepositoryUrl);
        dialog.SetIssueUrl(RepositoryUrl + "/issues");
        dialog.AddLink(L.T("Gnome.UpstreamProject"), UpstreamUrl);
        dialog.SetCopyright("© 2026 Disc Image Studio contributors");
        dialog.SetLicenseType(Gtk.License.Gpl30);
        dialog.AddCreditSection(L.T("Gnome.GtkPort"), ["nddevel https://github.com/nddevel"]);
        dialog.AddAcknowledgementSection(L.T("Gnome.Acknowledgements"),
            ["arduinocelentano — CDImage https://github.com/arduinocelentano/cdimage"]);
        dialog.AddLegalSection("GirCore", "© GirCore contributors", Gtk.License.MitX11, null);
        dialog.AddLegalSection("CommunityToolkit.Mvvm", "© .NET Foundation and Contributors", Gtk.License.MitX11, null);
        dialog.AddLegalSection("StbImageSharp / StbImageWriteSharp", "© Roman Shapiro, Sean Barrett", Gtk.License.MitX11, null);
        dialog.Present(parent);
    }
}
