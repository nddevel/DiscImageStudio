using System.ComponentModel;
using System.Windows.Input;
using DiscImageStudio.Controls;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>Assisted calibration (the WPF CalibrationView): pick a phase, create and burn a
/// calibration pattern, rectify a photo of the burned disc, then measure it on the photo
/// canvas and apply the fitted geometry to the project.</summary>
internal sealed class CalibrationPage : IPage
{
    private readonly Binder _binder = new();
    private readonly CalibrationViewModel _vm;
    private readonly CalibrationPhotoCanvas _canvas = new();
    private readonly Gtk.ScrolledWindow _stepsScroll;

    public CalibrationPage(ShellViewModel shell, CalibrationViewModel viewModel, Gtk.Window window)
    {
        _vm = viewModel;
        CalibrationViewModel vm = viewModel;
        Header = new PageHeader(L.T("Xaml.Calibration2"), workspace => shell.NavigateTo(workspace),
            L.T("Xaml.UseDiscImageStudioSBuiltInAlgorithmsTo"));
        _binder.Watch(shell, nameof(ShellViewModel.IsProjectMode), () => Header.ShowStages = shell.IsProjectMode);
        Gtk.Button savePreset = Ui.Button(L.T("Xaml.SaveAsPreset"));
        _binder.Command(savePreset, vm.SaveAsPresetCommand);
        Gtk.Button back = Ui.Button(L.T("Standalone.Back"));
        _binder.Command(back, shell.ReturnFromCalibrationCommand);
        Header.Actions.Append(savePreset);
        Header.Actions.Append(back);

        // ---- Phase selector ----
        Gtk.CheckButton? group = null;
        Gtk.Box phases = Ui.HBox(12);
        foreach ((string title, string purpose, string property, Func<bool> active, ICommand command) in new (string, string, string, Func<bool>, ICommand)[]
                 {
                     (L.T("Xaml.CoarseCalibrationPhase"), L.T("Xaml.CoarseCalibrationPurpose"), nameof(CalibrationViewModel.IsCoarseMode), () => vm.IsCoarseMode, vm.SelectCoarseModeCommand),
                     (L.T("HalfDisc.Stage"), L.T("HalfDisc.Purpose"), nameof(CalibrationViewModel.IsIntermediateMode), () => vm.IsIntermediateMode, vm.SelectIntermediateModeCommand),
                     (L.T("Xaml.FineCalibrationPhase"), L.T("Xaml.FineCalibrationPurpose"), nameof(CalibrationViewModel.IsFineMode), () => vm.IsFineMode, vm.SelectFineModeCommand),
                 })
        {
            Gtk.CheckButton radio = Gtk.CheckButton.New();
            radio.SetChild(Ui.VBox(2, Ui.Line(title, "heading"), Ui.Label(purpose, "dim-label", "caption")));
            if (group is not null) radio.SetGroup(group);
            group ??= radio;
            bool syncing = false;
            _binder.Watch(vm, [], () =>
            {
                syncing = true;
                radio.SetActive(active());
                syncing = false;
            });
            radio.OnToggled += (_, _) =>
            {
                if (syncing || !radio.GetActive() || active()) return;
                if (command.CanExecute(null)) command.Execute(null);
                syncing = true;
                radio.SetActive(active());
                syncing = false;
            };
            Gtk.Box card = Ui.VBox(0, radio).Css("card", "inset-panel");
            card.SetHexpand(true);
            phases.Append(card);
        }

        // ---- Photo monitor ----
        Gtk.Label viewDescription = Ui.Line("100%", "numeric", "caption");
        Gtk.Label selectionDescription = Ui.Label(string.Empty, "dim-label", "caption");
        _canvas.DescriptionsChanged += () =>
        {
            viewDescription.SetLabel(_canvas.ViewDescription);
            selectionDescription.SetLabel(_canvas.SelectionDescription);
        };
        Gtk.Button zoomOut = Ui.IconButton("zoom-out-symbolic", L.T("Xaml.ZoomOut"), "flat");
        zoomOut.OnClicked += (_, _) => _canvas.ZoomBy(1 / 1.3);
        Gtk.Button zoomIn = Ui.IconButton("zoom-in-symbolic", L.T("Xaml.ZoomIn"), "flat");
        zoomIn.OnClicked += (_, _) => _canvas.ZoomBy(1.3);
        Gtk.Button fit = Ui.IconButton("zoom-fit-best-symbolic", L.T("Xaml.FitToWindow"), "flat");
        fit.OnClicked += (_, _) => _canvas.ResetView();

        Gtk.CheckButton showPattern = Gtk.CheckButton.NewWithLabel(L.T("Xaml.ShowBurnPattern"));
        _binder.Check(showPattern, vm, nameof(CalibrationViewModel.ShowTargetPreview), () => vm.ShowTargetPreview, value => vm.ShowTargetPreview = value);
        Gtk.CheckButton showRectified = Gtk.CheckButton.NewWithLabel(L.T("Xaml.ShowRectifiedPreview"));
        _binder.Check(showRectified, vm, nameof(CalibrationViewModel.PreviewRectification), () => vm.PreviewRectification, value => vm.PreviewRectification = value);
        Gtk.CheckButton showPrediction = Gtk.CheckButton.NewWithLabel(L.T("Xaml.OverlayFittedCurves"));
        _binder.Check(showPrediction, vm, nameof(CalibrationViewModel.ShowPrediction), () => vm.ShowPrediction, value => vm.ShowPrediction = value);
        _binder.Watch(vm, [], () =>
        {
            showRectified.SetVisible(vm.IsPhotoStep);
            showRectified.SetSensitive(vm.IsRectificationReady);
            showPrediction.SetVisible(vm.IsLineFitStep);
        });
        viewDescription.SetWidthChars(5);
        viewDescription.SetEllipsize(Pango.EllipsizeMode.None);
        viewDescription.SetXalign(0.5f);
        Gtk.Box toggles = Ui.HBox(12, showPattern, showRectified, showPrediction);
        toggles.SetHexpand(true);
        Gtk.Box monitorTools = Ui.VBox(6, Ui.Line(L.T("Xaml.CalibrationPhoto"), "heading"),
            Ui.HBox(4, toggles, zoomOut, viewDescription, zoomIn, fit));

        Gtk.Label stageHint = Ui.Label(L.T("Stages.NewTargetHint"));
        stageHint.SetHalign(Gtk.Align.Center);
        stageHint.SetValign(Gtk.Align.Center);
        stageHint.SetMaxWidthChars(40);
        Gtk.Stack canvasStack = Gtk.Stack.New();
        canvasStack.AddNamed(_canvas.Root, "canvas");
        canvasStack.AddNamed(stageHint, "hint");
        Gtk.Frame canvasFrame = Gtk.Frame.New(null);
        canvasFrame.SetChild(canvasStack);
        canvasFrame.SetOverflow(Gtk.Overflow.Hidden);
        canvasFrame.AddCssClass("monitor-frame");
        canvasFrame.SetVexpand(true);

        // Legends and step-specific canvas tools under the photo.
        Gtk.Box legends = Ui.VBox(8);
        legends.Append(Visible(Ui.Hint(L.T("Arc.CanvasHint")), () => vm.IsRadialArcStep));
        legends.Append(Visible(Ui.Hint(L.T("Clock.Legend")), () => vm.IsClockStep));
        legends.Append(Visible(Ui.HBox(16, Legend(DiscPalette.Dark.Edge, L.T("Xaml.PhysicalOuterEdge")),
            Legend(DiscPalette.Dark.HubOutline, L.T("Xaml.PhysicalInnerEdge"))), () => vm.IsPhotoStep));

        Gtk.DropDown segment = Choice(() => vm.BoundarySegmentChoices.ToArray(), choice => choice.Label,
            () => vm.BoundarySegmentChoices.ToList().FindIndex(choice => choice.Id == vm.SelectedTraceArm),
            index => vm.SelectedTraceArm = vm.BoundarySegmentChoices[index].Id);
        Gtk.Box segmentTools = Ui.HBox(8, Ui.Line(L.T("HalfDisc.CurrentSegment"), "caption-heading"), segment,
            CommandButton(L.T("HalfDisc.AddSegment"), vm.AddBoundarySegmentCommand),
            CommandButton(L.T("HalfDisc.DeleteSegment"), vm.DeleteBoundarySegmentCommand));
        Visible(segmentTools, () => vm.IsBoundarySegmentStep);
        Gtk.Button deletePoint = Ui.Button(L.T("Xaml.DeletePoint"));
        deletePoint.OnClicked += (_, _) => _canvas.RemoveSelectedTracePoint();
        Gtk.Label traceSummary = BoundLabel(() => vm.TraceSummary, "dim-label", "caption");
        Gtk.Box traceTools = Ui.VBox(8, segmentTools,
            Ui.HBox(8, deletePoint, CommandButton(L.T("Xaml.ClearCurves"), vm.ClearTracesCommand), traceSummary),
            Ui.HBox(16, Legend(DiscPalette.Dark.Trace, L.T("Xaml.Photograph")), Legend(DiscPalette.Dark.Prediction, L.T("Xaml.Prediction")),
                Visible(Legend(DiscPalette.Dark.MissingImage, L.T("Xaml.LargeDeviation")), () => vm.HasOutlierPoints)));
        Visible(traceTools, () => vm.IsLineFitStep);
        legends.Append(traceTools);

        Gtk.Scale ringAngle = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 180, 1);
        ringAngle.SetDrawValue(false);
        ringAngle.SetSizeRequest(120, -1);
        ringAngle.SetTooltipText(L.T("Xaml.CoarseRingRotateHint"));
        ringAngle.OnValueChanged += (_, _) =>
        {
            _canvas.RingAngleDegrees = ringAngle.GetValue();
            _canvas.Redraw();
        };
        Gtk.Button removeRing = Ui.Button(L.T("Xaml.CoarseRingRemove"));
        removeRing.OnClicked += (_, _) => _canvas.RemoveSelectedRing();
        Gtk.Box dragRingTools = Visible(Ui.HBox(8, Ui.Line(L.T("Xaml.CoarseRingOrientation"), "caption-heading"), ringAngle, removeRing),
            () => vm.IsDragRingMode);
        Gtk.DropDown ringPicker = Choice(() => vm.RingChoices.ToArray(), choice => choice.Label,
            () => vm.RingChoices.ToList().FindIndex(choice => choice.Index == vm.SelectedRingIndex),
            index => vm.SelectedRingIndex = vm.RingChoices[index].Index);
        Gtk.Box pointRingTools = Ui.HBox(8, BoundLabel(() => vm.RingPickerLabel, "caption-heading"), ringPicker,
            Visible(Ui.HBox(8, CommandButton(L.T("Xaml.CoarseRingUndoPoint"), vm.UndoRingPointCommand),
                CommandButton(L.T("Xaml.CoarseRingRestartPoints"), vm.RestartRingPointsCommand)), () => vm.ThreePointMode),
            Visible(Ui.HBox(8, CommandButton(L.T("BandEdges.RedoEdge"), vm.RemoveSelectedBandEdgeCommand),
                BoundCommandButton(() => vm.RestartBandRayLabel, vm.RestartBandRayCommand)), () => vm.IsBandRayMode));
        Visible(pointRingTools, () => !vm.IsDragRingMode);
        legends.Append(Visible(Ui.VBox(8, dragRingTools, pointRingTools), () => vm.IsRingStep));

        Gtk.Box monitor = Ui.VBox(10, monitorTools, canvasFrame, selectionDescription, legends).Css("card", "section-card");
        monitor.SetHexpand(true);
        monitor.SetVexpand(true);

        // ---- Steps ----
        Gtk.Box steps = Ui.VBox(12,
            StepCard(1, () => L.T("Xaml.CreateCalibrationDisc"), () => vm.StepOneState, nameof(CalibrationViewModel.StepOneExpanded),
                () => vm.StepOneExpanded, value => vm.StepOneExpanded = value, BuildStepOne()),
            StepCard(2, () => L.T("Xaml.RectifyPhoto"), () => vm.StepTwoState, nameof(CalibrationViewModel.StepTwoExpanded),
                () => vm.StepTwoExpanded, value => vm.StepTwoExpanded = value, BuildStepTwo()),
            StepCard(3, () => L.T(vm.IsCoarseMode ? "Xaml.CoarseCalibrationPhase" : vm.IsIntermediateMode ? "HalfDisc.Stage" : "Xaml.FineCalibrationPhase"),
                () => vm.StepThreeState, nameof(CalibrationViewModel.StepThreeExpanded),
                () => vm.StepThreeExpanded, value => vm.StepThreeExpanded = value, BuildStepThree()));
        _stepsScroll = Ui.Scrolled(steps);
        _stepsScroll.SetSizeRequest(420, -1);
        _stepsScroll.SetHexpand(false);

        Gtk.Box workspace = Ui.HBox(16, monitor, _stepsScroll);
        workspace.SetVexpand(true);
        Gtk.Box page = Ui.VBox(14, Header.Root, phases, workspace);
        page.Margins(24, 24, 16, 24);
        Root = page;

        BindCanvas(canvasStack);
        _ = window;
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => L.T("Xaml.Calibration");

    public void ScrollToTop() => _stepsScroll.GetVadjustment()?.SetValue(0);

    // ---- Step 1: create the calibration disc ----

    private Gtk.Widget BuildStepOne()
    {
        CalibrationViewModel vm = _vm;
        DiscParametersState p = vm.Parameters;
        Gtk.DropDown discType = Choice(() => vm.DiscTypeChoices, text => text,
            () => vm.DiscTypeChoices.ToList().IndexOf(vm.SelectedDiscType), index => vm.SelectedDiscType = vm.DiscTypeChoices[index]);
        Gtk.DropDown cdPreset = Choice(() => p.CdPresets, preset => preset.DisplayName,
            () => p.CdPresets.ToList().IndexOf(p.SelectedCdPreset!), index => p.SelectedCdPreset = p.CdPresets[index], p);
        Gtk.DropDown dvdPreset = Choice(() => p.DvdPresets, preset => preset.DisplayName,
            () => p.DvdPresets.ToList().IndexOf(p.SelectedDvdPreset!), index => p.SelectedDvdPreset = p.DvdPresets[index], p);
        Gtk.DropDown bdPreset = Choice(() => p.BluRayPresets, preset => preset.DisplayName,
            () => p.BluRayPresets.ToList().IndexOf(p.SelectedBluRayPreset!), index => p.SelectedBluRayPreset = p.BluRayPresets[index], p);
        Visible(cdPreset, () => vm.IsCdSetup);
        Visible(dvdPreset, () => vm.IsDvdSetup);
        Visible(bdPreset, () => vm.IsBluRaySetup);

        Gtk.Grid grid = Gtk.Grid.New();
        grid.SetColumnSpacing(12);
        grid.SetRowSpacing(12);
        grid.SetColumnHomogeneous(true);
        grid.Attach(Ui.Field(L.T("Xaml.DiscType"), discType), 0, 0, 1, 1);
        grid.Attach(Ui.Field(L.T("CalibrationMedia.Preset"), Ui.VBox(0, cdPreset, dvdPreset, bdPreset)), 1, 0, 1, 1);
        grid.Attach(Ui.Field(L.T("Xaml.InnerRadiusMm"), FamilyEntry(
            (nameof(DiscParametersState.CdInnerRadius), () => p.CdInnerRadius, v => p.CdInnerRadius = v),
            (nameof(DiscParametersState.DvdInnerRadius), () => p.DvdInnerRadius, v => p.DvdInnerRadius = v),
            (nameof(DiscParametersState.BdInnerRadius), () => p.BdInnerRadius, v => p.BdInnerRadius = v))), 0, 1, 1, 1);
        grid.Attach(Ui.Field(L.T("Xaml.OuterRadiusMm"), FamilyEntry(
            (nameof(DiscParametersState.CdOuterRadius), () => p.CdOuterRadius, v => p.CdOuterRadius = v),
            (nameof(DiscParametersState.DvdOuterRadius), () => p.DvdOuterRadius, v => p.DvdOuterRadius = v),
            (nameof(DiscParametersState.BdOuterRadius), () => p.BdOuterRadius, v => p.BdOuterRadius = v))), 1, 1, 1, 1);
        Gtk.Widget sectors = FamilyEntry(
            (nameof(DiscParametersState.CdSectors), () => p.CdSectors, v => p.CdSectors = v),
            (nameof(DiscParametersState.DvdTotalSectors), () => p.DvdTotalSectors, v => p.DvdTotalSectors = v),
            (nameof(DiscParametersState.BdTotalSectors), () => p.BdTotalSectors, v => p.BdTotalSectors = v));
        Gtk.Button readSectors = CommandButton(L.T("CalibrationMedia.ReadButton"), vm.ReadDiscSectorsCommand);
        readSectors.SetTooltipText(L.T("CalibrationMedia.ReadHint"));
        grid.Attach(Ui.Field(L.T("Xaml.GeometryTotalSectors"), Ui.HBox(6, sectors.Expand(), readSectors)), 0, 2, 1, 1);
        Gtk.Widget usable = FamilyEntry(
            (nameof(DiscParametersState.CdUsableSectors), () => p.CdUsableSectors, v => p.CdUsableSectors = v),
            (nameof(DiscParametersState.DvdUsableSectors), () => p.DvdUsableSectors, v => p.DvdUsableSectors = v),
            (nameof(DiscParametersState.BdUsableSectors), () => p.BdUsableSectors, v => p.BdUsableSectors = v));
        usable.SetTooltipText(L.T("Xaml.UsableSectorsHint"));
        grid.Attach(Ui.Field(L.T("Xaml.UsableSectors"), usable), 1, 2, 1, 1);

        Gtk.DropDown recorder = Gtk.DropDown.New(null, null);
        _binder.DropDown(recorder, vm, nameof(CalibrationViewModel.CapacityRecorders), () => vm.CapacityRecorders ?? [],
            device => device.DisplayName, nameof(CalibrationViewModel.SelectedCapacityRecorder),
            () => vm.SelectedCapacityRecorder, device => vm.SelectedCapacityRecorder = device);
        Gtk.Box recorderBox = Visible(Ui.Field(L.T("Xaml.Recorder"), recorder), () => vm.HasRecorderChoice);
        Gtk.Label capacity = BoundLabel(() => vm.CapacityStatus, "dim-label", "caption");

        // Encoding (expander).
        Gtk.CheckButton cdInterleave = Gtk.CheckButton.NewWithLabel(L.T("CalibrationMedia.CdInterleave"));
        _binder.Check(cdInterleave, p, nameof(DiscParametersState.CdInterleave), () => p.CdInterleave, value => p.CdInterleave = value);
        Visible(cdInterleave, () => vm.IsCdSetup);
        Gtk.CheckButton dvdCompat = Gtk.CheckButton.NewWithLabel(L.T("DvdReadCompatibility.Label"));
        _binder.Check(dvdCompat, p, nameof(DiscParametersState.DvdReadCompatibility), () => p.DvdReadCompatibility,
            value => p.DvdReadCompatibility = value);
        Gtk.Box dvdBox = Visible(Ui.VBox(4, dvdCompat, Ui.Hint(L.T("DvdReadCompatibility.Hint"))), () => vm.IsDvdSetup);
        Gtk.Grid pitch = Gtk.Grid.New();
        pitch.SetColumnSpacing(8);
        pitch.SetColumnHomogeneous(true);
        pitch.Attach(Ui.Field(L.T("Xaml.LinearTerm"), TextEntry(nameof(CalibrationViewModel.SetupPitchLinear), () => vm.SetupPitchLinear,
            v => vm.SetupPitchLinear = v)), 0, 0, 1, 1);
        pitch.Attach(Ui.Field(L.T("Xaml.QuadraticTerm"), TextEntry(nameof(CalibrationViewModel.SetupPitchQuadratic), () => vm.SetupPitchQuadratic,
            v => vm.SetupPitchQuadratic = v)), 1, 0, 1, 1);
        pitch.Attach(Ui.Field(L.T("Xaml.CubicTerm"), TextEntry(nameof(CalibrationViewModel.SetupPitchCubic), () => vm.SetupPitchCubic,
            v => vm.SetupPitchCubic = v)), 2, 0, 1, 1);
        Gtk.CheckButton bdInvert = Gtk.CheckButton.NewWithLabel(L.T("Xaml.Invert3T6TMapping"));
        _binder.Check(bdInvert, p, nameof(DiscParametersState.BdInvertMapping), () => p.BdInvertMapping, value => p.BdInvertMapping = value);
        Visible(bdInvert, () => vm.IsBluRaySetup);
        Gtk.Expander encoding = Gtk.Expander.New(L.T("CalibrationMedia.Encoding"));
        encoding.SetChild(Ui.VBox(10, cdInterleave, dvdBox, pitch, bdInvert).Margins(8, 0, 0, 0));

        Gtk.Label coarseHint = Visible(BoundLabel(() => vm.CoarseSetupHint, "dim-label", "caption"), () => vm.IsCoarseMode);
        Gtk.Button create = BoundCommandButton(() => L.T(vm.IsCoarseMode ? "CalibrationArt.CreateCoarse"
            : vm.IsIntermediateMode ? "HalfDisc.Create" : "CalibrationArt.CreateFine"), vm.CreateTargetCommand, "suggested-action");
        Gtk.Label targetSummary = BoundLabel(() => vm.TargetSummary, "dim-label", "caption");
        Gtk.Label stageHint = Visible(Ui.Hint(L.T("Stages.NewTargetHint")), () => vm.NeedsStageTarget);

        // Burn and export.
        Gtk.Button burn = CommandButton(L.T("Xaml.OpenStreamingBurn"), vm.StreamBurnCommand);
        burn.SetTooltipText(L.T("Xaml.OpenBurnToSelectARecorderAndConfirmWriting"));
        Gtk.Box output = Ui.PathField(out Gtk.Entry outputEntry, out Gtk.Button outputBrowse, L.T("Xaml.ChooseLocation"));
        _binder.Entry(outputEntry, vm, nameof(CalibrationViewModel.OutputPath), () => vm.OutputPath, value => vm.OutputPath = value);
        _binder.Command(outputBrowse, vm.BrowseOutputCommand);
        Gtk.Button generate = CommandButton(L.T("Xaml.GenerateImageTrack"), vm.GenerateImageCommand);
        Gtk.Button exportPng = BoundCommandButton(() => L.T(vm.HasClockTarget || vm.HasWideBandTarget ? "Clock.ExportPreview" : "Xaml.ExportPatternPNG"),
            vm.ExportPngCommand);
        Gtk.Box burnBox = Ui.VBox(10,
            Ui.Line(L.T("Xaml.Burn"), "heading"), burn,
            Ui.Line(L.T("Xaml.ExternalBurning"), "heading"), Ui.Hint(L.T("Xaml.GenerateAnImageOrAudioTrackForExternalBurning")),
            output, Ui.HBox(8, generate, exportPng));

        Gtk.Expander session = Gtk.Expander.New(L.T("Xaml.CalibrationSessionAdvanced"));
        session.SetChild(Ui.HBox(8, CommandButton(L.T("Xaml.SaveCalibrationSession"), vm.SaveSessionCommand),
            CommandButton(L.T("Xaml.OpenCalibrationSession"), vm.OpenSessionCommand)).Margins(8, 0, 0, 0));

        return Ui.VBox(12, grid, recorderBox, capacity, encoding, coarseHint, create, targetSummary, stageHint,
            Gtk.Separator.New(Gtk.Orientation.Horizontal), burnBox, session);
    }

    // ---- Step 2: rectify the photo ----

    private Gtk.Widget BuildStepTwo()
    {
        CalibrationViewModel vm = _vm;
        Gtk.DropDown size = Choice(() => vm.PhysicalDiscSizeChoices, choice => choice.Label,
            () => vm.PhysicalDiscSizeChoices.ToList().IndexOf(vm.SelectedPhysicalDiscSize),
            index => vm.SelectedPhysicalDiscSize = vm.PhysicalDiscSizeChoices[index]);
        return Ui.VBox(12,
            Ui.Hint(L.T("Xaml.PhotoCalibrationFeatures")),
            CommandButton(L.T("Xaml.ImportDiscPhoto"), vm.ImportPhotoCommand),
            Ui.Field(L.T("Xaml.DiscSize"), size),
            Ui.HBox(8, CommandButton(L.T("Xaml.CalculateRectification"), vm.RectifyPhotoCommand),
                CommandButton(L.T("Xaml.ConfirmRectifiedPhoto"), vm.ConfirmRectificationCommand, "suggested-action")),
            BoundLabel(() => vm.RectificationSummary, "dim-label", "caption"));
    }

    // ---- Step 3: measure and fit ----

    private Gtk.Widget BuildStepThree()
    {
        CalibrationViewModel vm = _vm;
        Gtk.Box box = Ui.VBox(12, CommandButton(L.T("Xaml.BackToPhotoCorrection"), vm.BackToPhotoCommand).Align(Gtk.Align.Start));

        // Radial arc lengths (coarse, radial-arc pattern).
        Gtk.DropDown arcBand = Choice(() => vm.ArcBandNames, choice => choice.Label, () => vm.SelectedArcBand,
            index => vm.SelectedArcBand = index);
        Gtk.DropDown arcDirection = Choice(() => vm.ArcDirections, choice => choice.Label,
            () => vm.ArcDirections.ToList().FindIndex(choice => Equals(choice.Value, vm.SelectedArcDirection)),
            index => vm.SelectedArcDirection = vm.ArcDirections[index].Value);
        Gtk.CheckButton arcComplete = Gtk.CheckButton.NewWithLabel(L.T("Arc.Confirm"));
        _binder.Check(arcComplete, vm, nameof(CalibrationViewModel.ArcComplete), () => vm.ArcComplete, value => vm.ArcComplete = value);
        _binder.Watch(vm, [], () => arcComplete.SetSensitive(vm.CanConfirmArc));
        box.Append(Visible(Ui.VBox(10,
            Ui.Hint(L.T("Arc.Instructions")),
            Ui.HBox(12, Ui.Field(L.T("Arc.Segment"), arcBand).Expand(), Ui.Field(L.T("Arc.Direction"), arcDirection).Expand()),
            CommandButton(L.T("Xaml.CoarseRingUndoPoint"), vm.UndoArcLengthPointCommand).Align(Gtk.Align.Start),
            BoundLabel(() => vm.ArcMeasurementSummary, "dim-label", "caption"),
            arcComplete,
            Ui.HBox(8, CommandButton(L.T("Arc.Solve"), vm.SolveArcLengthsCommand),
                CommandButton(L.T("CalibrationMedia.ApplyRadii"), vm.ApplyArcLengthsCommand, "suggested-action")),
            BoundLabel(() => vm.ArcFitSummary, "dim-label", "caption")), () => vm.IsRadialArcStep));

        // Clock pattern.
        box.Append(Visible(Ui.VBox(10,
            Ui.Hint(L.T("Clock.Aim")),
            Ui.HBox(8, CommandButton(L.T("Clock.Read"), vm.ReadClockCommand), CommandButton(L.T("Xaml.CancelCalculation"), vm.CancelCommand)),
            BoundLabel(() => vm.ClockReadSummary, "dim-label", "caption"),
            CommandButton(L.T("Xaml.UseVerifiedRadii"), vm.ApplyClockCommand, "suggested-action").Align(Gtk.Align.Start)),
            () => vm.IsClockStep));

        // Rings / wide band edges.
        Gtk.CheckButton threePoint = Gtk.CheckButton.NewWithLabel(L.T("Xaml.CoarseRingThreePoints"));
        _binder.Check(threePoint, vm, nameof(CalibrationViewModel.ThreePointMode), () => vm.ThreePointMode, value => vm.ThreePointMode = value);
        box.Append(Visible(Ui.VBox(10,
            Visible(BoundLabel(() => vm.RingBoundaryInstructions), () => vm.HasWideBandTarget),
            Visible(threePoint, () => !vm.HasWideBandTarget),
            BoundLabel(() => vm.RingOperationHint, "dim-label", "caption"),
            Visible(BoundLabel(() => vm.BandRaySummary, "dim-label", "caption"), () => vm.IsBandRayMode),
            Visible(BoundLabel(() => vm.RingPointSummary, "dim-label", "caption"), () => vm.ThreePointMode),
            BoundLabel(() => vm.CoarseSummary, "dim-label", "caption"),
            Visible(BoundLabel(() => vm.BandIntervalSummary, "dim-label", "caption"), () => vm.HasWideBandTarget),
            Ui.HBox(8, BoundCommandButton(() => vm.RingSolveLabel, vm.SolveRingsCommand),
                CommandButton(L.T("CalibrationMedia.ApplyRadii"), vm.ApplyCoarseParametersCommand, "suggested-action")),
            Visible(Ui.VBox(8, Ui.HBox(8, CommandButton(L.T("Xaml.VerifySectorScale"), vm.VerifySectorScaleCommand),
                    CommandButton(L.T("Xaml.UseVerifiedRadii"), vm.ApplyVerifiedScaleCommand)),
                BoundLabel(() => vm.SectorCheckSummary, "dim-label", "caption")), () => vm.CanVerifySectorScale)),
            () => vm.IsRingStep));

        // Fine-mode hints for coarse-only patterns.
        box.Append(Visible(Ui.Hint(L.T("Arc.FineHint")), () => vm.HasRadialArcTarget && vm.IsFineMode));
        box.Append(Visible(Ui.Hint(L.T("WideBands.FineHint")), () => vm.HasWideBandTarget && vm.IsFineMode));
        box.Append(Visible(Ui.Hint(L.T("Clock.FineHint")), () => vm.HasClockTarget && vm.IsFineMode));

        // Line fit (fine and intermediate phases).
        Gtk.CheckButton fitPitch = Gtk.CheckButton.NewWithLabel(L.T("Xaml.FitCubicDVDTrackPitch"));
        _binder.Check(fitPitch, vm, nameof(CalibrationViewModel.FitDvdPitch), () => vm.FitDvdPitch, value => vm.FitDvdPitch = value);
        Gtk.DropDown searchRange = Gtk.DropDown.New(null, null);
        _binder.DropDown(searchRange, vm, nameof(CalibrationViewModel.ActiveSearchRangeChoices),
            () => vm.ActiveSearchRangeChoices, range => range.Label, nameof(CalibrationViewModel.SelectedCoarseSearchRange),
            () => vm.SelectedCoarseSearchRange, range => vm.SelectedCoarseSearchRange = range!);
        Gtk.Expander search = Gtk.Expander.New(string.Empty);
        _binder.Watch(vm, [], () => search.SetLabel(vm.SearchRangeHeader));
        search.SetChild(Ui.VBox(8, searchRange, Visible(Ui.Hint(L.T("HalfDisc.SearchHint")), () => vm.IsBoundarySegmentStep)).Margins(8, 0, 0, 0));
        Gtk.DropDown candidates = Gtk.DropDown.New(null, null);
        _binder.DropDown(candidates, vm, nameof(CalibrationViewModel.CoarseCandidates), () => vm.CoarseCandidates ?? [],
            candidate => candidate.Label, nameof(CalibrationViewModel.SelectedCoarseCandidate), () => vm.SelectedCoarseCandidate,
            candidate => vm.SelectedCoarseCandidate = candidate);
        candidates.SetTooltipText(L.T("Xaml.SelectACandidateToSeeItsPredictedCurvesOn"));
        _binder.Watch(vm, [], () => candidates.SetVisible(vm.CoarseCandidates is { Count: > 0 }));
        Gtk.CheckButton confirmed = Gtk.CheckButton.New();
        Gtk.Label confirmedLabel = BoundLabel(() => vm.TraceConfirmationLabel);
        confirmed.SetChild(confirmedLabel);
        _binder.Check(confirmed, vm, nameof(CalibrationViewModel.ContourConfirmed), () => vm.ContourConfirmed, value => vm.ContourConfirmed = value);
        box.Append(Visible(Ui.VBox(10,
            BoundLabel(() => vm.FineInitialSummary, "dim-label", "caption"),
            Visible(Ui.HBox(8,
                Visible(CommandButton(L.T("Xaml.UseMeasuredRingSeed"), vm.UseRingSeedCommand), () => !vm.HasAcceptedRingSeed),
                Visible(CommandButton(L.T("Xaml.RestoreFineInitial"), vm.ClearRingSeedCommand), () => vm.HasAcceptedRingSeed)),
                () => vm.HasAvailableRingSeed),
            BoundLabel(() => vm.TraceMarkingHint, "dim-label", "caption"),
            Visible(Ui.Hint(L.T("CalibrationArt.FineSpine")), () => vm.Target?.HasAngularReference == true),
            Visible(Ui.VBox(4, fitPitch, Ui.Hint(L.T("Xaml.CubicTrackPitchPointRequirement"))), () => vm.CanFitDvdPitch),
            search,
            Ui.HBox(8, CommandButton(L.T("Xaml.SolveParameters"), vm.SolveCommand), CommandButton(L.T("Xaml.CancelCalculation"), vm.CancelCommand)),
            candidates,
            BoundLabel(() => vm.FitAssessment, "dim-label", "caption"),
            confirmed,
            BoundCommandButton(() => vm.ApplyTraceLabel, vm.ApplyParametersCommand, "suggested-action").Align(Gtk.Align.Start),
            BoundLabel(() => vm.ApplyParametersHint, "dim-label", "caption")), () => vm.IsLineFitStep));
        return box;
    }

    // ---- Canvas wiring ----

    private void BindCanvas(Gtk.Stack canvasStack)
    {
        CalibrationViewModel vm = _vm;
        _canvas.BoundaryEditedCommand = vm.BoundaryEditedCommand;
        _canvas.TraceEditedCommand = vm.TraceEditedCommand;
        _canvas.BandRayPointCommand = vm.BandRayPointCommand;
        _canvas.ArcLengthPointCommand = vm.ArcLengthPointCommand;
        _canvas.UndoArcLengthPointCommand = vm.UndoArcLengthPointCommand;
        _canvas.PreferClockAngleCommand = vm.PreferClockAngleCommand;
        _canvas.RingPointCommand = vm.RingPointCommand;
        _canvas.UndoRingPointCommand = vm.UndoRingPointCommand;
        _canvas.RingEditedCommand = vm.RingEditedCommand;
        _canvas.SelectedTraceArmChanged = arm => vm.SelectedTraceArm = arm;
        (bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, bool, bool)? lastMode = null;
        int lastArm = int.MinValue;
        _binder.Watch(vm, [], () =>
        {
            canvasStack.SetVisibleChildName(vm.NeedsStageTarget ? "hint" : "canvas");
            _canvas.FallbackImage = vm.TargetImage;
            _canvas.Image = vm.IsTraceStep || vm.PreviewRectification ? vm.RectifiedPhoto : vm.PhotoImage;
            _canvas.TargetCanvasRadiusMm = vm.CanvasOuterRadiusMm;
            _canvas.CanvasRadiusMm = vm.PhotoCanvasRadiusMm;
            _canvas.BoundaryPoints = vm.PhotoBoundaryPoints;
            _canvas.BoundaryContours = vm.PhotoBoundaryContours;
            _canvas.TracePoints = vm.TracePoints;
            _canvas.PredictionContours = vm.CrossPredictionContours;
            _canvas.OutlierTracePointIndices = vm.OutlierTracePointIndices;
            _canvas.ArcLengthPoints = vm.ArcLengthPoints;
            _canvas.ClockReadAngles = vm.ClockReadAngles;
            _canvas.PreferredClockAngle = vm.PreferredClockAngle;
            _canvas.BandRayAngleDegrees = vm.BandRayAngleDegrees;
            _canvas.RingArcs = vm.RingArcs;
            _canvas.SelectedRingIndex = vm.SelectedRingIndex;
            _canvas.RingPointMarkers = vm.RingPointMarkers;
            _canvas.PhysicalRadiusMm = vm.SelectedPhysicalDiscSize?.OuterRadiusMm ?? 60;
            _canvas.ShowPrediction = vm.ShowPrediction;

            // Mode inputs: a change resets selections; step changes also reset the view.
            var mode = (vm.IsPhotoStep, vm.PreviewRectification, vm.ShowTargetPreview, vm.HasClockTarget, vm.HasWideBandTarget,
                vm.IsBandRayMode, vm.HasRadialArcTarget, vm.HasBoundarySegments, vm.IsCoarseMode, vm.ThreePointMode, vm.IsTraceStep,
                vm.NeedsStageTarget);
            if (lastMode != mode)
            {
                bool viewChanged = lastMode is not { } previous
                                   || previous.Item1 != mode.Item1 || previous.Item2 != mode.Item2 || previous.Item3 != mode.Item3
                                   || previous.Item11 != mode.Item11;
                _canvas.IsPhotoStep = vm.IsPhotoStep;
                _canvas.IsRectificationPreview = vm.PreviewRectification;
                _canvas.IsTargetPreview = vm.ShowTargetPreview;
                _canvas.HasClockTarget = vm.HasClockTarget;
                _canvas.HasWideBandTarget = vm.HasWideBandTarget;
                _canvas.IsBandRayMode = vm.IsBandRayMode;
                _canvas.HasRadialArcTarget = vm.HasRadialArcTarget;
                _canvas.IsBoundarySegmentMode = vm.HasBoundarySegments;
                _canvas.IsCoarseMode = vm.IsCoarseMode;
                _canvas.ThreePointMode = vm.ThreePointMode;
                lastMode = mode;
                _canvas.ViewportChanged(viewChanged);
            }

            if (lastArm != vm.SelectedTraceArm)
            {
                lastArm = vm.SelectedTraceArm;
                _canvas.SelectedTraceArm = vm.SelectedTraceArm;
                _canvas.SelectedArmChanged();
            }

            _canvas.PointsChanged();
        });
    }

    // ---- Helpers ----

    private Gtk.Widget StepCard(int number, Func<string> title, Func<StepCardState> state, string expandedProperty,
        Func<bool> getExpanded, Action<bool> setExpanded, Gtk.Widget body)
    {
        Gtk.Label numberLabel = Gtk.Label.New(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        numberLabel.AddCssClass("stage-number");
        Gtk.Label titleLabel = Ui.Line(string.Empty, "heading");
        Gtk.Label chip = Gtk.Label.New(string.Empty);
        chip.AddCssClass("step-chip");
        Gtk.Image arrow = Gtk.Image.NewFromIconName("pan-end-symbolic");
        _binder.Watch(_vm, [], () =>
        {
            titleLabel.SetLabel(title());
            StepCardState current = state();
            chip.SetLabel(L.T(current switch
            {
                StepCardState.Active => "Xaml.InProgress",
                StepCardState.Done => "Xaml.Complete",
                _ => "Xaml.NotStarted",
            }));
            chip.RemoveCssClass("active");
            chip.RemoveCssClass("done");
            if (current == StepCardState.Active) chip.AddCssClass("active");
            if (current == StepCardState.Done) chip.AddCssClass("done");
        });
        Gtk.Box headerContent = Ui.HBox(10, numberLabel, titleLabel.Expand(), chip, arrow);
        Gtk.Button header = Gtk.Button.New();
        header.SetChild(headerContent);
        header.AddCssClass("flat");
        Gtk.Revealer revealer = Gtk.Revealer.New();
        revealer.SetChild(body.Margins(4, 6, 6, 6));
        _binder.Watch(_vm, expandedProperty, () =>
        {
            revealer.SetRevealChild(getExpanded());
            arrow.SetFromIconName(getExpanded() ? "pan-down-symbolic" : "pan-end-symbolic");
        });
        header.OnClicked += (_, _) => setExpanded(!getExpanded());
        _binder.Sensitive(body, _vm, nameof(CalibrationViewModel.IsBusy), () => !_vm.IsBusy || number != 1);
        return Ui.VBox(6, header, revealer).Css("card", "section-card");
    }

    private T Visible<T>(T widget, Func<bool> visible) where T : Gtk.Widget
    {
        _binder.Watch(_vm, [], () => widget.SetVisible(visible()));
        return widget;
    }

    private Gtk.Label BoundLabel(Func<string?> text, params string[] cssClasses)
    {
        Gtk.Label label = Ui.Label(string.Empty, cssClasses);
        _binder.Watch(_vm, [], () =>
        {
            string value = text() ?? string.Empty;
            label.SetLabel(value);
            label.SetVisible(value.Length > 0);
        });
        return label;
    }

    private Gtk.Button CommandButton(string label, ICommand command, params string[] cssClasses)
    {
        Gtk.Button button = Ui.Button(label, cssClasses);
        _binder.Command(button, command);
        return button;
    }

    private Gtk.Button BoundCommandButton(Func<string> label, ICommand command, params string[] cssClasses)
    {
        Gtk.Button button = Ui.Button(string.Empty, cssClasses);
        _binder.Watch(_vm, [], () => button.SetLabel(label()));
        _binder.Command(button, command);
        return button;
    }

    private Gtk.Entry TextEntry(string property, Func<string> get, Action<string> set)
    {
        Gtk.Entry entry = Ui.Entry();
        entry.SetWidthChars(6);
        entry.AddCssClass("numeric");
        _binder.Entry(entry, _vm, property, get, set);
        return entry;
    }

    /// <summary>One field whose backing property follows the selected disc family.</summary>
    private Gtk.Widget FamilyEntry(params (string Property, Func<string> Get, Action<string> Set)[] families)
    {
        Gtk.Stack stack = Gtk.Stack.New();
        Func<bool>[] visible = [() => _vm.IsCdSetup, () => _vm.IsDvdSetup, () => _vm.IsBluRaySetup];
        for (int index = 0; index < families.Length; index++)
        {
            Gtk.Entry entry = Ui.Entry();
            entry.SetWidthChars(8);
            entry.AddCssClass("numeric");
            _binder.Entry(entry, _vm.Parameters, families[index].Property, families[index].Get, families[index].Set);
            stack.AddNamed(entry, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        _binder.Watch(_vm, [], () =>
        {
            int index = Array.FindIndex(visible, check => check());
            stack.SetVisibleChildName(Math.Max(0, index).ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
        return stack;
    }

    /// <summary>A drop-down over any list, synchronised by index on every model change.</summary>
    private Gtk.DropDown Choice<T>(Func<IReadOnlyList<T>> items, Func<T, string> label, Func<int> getIndex, Action<int> setIndex,
        INotifyPropertyChanged? source = null)
    {
        Gtk.DropDown dropDown = Gtk.DropDown.New(null, null);
        string[] labels = [];
        bool pushing = false;
        void Refresh()
        {
            IReadOnlyList<T> list = items();
            string[] next = list.Select(label).ToArray();
            pushing = true;
            try
            {
                if (!next.SequenceEqual(labels))
                {
                    labels = next;
                    dropDown.SetModel(Gtk.StringList.New(next));
                }

                int index = getIndex();
                uint wanted = index >= 0 && index < next.Length ? (uint)index : Gtk.Constants.INVALID_LIST_POSITION;
                if (dropDown.GetSelected() != wanted) dropDown.SetSelected(wanted);
            }
            finally
            {
                pushing = false;
            }
        }

        _binder.Watch(_vm, [], Refresh);
        if (source is not null) _binder.Watch(source, [], Refresh);
        dropDown.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected" || pushing) return;
            uint selected = dropDown.GetSelected();
            if (selected < labels.Length && (int)selected != getIndex()) setIndex((int)selected);
            Refresh();
        };
        return dropDown;
    }

    private static Gtk.Box Legend(Rgba color, string text)
    {
        Gtk.DrawingArea swatch = Gtk.DrawingArea.New();
        swatch.SetContentWidth(12);
        swatch.SetContentHeight(12);
        swatch.SetValign(Gtk.Align.Center);
        swatch.SetDrawFunc((_, cr, w, h) =>
        {
            cr.SetSourceRgba(color.R, color.G, color.B, 1);
            cr.Arc(w / 2.0, h / 2.0, Math.Min(w, h) / 2.0, 0, 2 * Math.PI);
            cr.Fill();
        });
        return Ui.HBox(6, swatch, Ui.Line(text, "caption"));
    }

    public void Dispose() => _binder.Dispose();
}
