using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome;

/// <summary>Developer hooks for scripted screenshots of the UI (used when updating docs):
/// DIS_GTK_DEBUG_PAGE=Design|Preview|Export|Burn|Calibration navigates once a project is
/// open, DIS_GTK_DEBUG_ACTION=&lt;window action&gt; activates e.g. new-project or preferences.</summary>
internal static class DevHooks
{
    public static void Install(MainWindow window)
    {
        string? page = Environment.GetEnvironmentVariable("DIS_GTK_DEBUG_PAGE");
        string? action = Environment.GetEnvironmentVariable("DIS_GTK_DEBUG_ACTION");
        if (string.IsNullOrEmpty(page) && string.IsNullOrEmpty(action)) return;
        int attempts = 0;
        GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 250, () =>
        {
            attempts++;
            if (!string.IsNullOrEmpty(page) && Enum.TryParse(page, out Workspace workspace))
            {
                if (!window.Shell.IsProjectOpen && attempts < 40) return true;
                window.Shell.NavigateTo(workspace);
            }

            // DIS_GTK_DEBUG_EXPORT=1 runs the export page's Generate command (end-to-end checks).
            if (Environment.GetEnvironmentVariable("DIS_GTK_DEBUG_EXPORT") == "1" && window.Shell.Export.GenerateCommand.CanExecute(null))
                _ = window.Shell.Export.GenerateCommand.ExecuteAsync(null);

            // The window is a Gio.ActionGroup holding the "win." actions without their prefix.
            if (!string.IsNullOrEmpty(action)) window.Window.ActivateAction(action, null);
            return false;
        });
    }
}
