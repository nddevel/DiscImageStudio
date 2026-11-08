using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Page title row (title, family badge, optional hint and actions) plus the stage rail
/// that lists the project workflow, matching the WPF PageHeader/StageBar pair.</summary>
internal sealed class PageHeader
{
    private static readonly (Workspace Workspace, string Key)[] Stages =
    [
        (Workspace.Design, "设计"),
        (Workspace.Export, "导出"),
        (Workspace.Burn, "刻录"),
        (Workspace.Preview, "预览"),
        (Workspace.Calibration, "标定"),
    ];

    private readonly Gtk.Box _stageRail;
    private readonly Action<Workspace> _navigate;
    private Workspace _current;

    public PageHeader(string title, Action<Workspace> navigate, string? hint = null)
    {
        _navigate = navigate;
        TitleLabel = Ui.Line(title, "title-1");
        Badge = Ui.Badge(string.Empty);
        Badge.SetVisible(false);
        Actions = Ui.HBox(8);
        Actions.SetHalign(Gtk.Align.End);
        Actions.SetHexpand(true);
        Actions.SetValign(Gtk.Align.Center);

        Gtk.Box titleRow = Ui.HBox(10, TitleLabel, Badge);
        if (hint is not null)
        {
            Gtk.MenuButton help = Gtk.MenuButton.New();
            help.SetIconName("help-about-symbolic");
            help.AddCssClass("flat");
            help.AddCssClass("circular");
            help.SetValign(Gtk.Align.Center);
            help.SetTooltipText(L.T("Xaml.Help"));
            Gtk.Label text = Ui.Label(hint);
            text.SetMaxWidthChars(48);
            text.Margins(6);
            Gtk.Popover popover = Gtk.Popover.New();
            popover.SetChild(text);
            help.SetPopover(popover);
            titleRow.Append(help);
        }

        titleRow.Append(Actions);

        _stageRail = Ui.HBox(4);
        _stageRail.AddCssClass("stage-rail");
        Root = Ui.VBox(10, titleRow, _stageRail);
        Root.SetMarginBottom(18);
        RebuildStages();
    }

    public Gtk.Box Root { get; }

    public Gtk.Label TitleLabel { get; }

    public Gtk.Label Badge { get; }

    public Gtk.Box Actions { get; }

    public bool ShowStages
    {
        get => _stageRail.GetVisible();
        set => _stageRail.SetVisible(value);
    }

    public Workspace Current
    {
        get => _current;
        set
        {
            _current = value;
            RebuildStages();
        }
    }

    public void SetBadge(string? text)
    {
        Badge.SetLabel(text ?? string.Empty);
        Badge.SetVisible(!string.IsNullOrEmpty(text));
    }

    private void RebuildStages()
    {
        _stageRail.RemoveAllChildren();
        for (int index = 0; index < Stages.Length; index++)
        {
            (Workspace workspace, string key) = Stages[index];
            if (workspace == Workspace.Preview)
            {
                Gtk.Separator separator = Gtk.Separator.New(Gtk.Orientation.Vertical);
                separator.SetMarginStart(6);
                separator.SetMarginEnd(6);
                separator.SetMarginTop(6);
                separator.SetMarginBottom(6);
                _stageRail.Append(separator);
            }

            bool active = workspace == _current
                          || workspace == Workspace.Calibration && _current == Workspace.CalibrationBurn;
            Gtk.Label number = Gtk.Label.New((index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            number.AddCssClass("stage-number");
            Gtk.Box content = Ui.HBox(6, number, Gtk.Label.New(L.T(key)));
            Gtk.Button button = Gtk.Button.New();
            button.SetChild(content);
            button.AddCssClass("flat");
            button.AddCssClass("stage-item");
            if (active) button.AddCssClass("active");
            Workspace target = workspace;
            button.OnClicked += (_, _) => _navigate(target);
            _stageRail.Append(button);
        }
    }
}
