using System.ComponentModel;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Dialogs;

/// <summary>Preferences (the WPF SettingsWindow): appearance and language, default locations
/// and the cache. As on Windows, nothing applies until Save.</summary>
internal static class SettingsDialog
{
    // SettingsViewModel.ThemeIndex: 0 dark, 1 light, 2 system. GNOME lists "follow system" first.
    private static readonly int[] ThemeOrder = [2, 1, 0];

    public static void Show(Gtk.Window parent, ShellViewModel shell, AppSettings settings, AppSettingsStore store,
        Action<AppSettings> apply)
    {
        using Binder binder = new();
        SettingsViewModel viewModel = new(settings, store) { IsBusyProvider = () => shell.IsBusy };
        Adw.Dialog dialog = Adw.Dialog.New();
        dialog.SetTitle(L.T("Settings.Settings"));
        dialog.SetContentWidth(640);
        dialog.SetContentHeight(620);

        viewModel.PickDirectory = (title, initial) =>
        {
            Gtk.FileDialog picker = Gtk.FileDialog.New();
            picker.SetTitle(title);
            string start = string.IsNullOrWhiteSpace(initial) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : initial;
            if (Directory.Exists(start)) picker.SetInitialFolder(Gio.FileHelper.NewForPath(start));
            return Modal.Wait(picker.SelectFolderAsync(parent))?.GetPath();
        };
        viewModel.ConfirmClearCache = () => Alerts.ConfirmAsync(dialog, L.T("清空缓存"),
            L.T("将删除本应用写入缓存目录的缩略图、合成图与预览图片。工程文件与导出文件不受影响。"), L.T("清空缓存"));
        PropertyChangedEventHandler busy = (_, args) =>
        {
            if (args.PropertyName == nameof(ShellViewModel.IsBusy)) viewModel.RefreshBusyState();
        };
        binder.Event(() => shell.PropertyChanged += busy, () => shell.PropertyChanged -= busy);
        viewModel.RefreshBusyState();

        // ---- General ----
        Adw.PreferencesPage general = Adw.PreferencesPage.New();
        general.SetTitle(L.T("Settings.General"));
        general.SetIconName("preferences-system-symbolic");
        Adw.PreferencesGroup appearance = Adw.PreferencesGroup.New();
        appearance.SetTitle(L.T("Settings.Appearance"));
        appearance.SetDescription(L.T("Settings.AppearanceAndLanguagePreferences"));
        Adw.ComboRow theme = Adw.ComboRow.New();
        theme.SetTitle(L.T("Settings.Theme"));
        theme.SetModel(Gtk.StringList.New([L.T("Settings.System"), L.T("Settings.Light"), L.T("Settings.Dark")]));
        theme.SetSelected((uint)Math.Max(0, Array.IndexOf(ThemeOrder, viewModel.ThemeIndex)));
        theme.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "selected" && theme.GetSelected() < ThemeOrder.Length)
                viewModel.ThemeIndex = ThemeOrder[theme.GetSelected()];
        };
        Adw.ComboRow language = Adw.ComboRow.New();
        language.SetTitle(L.T("Settings.Language"));
        language.SetModel(Gtk.StringList.New([L.T("Settings.System"), L.T("Settings.SimplifiedChinese"),
            L.T("Settings.TraditionalChinese"), "English", "日本語", "Türkçe", "Español", "Čeština"]));
        language.SetSelected((uint)Math.Max(0, viewModel.LanguageIndex));
        language.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "selected") viewModel.LanguageIndex = (int)language.GetSelected();
        };
        appearance.Add(theme);
        appearance.Add(language);
        general.Add(appearance);

        // ---- Storage ----
        Adw.PreferencesPage storage = Adw.PreferencesPage.New();
        storage.SetTitle(L.T("Settings.Storage"));
        storage.SetIconName("drive-harddisk-symbolic");
        Adw.PreferencesGroup locations = Adw.PreferencesGroup.New();
        locations.SetTitle(L.T("Settings.Locations"));
        locations.SetDescription(L.T("Settings.DefaultLocationsForNewProjectsAndTemporaryFiles"));
        Adw.EntryRow projectDirectory = DirectoryRow(binder, L.T("Settings.DefaultProjectLocation"), viewModel,
            nameof(SettingsViewModel.DefaultProjectDirectory), () => viewModel.DefaultProjectDirectory,
            value => viewModel.DefaultProjectDirectory = value,
            viewModel.BrowseDefaultProjectDirectoryCommand, viewModel.ResetDefaultProjectDirectoryCommand);
        projectDirectory.SetTooltipText(viewModel.DefaultProjectDirectoryPlaceholder);
        Adw.EntryRow cacheDirectory = DirectoryRow(binder, L.T("Settings.CacheLocation"), viewModel,
            nameof(SettingsViewModel.CacheDirectory), () => viewModel.CacheDirectory, value => viewModel.CacheDirectory = value,
            viewModel.BrowseCacheDirectoryCommand, viewModel.ResetCacheDirectoryCommand);
        cacheDirectory.SetTooltipText(viewModel.DefaultCacheDirectoryPlaceholder);
        locations.Add(projectDirectory);
        locations.Add(cacheDirectory);
        storage.Add(locations);

        Adw.PreferencesGroup cache = Adw.PreferencesGroup.New();
        cache.SetTitle(L.T("Settings.Cache"));
        Adw.ActionRow usage = Adw.ActionRow.New();
        binder.Watch(viewModel, [nameof(SettingsViewModel.CacheUsageText), nameof(SettingsViewModel.ActiveCacheDirectory)], () =>
        {
            usage.SetTitle(GLib.Markup.EscapeText(viewModel.CacheUsageText ?? string.Empty));
            usage.SetSubtitle(GLib.Markup.EscapeText(viewModel.ActiveCacheDirectory ?? string.Empty));
        });
        Gtk.Button clear = Ui.Button(L.T("Settings.ClearCache"), "destructive-action");
        clear.SetValign(Gtk.Align.Center);
        binder.Command(clear, viewModel.ClearCacheCommand);
        usage.AddSuffix(clear);
        cache.Add(usage);
        Adw.ButtonRow openData = Adw.ButtonRow.New();
        openData.SetTitle(L.T("Settings.OpenAppDataFolder"));
        openData.SetEndIconName("folder-open-symbolic");
        openData.OnActivated += (_, _) => viewModel.OpenAppDataDirectoryCommand.Execute(null);
        cache.Add(openData);
        Gtk.Label cacheMessage = Ui.Hint(string.Empty);
        binder.Watch(viewModel, nameof(SettingsViewModel.CacheMessage), () =>
        {
            cacheMessage.SetLabel(viewModel.CacheMessage ?? string.Empty);
            cacheMessage.SetVisible(!string.IsNullOrWhiteSpace(viewModel.CacheMessage));
        });
        cacheMessage.SetMarginTop(8);
        Adw.PreferencesGroup messageGroup = Adw.PreferencesGroup.New();
        messageGroup.Add(cacheMessage);
        storage.Add(cache);
        storage.Add(messageGroup);
        viewModel.RefreshCacheUsage();

        // ---- Chrome ----
        Adw.ViewStack stack = Adw.ViewStack.New();
        stack.AddTitledWithIcon(general, "general", L.T("Settings.General"), "preferences-system-symbolic");
        stack.AddTitledWithIcon(storage, "storage", L.T("Settings.Storage"), "drive-harddisk-symbolic");
        Adw.ViewSwitcher switcher = Adw.ViewSwitcher.New();
        switcher.SetStack(stack);
        switcher.SetPolicy(Adw.ViewSwitcherPolicy.Wide);

        Adw.HeaderBar header = Adw.HeaderBar.New();
        header.SetShowEndTitleButtons(false);
        header.SetShowStartTitleButtons(false);
        header.SetTitleWidget(switcher);
        Gtk.Button cancel = Ui.Button(L.T("Settings.Cancel"));
        cancel.OnClicked += (_, _) => dialog.Close();
        Gtk.Button save = Ui.Button(L.T("Settings.Save"), "suggested-action");
        header.PackStart(cancel);
        header.PackEnd(save);

        Adw.Banner error = Adw.Banner.New(string.Empty);
        binder.Watch(viewModel, nameof(SettingsViewModel.ErrorMessage), () =>
        {
            error.SetTitle(GLib.Markup.EscapeText(viewModel.ErrorMessage ?? string.Empty));
            error.SetRevealed(!string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
        });

        Adw.ToolbarView toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(header);
        toolbar.AddTopBar(error);
        toolbar.SetContent(stack);
        dialog.SetChild(toolbar);

        bool saved = false;
        save.OnClicked += (_, _) =>
        {
            if (!viewModel.TrySave()) return;
            saved = true;
            dialog.Close();
        };
        Modal.Run(dialog, parent);
        if (saved && viewModel.SavedSettings is { } result) apply(result);
    }

    private static Adw.EntryRow DirectoryRow(Binder binder, string title, SettingsViewModel viewModel, string property,
        Func<string> get, Action<string> set, System.Windows.Input.ICommand browse, System.Windows.Input.ICommand reset)
    {
        Adw.EntryRow row = Adw.EntryRow.New();
        row.SetTitle(title);
        binder.Entry(row, viewModel, property, get, set);
        Gtk.Button browseButton = Ui.IconButton("document-open-symbolic", L.T("Xaml.Browse"), "flat");
        browseButton.SetValign(Gtk.Align.Center);
        binder.Command(browseButton, browse);
        Gtk.Button resetButton = Ui.IconButton("edit-undo-symbolic", L.T("Settings.Reset"), "flat");
        resetButton.SetValign(Gtk.Align.Center);
        binder.Command(resetButton, reset);
        row.AddSuffix(browseButton);
        row.AddSuffix(resetButton);
        return row;
    }
}
