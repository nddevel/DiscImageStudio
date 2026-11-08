using System.Globalization;
using DiscImageStudio.Gnome.Imaging;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>Design workspace: layer list and disc files on the left, the disc canvas in the
/// middle, layer properties and tone on the right (the WPF DesignView).</summary>
internal sealed class DesignPage : IPage
{
    private readonly Binder _binder = new();
    private readonly DesignViewModel _design;
    private readonly DiscDesignCanvas _canvas;
    private readonly TonePanel _tone;
    private readonly Gtk.ListBox _layerList;
    private readonly List<(DesignLayerViewModel Layer, Gtk.ListBoxRow Row, Binder Binder)> _rows = [];
    private readonly Gtk.Box _properties;
    private Binder? _propertiesBinder;
    private bool _syncingSelection;

    public DesignPage(ShellViewModel shell, DesignViewModel design, Gtk.Window window)
    {
        _design = design;
        DiscParametersState state = design.State;
        Header = new PageHeader(L.T("Xaml.Design"), workspace => design.NavigateCommand.Execute(workspace));
        _binder.Watch(design, [nameof(DesignViewModel.FamilyLabel), nameof(DesignViewModel.Family)], () => Header.SetBadge(design.FamilyLabel));
        Gtk.Button next = Ui.Button(L.T("Xaml.NextPreview"));
        _binder.Command(next, design.NavigateCommand, () => Workspace.Preview);
        Header.Actions.Append(next);

        // ---- Layers ----
        Gtk.Label count = Ui.Line(string.Empty, "dim-label", "caption");
        _binder.Collection(design.Layers, () =>
            count.SetLabel(string.Format(CultureInfo.CurrentCulture, L.T("Xaml.LayerCountFormat"), design.Layers.Count)));
        Gtk.Box layersHeader = Ui.HBox(8, Ui.Line(L.T("Xaml.Layers"), "heading").Expand(), count);

        _layerList = Gtk.ListBox.New();
        _layerList.AddCssClass("navigation-sidebar");
        _layerList.SetSelectionMode(Gtk.SelectionMode.Single);
        _layerList.OnRowSelected += (_, args) =>
        {
            if (_syncingSelection) return;
            DesignLayerViewModel? layer = args.Row is null ? null : _rows.FirstOrDefault(entry => Same(entry.Row, args.Row)).Layer;
            if (!ReferenceEquals(layer, design.SelectedLayer)) design.SelectedLayer = layer;
        };
        Gtk.ScrolledWindow layerScroll = Ui.Scrolled(_layerList);
        layerScroll.SetMinContentHeight(220);

        Gtk.Button up = Ui.IconButton("go-up-symbolic", L.T("Gnome.MoveLayerUp"), "flat");
        up.OnClicked += (_, _) =>
        {
            if (design.SelectedLayer is { } layer) design.MoveLayer(layer, design.Layers.IndexOf(layer) - 1);
        };
        Gtk.Button down = Ui.IconButton("go-down-symbolic", L.T("Gnome.MoveLayerDown"), "flat");
        down.OnClicked += (_, _) =>
        {
            if (design.SelectedLayer is { } layer) design.MoveLayer(layer, design.Layers.IndexOf(layer) + 2);
        };
        Gtk.Button remove = Ui.IconButton("user-trash-symbolic", L.T("Xaml.DeleteLayer"), "flat");
        _binder.Command(remove, design.RemoveSelectedCommand);
        _binder.Watch(design, nameof(DesignViewModel.SelectedLayer), () =>
        {
            int index = design.SelectedLayer is { } layer ? design.Layers.IndexOf(layer) : -1;
            up.SetSensitive(index > 0);
            down.SetSensitive(index >= 0 && index < design.Layers.Count - 1);
        });
        Gtk.Button import = Ui.IconLabelButton("list-add-symbolic", L.T("Xaml.ImportImages"));
        _binder.Command(import, design.ImportImagesCommand);
        import.SetHexpand(true);
        Gtk.Box layerTools = Ui.HBox(4, up, down, remove, import);

        Gtk.Box layersCard = Ui.VBox(10, layersHeader, layerScroll, layerTools).Css("card", "section-card");
        layersCard.SetVexpand(true);

        // ---- Files on disc ----
        Gtk.Image overflowIcon = Gtk.Image.NewFromIconName("dialog-warning-symbolic");
        overflowIcon.AddCssClass("error");
        Gtk.Label overflowText = Ui.Label(string.Empty, "error");
        Gtk.Box overflow = Ui.HBox(6, overflowIcon.Align(Gtk.Align.Start, Gtk.Align.Start), overflowText);
        _binder.Watch(design, [nameof(DesignViewModel.DataRegionOverflow), nameof(DesignViewModel.DataRegionOverflowText)], () =>
        {
            overflow.SetVisible(design.DataRegionOverflow);
            overflowText.SetLabel(design.DataRegionOverflowText ?? string.Empty);
        });

        Gtk.Box cdFiles = FilesSection(L.T("Xaml.Folder"), state, nameof(DiscParametersState.CdDataDirectory),
            () => state.CdDataDirectory, value => state.CdDataDirectory = value,
            nameof(DiscParametersState.CdFilesAtOuterRing), () => state.CdFilesAtOuterRing, value => state.CdFilesAtOuterRing = value,
            () => state.HasCdFiles, design, L.T("Xaml.UsesAnAudioTrackWithoutFilesSwitchesToA"));
        Gtk.Box dvdFiles = FilesSection(L.T("Xaml.FolderOptional"), state, nameof(DiscParametersState.DvdDataDirectory),
            () => state.DvdDataDirectory, value => state.DvdDataDirectory = value,
            nameof(DiscParametersState.DvdFilesAtOuterRing), () => state.DvdFilesAtOuterRing, value => state.DvdFilesAtOuterRing = value,
            () => state.HasDvdFiles, design, null);
        Gtk.Box bdFiles = FilesSection(L.T("Xaml.FolderOptional"), state, nameof(DiscParametersState.BdDataDirectory),
            () => state.BdDataDirectory, value => state.BdDataDirectory = value,
            nameof(DiscParametersState.BdFilesAtOuterRing), () => state.BdFilesAtOuterRing, value => state.BdFilesAtOuterRing = value,
            () => state.HasBluRayFiles, design, null);
        _binder.Watch(design, [nameof(DesignViewModel.Family), nameof(DesignViewModel.IsCd), nameof(DesignViewModel.IsDvd),
            nameof(DesignViewModel.IsBluRay)], () =>
        {
            cdFiles.SetVisible(design.IsCd);
            dvdFiles.SetVisible(design.IsDvd);
            bdFiles.SetVisible(design.IsBluRay);
        });
        Gtk.Box filesCard = Ui.VBox(12, Ui.Line(L.T("Xaml.DiscFiles"), "heading"), overflow, cdFiles, dvdFiles, bdFiles)
            .Css("card", "section-card");

        Gtk.Box left = Ui.VBox(16, layersCard, filesCard);
        left.SetSizeRequest(250, -1);

        // ---- Canvas ----
        _canvas = new DiscDesignCanvas(design);
        Gtk.Frame canvasFrame = Gtk.Frame.New(null);
        canvasFrame.SetChild(_canvas.Root);
        canvasFrame.AddCssClass("disc-canvas-frame");
        canvasFrame.SetOverflow(Gtk.Overflow.Hidden);

        Gtk.Button zoomOut = Ui.IconButton("zoom-out-symbolic", L.T("Xaml.ZoomOut"), "flat");
        zoomOut.OnClicked += (_, _) => _canvas.ZoomOut();
        Gtk.Label zoomLabel = Ui.Line(_canvas.ZoomPercent, "numeric", "caption");
        zoomLabel.SetWidthChars(5);
        zoomLabel.SetXalign(0.5f);
        _canvas.ZoomChanged += text => zoomLabel.SetLabel(text);
        Gtk.Button zoomIn = Ui.IconButton("zoom-in-symbolic", L.T("Xaml.ZoomIn"), "flat");
        zoomIn.OnClicked += (_, _) => _canvas.ZoomIn();
        Gtk.Button fit = Ui.IconButton("zoom-fit-best-symbolic", L.T("Xaml.FitToWindowOrDoubleClickEmptySpace"), "flat");
        fit.OnClicked += (_, _) => _canvas.ResetView();
        Gtk.Box zoomBar = Ui.HBox(2, zoomOut, zoomLabel, zoomIn, Gtk.Separator.New(Gtk.Orientation.Vertical), fit)
            .Css("osd", "canvas-toolbar");
        zoomBar.Align(Gtk.Align.End, Gtk.Align.End);
        zoomBar.Margins(0, 10, 10, 0);
        zoomBar.SetTooltipText(L.T("Xaml.CtrlAltScrollToZoomMiddleDragToPan"));

        Gtk.Box swatch = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        swatch.AddCssClass("legend-swatch");
        swatch.SetValign(Gtk.Align.Center);
        Gtk.Box legend = Ui.HBox(8, swatch, Ui.Line(L.T("Xaml.FileAreaIncludingFilesystem"), "caption")).Css("osd", "canvas-toolbar");
        legend.Margins(0, 0, 10, 10);
        legend.Align(Gtk.Align.Start, Gtk.Align.End);
        _binder.Visible(legend, design.Tone, nameof(ToneViewModel.HasFileRegionOverlay), () => design.Tone.HasFileRegionOverlay);

        Gtk.Overlay canvasOverlay = Gtk.Overlay.New();
        canvasOverlay.SetChild(canvasFrame);
        canvasOverlay.AddOverlay(zoomBar);
        canvasOverlay.AddOverlay(legend);
        canvasOverlay.SetHexpand(true);
        canvasOverlay.SetVexpand(true);

        // ---- Properties and tone ----
        _properties = Ui.VBox(10);
        Gtk.Box propertiesCard = Ui.VBox(12, Ui.Line(L.T("Xaml.Properties"), "heading"), _properties).Css("card", "section-card");
        _tone = new TonePanel(design.Tone);
        Gtk.Box toneCard = _tone.Root.Css("card", "section-card");
        Gtk.Box right = Ui.VBox(16, propertiesCard, toneCard);
        Gtk.ScrolledWindow rightScroll = Ui.Scrolled(right);
        rightScroll.SetSizeRequest(270, -1);
        rightScroll.SetHexpand(false);
        rightScroll.SetPropagateNaturalWidth(true);

        Gtk.ScrolledWindow leftScroll = Ui.Scrolled(left);
        leftScroll.SetHexpand(false);
        leftScroll.SetSizeRequest(260, -1);

        Gtk.Box workspace = Ui.HBox(16, leftScroll, canvasOverlay, rightScroll);
        workspace.SetVexpand(true);

        Gtk.Box page = Ui.VBox(0, Header.Root, workspace);
        page.Margins(24, 24, 16, 24);
        Root = page;

        Gtk.ShortcutController shortcuts = Gtk.ShortcutController.New();
        shortcuts.SetScope(Gtk.ShortcutScope.Local);
        AddShortcut(shortcuts, "<Control>plus|<Control>equal|<Control>KP_Add", _canvas.ZoomIn);
        AddShortcut(shortcuts, "<Control>minus|<Control>KP_Subtract", _canvas.ZoomOut);
        AddShortcut(shortcuts, "<Control>0|<Control>KP_0", _canvas.ResetView);
        page.AddController(shortcuts);

        // Bound last: both handlers touch widgets created above.
        _binder.Collection(design.Layers, RebuildLayerRows);
        _binder.Watch(design, nameof(DesignViewModel.SelectedLayer), () =>
        {
            SyncLayerSelection();
            RebuildProperties();
        });
        _ = shell;
        _ = window;
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => L.T("Xaml.Design");

    public void ScrollToTop()
    {
    }

    private static void AddShortcut(Gtk.ShortcutController controller, string triggers, Action action)
    {
        Gtk.ShortcutTrigger trigger = Gtk.ShortcutTrigger.ParseString(triggers)!;
        Gtk.CallbackAction callback = Gtk.CallbackAction.New((_, _) =>
        {
            action();
            return true;
        });
        controller.AddShortcut(Gtk.Shortcut.New(trigger, callback));
    }

    private Gtk.Box FilesSection(string caption, DiscParametersState state, string directoryProperty, Func<string> getDirectory,
        Action<string> setDirectory, string outerProperty, Func<bool> getOuter, Action<bool> setOuter, Func<bool> hasFiles,
        DesignViewModel design, string? hint)
    {
        Gtk.Box path = Ui.PathField(out Gtk.Entry entry, out Gtk.Button browse, L.T("Xaml.Browse"));
        _binder.Entry(entry, state, directoryProperty, getDirectory, setDirectory);
        _binder.Watch(state, directoryProperty, () => entry.SetTooltipText(getDirectory()));
        _binder.Command(browse, design.BrowseDataCommand);

        Adw.ToggleGroup placement = Ui.Segmented(L.T("Xaml.InnerRing"), L.T("Xaml.OuterRing"));
        _binder.Segmented(placement, state, outerProperty, getOuter, setOuter);
        Gtk.Box placementBox = Ui.VBox(6, Ui.Line(L.T("Xaml.FilePlacement"), "caption-heading", "dim-label"), placement);
        if (hint is not null) placementBox.Append(Ui.Hint(hint));
        _binder.Watch(state, [], () => placementBox.SetVisible(hasFiles()));
        return Ui.VBox(12, Ui.Field(caption, path), placementBox);
    }

    private void RebuildLayerRows()
    {
        foreach ((_, _, Binder binder) in _rows) binder.Dispose();
        _rows.Clear();
        _syncingSelection = true;
        try
        {
            _layerList.RemoveAll();
            foreach (DesignLayerViewModel layer in _design.Layers)
            {
                Binder binder = new();
                Gtk.Image thumbnail = Gtk.Image.New();
                thumbnail.SetPixelSize(40);
                thumbnail.SetValign(Gtk.Align.Center);
                thumbnail.AddCssClass("layer-thumb");
                binder.Watch(layer, [nameof(DesignLayerViewModel.Thumbnail), nameof(DesignLayerViewModel.Image)], () =>
                    thumbnail.SetFromPaintable(layer.Thumbnail is { } bitmap ? TextureFactory.FromBitmap(bitmap) : null));
                Gtk.Label name = Ui.Line(string.Empty);
                binder.Text(name, layer, nameof(DesignLayerViewModel.Name), () => layer.Name);
                Gtk.Label position = Ui.Line(string.Empty, "caption", "dim-label", "numeric");
                binder.Watch(layer, [nameof(DesignLayerViewModel.TranslateXMm), nameof(DesignLayerViewModel.TranslateYMm)], () =>
                    position.SetLabel(string.Create(CultureInfo.InvariantCulture, $"{layer.TranslateXMm:F1}, {layer.TranslateYMm:F1} mm")));
                Gtk.Box text = Ui.VBox(2, name, position);
                text.SetHexpand(true);
                text.SetValign(Gtk.Align.Center);
                Gtk.Button visibility = Ui.IconButton("view-reveal-symbolic", L.T("Xaml.ShowHide"), "flat", "circular");
                visibility.SetValign(Gtk.Align.Center);
                binder.Watch(layer, nameof(DesignLayerViewModel.IsVisible), () =>
                {
                    visibility.SetIconName(layer.IsVisible ? "view-reveal-symbolic" : "view-conceal-symbolic");
                    text.SetOpacity(layer.IsVisible ? 1 : 0.55);
                });
                DesignLayerViewModel captured = layer;
                binder.Command(visibility, _design.ToggleVisibilityCommand, () => captured);
                Gtk.ListBoxRow row = Gtk.ListBoxRow.New();
                row.SetChild(Ui.HBox(10, thumbnail, text, visibility).Css("layer-row"));
                AttachLayerMenu(row);
                _layerList.Append(row);
                _rows.Add((layer, row, binder));
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        SyncLayerSelection();
    }

    private void AttachLayerMenu(Gtk.ListBoxRow row)
    {
        Gio.Menu menu = Gio.Menu.New();
        menu.Append(L.T("Xaml.DeleteLayer"), "layer.delete");
        Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModel(menu);
        popover.SetParent(row);
        popover.SetHasArrow(false);
        Gio.SimpleActionGroup group = Gio.SimpleActionGroup.New();
        Gio.SimpleAction delete = Gio.SimpleAction.New("delete", null);
        delete.OnActivate += (_, _) =>
        {
            if (_design.RemoveSelectedCommand.CanExecute(null)) _design.RemoveSelectedCommand.Execute(null);
        };
        group.AddAction(delete);
        row.InsertActionGroup("layer", group);
        Gtk.GestureClick secondary = Gtk.GestureClick.New();
        secondary.SetButton(3);
        secondary.OnPressed += (_, args) =>
        {
            _layerList.SelectRow(row);
            popover.SetPointingTo(new Gdk.Rectangle { X = (int)args.X, Y = (int)args.Y, Width = 1, Height = 1 });
            popover.Popup();
        };
        row.AddController(secondary);
        row.OnDestroy += (_, _) => popover.Unparent();
    }

    private void SyncLayerSelection()
    {
        _syncingSelection = true;
        try
        {
            Gtk.ListBoxRow? row = _rows.FirstOrDefault(entry => ReferenceEquals(entry.Layer, _design.SelectedLayer)).Row;
            if (row is null) _layerList.UnselectAll();
            else _layerList.SelectRow(row);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void RebuildProperties()
    {
        _propertiesBinder?.Dispose();
        _propertiesBinder = new Binder();
        _properties.RemoveAllChildren();
        if (_design.SelectedLayer is not { } layer)
        {
            _properties.Append(Ui.Hint(L.T("Xaml.SelectALayerOnTheCanvasToEditIts")));
            return;
        }

        Binder binder = _propertiesBinder;
        Gtk.Entry name = Ui.Entry();
        binder.Entry(name, layer, nameof(DesignLayerViewModel.Name), () => layer.Name, value => layer.Name = value);
        Gtk.Entry x = NumberField();
        binder.NumberEntry(x, layer, nameof(DesignLayerViewModel.TranslateXMm), () => layer.TranslateXMm,
            value => layer.TranslateXMm = value, "F2");
        Gtk.Entry y = NumberField();
        binder.NumberEntry(y, layer, nameof(DesignLayerViewModel.TranslateYMm), () => layer.TranslateYMm,
            value => layer.TranslateYMm = value, "F2");
        Gtk.Entry rotation = NumberField();
        binder.NumberEntry(rotation, layer, nameof(DesignLayerViewModel.RotationDeg), () => layer.RotationDeg,
            value => layer.RotationDeg = value, "F1");
        Gtk.Entry scale = NumberField();
        binder.NumberEntry(scale, layer, nameof(DesignLayerViewModel.ScalePercent), () => layer.ScalePercent,
            value => layer.ScalePercent = value, "0.##");
        Gtk.Label path = Ui.Line(string.Empty, "dim-label", "caption");
        path.SetEllipsize(Pango.EllipsizeMode.Middle);
        binder.Watch(layer, nameof(DesignLayerViewModel.ImagePath), () =>
        {
            path.SetLabel(layer.ImagePath);
            path.SetTooltipText(layer.ImagePath);
        });

        Gtk.Grid grid = Gtk.Grid.New();
        grid.SetColumnSpacing(12);
        grid.SetRowSpacing(12);
        grid.SetColumnHomogeneous(true);
        grid.Attach(Ui.Field("X (mm)", x), 0, 0, 1, 1);
        grid.Attach(Ui.Field("Y (mm)", y), 1, 0, 1, 1);
        grid.Attach(Ui.Field(L.T("Xaml.Rotation"), rotation), 0, 1, 1, 1);
        grid.Attach(Ui.Field(L.T("Xaml.Scale"), scale), 1, 1, 1, 1);
        _properties.Append(Ui.Field(L.T("Xaml.LayerName"), name));
        _properties.Append(grid);
        _properties.Append(path);
    }

    private static Gtk.Entry NumberField()
    {
        Gtk.Entry entry = Ui.Entry();
        entry.SetWidthChars(6);
        entry.AddCssClass("numeric");
        entry.SetInputPurpose(Gtk.InputPurpose.Number);
        return entry;
    }

    private static bool Same(Gtk.Widget a, Gtk.Widget b) => a.Handle.DangerousGetHandle() == b.Handle.DangerousGetHandle();

    public void Dispose()
    {
        foreach ((_, _, Binder binder) in _rows) binder.Dispose();
        _rows.Clear();
        _propertiesBinder?.Dispose();
        _tone.Dispose();
        _canvas.Dispose();
        _binder.Dispose();
    }
}
