using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

internal sealed class PreviewPage : IPage
{
    public PreviewPage(ShellViewModel shell, LivePreviewViewModel viewModel, Gtk.Window window)
    {
        Root = Adw.StatusPage.New();
        Header = null;
        _ = (shell, viewModel, window);
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => "PreviewPage";

    public void ScrollToTop()
    {
    }

    public void Dispose()
    {
    }
}
