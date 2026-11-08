using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>Writes the design composite to an image file (ISO, or WAV/RAW + CUE for CD).</summary>
internal sealed class ExportPage : IPage
{
    private readonly Binder _binder = new();
    private readonly Gtk.ScrolledWindow _scroller;

    public ExportPage(ExportViewModel viewModel, Gtk.Window window)
    {
        DiscParametersState state = viewModel.State;
        Header = new PageHeader(L.T("Xaml.Export"), workspace => viewModel.NavigateCommand.Execute(workspace),
            L.T("Xaml.ExportTheDesignCompositeAsABurnableImageAdjust"));
        _binder.Watch(viewModel, nameof(ExportViewModel.FamilyLabel), () => Header.SetBadge(viewModel.FamilyLabel));
        Gtk.Button next = Ui.Button(L.T("Xaml.NextBurn"));
        _binder.Command(next, viewModel.NavigateCommand, () => Workspace.Burn);
        Header.Actions.Append(next);

        // ---- Source image ----
        Adw.PreferencesGroup source = Adw.PreferencesGroup.New();
        source.SetTitle(L.T("Xaml.SourceImage"));
        Adw.ActionRow summary = Adw.ActionRow.New();
        summary.AddPrefix(Gtk.Image.NewFromIconName("image-x-generic-symbolic"));
        _binder.Watch(viewModel, nameof(ExportViewModel.SourceSummary), () => summary.SetTitle(GLib.Markup.EscapeText(viewModel.SourceSummary)));
        Gtk.Button settings = Ui.Button(L.T("Xaml.ProjectSettings2"), "flat");
        settings.SetValign(Gtk.Align.Center);
        settings.SetTooltipText(L.T("Xaml.EditGeometryRadiiSectorsAndTrackPitchInProject"));
        _binder.Command(settings, viewModel.OpenProjectSettingsCommand);
        summary.AddSuffix(settings);
        source.Add(summary);
        Adw.ButtonRow importRow = Adw.ButtonRow.New();
        importRow.SetTitle(L.T("Xaml.ImportImagesInDesign"));
        importRow.SetStartIconName("list-add-symbolic");
        importRow.OnActivated += (_, _) => viewModel.NavigateCommand.Execute(Workspace.Design);
        _binder.Visible(importRow, viewModel, nameof(ExportViewModel.HasLayers), () => !viewModel.HasLayers);
        source.Add(importRow);

        // ---- Output file ----
        Adw.PreferencesGroup output = Adw.PreferencesGroup.New();
        output.SetTitle(L.T("Xaml.OutputFile"));

        // CD
        Adw.EntryRow cdPath = PathRow(L.T("Xaml.OutputISO"), viewModel.BrowseOutputCommand);
        _binder.Watch(state, [nameof(DiscParametersState.CdOutputLabel), nameof(DiscParametersState.CdDataDirectory)],
            () => cdPath.SetTitle(state.CdOutputLabel));
        _binder.Entry(cdPath, state, nameof(DiscParametersState.CdOutputPath), () => state.CdOutputPath, value => state.CdOutputPath = value);
        Adw.EntryRow cdVolume = VolumeRow();
        _binder.Entry(cdVolume, state, nameof(DiscParametersState.CdVolumeLabel), () => state.CdVolumeLabel, value => state.CdVolumeLabel = value);
        _binder.Watch(state, [], () => cdVolume.SetVisible(viewModel.IsCd && state.HasCdFiles));
        Adw.SwitchRow interleave = Adw.SwitchRow.New();
        interleave.SetTitle(L.T("Xaml.EnableCDDADelayInterleaving"));
        interleave.SetSubtitle(L.T("Xaml.ApplyCDDADelayInterleavingWhenGeneratingAudioTracks"));
        _binder.Switch(interleave, state, nameof(DiscParametersState.CdInterleave), () => state.CdInterleave, value => state.CdInterleave = value);
        Adw.SwitchRow cue = Adw.SwitchRow.New();
        cue.SetTitle(L.T("Xaml.AlsoExportACUEFile"));
        _binder.Switch(cue, state, nameof(DiscParametersState.CdWriteCue), () => state.CdWriteCue, value => state.CdWriteCue = value);
        _binder.Watch(state, [], () =>
        {
            interleave.SetVisible(viewModel.IsCd && state.IsCdAudioMode);
            cue.SetVisible(viewModel.IsCd && state.IsCdAudioMode && state.IsCdWaveOutput);
        });
        Gtk.Label cdHint = Ui.Hint(string.Empty);
        _binder.Watch(state, [], () =>
        {
            cdHint.SetLabel(state.CdOutputHint ?? string.Empty);
            cdHint.SetVisible(viewModel.IsCd && !string.IsNullOrWhiteSpace(state.CdOutputHint));
        });

        // DVD
        Adw.EntryRow dvdPath = PathRow(L.T("Xaml.OutputISO"), viewModel.BrowseOutputCommand);
        _binder.Entry(dvdPath, state, nameof(DiscParametersState.DvdOutputPath), () => state.DvdOutputPath, value => state.DvdOutputPath = value);
        Adw.EntryRow dvdVolume = VolumeRow();
        _binder.Entry(dvdVolume, state, nameof(DiscParametersState.DvdVolumeLabel), () => state.DvdVolumeLabel, value => state.DvdVolumeLabel = value);
        Adw.SwitchRow dvdCompatibility = Adw.SwitchRow.New();
        dvdCompatibility.SetTitle(L.T("DvdReadCompatibility.Label"));
        dvdCompatibility.SetSubtitle(L.T("DvdReadCompatibility.Hint"));
        _binder.Switch(dvdCompatibility, state, nameof(DiscParametersState.DvdReadCompatibility),
            () => state.DvdReadCompatibility, value => state.DvdReadCompatibility = value);

        // Blu-ray
        Adw.EntryRow bdPath = PathRow(L.T("Xaml.OutputISO"), viewModel.BrowseOutputCommand);
        _binder.Entry(bdPath, state, nameof(DiscParametersState.BdOutputPath), () => state.BdOutputPath, value => state.BdOutputPath = value);
        Adw.EntryRow bdVolume = VolumeRow();
        _binder.Entry(bdVolume, state, nameof(DiscParametersState.BdVolumeLabel), () => state.BdVolumeLabel, value => state.BdVolumeLabel = value);

        foreach (Gtk.Widget row in new Gtk.Widget[] { cdPath, cdVolume, interleave, cue, dvdPath, dvdVolume, dvdCompatibility, bdPath, bdVolume })
            output.Add(row);
        _binder.Watch(viewModel, [nameof(ExportViewModel.IsCd), nameof(ExportViewModel.IsDvd), nameof(ExportViewModel.IsBluRay)], () =>
        {
            cdPath.SetVisible(viewModel.IsCd);
            dvdPath.SetVisible(viewModel.IsDvd);
            dvdVolume.SetVisible(viewModel.IsDvd);
            dvdCompatibility.SetVisible(viewModel.IsDvd);
            bdPath.SetVisible(viewModel.IsBluRay);
            bdVolume.SetVisible(viewModel.IsBluRay);
            cdVolume.SetVisible(viewModel.IsCd && state.HasCdFiles);
            interleave.SetVisible(viewModel.IsCd && state.IsCdAudioMode);
            cue.SetVisible(viewModel.IsCd && state.IsCdAudioMode && state.IsCdWaveOutput);
        });

        Gtk.Button generate = Ui.Button(string.Empty, "suggested-action", "pill");
        _binder.Watch(viewModel, nameof(ExportViewModel.GenerateButtonText), () => generate.SetLabel(viewModel.GenerateButtonText));
        _binder.Command(generate, viewModel.GenerateCommand);
        Gtk.Box generateRow = Ui.HBox(12, generate, Ui.Hint(L.T("Xaml.WritingAFileDoesNotModifyTheDisc")).Align(Gtk.Align.Start, Gtk.Align.Center));

        Gtk.Box content = Ui.VBox(24, Header.Root, source, Ui.VBox(8, output, cdHint), generateRow);
        _scroller = Ui.Page(content, 900);
        Root = _scroller;
        _ = window;
    }

    public Gtk.Widget Root { get; }

    public PageHeader? Header { get; }

    public string Title => L.T("Xaml.Export");

    public void ScrollToTop() => _scroller.GetVadjustment()?.SetValue(0);

    internal static Adw.EntryRow PathRow(string title, System.Windows.Input.ICommand browse)
    {
        Adw.EntryRow row = Adw.EntryRow.New();
        row.SetTitle(title);
        Gtk.Button button = Ui.IconButton("document-open-symbolic", L.T("Xaml.Select"), "flat");
        button.SetValign(Gtk.Align.Center);
        button.OnClicked += (_, _) =>
        {
            if (browse.CanExecute(null)) browse.Execute(null);
        };
        row.AddSuffix(button);
        return row;
    }

    internal static Adw.EntryRow VolumeRow()
    {
        Adw.EntryRow row = Adw.EntryRow.New();
        row.SetTitle(L.T("Xaml.VolumeLabel"));
        if (row.GetDelegate() is Gtk.Text text) text.SetMaxLength(32);
        return row;
    }

    public void Dispose() => _binder.Dispose();
}
