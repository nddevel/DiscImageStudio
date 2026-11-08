using DiscImageStudio.Core;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.Services;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Dialogs;

/// <summary>New project / project settings dialog (the WPF ProjectDialogWindow). Runs modally:
/// <see cref="Show"/> returns once the user confirms or closes it.</summary>
internal sealed class ProjectDialog : IDisposable
{
    private readonly Binder _binder = new();
    private readonly ProjectDialogViewModel _viewModel;
    private readonly Adw.Dialog _dialog;
    private readonly Gtk.FlowBox _templates;
    private bool _confirmed;
    private bool _syncingSelection;

    private ProjectDialog(Gtk.Window parent, ProjectDialogViewModel viewModel)
    {
        _viewModel = viewModel;
        _dialog = Adw.Dialog.New();
        _dialog.SetContentWidth(980);
        _dialog.SetContentHeight(640);
        _binder.Watch(viewModel, nameof(ProjectDialogViewModel.DialogTitle), () => _dialog.SetTitle(viewModel.DialogTitle));

        viewModel.ShowPresetSaveDialog = () => PresetSaveDialog.Show(parent);
        viewModel.PickSaveDirectory = initial => PickDirectory(parent, initial);
        viewModel.CloseWithConfirm = () =>
        {
            _confirmed = true;
            _dialog.ForceClose();
        };
        viewModel.ConfirmPresetSwitch = tile => Alerts.ConfirmAsync(_dialog,
            L.T("切换预设"),
            L.F($"切换为“{tile.Title}”会用该预设的几何参数覆盖当前参数，确认后无法撤销。是否继续？"),
            L.T("覆盖参数"));

        // ---- Left: template browser ----
        Adw.ToggleGroup categories = Adw.ToggleGroup.New();
        foreach (string category in viewModel.TemplateCategories)
        {
            Adw.Toggle toggle = Adw.Toggle.New();
            toggle.SetLabel(category);
            categories.Add(toggle);
        }

        categories.SetHalign(Gtk.Align.Start);
        categories.SetVisible(!viewModel.EditMode);
        bool syncingCategory = false;
        _binder.Watch(viewModel, nameof(ProjectDialogViewModel.TemplateCategoryIndex), () =>
        {
            syncingCategory = true;
            categories.SetActive((uint)Math.Max(0, viewModel.TemplateCategoryIndex));
            syncingCategory = false;
        });
        categories.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "active" && !syncingCategory)
                viewModel.TemplateCategoryIndex = (int)categories.GetActive();
        };

        Gtk.Box editHeader = Ui.VBox(4,
            Ui.Line(L.T("Xaml.ChangePreset"), "heading"),
            Ui.Hint(L.T("Xaml.SelectingAnotherPresetReplacesTheCurrentGeometryParametersAfter")));
        editHeader.SetVisible(viewModel.EditMode);

        _templates = Gtk.FlowBox.New();
        _templates.SetSelectionMode(Gtk.SelectionMode.Single);
        _templates.SetHomogeneous(true);
        _templates.SetMinChildrenPerLine(2);
        _templates.SetMaxChildrenPerLine(4);
        _templates.SetColumnSpacing(10);
        _templates.SetRowSpacing(10);
        _templates.SetValign(Gtk.Align.Start);
        _templates.OnChildActivated += (_, args) => PushSelection(args.Child.GetIndex());
        _binder.Watch(viewModel, nameof(ProjectDialogViewModel.VisibleTemplates), RebuildTemplates);
        _binder.Watch(viewModel, nameof(ProjectDialogViewModel.SelectedTemplate), SyncSelection);

        Gtk.Label emptyHint = Ui.Label(string.Empty, "dim-label");
        emptyHint.SetHalign(Gtk.Align.Center);
        emptyHint.SetMarginTop(24);
        _binder.Watch(viewModel, [nameof(ProjectDialogViewModel.HasVisibleTemplates), nameof(ProjectDialogViewModel.EmptyCategoryHint),
            nameof(ProjectDialogViewModel.VisibleTemplates)], () =>
        {
            emptyHint.SetLabel(viewModel.EmptyCategoryHint);
            emptyHint.SetVisible(!viewModel.HasVisibleTemplates);
        });

        Gtk.Box templateColumn = Ui.VBox(12, categories, editHeader, Ui.Scrolled(Ui.VBox(0, _templates, emptyHint)));
        templateColumn.SetHexpand(true);

        // ---- Right: preset details ----
        Gtk.Entry name = Ui.Entry(80);
        _binder.Entry(name, viewModel, nameof(ProjectDialogViewModel.Name), () => viewModel.Name, value => viewModel.Name = value);

        Gtk.FlowBox geometry = Gtk.FlowBox.New();
        geometry.SetSelectionMode(Gtk.SelectionMode.None);
        geometry.SetMinChildrenPerLine(2);
        geometry.SetMaxChildrenPerLine(2);
        geometry.SetColumnSpacing(12);
        geometry.SetRowSpacing(12);
        geometry.SetHomogeneous(true);
        AddGeometryField(geometry, L.T("Xaml.InnerR0Mm"), nameof(ProjectDialogViewModel.InnerRadiusText),
            () => viewModel.InnerRadiusText, value => viewModel.InnerRadiusText = value);
        AddGeometryField(geometry, L.T("Xaml.OuterR1Mm"), nameof(ProjectDialogViewModel.OuterRadiusText),
            () => viewModel.OuterRadiusText, value => viewModel.OuterRadiusText = value);
        AddGeometryField(geometry, L.T("Xaml.GeometryTotalSectors"), nameof(ProjectDialogViewModel.SectorsText),
            () => viewModel.SectorsText, value => viewModel.SectorsText = value);
        Gtk.Entry usable = AddGeometryField(geometry, L.T("Xaml.UsableSectors"), nameof(ProjectDialogViewModel.UsableSectorsText),
            () => viewModel.UsableSectorsText, value => viewModel.UsableSectorsText = value);
        usable.SetTooltipText(L.T("Xaml.UsableSectorsHint"));
        AddGeometryField(geometry, L.T("Xaml.LinearTerm"), nameof(ProjectDialogViewModel.PitchLinearText),
            () => viewModel.PitchLinearText, value => viewModel.PitchLinearText = value);
        AddGeometryField(geometry, L.T("Xaml.QuadraticTerm"), nameof(ProjectDialogViewModel.PitchQuadraticText),
            () => viewModel.PitchQuadraticText, value => viewModel.PitchQuadraticText = value);
        AddGeometryField(geometry, L.T("Xaml.CubicTerm"), nameof(ProjectDialogViewModel.PitchCubicText),
            () => viewModel.PitchCubicText, value => viewModel.PitchCubicText = value);

        Gtk.Label bdHint = Ui.Hint(L.T("Xaml.SingleLayerBDRSectorsMustBeAMultiple"));
        _binder.Visible(bdHint, viewModel, [nameof(ProjectDialogViewModel.Family), nameof(ProjectDialogViewModel.IsBluRayFieldsVisibility)],
            () => viewModel.IsBluRay);

        Gtk.Label familyBadge = Ui.Badge(string.Empty);
        _binder.Text(familyBadge, viewModel, nameof(ProjectDialogViewModel.FamilyBadge), () => viewModel.FamilyBadge);
        Gtk.Button savePreset = Ui.Button(L.T("Xaml.SaveParametersAsPreset"));
        _binder.Command(savePreset, viewModel.SaveAsPresetCommand);
        Gtk.Label savedMessage = Ui.Line(string.Empty, "success", "caption");
        _binder.Text(savedMessage, viewModel, nameof(ProjectDialogViewModel.SavedPresetMessage), () => viewModel.SavedPresetMessage);
        Gtk.Box editTools = Ui.VBox(10,
            Ui.HBox(8, Ui.Line(L.T("Xaml.DiscType"), "caption-heading", "dim-label"), familyBadge),
            Ui.HBox(10, savePreset, savedMessage));
        editTools.SetVisible(viewModel.EditMode);

        Gtk.Label error = Ui.Label(string.Empty, "error");
        _binder.Watch(viewModel, nameof(ProjectDialogViewModel.ErrorMessage), () =>
        {
            error.SetLabel(viewModel.ErrorMessage);
            error.SetVisible(!string.IsNullOrWhiteSpace(viewModel.ErrorMessage));
        });

        Gtk.Box details = Ui.VBox(14,
            Ui.Line(L.T("Xaml.PresetDetails"), "heading"),
            Ui.Field(L.T("Xaml.ProjectName"), name),
            geometry, bdHint, editTools, error);
        details.Margins(16);
        Gtk.ScrolledWindow detailsScroll = Ui.Scrolled(details);
        detailsScroll.AddCssClass("card");
        detailsScroll.SetSizeRequest(330, -1);
        detailsScroll.SetHexpand(false);

        Gtk.Box columns = Ui.HBox(24, templateColumn, detailsScroll);
        columns.SetVexpand(true);

        // ---- Bottom: location and buttons ----
        Gtk.Box location = Ui.PathField(out Gtk.Entry directory, out Gtk.Button browse, L.T("Xaml.Browse"));
        _binder.Entry(directory, viewModel, nameof(ProjectDialogViewModel.SaveDirectory), () => viewModel.SaveDirectory,
            value => viewModel.SaveDirectory = value);
        _binder.Command(browse, viewModel.BrowseSavePathCommand);
        Gtk.Label fileHint = Ui.Hint(string.Empty);
        _binder.Watch(viewModel, [], () => fileHint.SetLabel(viewModel.SaveFileNameHint));
        Gtk.Box locationBox = Ui.VBox(6, Ui.Line(L.T("Xaml.SaveLocation"), "caption-heading", "dim-label"), location, fileHint);
        locationBox.SetHexpand(true);

        Gtk.Button cancel = Ui.Button(L.T("Xaml.Cancel"));
        cancel.OnClicked += (_, _) => _dialog.Close();
        Gtk.Button confirm = Ui.Button(string.Empty, "suggested-action");
        _binder.Watch(viewModel, nameof(ProjectDialogViewModel.ConfirmLabel), () => confirm.SetLabel(viewModel.ConfirmLabel));
        _binder.Command(confirm, viewModel.ConfirmCommand);
        Gtk.Box buttons = Ui.HBox(10, cancel, confirm).Align(Gtk.Align.End, Gtk.Align.End);
        Gtk.Box footer = Ui.HBox(24, locationBox, buttons);

        Gtk.Box content = Ui.VBox(18, columns, footer);
        content.Margins(12, 24, 20, 24);

        Adw.ToolbarView toolbar = Adw.ToolbarView.New();
        toolbar.AddTopBar(Adw.HeaderBar.New());
        toolbar.SetContent(content);
        _dialog.SetChild(toolbar);
        _dialog.SetDefaultWidget(confirm);
    }

    public static ProjectDialogResult? Show(Gtk.Window parent, ShellViewModel shell, ProjectDialogRequest request, AppSettings settings)
    {
        ProjectDialogViewModel viewModel = new(shell.State, request, new RecentTemplateStore(), settings.DefaultProjectDirectory);
        if (request.EditMode && shell.CurrentProject is { } project)
        {
            // Prefill from State so calibration-updated geometry shows its live values.
            DiscParametersState state = shell.State;
            viewModel.Family = project.Family;
            viewModel.Name = project.Name;
            (string inner, string outer, string sectors, string usable, string linear, string quadratic, string cubic) = project.Family switch
            {
                OpticalDiscFamily.Dvd => (state.DvdInnerRadius, state.DvdOuterRadius, state.DvdTotalSectors, state.DvdUsableSectors,
                    state.DvdPitchLinear, state.DvdPitchQuadratic, state.DvdPitchCubic),
                OpticalDiscFamily.BluRay => (state.BdInnerRadius, state.BdOuterRadius, state.BdTotalSectors, state.BdUsableSectors,
                    state.BdPitchLinear, state.BdPitchQuadratic, state.BdPitchCubic),
                _ => (state.CdInnerRadius, state.CdOuterRadius, state.CdSectors, state.CdUsableSectors,
                    state.CdPitchLinear, state.CdPitchQuadratic, state.CdPitchCubic),
            };
            viewModel.InnerRadiusText = inner;
            viewModel.OuterRadiusText = outer;
            viewModel.SectorsText = sectors;
            viewModel.UsableSectorsText = usable;
            viewModel.PitchLinearText = linear;
            viewModel.PitchQuadraticText = quadratic;
            viewModel.PitchCubicText = cubic;
        }

        using ProjectDialog dialog = new(parent, viewModel);
        Modal.Run(dialog._dialog, parent);
        return dialog._confirmed ? viewModel.Result : null;
    }

    private Gtk.Entry AddGeometryField(Gtk.FlowBox box, string caption, string property, Func<string> get, Action<string> set)
    {
        Gtk.Entry entry = Ui.Entry();
        entry.SetWidthChars(8);
        entry.AddCssClass("numeric");
        _binder.Entry(entry, _viewModel, property, get, set);
        box.Append(Ui.Field(caption, entry));
        return entry;
    }

    private void RebuildTemplates()
    {
        _syncingSelection = true;
        try
        {
            _templates.RemoveAll();
            foreach (ProjectTemplateTile tile in _viewModel.VisibleTemplates)
            {
                Gtk.Image icon = Gtk.Image.NewFromIconName(Icons.ForLucide(tile.IconKind));
                icon.SetPixelSize(28);
                icon.AddCssClass("dim-label");
                Gtk.Label title = Gtk.Label.New(tile.Title);
                title.SetWrap(true);
                title.SetJustify(Gtk.Justification.Center);
                title.SetMaxWidthChars(18);
                title.AddCssClass("heading");
                Gtk.Label detail = Gtk.Label.New(tile.Detail);
                detail.SetWrap(true);
                detail.SetJustify(Gtk.Justification.Center);
                detail.SetMaxWidthChars(22);
                detail.AddCssClass("caption");
                detail.AddCssClass("dim-label");
                Gtk.Box body = Ui.VBox(6, icon, title, detail);
                Gtk.Overlay overlay = Gtk.Overlay.New();
                overlay.SetChild(body);
                if (tile.IsUserPreset)
                {
                    Gtk.Label custom = Ui.Badge(L.T("Xaml.Custom"));
                    custom.Align(Gtk.Align.End, Gtk.Align.Start);
                    overlay.AddOverlay(custom);
                }

                if (tile.HasContributor)
                {
                    Gtk.Image sprout = Gtk.Image.NewFromIconName("emblem-favorite-symbolic");
                    sprout.SetPixelSize(12);
                    sprout.AddCssClass("accent");
                    sprout.Align(Gtk.Align.Start, Gtk.Align.Start);
                    sprout.SetTooltipText(tile.ContributorToolTip);
                    overlay.AddOverlay(sprout);
                }

                overlay.AddCssClass("template-card");
                overlay.AddCssClass("card");
                Gtk.FlowBoxChild child = Gtk.FlowBoxChild.New();
                child.SetChild(overlay);
                if (tile.IsUserPreset) AttachDeleteMenu(child, tile);
                _templates.Append(child);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        SyncSelection();
    }

    private void AttachDeleteMenu(Gtk.FlowBoxChild child, ProjectTemplateTile tile)
    {
        Gio.Menu menu = Gio.Menu.New();
        menu.Append(L.T("Xaml.DeletePreset"), "preset.delete");
        Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModel(menu);
        popover.SetParent(child);
        popover.SetHasArrow(false);
        Gio.SimpleActionGroup group = Gio.SimpleActionGroup.New();
        Gio.SimpleAction delete = Gio.SimpleAction.New("delete", null);
        delete.OnActivate += (_, _) =>
        {
            if (_viewModel.DeletePresetCommand.CanExecute(tile)) _viewModel.DeletePresetCommand.Execute(tile);
        };
        group.AddAction(delete);
        child.InsertActionGroup("preset", group);
        Gtk.GestureClick secondary = Gtk.GestureClick.New();
        secondary.SetButton(3);
        secondary.OnPressed += (_, args) =>
        {
            popover.SetPointingTo(new Gdk.Rectangle { X = (int)args.X, Y = (int)args.Y, Width = 1, Height = 1 });
            popover.Popup();
        };
        child.AddController(secondary);
        child.OnDestroy += (_, _) => popover.Unparent();
    }

    private void SyncSelection()
    {
        IReadOnlyList<ProjectTemplateTile> tiles = _viewModel.VisibleTemplates;
        int index = -1;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (Equals(tiles[i], _viewModel.SelectedTemplate))
            {
                index = i;
                break;
            }
        }

        _syncingSelection = true;
        try
        {
            if (index >= 0 && _templates.GetChildAtIndex(index) is { } child) _templates.SelectChild(child);
            else _templates.UnselectAll();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void PushSelection(int index)
    {
        if (_syncingSelection) return;
        IReadOnlyList<ProjectTemplateTile> tiles = _viewModel.VisibleTemplates;
        ProjectTemplateTile? tile = index >= 0 && index < tiles.Count ? tiles[index] : null;
        if (tile is null || Equals(tile, _viewModel.SelectedTemplate)) return;
        _viewModel.SelectedTemplate = tile;
        // The view model may veto the switch (overwrite guard) and keep the old preset.
        SyncSelection();
    }

    private static string? PickDirectory(Gtk.Window parent, string initialDirectory)
    {
        Gtk.FileDialog dialog = Gtk.FileDialog.New();
        dialog.SetTitle(L.T("选择工程保存位置"));
        dialog.SetModal(true);
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.SetInitialFolder(Gio.FileHelper.NewForPath(initialDirectory));
        return Modal.Wait(dialog.SelectFolderAsync(parent))?.GetPath();
    }

    public void Dispose() => _binder.Dispose();
}
