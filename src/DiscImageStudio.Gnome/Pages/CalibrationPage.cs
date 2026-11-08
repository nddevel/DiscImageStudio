using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

internal sealed class CalibrationPage : IPage
{
    public CalibrationPage(ShellViewModel shell, CalibrationViewModel viewModel, Gtk.Window window)
    {
        Root = Adw.StatusPage.New();
        Header = null;
        _ = (shell, viewModel, window);
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => "CalibrationPage";

    public void ScrollToTop()
    {
    }

    public void Dispose()
    {
    }
}
