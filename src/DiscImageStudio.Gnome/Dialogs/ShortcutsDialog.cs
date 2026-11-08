using DiscImageStudio.Localization;

namespace DiscImageStudio.Gnome.Dialogs;

/// <summary>Keyboard shortcut reference (Ctrl+?).</summary>
internal static class ShortcutsDialog
{
    public static void Show(Gtk.Widget parent)
    {
        Adw.ShortcutsDialog dialog = Adw.ShortcutsDialog.New();
        Adw.ShortcutsSection project = Adw.ShortcutsSection.New(L.T("Gnome.ShortcutsProject"));
        project.Add(Adw.ShortcutsItem.New(L.T("Xaml.NewProject"), "<Control>n"));
        project.Add(Adw.ShortcutsItem.New(L.T("Xaml.OpenProject"), "<Control>o"));
        project.Add(Adw.ShortcutsItem.New(L.T("Xaml.Save"), "<Control>s"));
        project.Add(Adw.ShortcutsItem.New(L.T("Gnome.SaveAs"), "<Control><Shift>s"));
        dialog.Add(project);
        Adw.ShortcutsSection canvas = Adw.ShortcutsSection.New(L.T("Xaml.Design"));
        canvas.Add(Adw.ShortcutsItem.New(L.T("Xaml.ZoomIn"), "<Control>plus"));
        canvas.Add(Adw.ShortcutsItem.New(L.T("Xaml.ZoomOut"), "<Control>minus"));
        canvas.Add(Adw.ShortcutsItem.New(L.T("Xaml.FitToWindow2"), "<Control>0"));
        canvas.Add(Adw.ShortcutsItem.New(L.T("Xaml.DeleteLayer"), "Delete"));
        dialog.Add(canvas);
        Adw.ShortcutsSection general = Adw.ShortcutsSection.New(L.T("Gnome.ShortcutsGeneral"));
        general.Add(Adw.ShortcutsItem.New(L.T("Settings.Settings"), "<Control>comma"));
        general.Add(Adw.ShortcutsItem.New(L.T("Gnome.KeyboardShortcuts"), "<Control>question"));
        general.Add(Adw.ShortcutsItem.New(L.T("Gnome.CloseWindow"), "<Control>w"));
        dialog.Add(general);
        dialog.Present(parent);
    }
}
