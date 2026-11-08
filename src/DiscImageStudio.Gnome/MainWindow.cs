using System.ComponentModel;
using System.Windows.Threading;
using DiscImageStudio.Burning;
using DiscImageStudio.Core;
using DiscImageStudio.Gnome.Dialogs;
using DiscImageStudio.Gnome.Pages;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome;

/// <summary>The application window: the home start screen, and for an open project a sidebar
/// with the workflow pages, the job dock and toasts. It plays the role of the WPF MainWindow
/// and drives the same <see cref="ShellViewModel"/>.</summary>
internal sealed class MainWindow
{
    private static readonly (Workspace Workspace, string Key, string Icon)[] DiscMakingNav =
    [
        (Workspace.Design, "Xaml.Design", "applications-graphics-symbolic"),
        (Workspace.Export, "Xaml.Export", "folder-download-symbolic"),
        (Workspace.Burn, "Xaml.Burn", "media-record-symbolic"),
    ];

    private static readonly (Workspace Workspace, string Key, string Icon)[] CalibrationNav =
    [
        (Workspace.Preview, "Xaml.Preview", "view-reveal-symbolic"),
        (Workspace.Calibration, "Xaml.Calibration", "find-location-symbolic"),
    ];

    private readonly Adw.Application _application;
    private readonly AppSettingsStore _settingsStore;
    private readonly ShellViewModel _shell;
    private readonly Binder _binder = new();
    private readonly Adw.ToastOverlay _toastOverlay;
    private readonly Gtk.Stack _root;
    private readonly Adw.OverlaySplitView _split;
    private readonly Gtk.Stack _pageStack;
    private readonly Adw.ToolbarView _homeView;
    private readonly Adw.ToolbarView _contentView;
    private readonly Adw.WindowTitle _contentTitle;
    private readonly Gtk.ListBox _navList;
    private readonly Dictionary<Workspace, Gtk.ListBoxRow> _navRows = [];
    private readonly Dictionary<ToastItem, Adw.Toast> _toasts = [];
    private readonly Gtk.Label _projectNameLabel;
    private readonly Gtk.Label _projectBadge;
    private readonly Gtk.Label _projectDirtyLabel;
    private readonly JobDock _homeDock;
    private readonly JobDock _workspaceDock;
    private AppSettings _settings;
    private PageSet? _pages;
    private bool _closeApproved;
    private bool _isCloseFlowRunning;
    private bool _syncingNav;

    public MainWindow(Adw.Application application, AppSettings settings, AppSettingsStore settingsStore)
    {
        _application = application;
        _settings = settings;
        _settingsStore = settingsStore;

        Window = Adw.ApplicationWindow.New(application);
        Window.SetTitle("Disc Image Studio");
        Window.SetIconName(App.ApplicationId);
        Window.SetDefaultSize(1300, 820);
        Window.SetSizeRequest(720, 560);

        _shell = new ShellViewModel(Dispatcher.CurrentDispatcher, new LinuxOpticalDiscBurner())
        {
            Dialogs = new FileDialogService(() => Window),
            ConfirmHandler = (title, message, confirmLabel) => Alerts.ConfirmAsync(Window, title, message, confirmLabel),
            SaveConfirmationHandler = (title, message, confirmLabel, neutralLabel) =>
                Alerts.ThreeWayAsync(Window, title, message, confirmLabel, neutralLabel),
        };
        _shell.ShowProjectDialogHandler = request => ProjectDialog.Show(Window, _shell, request, _settings);
        _shell.Calibration.ShowPresetSaveDialog = () => PresetSaveDialog.Show(Window);

        // ---- Home ----
        _homeDock = new JobDock(_shell, Window);
        _homeView = Adw.ToolbarView.New();
        Adw.HeaderBar homeHeader = Adw.HeaderBar.New();
        homeHeader.SetShowTitle(false);
        homeHeader.PackEnd(CreateMainMenuButton());
        _homeView.AddTopBar(homeHeader);
        _homeView.AddBottomBar(_homeDock.Root);

        // ---- Project workspace: sidebar + content ----
        Adw.ToolbarView sidebarView = Adw.ToolbarView.New();
        Adw.HeaderBar sidebarHeader = Adw.HeaderBar.New();
        sidebarHeader.SetShowEndTitleButtons(false);
        Gtk.Button backHome = Ui.IconButton("go-previous-symbolic", L.T("Xaml.BackToHome"));
        _binder.Command(backHome, _shell.BackHomeCommand);
        sidebarHeader.PackStart(backHome);
        Gtk.Button save = Ui.IconButton("document-save-symbolic", L.T("Xaml.Save"));
        _binder.Command(save, _shell.SaveProjectCommand);
        sidebarHeader.PackEnd(save);
        sidebarHeader.SetTitleWidget(Adw.WindowTitle.New("Disc Image Studio", string.Empty));
        sidebarView.AddTopBar(sidebarHeader);

        _projectNameLabel = Ui.Line(string.Empty, "heading");
        _projectBadge = Ui.Badge(string.Empty);
        _projectDirtyLabel = Ui.Line(L.T("Xaml.Unsaved"), "caption", "warning");
        Gtk.Box projectInfo = Ui.VBox(4, _projectNameLabel, Ui.HBox(6, _projectBadge, _projectDirtyLabel));
        Gtk.Image settingsIcon = Gtk.Image.NewFromIconName("document-properties-symbolic");
        settingsIcon.AddCssClass("dim-label");
        Gtk.Box projectButtonContent = Ui.HBox(8, projectInfo.Expand(), settingsIcon.Align(Gtk.Align.End, Gtk.Align.Center));
        Gtk.Button projectButton = Gtk.Button.New();
        projectButton.SetChild(projectButtonContent);
        projectButton.AddCssClass("flat");
        projectButton.AddCssClass("project-header");
        projectButton.SetTooltipText(L.T("Xaml.ProjectSettings"));
        _binder.Command(projectButton, _shell.EditProjectCommand);

        _navList = Gtk.ListBox.New();
        _navList.AddCssClass("navigation-sidebar");
        _navList.SetSelectionMode(Gtk.SelectionMode.Single);
        AddNavGroup(L.T("Xaml.GroupDiscMaking"), DiscMakingNav);
        AddNavGroup(L.T("Xaml.GroupCalibration"), CalibrationNav);
        _navList.OnRowActivated += (_, args) =>
        {
            foreach ((Workspace workspace, Gtk.ListBoxRow row) in _navRows)
            {
                if (row.Handle.DangerousGetHandle() == args.Row.Handle.DangerousGetHandle())
                    _shell.NavigateTo(workspace);
            }
        };
        _navList.OnRowSelected += (_, args) =>
        {
            if (_syncingNav || args.Row is null) return;
            foreach ((Workspace workspace, Gtk.ListBoxRow row) in _navRows)
            {
                if (row.Handle.DangerousGetHandle() == args.Row.Handle.DangerousGetHandle()
                    && workspace != NavWorkspace(_shell.CurrentWorkspace))
                    _shell.NavigateTo(workspace);
            }
        };

        Gtk.Box sidebarContent = Ui.VBox(6, projectButton.Margins(6, 8, 0, 8), _navList);
        sidebarView.SetContent(Ui.Scrolled(sidebarContent));

        _pageStack = Gtk.Stack.New();
        _pageStack.SetTransitionType(Gtk.StackTransitionType.Crossfade);
        _pageStack.SetTransitionDuration(150);
        _pageStack.SetVhomogeneous(false);
        _workspaceDock = new JobDock(_shell, Window);
        _contentView = Adw.ToolbarView.New();
        Adw.HeaderBar contentHeader = Adw.HeaderBar.New();
        _contentTitle = Adw.WindowTitle.New("Disc Image Studio", string.Empty);
        contentHeader.SetTitleWidget(_contentTitle);
        Gtk.ToggleButton sidebarToggle = Gtk.ToggleButton.New();
        sidebarToggle.SetIconName("sidebar-show-symbolic");
        sidebarToggle.SetTooltipText(L.T("Xaml.CollapseSidebar"));
        contentHeader.PackStart(sidebarToggle);
        contentHeader.PackEnd(CreateMainMenuButton());
        _contentView.AddTopBar(contentHeader);
        _contentView.SetContent(_pageStack);
        _contentView.AddBottomBar(_workspaceDock.Root);

        _split = Adw.OverlaySplitView.New();
        _split.SetSidebar(sidebarView);
        _split.SetContent(_contentView);
        _split.SetSidebarWidthFraction(0.2);
        _split.SetMinSidebarWidth(220);
        _split.SetMaxSidebarWidth(280);
        sidebarToggle.SetActive(true);
        sidebarToggle.OnToggled += (_, _) => _split.SetShowSidebar(sidebarToggle.GetActive());
        _split.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "show-sidebar" && sidebarToggle.GetActive() != _split.GetShowSidebar())
                sidebarToggle.SetActive(_split.GetShowSidebar());
        };

        _root = Gtk.Stack.New();
        _root.SetTransitionType(Gtk.StackTransitionType.Crossfade);
        _root.SetTransitionDuration(150);
        _root.AddNamed(_homeView, "home");
        _root.AddNamed(_split, "workspace");

        _toastOverlay = Adw.ToastOverlay.New();
        _toastOverlay.SetChild(_root);
        Window.SetContent(_toastOverlay);

        // Narrow windows fold the sidebar into an overlay, like the WPF collapsed rail.
        Adw.Breakpoint narrow = Adw.Breakpoint.New(Adw.BreakpointCondition.Parse("max-width: 860sp"));
        GObject.Value collapsed = new(true);
        narrow.AddSetter(_split, "collapsed", collapsed);
        Window.AddBreakpoint(narrow);

        BuildPages();
        InstallActions();
        WireShell();

        Window.OnCloseRequest += OnCloseRequest;
        Window.OnMap += (_, _) => GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
        {
            _shell.MarkWindowLoaded();
            return false;
        });
        DevHooks.Install(this);
    }

    public Adw.ApplicationWindow Window { get; }

    public ShellViewModel Shell => _shell;

    /// <summary>Opens a project file passed on the command line or from the file manager.</summary>
    public Task OpenProjectAsync(string path) => _shell.OpenProjectFromAsync(path);

    private void AddNavGroup(string title, (Workspace Workspace, string Key, string Icon)[] items)
    {
        Gtk.Label header = Ui.Line(title, "caption-heading", "dim-label");
        header.Margins(12, 12, 4, 12);
        Gtk.ListBoxRow headerRow = Gtk.ListBoxRow.New();
        headerRow.SetChild(header);
        headerRow.SetSelectable(false);
        headerRow.SetActivatable(false);
        headerRow.SetCanFocus(false);
        _navList.Append(headerRow);
        foreach ((Workspace workspace, string key, string icon) in items)
        {
            Gtk.Image image = Gtk.Image.NewFromIconName(icon);
            Gtk.Label label = Ui.Line(L.T(key));
            Gtk.ListBoxRow row = Gtk.ListBoxRow.New();
            row.SetChild(Ui.HBox(12, image, label).Margins(8, 8, 8, 8));
            _navList.Append(row);
            _navRows[workspace] = row;
        }
    }

    private static Workspace NavWorkspace(Workspace workspace) =>
        workspace == Workspace.CalibrationBurn ? Workspace.Calibration : workspace;

    private Gtk.MenuButton CreateMainMenuButton()
    {
        Gio.Menu menu = Gio.Menu.New();
        Gio.Menu project = Gio.Menu.New();
        project.Append(L.T("Xaml.NewProject"), "win.new-project");
        project.Append(L.T("Xaml.OpenProject"), "win.open-project");
        project.Append(L.T("Xaml.Save"), "win.save-project");
        project.Append(L.T("Gnome.SaveAs"), "win.save-project-as");
        project.Append(L.T("Xaml.ProjectSettings"), "win.project-settings");
        menu.AppendSection(null, project);
        Gio.Menu app = Gio.Menu.New();
        app.Append(L.T("Settings.Settings"), "win.preferences");
        app.Append(L.T("Gnome.KeyboardShortcuts"), "win.show-help-overlay");
        app.Append(L.T("Gnome.AboutApp"), "win.about");
        menu.AppendSection(null, app);

        Gtk.MenuButton button = Gtk.MenuButton.New();
        button.SetIconName("open-menu-symbolic");
        button.SetMenuModel(menu);
        button.SetTooltipText(L.T("Gnome.MainMenu"));
        button.SetPrimary(true);
        return button;
    }

    private void InstallActions()
    {
        AddAction("new-project", () => _ = _shell.NewProjectCommand.ExecuteAsync(null), _shell.NewProjectCommand);
        AddAction("open-project", () => _ = _shell.OpenProjectCommand.ExecuteAsync(null), _shell.OpenProjectCommand);
        AddAction("save-project", SaveFromShortcut, null);
        AddAction("save-project-as", () => _ = _shell.SaveProjectAsCommand.ExecuteAsync(null), _shell.SaveProjectAsCommand);
        AddAction("project-settings", () => _ = _shell.EditProjectCommand.ExecuteAsync(null), _shell.EditProjectCommand);
        AddAction("preferences", OpenSettingsDialog, null);
        AddAction("about", () => AboutDialog.Show(Window), null);
        AddAction("show-help-overlay", () => ShortcutsDialog.Show(Window), null);

        _application.SetAccelsForAction("win.new-project", ["<Control>n"]);
        _application.SetAccelsForAction("win.open-project", ["<Control>o"]);
        _application.SetAccelsForAction("win.save-project", ["<Control>s"]);
        _application.SetAccelsForAction("win.save-project-as", ["<Control><Shift>s"]);
        _application.SetAccelsForAction("win.preferences", ["<Control>comma"]);
        _application.SetAccelsForAction("win.show-help-overlay", ["<Control>question"]);
        _application.SetAccelsForAction("window.close", ["<Control>w"]);
    }

    private void AddAction(string name, Action activate, System.Windows.Input.ICommand? command)
    {
        Gio.SimpleAction action = Gio.SimpleAction.New(name, null);
        action.OnActivate += (_, _) => activate();
        Window.AddAction(action);
        if (command is not null)
        {
            void Refresh() => action.SetEnabled(command.CanExecute(null));
            command.CanExecuteChanged += (_, _) => Refresh();
            Refresh();
        }
    }

    /// <summary>Ctrl+S saves the calibration session on the calibration pages and the project
    /// everywhere else, as on Windows.</summary>
    private void SaveFromShortcut()
    {
        if (_shell.CurrentWorkspace is Workspace.Calibration or Workspace.CalibrationBurn)
        {
            if (_shell.Calibration.SaveSessionCommand.CanExecute(null))
                _shell.Calibration.SaveSessionCommand.Execute(null);
        }
        else if (_shell.IsProjectOpen)
        {
            _ = _shell.SaveProjectCommand.ExecuteAsync(null);
        }
    }

    private void WireShell()
    {
        _binder.Watch(_shell,
            [nameof(ShellViewModel.CurrentWorkspace), nameof(ShellViewModel.IsProjectOpen),
             nameof(ShellViewModel.IsDirty), nameof(ShellViewModel.CurrentProject),
             nameof(ShellViewModel.IsProjectMode), nameof(ShellViewModel.ProjectTitleDisplay)],
            UpdateChrome);
        _binder.Visible(_homeDock.Root, _shell, [nameof(ShellViewModel.IsJobDockVisible), nameof(ShellViewModel.IsJobRunning)],
            () => _shell.IsJobRunning);
        _binder.Visible(_workspaceDock.Root, _shell, nameof(ShellViewModel.IsJobDockVisible), () => _shell.IsJobDockVisible);
        _binder.Collection(_shell.Toasts, SyncToasts);
        _binder.Event(() => L.LanguageChanged += OnLanguageChanged, () => L.LanguageChanged -= OnLanguageChanged);
    }

    private void UpdateChrome()
    {
        bool projectMode = _shell.IsProjectMode;
        _root.SetVisibleChildName(projectMode ? "workspace" : "home");
        string title = _shell.IsProjectOpen ? _shell.ProjectTitleDisplay : string.Empty;
        Window.SetTitle(string.IsNullOrEmpty(title) ? "Disc Image Studio" : title + " — Disc Image Studio");

        if (_shell.IsProjectOpen && _shell.CurrentProject is { } project)
        {
            _projectNameLabel.SetLabel(project.Name);
            _projectBadge.SetLabel(project.FamilyLabel);
            _projectDirtyLabel.SetVisible(_shell.IsDirty);
        }

        Workspace current = _shell.CurrentWorkspace;
        if (_pages is not null && current != Workspace.Home)
        {
            string name = current.ToString();
            if (_pageStack.GetVisibleChildName() != name)
            {
                _pageStack.SetVisibleChildName(name);
                _pages.ScrollToTop(current);
            }

            _pages.SetCurrent(current);
            _contentTitle.SetTitle(_pages.Title(current));
            _contentTitle.SetSubtitle(_shell.IsProjectOpen ? _shell.ProjectTitleDisplay : string.Empty);
        }

        _syncingNav = true;
        try
        {
            if (_navRows.TryGetValue(NavWorkspace(current), out Gtk.ListBoxRow? row)) _navList.SelectRow(row);
            else _navList.UnselectAll();
        }
        finally
        {
            _syncingNav = false;
        }
    }

    private void BuildPages()
    {
        Workspace visible = _shell.CurrentWorkspace;
        _pages?.Dispose();
        while (_pageStack.GetFirstChild() is { } child) _pageStack.Remove(child);
        if (_homeView.GetContent() is not null) _homeView.SetContent(null);

        _pages = new PageSet(_shell, Window);
        _homeView.SetContent(_pages.Home.Root);
        foreach ((Workspace workspace, Gtk.Widget root) in _pages.WorkspacePages)
            _pageStack.AddNamed(root, workspace.ToString());
        if (visible != Workspace.Home) _pageStack.SetVisibleChildName(visible.ToString());
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Pages are rebuilt from the view models, which hold all state; menus and the sidebar
        // pick up the new language the next time the window is created.
        _shell.Relocalize();
        BuildPages();
        UpdateChrome();
    }

    private void SyncToasts()
    {
        foreach (ToastItem item in _shell.Toasts)
        {
            if (_toasts.ContainsKey(item)) continue;
            string text = string.IsNullOrWhiteSpace(item.Message)
                ? item.Title
                : item.Title + " — " + item.Message;
            Adw.Toast toast = Adw.Toast.New(GLib.Markup.EscapeText(text));
            toast.SetTimeout((uint)Math.Max(1, Math.Round(item.Timeout.TotalSeconds)));
            if (item.Kind is ToastKind.Error or ToastKind.Warning) toast.SetPriority(Adw.ToastPriority.High);
            ToastItem captured = item;
            toast.OnDismissed += (_, _) => _toasts.Remove(captured);
            _toasts[item] = toast;
            _toastOverlay.AddToast(toast);
        }

        foreach (ToastItem stale in _toasts.Keys.Where(item => !_shell.Toasts.Contains(item)).ToArray())
        {
            _toasts[stale].Dismiss();
            _toasts.Remove(stale);
        }
    }

    // ---- Settings ----

    private void OpenSettingsDialog()
    {
        SettingsDialog.Show(Window, _shell, _settings, _settingsStore, ApplySettings);
    }

    private void ApplySettings(AppSettings settings)
    {
        bool languageChanged = _settings.Language != settings.Language;
        _settings = settings;
        App.ApplyTheme(settings.Theme);
        if (languageChanged) L.SetLanguage(settings.Language);

        string cacheDirectory = TempFiles.TryConfigureCacheDirectory(settings.CacheDirectory);
        if (settings.CacheDirectory.Length > 0
            && !string.Equals(cacheDirectory, settings.CacheDirectory, StringComparison.Ordinal))
        {
            _shell.ShowToast(L.T("缓存位置不可用"), L.T("已改用默认缓存目录。"), ToastKind.Warning);
        }

        if (languageChanged)
        {
            _shell.ShowToast(L.T("语言设置已保存"), L.T("界面文字已立即更新。"), ToastKind.Info);
        }
    }

    // ---- Closing ----

    private bool OnCloseRequest(Gtk.Window sender, EventArgs args)
    {
        if (_closeApproved || !_shell.IsProjectOpen || !_shell.IsDirty)
        {
            Shutdown();
            return false;
        }

        if (!_isCloseFlowRunning) _ = RunCloseFlowAsync();
        return true;
    }

    private async Task RunCloseFlowAsync()
    {
        _isCloseFlowRunning = true;
        try
        {
            CancelEventArgs decision = new();
            try
            {
                await _shell.HandleWindowClosingAsync(decision);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (decision.Cancel) return;
        }
        finally
        {
            _isCloseFlowRunning = false;
        }

        _closeApproved = true;
        Window.Close();
    }

    private void Shutdown()
    {
        _shell.LivePreview.OnWindowClosed();
        _shell.Calibration.OnWindowClosed();
        _pages?.Dispose();
        _binder.Dispose();
    }
}
