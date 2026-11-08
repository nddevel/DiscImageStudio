using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>The status strip under the pages: task title and status, progress, burn telemetry,
/// result actions and an expandable run log. Bound to the shell like the WPF JobDock.</summary>
internal sealed class JobDock : IDisposable
{
    private readonly Binder _binder = new();
    private readonly ShellViewModel _shell;
    private readonly Gtk.TextBuffer _log;
    private readonly Gtk.TextView _logView;

    public JobDock(ShellViewModel shell, Gtk.Window window)
    {
        _shell = shell;

        Adw.Spinner spinner = Adw.Spinner.New();
        spinner.SetSizeRequest(16, 16);
        Gtk.Image doneIcon = Gtk.Image.NewFromIconName("object-select-symbolic");
        doneIcon.AddCssClass("success");
        Gtk.Image idleIcon = Gtk.Image.NewFromIconName("content-loading-symbolic");
        idleIcon.AddCssClass("dim-label");
        Gtk.Stack indicator = Gtk.Stack.New();
        indicator.AddNamed(spinner, "running");
        indicator.AddNamed(doneIcon, "done");
        indicator.AddNamed(idleIcon, "idle");
        _binder.Watch(shell, [nameof(ShellViewModel.IsJobRunning), nameof(ShellViewModel.JobHasResult)], () =>
            indicator.SetVisibleChildName(shell.IsJobRunning ? "running" : shell.JobHasResult ? "done" : "idle"));

        Gtk.Label title = Ui.Line(string.Empty, "heading");
        _binder.Text(title, shell, nameof(ShellViewModel.JobTitle), () => shell.JobTitle);
        Gtk.Label status = Ui.Line(string.Empty, "dim-label");
        status.SetHexpand(true);
        _binder.Watch(shell, nameof(ShellViewModel.JobStatusText), () =>
        {
            status.SetLabel(shell.JobStatusText);
            status.SetTooltipText(shell.JobStatusText);
        });

        Gtk.ProgressBar progress = Gtk.ProgressBar.New();
        progress.SetValign(Gtk.Align.Center);
        progress.SetSizeRequest(180, -1);
        _binder.Visible(progress, shell, [nameof(ShellViewModel.JobProgressVisibility), nameof(ShellViewModel.IsJobRunning),
            nameof(ShellViewModel.JobHasResult)], () => shell.JobProgressVisibility == System.Windows.Visibility.Visible);
        uint pulseSource = 0;
        _binder.Watch(shell, [nameof(ShellViewModel.JobProgressValue), nameof(ShellViewModel.IsJobProgressIndeterminate),
            nameof(ShellViewModel.IsJobRunning)], () =>
        {
            bool pulsing = shell.IsJobProgressIndeterminate && shell.IsJobRunning;
            if (pulsing && pulseSource == 0)
            {
                pulseSource = GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 120, () =>
                {
                    progress.Pulse();
                    return true;
                });
            }
            else if (!pulsing && pulseSource != 0)
            {
                GLib.Functions.SourceRemove(pulseSource);
                pulseSource = 0;
            }

            if (!pulsing) progress.SetFraction(Math.Clamp(shell.JobProgressValue / 100.0, 0, 1));
        });
        _binder.OnDispose(() =>
        {
            if (pulseSource != 0) GLib.Functions.SourceRemove(pulseSource);
        });

        Gtk.Label amount = Ui.Line(string.Empty, "numeric", "caption");
        Gtk.Label rate = Ui.Line(string.Empty, "numeric", "caption");
        Gtk.Label multiplier = Ui.Line(string.Empty, "numeric", "caption");
        _binder.Text(amount, shell, nameof(ShellViewModel.BurnAmountText), () => shell.BurnAmountText);
        _binder.Text(rate, shell, nameof(ShellViewModel.BurnRateText), () => shell.BurnRateText);
        _binder.Text(multiplier, shell, nameof(ShellViewModel.BurnMultiplierText), () => shell.BurnMultiplierText);
        Gtk.Box telemetry = Ui.HBox(16, amount, rate, multiplier);
        _binder.Visible(telemetry, shell, nameof(ShellViewModel.IsBurnTelemetryVisible), () => shell.IsBurnTelemetryVisible);

        Gtk.Button cancel = Ui.Button(L.T("Xaml.Cancel"));
        cancel.SetTooltipText(L.T("Xaml.StopTheCurrentTaskIfItSupportsCancellation"));
        _binder.Command(cancel, shell.CancelJobCommand);
        _binder.Visible(cancel, shell, nameof(ShellViewModel.CancelJobVisibility),
            () => shell.CancelJobVisibility == System.Windows.Visibility.Visible);

        Gtk.Button openOutput = Ui.Button(L.T("Xaml.OpenOutputFolder"));
        _binder.Command(openOutput, shell.OpenOutputCommand);
        Gtk.Button previewOutput = Ui.Button(L.T("Xaml.PreviewOutput"));
        previewOutput.SetTooltipText(L.T("Xaml.SimulateReadingBackTheMostRecentlyGeneratedFileIn"));
        _binder.Command(previewOutput, shell.PreviewLastOutputCommand);
        _binder.Visible(previewOutput, shell, nameof(ShellViewModel.CanPreviewLastOutputVisibility),
            () => shell.CanPreviewLastOutputVisibility == System.Windows.Visibility.Visible);
        Gtk.Box results = Ui.HBox(8, openOutput, previewOutput);
        _binder.Visible(results, shell, [nameof(ShellViewModel.ResultActionsVisibility), nameof(ShellViewModel.JobHasResult),
            nameof(ShellViewModel.HasJobOutput)], () => shell.ResultActionsVisibility == System.Windows.Visibility.Visible);

        Gtk.Button clearLog = Ui.Button(L.T("Xaml.ClearLog"), "flat");
        _binder.Command(clearLog, shell.ClearLogCommand);
        _binder.Visible(clearLog, shell, [nameof(ShellViewModel.LogActionsVisibility), nameof(ShellViewModel.HasLogContent)],
            () => shell.LogActionsVisibility == System.Windows.Visibility.Visible);

        Gtk.ToggleButton toggle = Gtk.ToggleButton.New();
        toggle.SetIconName("pan-up-symbolic");
        toggle.AddCssClass("flat");
        toggle.SetTooltipText(L.T("Xaml.TaskDetailsLog"));
        _binder.Visible(toggle, shell, [nameof(ShellViewModel.DockToggleVisibility), nameof(ShellViewModel.HasLogContent),
            nameof(ShellViewModel.IsJobDockExpanded)], () => shell.DockToggleVisibility == System.Windows.Visibility.Visible);
        bool syncing = false;
        _binder.Watch(shell, nameof(ShellViewModel.IsJobDockExpanded), () =>
        {
            syncing = true;
            toggle.SetActive(shell.IsJobDockExpanded);
            toggle.SetIconName(shell.IsJobDockExpanded ? "pan-down-symbolic" : "pan-up-symbolic");
            syncing = false;
        });
        toggle.OnToggled += (_, _) =>
        {
            if (!syncing && toggle.GetActive() != shell.IsJobDockExpanded) shell.ToggleJobDockCommand.Execute(null);
        };

        Gtk.Box strip = Ui.HBox(10, indicator, title, status, telemetry, progress, cancel, results, clearLog, toggle);
        strip.Margins(6, 12, 6, 12);

        _log = Gtk.TextBuffer.New(null);
        _log.SetText(shell.LogText, -1);
        _logView = Gtk.TextView.NewWithBuffer(_log);
        _logView.SetEditable(false);
        _logView.SetMonospace(true);
        _logView.SetWrapMode(Gtk.WrapMode.WordChar);
        _logView.Margins(8, 12, 8, 12);
        Gtk.ScrolledWindow logScroll = Ui.Scrolled(_logView);
        logScroll.SetMinContentHeight(160);
        logScroll.SetMaxContentHeight(220);
        logScroll.SetPropagateNaturalHeight(true);
        Gtk.Revealer revealer = Gtk.Revealer.New();
        revealer.SetChild(logScroll);
        revealer.SetTransitionType(Gtk.RevealerTransitionType.SlideUp);
        _binder.Watch(shell, nameof(ShellViewModel.IsJobDockExpanded), () => revealer.SetRevealChild(shell.IsJobDockExpanded));

        Action<string> appended = text => Gnome.App.RunOnMain(() => AppendLog(text));
        Action cleared = () => Gnome.App.RunOnMain(() => _log.SetText(string.Empty, -1));
        _binder.Event(() => shell.LogAppended += appended, () => shell.LogAppended -= appended);
        _binder.Event(() => shell.LogCleared += cleared, () => shell.LogCleared -= cleared);

        Root = Ui.VBox(0, revealer, strip);
        Root.AddCssClass("job-dock");
    }

    public Gtk.Box Root { get; }

    private void AppendLog(string text)
    {
        _log.GetEndIter(out Gtk.TextIter end);
        _log.Insert(end, text, -1);
        _log.GetEndIter(out end);
        Gtk.TextMark mark = _log.CreateMark(null, end, false);
        _logView.ScrollMarkOnscreen(mark);
        _log.DeleteMark(mark);
    }

    public void Dispose() => _binder.Dispose();
}
