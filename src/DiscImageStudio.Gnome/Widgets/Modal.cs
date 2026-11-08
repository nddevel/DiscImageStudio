namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Blocks the caller on the GTK main thread until an asynchronous GTK operation
/// finishes, while a nested main loop keeps the UI responsive. This is what modal dialogs
/// do on Windows, and it lets the shared view models keep their synchronous dialog calls.</summary>
internal static class Modal
{
    /// <summary>Returns the task's result, or default when the user dismissed the dialog.</summary>
    /// <summary>Presents a dialog and returns when it closes.</summary>
    internal static void Run(Adw.Dialog dialog, Gtk.Widget parent)
    {
        GLib.MainLoop loop = GLib.MainLoop.New(null, false);
        dialog.OnClosed += (_, _) => loop.Quit();
        dialog.Present(parent);
        loop.Run();
    }

    internal static T? Wait<T>(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!task.IsCompleted)
        {
            GLib.MainLoop loop = GLib.MainLoop.New(null, false);
            task.ContinueWith(_ => loop.Quit(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (!task.IsCompleted) loop.Run();
        }

        try
        {
            return task.GetAwaiter().GetResult();
        }
        catch (GLib.GException)
        {
            // Gtk.DialogError.Dismissed / Cancelled: the user closed the dialog.
            return default;
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }
}
