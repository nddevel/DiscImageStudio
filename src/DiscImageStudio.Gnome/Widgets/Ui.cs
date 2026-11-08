using DiscImageStudio.Localization;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Small widget factories shared by the pages. They mirror the WPF app's reusable
/// controls (section cards, path fields, badges) using libadwaita styles.</summary>
internal static class Ui
{
    public static string T(string key) => L.T(key);

    public static Gtk.Label Label(string text, params string[] cssClasses)
    {
        Gtk.Label label = Gtk.Label.New(text);
        label.SetXalign(0);
        label.SetWrap(true);
        label.SetWrapMode(Pango.WrapMode.WordChar);
        foreach (string cssClass in cssClasses) label.AddCssClass(cssClass);
        return label;
    }

    /// <summary>Single-line label that ellipsizes instead of wrapping.</summary>
    public static Gtk.Label Line(string text, params string[] cssClasses)
    {
        Gtk.Label label = Gtk.Label.New(text);
        label.SetXalign(0);
        label.SetEllipsize(Pango.EllipsizeMode.End);
        label.SetSingleLineMode(true);
        foreach (string cssClass in cssClasses) label.AddCssClass(cssClass);
        return label;
    }

    public static Gtk.Label Hint(string text)
    {
        Gtk.Label label = Label(text, "dim-label", "caption");
        return label;
    }

    public static Gtk.Box VBox(int spacing = 0, params Gtk.Widget[] children) => Box(Gtk.Orientation.Vertical, spacing, children);

    public static Gtk.Box HBox(int spacing = 0, params Gtk.Widget[] children) => Box(Gtk.Orientation.Horizontal, spacing, children);

    private static Gtk.Box Box(Gtk.Orientation orientation, int spacing, Gtk.Widget[] children)
    {
        Gtk.Box box = Gtk.Box.New(orientation, spacing);
        foreach (Gtk.Widget child in children) box.Append(child);
        return box;
    }

    public static Gtk.Button Button(string label, params string[] cssClasses)
    {
        Gtk.Button button = Gtk.Button.NewWithLabel(label);
        foreach (string cssClass in cssClasses) button.AddCssClass(cssClass);
        return button;
    }

    public static Gtk.Button IconButton(string iconName, string tooltip, params string[] cssClasses)
    {
        Gtk.Button button = Gtk.Button.NewFromIconName(iconName);
        button.SetTooltipText(tooltip);
        foreach (string cssClass in cssClasses) button.AddCssClass(cssClass);
        return button;
    }

    public static Gtk.Button IconLabelButton(string iconName, string label, params string[] cssClasses)
    {
        Adw.ButtonContent content = Adw.ButtonContent.New();
        content.SetIconName(iconName);
        content.SetLabel(label);
        content.SetUseUnderline(false);
        Gtk.Button button = Gtk.Button.New();
        button.SetChild(content);
        foreach (string cssClass in cssClasses) button.AddCssClass(cssClass);
        return button;
    }

    /// <summary>Rounded tag such as the CD/DVD/BD family marker.</summary>
    public static Gtk.Label Badge(string text)
    {
        Gtk.Label badge = Gtk.Label.New(text);
        badge.AddCssClass("badge");
        badge.SetValign(Gtk.Align.Center);
        badge.SetHalign(Gtk.Align.Start);
        return badge;
    }

    /// <summary>A card with an icon + title header, the WPF SectionCard.</summary>
    public static Gtk.Box SectionCard(string title, string? iconName, Gtk.Widget content, out Gtk.Label titleLabel)
    {
        Gtk.Box card = VBox(12);
        card.AddCssClass("card");
        card.AddCssClass("section-card");
        Gtk.Box header = HBox(8);
        if (iconName is not null)
        {
            Gtk.Image icon = Gtk.Image.NewFromIconName(iconName);
            icon.AddCssClass("dim-label");
            header.Append(icon);
        }

        titleLabel = Line(title, "heading");
        header.Append(titleLabel);
        card.Append(header);
        card.Append(content);
        return card;
    }

    public static Gtk.Box SectionCard(string title, string? iconName, Gtk.Widget content) =>
        SectionCard(title, iconName, content, out _);

    /// <summary>Field caption above an input, like the WPF FieldLabel style.</summary>
    public static Gtk.Box Field(string caption, Gtk.Widget input, out Gtk.Label captionLabel)
    {
        captionLabel = Line(caption, "caption-heading", "dim-label");
        return VBox(6, captionLabel, input);
    }

    public static Gtk.Box Field(string caption, Gtk.Widget input) => Field(caption, input, out _);

    public static Gtk.Entry Entry(int maxLength = 0, string? placeholder = null)
    {
        Gtk.Entry entry = Gtk.Entry.New();
        if (maxLength > 0) entry.SetMaxLength(maxLength);
        if (placeholder is not null) entry.SetPlaceholderText(placeholder);
        entry.SetHexpand(true);
        return entry;
    }

    /// <summary>Text field plus a browse button, the WPF PathField.</summary>
    public static Gtk.Box PathField(out Gtk.Entry entry, out Gtk.Button browse, string buttonLabel)
    {
        entry = Entry();
        browse = Button(buttonLabel);
        Gtk.Box row = HBox(8, entry, browse);
        return row;
    }

    public static Gtk.ScrolledWindow Scrolled(Gtk.Widget child, bool horizontal = false)
    {
        Gtk.ScrolledWindow scrolled = Gtk.ScrolledWindow.New();
        scrolled.SetPolicy(horizontal ? Gtk.PolicyType.Automatic : Gtk.PolicyType.Never, Gtk.PolicyType.Automatic);
        scrolled.SetChild(child);
        scrolled.SetVexpand(true);
        scrolled.SetHexpand(true);
        return scrolled;
    }

    /// <summary>Scrollable page column capped at a readable width, centred in wide windows.</summary>
    public static Gtk.ScrolledWindow Page(Gtk.Widget content, int maximumWidth = 1120)
    {
        Adw.Clamp clamp = Adw.Clamp.New();
        clamp.SetMaximumSize(maximumWidth);
        clamp.SetTighteningThreshold(maximumWidth);
        clamp.SetChild(content);
        content.AddCssClass("page-content");
        return Scrolled(clamp);
    }

    public static T Margins<T>(this T widget, int top, int end, int bottom, int start) where T : Gtk.Widget
    {
        widget.SetMarginTop(top);
        widget.SetMarginEnd(end);
        widget.SetMarginBottom(bottom);
        widget.SetMarginStart(start);
        return widget;
    }

    public static T Margins<T>(this T widget, int all) where T : Gtk.Widget => widget.Margins(all, all, all, all);

    public static T Css<T>(this T widget, params string[] cssClasses) where T : Gtk.Widget
    {
        foreach (string cssClass in cssClasses) widget.AddCssClass(cssClass);
        return widget;
    }

    public static T Expand<T>(this T widget, bool horizontal = true, bool vertical = false) where T : Gtk.Widget
    {
        widget.SetHexpand(horizontal);
        widget.SetVexpand(vertical);
        return widget;
    }

    public static T Align<T>(this T widget, Gtk.Align horizontal, Gtk.Align vertical = Gtk.Align.Fill) where T : Gtk.Widget
    {
        widget.SetHalign(horizontal);
        widget.SetValign(vertical);
        return widget;
    }

    public static void RemoveAllChildren(this Gtk.Box box)
    {
        while (box.GetFirstChild() is { } child) box.Remove(child);
    }

    public static void RemoveAllChildren(this Gtk.FlowBox box)
    {
        box.RemoveAll();
    }

    public static void RemoveAllChildren(this Gtk.ListBox box)
    {
        box.RemoveAll();
    }

    /// <summary>Two-choice segmented control (WPF SegmentedControl) built on Adw.ToggleGroup.</summary>
    public static Adw.ToggleGroup Segmented(string first, string second)
    {
        Adw.ToggleGroup group = Adw.ToggleGroup.New();
        Adw.Toggle a = Adw.Toggle.New();
        a.SetLabel(first);
        Adw.Toggle b = Adw.Toggle.New();
        b.SetLabel(second);
        group.Add(a);
        group.Add(b);
        group.SetHomogeneous(true);
        return group;
    }

    /// <summary>Opens a file or folder with the desktop's default handler.</summary>
    public static void OpenWithSystem(Gtk.Window? parent, string path)
    {
        Gio.File file = Gio.FileHelper.NewForPath(path);
        Gtk.FileLauncher launcher = Gtk.FileLauncher.New(file);
        _ = launcher.LaunchAsync(parent);
    }
}
