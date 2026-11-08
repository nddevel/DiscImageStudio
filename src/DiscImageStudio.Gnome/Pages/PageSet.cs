using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>A page of the main window bound to one view model.</summary>
internal interface IPage : IDisposable
{
    Gtk.Widget Root { get; }

    /// <summary>The page's title row and stage rail, when it has one.</summary>
    PageHeader? Header { get; }

    string Title { get; }

    void ScrollToTop();
}

/// <summary>All pages of the window, created together from the shell's view models (the WPF
/// MainWindow's _pages array). Recreated wholesale when the UI language changes.</summary>
internal sealed class PageSet : IDisposable
{
    private readonly Dictionary<Workspace, IPage> _pages = [];

    public PageSet(ShellViewModel shell, Gtk.Window window)
    {
        Home = new HomePage(shell.Home, window);
        _pages[Workspace.Design] = new DesignPage(shell, shell.Design, window);
        _pages[Workspace.Preview] = new PreviewPage(shell, shell.LivePreview, window);
        _pages[Workspace.Export] = new ExportPage(shell.Export, window);
        _pages[Workspace.Burn] = new BurnPage(shell, shell.Burn, window);
        _pages[Workspace.Calibration] = new CalibrationPage(shell, shell.Calibration, window);
        _pages[Workspace.CalibrationBurn] = new BurnPage(shell, shell.CalibrationBurn, window);
    }

    public HomePage Home { get; }

    public IEnumerable<(Workspace Workspace, Gtk.Widget Root)> WorkspacePages =>
        _pages.Select(pair => (pair.Key, pair.Value.Root));

    public string Title(Workspace workspace) =>
        _pages.TryGetValue(workspace, out IPage? page) ? page.Title : L.T("Gnome.Home");

    public void SetCurrent(Workspace workspace)
    {
        foreach (IPage page in _pages.Values)
        {
            if (page.Header is { } header) header.Current = workspace;
        }
    }

    public void ScrollToTop(Workspace workspace)
    {
        if (_pages.TryGetValue(workspace, out IPage? page)) page.ScrollToTop();
    }

    public void Dispose()
    {
        Home.Dispose();
        foreach (IPage page in _pages.Values) page.Dispose();
        _pages.Clear();
    }
}
