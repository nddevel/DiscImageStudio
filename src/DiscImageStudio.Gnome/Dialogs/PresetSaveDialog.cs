using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Dialogs;

/// <summary>Asks for a preset name (the WPF PresetSaveWindow). Modal; null means cancelled.</summary>
internal static class PresetSaveDialog
{
    public static PresetSaveResult? Show(Gtk.Widget parent)
    {
        Adw.AlertDialog dialog = Adw.AlertDialog.New(L.T("Xaml.SaveAsPreset"), null);
        Adw.EntryRow name = Adw.EntryRow.New();
        name.SetTitle(L.T("Xaml.PresetName"));
        name.SetActivatesDefault(true);
        Gtk.ListBox list = Gtk.ListBox.New();
        list.AddCssClass("boxed-list");
        list.SetSelectionMode(Gtk.SelectionMode.None);
        list.Append(name);
        dialog.SetExtraChild(list);
        dialog.AddResponse("cancel", L.T("Xaml.Cancel"));
        dialog.AddResponse("save", L.T("Xaml.Save"));
        dialog.SetResponseAppearance("save", Adw.ResponseAppearance.Suggested);
        dialog.SetDefaultResponse("save");
        dialog.SetCloseResponse("cancel");
        dialog.SetResponseEnabled("save", false);
        name.OnChanged += (_, _) => dialog.SetResponseEnabled("save", name.GetText().Trim().Length > 0);

        string? response = null;
        dialog.OnResponse += (_, args) => response = args.Response;
        Modal.Run(dialog, parent);
        string preset = name.GetText().Trim();
        return response == "save" && preset.Length > 0 ? new PresetSaveResult(preset) : null;
    }
}
