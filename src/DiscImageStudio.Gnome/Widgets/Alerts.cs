using DiscImageStudio.Localization;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Confirmation prompts as libadwaita alert dialogs, standing in for the WPF
/// ConfirmModal overlay.</summary>
internal static class Alerts
{
    /// <summary>Two-way confirm (burn confirmation, overwrite guards). True confirms.</summary>
    public static Task<bool> ConfirmAsync(Gtk.Widget parent, string title, string message, string confirmLabel,
        bool destructive = true)
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Adw.AlertDialog dialog = Adw.AlertDialog.New(title, message);
        dialog.AddResponse("cancel", L.T("Xaml.Cancel"));
        dialog.AddResponse("confirm", confirmLabel);
        dialog.SetResponseAppearance("confirm", destructive ? Adw.ResponseAppearance.Destructive : Adw.ResponseAppearance.Suggested);
        dialog.SetDefaultResponse("cancel");
        dialog.SetCloseResponse("cancel");
        dialog.OnResponse += (_, args) => completion.TrySetResult(args.Response == "confirm");
        dialog.Present(parent);
        return completion.Task;
    }

    /// <summary>Save / discard / cancel. True saves, false discards, null cancels.</summary>
    public static Task<bool?> ThreeWayAsync(Gtk.Widget parent, string title, string message, string confirmLabel,
        string neutralLabel)
    {
        TaskCompletionSource<bool?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Adw.AlertDialog dialog = Adw.AlertDialog.New(title, message);
        dialog.AddResponse("cancel", L.T("Xaml.Cancel"));
        dialog.AddResponse("discard", neutralLabel);
        dialog.AddResponse("save", confirmLabel);
        dialog.SetResponseAppearance("discard", Adw.ResponseAppearance.Destructive);
        dialog.SetResponseAppearance("save", Adw.ResponseAppearance.Suggested);
        dialog.SetDefaultResponse("save");
        dialog.SetCloseResponse("cancel");
        dialog.OnResponse += (_, args) => completion.TrySetResult(args.Response switch
        {
            "save" => true,
            "discard" => false,
            _ => null,
        });
        dialog.Present(parent);
        return completion.Task;
    }

    public static void Message(Gtk.Widget parent, string title, string message)
    {
        Adw.AlertDialog dialog = Adw.AlertDialog.New(title, message);
        dialog.AddResponse("close", L.T("Gnome.Close"));
        dialog.SetDefaultResponse("close");
        dialog.SetCloseResponse("close");
        dialog.Present(parent);
    }
}
