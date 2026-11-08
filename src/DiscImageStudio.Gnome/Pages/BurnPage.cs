using DiscImageStudio.Burning;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>Streams the project composite (or the calibration pattern) to a blank disc.</summary>
internal sealed class BurnPage : IPage
{
    private readonly Binder _binder = new();
    private readonly BurnViewModel _viewModel;
    private readonly Gtk.ScrolledWindow _scroller;

    public BurnPage(ShellViewModel shell, BurnViewModel viewModel, Gtk.Window window)
    {
        _viewModel = viewModel;
        Header = new PageHeader(viewModel.PageTitle, workspace => viewModel.NavigateCommand.Execute(workspace),
            L.T("Xaml.StreamDataDirectlyToABlankDiscWithoutFirst"));
        _binder.Watch(viewModel, [nameof(BurnViewModel.FamilyLabel), nameof(BurnViewModel.PageTitle)], () =>
        {
            Header.SetBadge(viewModel.FamilyLabel);
            Header.TitleLabel.SetLabel(viewModel.PageTitle);
        });
        _binder.Watch(shell, nameof(ShellViewModel.IsProjectMode), () => Header.ShowStages = shell.IsProjectMode);
        if (viewModel.UseCalibrationPattern)
        {
            Gtk.Button back = Ui.Button(L.T("Standalone.ReturnToCalibration"));
            _binder.Command(back, viewModel.NavigateCommand, () => Workspace.Calibration);
            _binder.Sensitive(back, viewModel, nameof(BurnViewModel.IsSetupEnabled), () => viewModel.IsSetupEnabled);
            Header.Actions.Append(back);
        }

        // ---- Content to write ----
        Gtk.Label sourceTitle = Ui.Label(string.Empty, "heading");
        Gtk.Label sourceDetails = Ui.Label(string.Empty, "dim-label");
        _binder.Text(sourceTitle, viewModel, nameof(BurnViewModel.SourceTitle), () => viewModel.SourceTitle);
        _binder.Text(sourceDetails, viewModel, nameof(BurnViewModel.SourceDetails), () => viewModel.SourceDetails);
        Gtk.Box sourceBox = Ui.VBox(6, sourceTitle, sourceDetails).Css("inset-panel");

        Gtk.CheckButton compatibility = Gtk.CheckButton.NewWithLabel(L.T("DvdReadCompatibility.Label"));
        _binder.Check(compatibility, viewModel, nameof(BurnViewModel.DvdReadCompatibility),
            () => viewModel.DvdReadCompatibility, value => viewModel.DvdReadCompatibility = value);
        _binder.Sensitive(compatibility, viewModel, [nameof(BurnViewModel.CanEditDvdReadCompatibility), nameof(BurnViewModel.IsSetupEnabled)],
            () => viewModel.CanEditDvdReadCompatibility);
        Gtk.Box compatibilityBox = Ui.VBox(4, compatibility, Ui.Hint(L.T("DvdReadCompatibility.Hint")));
        if (viewModel.UseCalibrationPattern) compatibilityBox.Append(Ui.Hint(L.T("DvdReadCompatibility.FrozenHint")));
        _binder.Visible(compatibilityBox, viewModel, nameof(BurnViewModel.IsDvdBurn), () => viewModel.IsDvdBurn);

        Gtk.Box contentCard = Ui.SectionCard(L.T("Xaml.ContentToWrite"), "media-record-symbolic",
            Ui.VBox(14, sourceBox, compatibilityBox));

        // ---- Recorder and speed ----
        Adw.PreferencesGroup recorder = Adw.PreferencesGroup.New();
        recorder.SetTitle(L.T("Xaml.RecorderAndSpeed"));
        Adw.ComboRow devices = Adw.ComboRow.New();
        devices.SetTitle(L.T("Xaml.Recorder"));
        _binder.Combo<OpticalBurnDevice>(devices, viewModel, nameof(BurnViewModel.Devices), () => viewModel.Devices ?? [],
            device => device.DisplayName, nameof(BurnViewModel.SelectedDevice), () => viewModel.SelectedDevice,
            device => viewModel.SelectedDevice = device);
        Gtk.Button refreshDevices = Ui.IconButton("view-refresh-symbolic", L.T("Xaml.Refresh"), "flat");
        refreshDevices.SetValign(Gtk.Align.Center);
        _binder.Command(refreshDevices, viewModel.RefreshDevicesCommand);
        devices.AddSuffix(refreshDevices);
        _binder.Watch(viewModel, nameof(BurnViewModel.DeviceStatusText), () => devices.SetSubtitle(GLib.Markup.EscapeText(viewModel.DeviceStatusText ?? string.Empty)));
        recorder.Add(devices);

        Adw.ComboRow speeds = Adw.ComboRow.New();
        speeds.SetTitle(L.T("Xaml.WriteSpeed"));
        _binder.Combo<BurnViewModel.BurnWriteSpeedOption>(speeds, viewModel, nameof(BurnViewModel.Speeds), () => viewModel.Speeds,
            option => option.DisplayName, nameof(BurnViewModel.SelectedSpeed), () => viewModel.SelectedSpeed,
            option => viewModel.SelectedSpeed = option);
        _binder.Sensitive(speeds, viewModel, nameof(BurnViewModel.IsSpeedComboEnabled), () => viewModel.IsSpeedComboEnabled);
        Gtk.Button readDisc = Ui.IconLabelButton("media-optical-symbolic", L.T("Xaml.ReadDisc"), "flat");
        readDisc.SetValign(Gtk.Align.Center);
        _binder.Command(readDisc, viewModel.RefreshSpeedsCommand);
        _binder.Sensitive(readDisc, viewModel, nameof(BurnViewModel.IsSpeedRefreshEnabled), () => viewModel.IsSpeedRefreshEnabled);
        _binder.Watch(viewModel, nameof(BurnViewModel.SpeedStatusText), () => speeds.SetSubtitle(GLib.Markup.EscapeText(viewModel.SpeedStatusText ?? string.Empty)));
        recorder.Add(speeds);
        speeds.AddSuffix(readDisc);
        _binder.Sensitive(recorder, viewModel, nameof(BurnViewModel.IsSetupEnabled), () => viewModel.IsSetupEnabled);
        recorder.SetDescription(LinuxOpticalDiscBurner.BackendSummary);

        // ---- Danger zone ----
        Gtk.Image warning = Gtk.Image.NewFromIconName("dialog-warning-symbolic");
        warning.SetPixelSize(24);
        warning.AddCssClass("warning");
        Gtk.Box dangerTitle = Ui.HBox(10, warning, Ui.Label(L.T("Xaml.BeforeBurning"), "title-4"));
        Gtk.Label dangerBody = Ui.Label(L.T("Xaml.StreamingRequiresBlankMediaInterruptionsPowerLossOrInsufficient"));
        Gtk.CheckButton confirm = Gtk.CheckButton.NewWithLabel(L.T("Xaml.IHaveInsertedTheCorrectBlankDisc"));
        _binder.Check(confirm, viewModel, nameof(BurnViewModel.ConfirmChecked), () => viewModel.ConfirmChecked,
            value => viewModel.ConfirmChecked = value);
        _binder.Sensitive(confirm, viewModel, nameof(BurnViewModel.IsSetupEnabled), () => viewModel.IsSetupEnabled);
        Gtk.Button start = Ui.Button(L.T("Xaml.StartStreamingBurn"), "destructive-action");
        _binder.Command(start, viewModel.StartBurnCommand);
        Gtk.Button cancel = Ui.Button(L.T("Xaml.CancelBurn"));
        _binder.Command(cancel, viewModel.CancelBurnCommand);
        Gtk.Box danger = Ui.VBox(12, dangerTitle, dangerBody, confirm, Ui.HBox(10, start, cancel)).Css("card", "danger-card");

        Gtk.Box content = Ui.VBox(24, Header.Root, contentCard, recorder, danger);
        _scroller = Ui.Page(content, 900);
        Root = _scroller;
        _ = window;
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => _viewModel.PageTitle;

    public void ScrollToTop() => _scroller.GetVadjustment()?.SetValue(0);

    public void Dispose() => _binder.Dispose();
}
