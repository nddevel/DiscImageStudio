using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Services;

/// <summary>GTK file dialogs behind the synchronous API the shared view models expect.
/// GTK 4 dialogs are asynchronous (and go through the desktop portal on GNOME), so each
/// call spins a nested main loop until the user answers, the way a modal Win32 dialog
/// pumps messages while it is open.</summary>
internal sealed class FileDialogService
{
    private const string ImageFilter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|所有文件|*.*";
    private readonly Func<Gtk.Window?> _ownerProvider;
    private string? _lastFolder;

    internal FileDialogService(Func<Gtk.Window?> ownerProvider)
    {
        _ownerProvider = ownerProvider;
    }

    internal string? PickImage() => PickOpen(L.T("选择源图片"), L.T(ImageFilter));

    /// <summary>Multi-select image picker for the design page's import action.</summary>
    internal string[]? PickImages()
    {
        Gtk.FileDialog dialog = CreateDialog(L.T("导入图片"), L.T(ImageFilter));
        Gio.ListModel? files = Modal.Wait(dialog.OpenMultipleAsync(_ownerProvider()));
        if (files is null) return null;
        List<string> paths = [];
        for (uint index = 0; index < files.GetNItems(); index++)
        {
            if (files.GetObject(index) is Gio.File file && file.GetPath() is { } path) paths.Add(path);
        }

        Remember(paths.FirstOrDefault());
        return paths.Count == 0 ? null : [.. paths];
    }

    internal string? PickOpen(string title, string filter)
    {
        Gtk.FileDialog dialog = CreateDialog(title, filter);
        string? path = Modal.Wait(dialog.OpenAsync(_ownerProvider()))?.GetPath();
        Remember(path);
        return path;
    }

    internal string? PickSave(string filter, string extension, string fileName)
    {
        Gtk.FileDialog dialog = CreateDialog(L.T("选择输出位置"), filter);
        dialog.SetInitialName(fileName);
        string? path = Modal.Wait(dialog.SaveAsync(_ownerProvider()))?.GetPath();
        if (path is null) return null;
        // SaveFileDialog.AddExtension: a bare name gets the default extension.
        if (!string.IsNullOrEmpty(extension) && string.IsNullOrEmpty(Path.GetExtension(path)))
            path += extension.StartsWith('.') ? extension : "." + extension;
        Remember(path);
        return path;
    }

    internal string? PickFolder(string title)
    {
        Gtk.FileDialog dialog = CreateDialog(title, filter: null);
        string? path = Modal.Wait(dialog.SelectFolderAsync(_ownerProvider()))?.GetPath();
        if (path is not null) _lastFolder = path;
        return path;
    }

    private Gtk.FileDialog CreateDialog(string title, string? filter)
    {
        Gtk.FileDialog dialog = Gtk.FileDialog.New();
        dialog.SetTitle(title);
        dialog.SetModal(true);
        if (_lastFolder is not null && Directory.Exists(_lastFolder))
            dialog.SetInitialFolder(Gio.FileHelper.NewForPath(_lastFolder));
        if (!string.IsNullOrWhiteSpace(filter))
        {
            Gio.ListStore filters = Gio.ListStore.New(Gtk.FileFilter.GetGType());
            Gtk.FileFilter? first = null;
            foreach (Gtk.FileFilter fileFilter in ParseFilter(filter))
            {
                first ??= fileFilter;
                filters.Append(fileFilter);
            }

            dialog.SetFilters(filters);
            if (first is not null) dialog.SetDefaultFilter(first);
        }

        return dialog;
    }

    private void Remember(string? path)
    {
        string? folder = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) _lastFolder = folder;
    }

    /// <summary>Turns a Win32 filter ("Images|*.png;*.jpg|All files|*.*") into GTK filters.
    /// Suffix matching is case-insensitive, matching Windows behaviour.</summary>
    internal static IEnumerable<Gtk.FileFilter> ParseFilter(string filter)
    {
        string[] parts = filter.Split('|');
        for (int index = 0; index + 1 < parts.Length; index += 2)
        {
            Gtk.FileFilter fileFilter = Gtk.FileFilter.New();
            fileFilter.SetName(parts[index].Trim());
            foreach (string pattern in parts[index + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (pattern is "*.*" or "*")
                    fileFilter.AddPattern("*");
                else if (pattern.StartsWith("*.", StringComparison.Ordinal) && pattern.IndexOfAny(['*', '?'], 2) < 0)
                    fileFilter.AddSuffix(pattern[2..]);
                else
                    fileFilter.AddPattern(pattern);
            }

            yield return fileFilter;
        }
    }
}
