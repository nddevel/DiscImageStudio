using System.Reflection;
using System.Windows.Threading;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;

namespace DiscImageStudio.Gnome;

/// <summary>GTK application bootstrap: settings, language, theme, stylesheet and the single
/// main window. Opening .discproj files from the file manager goes through GApplication's
/// open handler, so a second launch hands the file to the running instance.</summary>
internal static class App
{
    public const string ApplicationId = "io.github.nddevel.DiscImageStudio";

    private static MainWindow? _window;

    public static int Run(string[] arguments)
    {
        AppSettingsStore store = new();
        AppSettings settings = store.Load();
        L.Initialize(settings.Language);
        TempFiles.TryConfigureCacheDirectory(settings.CacheDirectory);

        Adw.Module.Initialize();
        // The dispatcher binds to the thread that touches it first: the GTK main thread.
        _ = Dispatcher.CurrentDispatcher;

        Adw.Application application = Adw.Application.New(ApplicationId, Gio.ApplicationFlags.HandlesOpen);
        GLib.Functions.SetApplicationName("Disc Image Studio");
        application.OnStartup += (_, _) =>
        {
            LoadStyleSheet();
            ApplyTheme(settings.Theme);
        };
        application.OnActivate += (_, _) => EnsureWindow(application, settings, store).Window.Present();
        application.OnOpen += (sender, args) =>
        {
            MainWindow window = EnsureWindow(application, settings, store);
            window.Window.Present();
            if (args.Files.FirstOrDefault()?.GetPath() is { } path)
                _ = window.OpenProjectAsync(path);
        };

        string[] argv = ["disc-image-studio-gnome", .. arguments];
        return application.RunWithSynchronizationContext(argv);
    }

    private static MainWindow EnsureWindow(Adw.Application application, AppSettings settings, AppSettingsStore store)
    {
        if (_window is null)
        {
            _window = new MainWindow(application, settings, store);
            _window.Window.OnDestroy += (_, _) => _window = null;
        }

        return _window;
    }

    public static void ApplyTheme(AppTheme theme)
    {
        Adw.StyleManager.GetDefault().SetColorScheme(theme switch
        {
            AppTheme.Light => Adw.ColorScheme.ForceLight,
            AppTheme.Dark => Adw.ColorScheme.ForceDark,
            _ => Adw.ColorScheme.Default,
        });
    }

    /// <summary>Runs on the GTK main loop; inline when already there.</summary>
    public static void RunOnMain(Action action)
    {
        if (Dispatcher.CurrentDispatcher.CheckAccess()) action();
        else Dispatcher.Post(action);
    }

    private static void LoadStyleSheet()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DiscImageStudio.Gnome.style.css");
        if (stream is null || Gdk.Display.GetDefault() is not { } display) return;
        using StreamReader reader = new(stream);
        Gtk.CssProvider provider = Gtk.CssProvider.New();
        provider.LoadFromString(reader.ReadToEnd());
        Gtk.StyleContext.AddProviderForDisplay(display, provider, Gtk.Constants.STYLE_PROVIDER_PRIORITY_APPLICATION);
    }
}
