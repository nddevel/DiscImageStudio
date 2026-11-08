using DiscImageStudio.Gnome.Imaging;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>Preview workspace: the design projection or a read-back simulation of a generated
/// image, with its inspector on the left and the result monitor on the right.</summary>
internal sealed class PreviewPage : IPage
{
    private readonly Binder _binder = new();
    private readonly Gtk.ScrolledWindow _inspectorScroll;

    public PreviewPage(ShellViewModel shell, LivePreviewViewModel preview, Gtk.Window window)
    {
        PreviewInspectorViewModel inspector = preview.Inspector;
        Header = new PageHeader(L.T("Xaml.Preview"), workspace => preview.NavigateCommand.Execute(workspace),
            L.T("Xaml.DesignProjectionQuicklyMapsTheSourceImageUsingProject"));
        _binder.Watch(preview, nameof(LivePreviewViewModel.DiscFamilyLabel), () => Header.SetBadge(preview.DiscFamilyLabel));
        Gtk.Button next = Ui.Button(L.T("Xaml.NextExport"));
        _binder.Command(next, preview.NavigateCommand, () => Workspace.Export);
        Header.Actions.Append(next);

        Adw.ToggleGroup mode = Ui.Segmented(L.T("Xaml.DesignProjection"), L.T("Xaml.OutputReadback"));
        _binder.Segmented(mode, preview, nameof(LivePreviewViewModel.IsCalibrationPreview), () => preview.IsReadbackPreview,
            value => preview.IsReadbackPreview = value);

        // ---- Projection inspector ----
        Gtk.Box projection = Ui.VBox(12,
            Ui.Line(L.T("Xaml.SourceImage"), "heading"),
            Ui.HBox(8, Gtk.Image.NewFromIconName("image-x-generic-symbolic"), Ui.Line(L.T("来自设计页合成图"))).Css("inset-panel"),
            GeometrySummary(preview));

        Gtk.Box measurements = Ui.VBox(12);
        Gtk.Grid radii = Gtk.Grid.New();
        radii.SetColumnSpacing(12);
        radii.SetRowSpacing(12);
        radii.SetColumnHomogeneous(true);
        radii.Attach(Field(L.T("Xaml.InnerRadiusMm"), inspector, nameof(PreviewInspectorViewModel.MeasuredInnerRadius),
            () => inspector.MeasuredInnerRadius, value => inspector.MeasuredInnerRadius = value), 0, 0, 1, 1);
        radii.Attach(Field(L.T("Xaml.OuterRadiusMm"), inspector, nameof(PreviewInspectorViewModel.MeasuredOuterRadius),
            () => inspector.MeasuredOuterRadius, value => inspector.MeasuredOuterRadius = value), 1, 0, 1, 1);
        Gtk.Grid pitch = Gtk.Grid.New();
        pitch.SetColumnSpacing(12);
        pitch.SetColumnHomogeneous(true);
        pitch.Attach(Field(L.T("Xaml.LinearTerm"), inspector, nameof(PreviewInspectorViewModel.MeasuredPitchLinear),
            () => inspector.MeasuredPitchLinear, value => inspector.MeasuredPitchLinear = value), 0, 0, 1, 1);
        pitch.Attach(Field(L.T("Xaml.QuadraticTerm"), inspector, nameof(PreviewInspectorViewModel.MeasuredPitchQuadratic),
            () => inspector.MeasuredPitchQuadratic, value => inspector.MeasuredPitchQuadratic = value), 1, 0, 1, 1);
        pitch.Attach(Field(L.T("Xaml.CubicTerm"), inspector, nameof(PreviewInspectorViewModel.MeasuredPitchCubic),
            () => inspector.MeasuredPitchCubic, value => inspector.MeasuredPitchCubic = value), 2, 0, 1, 1);
        Gtk.Label pitchSummary = Ui.Hint(string.Empty);
        _binder.Watch(inspector, [], () => pitchSummary.SetLabel(inspector.MeasuredPitchSummary));
        Gtk.Widget size = Field(L.T("Xaml.OutputSizePx"), inspector, nameof(PreviewInspectorViewModel.PreviewSize),
            () => inspector.PreviewSize, value => inspector.PreviewSize = value);
        Gtk.Widget samples = Field(L.T("Xaml.SamplesPerSector"), inspector, nameof(PreviewInspectorViewModel.SamplesPerSector),
            () => inspector.SamplesPerSector, value => inspector.SamplesPerSector = value);
        _binder.Watch(inspector, [], () => samples.SetVisible(inspector.HasSamplesPerSector));
        Gtk.Box quality = Ui.HBox(12, size.Expand(), samples.Expand());
        measurements.Append(radii);
        measurements.Append(pitch);
        measurements.Append(pitchSummary);
        measurements.Append(Ui.Line(L.T("Xaml.PreviewQuality"), "heading"));
        measurements.Append(quality);
        measurements.Append(Ui.Hint(L.T("Xaml.LargerSizesShowMoreDetailButTakeLongerTo")));
        measurements.SetMarginTop(8);
        Gtk.Expander advanced = Gtk.Expander.New(L.T("Xaml.MeasurementsAndQualityAdvanced"));
        advanced.SetChild(measurements);
        projection.Append(advanced);
        _binder.Visible(projection, preview, nameof(LivePreviewViewModel.IsCalibrationPreview), () => preview.IsCalibrationPreview);

        // ---- Read-back inspector ----
        Gtk.Label artifactLabel = Ui.Line(string.Empty, "heading");
        _binder.Watch(inspector, [], () => artifactLabel.SetLabel(inspector.ArtifactLabel));
        Gtk.Box artifactPath = Ui.PathField(out Gtk.Entry artifactEntry, out Gtk.Button artifactBrowse, L.T("Xaml.Browse"));
        _binder.Entry(artifactEntry, inspector, nameof(PreviewInspectorViewModel.ArtifactPath), () => inspector.ArtifactPath,
            value => inspector.ArtifactPath = value);
        _binder.Command(artifactBrowse, inspector.BrowseArtifactCommand);
        Gtk.Label artifactHint = Ui.Hint(string.Empty);
        _binder.Watch(inspector, [], () => artifactHint.SetLabel(inspector.ArtifactHint));
        Gtk.CheckButton deinterleave = Gtk.CheckButton.NewWithLabel(L.T("Xaml.Deinterleave"));
        _binder.Check(deinterleave, inspector.State, nameof(DiscParametersState.CdInterleave), () => inspector.State.CdInterleave,
            value => inspector.State.CdInterleave = value);
        Gtk.Box deinterleaveBox = Ui.VBox(4, deinterleave, Ui.Hint(L.T("Xaml.EnableThisIfTheAudioTrackWasGeneratedWith")));
        _binder.Watch(inspector, [], () => deinterleaveBox.SetVisible(inspector.IsCdAudioArtifact));
        Gtk.Box readback = Ui.VBox(12, artifactLabel, artifactPath, artifactHint, GeometrySummary(preview), deinterleaveBox);
        _binder.Visible(readback, preview, nameof(LivePreviewViewModel.IsCalibrationPreview), () => !preview.IsCalibrationPreview);

        Gtk.Button refresh = Ui.IconLabelButton("view-refresh-symbolic", L.T("Xaml.RefreshPreview"));
        _binder.Command(refresh, preview.RefreshCommand);
        Gtk.Box inspectorBox = Ui.VBox(16, projection, readback).Margins(18);
        _inspectorScroll = Ui.Scrolled(inspectorBox);
        Gtk.Box inspectorCard = Ui.VBox(0, _inspectorScroll, Gtk.Separator.New(Gtk.Orientation.Horizontal), refresh.Margins(12))
            .Css("card");
        inspectorCard.SetVexpand(true);
        Gtk.Box left = Ui.VBox(16, mode, inspectorCard);
        left.SetSizeRequest(360, -1);

        // ---- Result monitor ----
        Gtk.Label monitorTitle = Ui.Line(string.Empty, "heading");
        _binder.Text(monitorTitle, preview, nameof(LivePreviewViewModel.ResultPreviewTitle), () => preview.ResultPreviewTitle);
        Gtk.Label monitorPath = Ui.Line(string.Empty, "dim-label", "caption");
        monitorPath.SetEllipsize(Pango.EllipsizeMode.Middle);
        _binder.Text(monitorPath, preview, nameof(LivePreviewViewModel.ResultPreviewPath), () => preview.ResultPreviewPath);
        Gtk.Label status = Ui.Line(string.Empty, "dim-label", "caption");
        _binder.Watch(preview, nameof(LivePreviewViewModel.StatusText), () =>
        {
            status.SetLabel(preview.StatusText);
            status.SetTooltipText(preview.StatusText);
        });
        Gtk.Button download = Ui.IconButton("document-save-symbolic", L.T("Xaml.ExportTheCurrentPreviewToTheInputSourceFolder"), "flat");
        _binder.Command(download, preview.DownloadPreviewCommand);
        Gtk.Box monitorHeader = Ui.HBox(10, Ui.VBox(2, monitorTitle, monitorPath, status).Expand(), download.Align(Gtk.Align.End, Gtk.Align.Start));
        monitorHeader.Margins(14, 14, 0, 14);

        Gtk.Picture result = Gtk.Picture.New();
        result.SetContentFit(Gtk.ContentFit.Contain);
        result.SetCanShrink(true);
        result.Margins(18);
        Adw.StatusPage empty = Adw.StatusPage.New();
        empty.SetIconName("media-optical-symbolic");
        empty.AddCssClass("compact");
        Gtk.Button emptyAction = Ui.Button(string.Empty, "pill", "suggested-action");
        emptyAction.SetHalign(Gtk.Align.Center);
        emptyAction.OnClicked += (_, _) => preview.NavigateCommand.Execute(preview.EmptyStateActionWorkspace);
        empty.SetChild(emptyAction);
        _binder.Watch(preview, [], () =>
        {
            empty.SetDescription(GLib.Markup.EscapeText(preview.ResultPreviewPlaceholderText ?? string.Empty));
            emptyAction.SetLabel(preview.EmptyStateActionLabel);
            emptyAction.SetVisible(!string.IsNullOrEmpty(preview.EmptyStateActionLabel));
        });
        Gtk.Stack monitorStack = Gtk.Stack.New();
        monitorStack.AddNamed(result, "image");
        monitorStack.AddNamed(empty, "empty");
        monitorStack.SetVexpand(true);
        _binder.Watch(preview, [nameof(LivePreviewViewModel.ResultImage), nameof(LivePreviewViewModel.HasResultImage)], () =>
        {
            bool has = preview.HasResultImage && preview.ResultImage is not null;
            result.SetPaintable(has ? TextureFactory.FromBitmap(preview.ResultImage!) : null);
            monitorStack.SetVisibleChildName(has ? "image" : "empty");
        });

        Gtk.Box swatch = Gtk.Box.New(Gtk.Orientation.Horizontal, 0);
        swatch.AddCssClass("legend-swatch");
        swatch.SetValign(Gtk.Align.Center);
        Gtk.Box legend = Ui.HBox(8, swatch, Ui.Line(L.T("Xaml.FileAreaIncludingFilesystem"), "dim-label"));
        legend.Margins(0, 18, 14, 18);
        _binder.Visible(legend, preview, nameof(LivePreviewViewModel.HasFileRegionOverlay), () => preview.HasFileRegionOverlay);

        Gtk.Box monitor = Ui.VBox(8, monitorHeader, monitorStack, legend).Css("card");
        monitor.SetHexpand(true);
        monitor.SetVexpand(true);

        Gtk.Box workspace = Ui.HBox(16, left, monitor);
        workspace.SetVexpand(true);
        Gtk.Box page = Ui.VBox(0, Header.Root, workspace);
        page.Margins(24, 24, 16, 24);
        Root = page;
        _ = shell;
        _ = window;
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => L.T("Xaml.Preview");

    public void ScrollToTop() => _inspectorScroll.GetVadjustment()?.SetValue(0);

    private Gtk.Widget GeometrySummary(LivePreviewViewModel preview)
    {
        Gtk.Button edit = Ui.Button(L.T("编辑…"), "flat");
        _binder.Command(edit, preview.EditGeometryCommand);
        return Ui.HBox(8, Ui.Line(L.T("Xaml.ProjectParameters"), "heading").Expand(), edit).Css("inset-panel");
    }

    private Gtk.Widget Field(string caption, System.ComponentModel.INotifyPropertyChanged source, string property,
        Func<string> get, Action<string> set)
    {
        Gtk.Entry entry = Ui.Entry();
        entry.SetWidthChars(6);
        entry.AddCssClass("numeric");
        _binder.Entry(entry, source, property, get, set);
        return Ui.Field(caption, entry);
    }

    public void Dispose() => _binder.Dispose();
}
