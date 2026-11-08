using DiscImageStudio.Gnome.Imaging;
using DiscImageStudio.Gnome.Widgets;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;
using MahApps.Metro.IconPacks;

namespace DiscImageStudio.Gnome.Pages;

/// <summary>Start screen: open/new project, templates and recent projects.</summary>
internal sealed class HomePage : IDisposable
{
    private readonly Binder _binder = new();
    private readonly HomeViewModel _viewModel;
    private readonly Gtk.FlowBox _templates;
    private readonly Gtk.FlowBox _recents;
    private readonly Gtk.ScrolledWindow _scroller;

    public HomePage(HomeViewModel viewModel, Gtk.Window window)
    {
        _viewModel = viewModel;

        Gtk.Label title = Ui.Label("Disc Image Studio", "title-1");
        Gtk.Label subtitle = Ui.Label(L.T("Xaml.CreateAndBurnVisibleArtworkOnOpticalDiscs"), "dim-label");

        // One card for the three ways forward: the open project on the left, starting
        // something else on the right.
        Gtk.Label continueName = Ui.Line(string.Empty, "dim-label");
        continueName.SetMaxWidthChars(32);
        Gtk.Box continueContent = Ui.HBox(8, Gtk.Image.NewFromIconName("go-previous-symbolic"),
            Gtk.Label.New(L.T("Xaml.BackToProject")), continueName);
        Gtk.Button continueButton = Gtk.Button.New();
        continueButton.SetChild(continueContent);
        continueButton.SetTooltipText(L.T("Xaml.ReturnToTheOpenProjectAndContinueInDesign"));
        _binder.Command(continueButton, viewModel.ContinueProjectCommand);
        _binder.Visible(continueButton, viewModel, [nameof(HomeViewModel.IsProjectOpen), nameof(HomeViewModel.IsProjectOpenVisibility)],
            () => viewModel.IsProjectOpen);
        _binder.Text(continueName, viewModel, nameof(HomeViewModel.CurrentProjectName), () => viewModel.CurrentProjectName);

        Gtk.Button open = Ui.IconLabelButton("document-open-symbolic", L.T("Xaml.OpenProject"));
        _binder.Command(open, viewModel.OpenProjectCommand);
        Gtk.Button create = Ui.IconLabelButton("document-new-symbolic", L.T("Xaml.NewProject"), "suggested-action");
        _binder.Command(create, viewModel.NewProjectCommand);
        Gtk.Box actions = Ui.HBox(10, open, create).Align(Gtk.Align.End, Gtk.Align.Center);
        actions.SetHexpand(true);
        Gtk.Box actionCard = Ui.HBox(10, continueButton, actions);
        actionCard.AddCssClass("card");
        actionCard.AddCssClass("home-actions");

        Gtk.Label templatesTitle = Ui.Label(L.T("Xaml.StartFromATemplate"), "title-4");
        _templates = Gtk.FlowBox.New();
        _templates.SetSelectionMode(Gtk.SelectionMode.None);
        _templates.SetHomogeneous(true);
        _templates.SetMaxChildrenPerLine(8);
        _templates.SetMinChildrenPerLine(2);
        _templates.SetColumnSpacing(4);
        _templates.SetRowSpacing(4);
        _binder.Collection(viewModel.Templates, RebuildTemplates);

        Gtk.Label recentsTitle = Ui.Label(L.T("Xaml.RecentProjects"), "title-4");
        recentsTitle.SetHexpand(true);
        Gtk.Button clear = Ui.Button(L.T("Xaml.Clear"), "flat");
        _binder.Command(clear, viewModel.ClearRecentsCommand);
        Gtk.Box recentsHeader = Ui.HBox(8, recentsTitle, clear);
        _recents = Gtk.FlowBox.New();
        _recents.SetSelectionMode(Gtk.SelectionMode.None);
        _recents.SetHomogeneous(true);
        _recents.SetMinChildrenPerLine(1);
        _recents.SetMaxChildrenPerLine(3);
        _recents.SetColumnSpacing(8);
        _recents.SetRowSpacing(8);
        Gtk.Box recentsSection = Ui.VBox(12, recentsHeader, _recents);
        recentsSection.SetMarginTop(24);
        _binder.Visible(recentsSection, viewModel, nameof(HomeViewModel.HasRecents), () => viewModel.HasRecents);
        _binder.Collection(viewModel.Recents, RebuildRecents);

        Gtk.Box content = Ui.VBox(0, title, subtitle.Margins(8, 0, 0, 0), actionCard.Margins(20, 0, 0, 0),
            templatesTitle.Margins(32, 0, 8, 0), _templates, recentsSection);
        _scroller = Ui.Page(content);
        Root = _scroller;
        _ = window;
    }

    public Gtk.Widget Root { get; }

    private void RebuildTemplates()
    {
        _templates.RemoveAllChildren();
        foreach (HomeTemplateTile tile in _viewModel.Templates)
        {
            Gtk.Image icon = Gtk.Image.NewFromIconName(Icons.ForLucide(tile.Icon));
            icon.SetPixelSize(32);
            icon.AddCssClass("dim-label");
            Gtk.Label name = Gtk.Label.New(tile.Title);
            name.SetWrap(true);
            name.SetJustify(Gtk.Justification.Center);
            name.SetMaxWidthChars(16);
            name.SetLines(2);
            name.SetEllipsize(Pango.EllipsizeMode.End);
            Gtk.Label detail = Ui.Line(tile.Detail, "caption", "dim-label");
            detail.SetXalign(0.5f);
            Gtk.Box body = Ui.VBox(8, icon, name, detail);
            body.SetValign(Gtk.Align.Start);
            name.SetSizeRequest(-1, 40);
            name.SetValign(Gtk.Align.Start);
            Gtk.Overlay overlay = Gtk.Overlay.New();
            overlay.SetChild(body);
            if (tile.HasContributor)
            {
                Gtk.Image sprout = Gtk.Image.NewFromIconName("emblem-favorite-symbolic");
                sprout.AddCssClass("accent");
                sprout.SetPixelSize(12);
                sprout.Align(Gtk.Align.Start, Gtk.Align.Start);
                sprout.SetTooltipText(tile.ContributorToolTip);
                overlay.AddOverlay(sprout);
            }

            Gtk.Button button = Gtk.Button.New();
            button.SetChild(overlay);
            button.AddCssClass("flat");
            button.AddCssClass("template-tile");
            button.SetTooltipText(tile.ToolTipText);
            HomeTemplateTile captured = tile;
            _binder.Command(button, _viewModel.TemplateCommand, () => captured);
            _templates.Append(button);
        }
    }

    private void RebuildRecents()
    {
        _recents.RemoveAllChildren();
        foreach (RecentProjectTile tile in _viewModel.Recents)
        {
            _recents.Append(CreateRecentCard(tile));
        }
    }

    private Gtk.Widget CreateRecentCard(RecentProjectTile tile)
    {
        Gtk.Label name = Ui.Line(tile.Title, "heading");
        Gtk.Image folder = Gtk.Image.NewFromIconName("folder-symbolic");
        folder.AddCssClass("dim-label");
        Gtk.Label location = Ui.Line(tile.LocationText, "caption", "dim-label");
        Gtk.Box meta = Ui.HBox(6, Ui.Badge(tile.FamilyLabel), folder, location);
        Gtk.Box text = Ui.VBox(6, name, meta);
        text.SetValign(Gtk.Align.Center);
        text.SetHexpand(true);

        DiscThumbnail disc = new(72);
        Adw.Spinner spinner = Adw.Spinner.New();
        spinner.SetSizeRequest(24, 24);
        Gtk.Stack thumbnailStack = Gtk.Stack.New();
        thumbnailStack.AddNamed(disc.Root, "disc");
        thumbnailStack.AddNamed(spinner, "loading");
        void Refresh()
        {
            thumbnailStack.SetVisibleChildName(tile.IsLoading ? "loading" : "disc");
            disc.SetTexture(tile.Thumbnail is { } thumbnail ? TextureFactory.FromBitmap(thumbnail) : null);
        }

        _binder.Watch(tile, [nameof(RecentProjectTile.IsLoading), nameof(RecentProjectTile.Thumbnail)], Refresh);

        Gtk.Box body = Ui.HBox(12, text, thumbnailStack);
        Gtk.Button card = Gtk.Button.New();
        card.SetChild(body);
        card.AddCssClass("card");
        card.AddCssClass("recent-card");
        card.SetTooltipText(tile.ToolTipText);
        _binder.Command(card, _viewModel.OpenRecentCommand, () => tile);

        // Right-click (or long-press) offers "Remove from list", as the WPF context menu does.
        Gio.Menu menu = Gio.Menu.New();
        menu.Append(L.T("Xaml.RemoveFromList"), "recent.remove");
        Gtk.PopoverMenu popover = Gtk.PopoverMenu.NewFromModel(menu);
        popover.SetParent(card);
        popover.SetHasArrow(false);
        Gio.SimpleActionGroup group = Gio.SimpleActionGroup.New();
        Gio.SimpleAction remove = Gio.SimpleAction.New("remove", null);
        remove.OnActivate += (_, _) =>
        {
            if (_viewModel.RemoveRecentCommand.CanExecute(tile)) _viewModel.RemoveRecentCommand.Execute(tile);
        };
        group.AddAction(remove);
        card.InsertActionGroup("recent", group);
        Gtk.GestureClick secondary = Gtk.GestureClick.New();
        secondary.SetButton(3);
        secondary.OnPressed += (_, args) =>
        {
            Gdk.Rectangle rectangle = new() { X = (int)args.X, Y = (int)args.Y, Width = 1, Height = 1 };
            popover.SetPointingTo(rectangle);
            popover.Popup();
        };
        card.AddController(secondary);
        card.OnDestroy += (_, _) => popover.Unparent();
        return card;
    }

    public void Dispose() => _binder.Dispose();
}
