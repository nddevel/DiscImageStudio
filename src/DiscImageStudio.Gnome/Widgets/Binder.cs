using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Minimal one-way/two-way binding between the shared view models and GTK widgets.
/// Every subscription is recorded so a page can drop all of them at once when it is rebuilt
/// (for example after a language switch). Notifications raised on worker threads are
/// marshalled to the GTK main loop, as WPF bindings do.</summary>
internal sealed class Binder : IDisposable
{
    private readonly List<Action> _disposers = [];
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Action dispose in _disposers) dispose();
        _disposers.Clear();
    }

    /// <summary>Runs <paramref name="apply"/> now and whenever one of the properties changes.
    /// An empty name list reacts to every notification.</summary>
    public void Watch(INotifyPropertyChanged source, string[] properties, Action apply)
    {
        ArgumentNullException.ThrowIfNull(source);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (properties.Length != 0 && !string.IsNullOrEmpty(args.PropertyName)
                && Array.IndexOf(properties, args.PropertyName) < 0) return;
            OnMain(apply);
        };
        source.PropertyChanged += handler;
        _disposers.Add(() => source.PropertyChanged -= handler);
        apply();
    }

    public void Watch(INotifyPropertyChanged source, string property, Action apply) => Watch(source, [property], apply);

    public void Collection(INotifyCollectionChanged collection, Action rebuild)
    {
        NotifyCollectionChangedEventHandler handler = (_, _) => OnMain(rebuild);
        collection.CollectionChanged += handler;
        _disposers.Add(() => collection.CollectionChanged -= handler);
        rebuild();
    }

    public void Event(Action subscribe, Action unsubscribe)
    {
        subscribe();
        _disposers.Add(unsubscribe);
    }

    public void Language(Action apply)
    {
        EventHandler handler = (_, _) => OnMain(apply);
        Localization.L.LanguageChanged += handler;
        _disposers.Add(() => Localization.L.LanguageChanged -= handler);
        apply();
    }

    public void OnDispose(Action action) => _disposers.Add(action);

    // ---- One-way helpers ----

    public void Text(Gtk.Label label, INotifyPropertyChanged source, string property, Func<string?> get) =>
        Watch(source, property, () => label.SetLabel(get() ?? string.Empty));

    public void Visible(Gtk.Widget widget, INotifyPropertyChanged source, string property, Func<bool> get) =>
        Watch(source, property, () => widget.SetVisible(get()));

    public void Visible(Gtk.Widget widget, INotifyPropertyChanged source, string[] properties, Func<bool> get) =>
        Watch(source, properties, () => widget.SetVisible(get()));

    public void Sensitive(Gtk.Widget widget, INotifyPropertyChanged source, string property, Func<bool> get) =>
        Watch(source, property, () => widget.SetSensitive(get()));

    public void Sensitive(Gtk.Widget widget, INotifyPropertyChanged source, string[] properties, Func<bool> get) =>
        Watch(source, properties, () => widget.SetSensitive(get()));

    public void CssClass(Gtk.Widget widget, string cssClass, INotifyPropertyChanged source, string property, Func<bool> get) =>
        Watch(source, property, () =>
        {
            if (get()) widget.AddCssClass(cssClass);
            else widget.RemoveCssClass(cssClass);
        });

    /// <summary>Wires a button to a command: clicks execute it, CanExecute drives sensitivity.</summary>
    public void Command(Gtk.Button button, ICommand? command, Func<object?>? parameter = null)
    {
        if (command is null)
        {
            button.SetSensitive(false);
            return;
        }

        void Refresh() => button.SetSensitive(command.CanExecute(parameter?.Invoke()));
        EventHandler changed = (_, _) => OnMain(Refresh);
        command.CanExecuteChanged += changed;
        _disposers.Add(() => command.CanExecuteChanged -= changed);
        button.OnClicked += (_, _) =>
        {
            object? argument = parameter?.Invoke();
            if (command.CanExecute(argument)) command.Execute(argument);
        };
        Refresh();
    }

    /// <summary>Command used by a row or other activatable widget without its own click signal.</summary>
    public void Command(Adw.ActionRow row, ICommand command, Func<object?>? parameter = null)
    {
        void Refresh() => row.SetSensitive(command.CanExecute(parameter?.Invoke()));
        EventHandler changed = (_, _) => OnMain(Refresh);
        command.CanExecuteChanged += changed;
        _disposers.Add(() => command.CanExecuteChanged -= changed);
        row.SetActivatable(true);
        row.OnActivated += (_, _) =>
        {
            object? argument = parameter?.Invoke();
            if (command.CanExecute(argument)) command.Execute(argument);
        };
        Refresh();
    }

    // ---- Two-way helpers ----

    /// <summary>Two-way text binding with WPF's UpdateSourceTrigger=PropertyChanged behaviour:
    /// every edit is pushed to the view model, and model changes refresh the field unless the
    /// field itself caused them (so the caret never jumps while typing).</summary>
    public void Entry(Gtk.Editable editable, INotifyPropertyChanged source, string property,
        Func<string?> get, Action<string> set)
    {
        bool pushing = false;
        Watch(source, property, () =>
        {
            if (pushing) return;
            string value = get() ?? string.Empty;
            if (editable.GetText() != value) editable.SetText(value);
        });
        editable.OnChanged += (_, _) =>
        {
            if (pushing) return;
            string text = editable.GetText();
            if (text == (get() ?? string.Empty)) return;
            pushing = true;
            try
            {
                set(text);
            }
            finally
            {
                pushing = false;
            }
        };
    }

    /// <summary>Two-way binding of a double to a text field, formatted and parsed with the
    /// invariant culture (WPF binding's default en-US culture). Unparsable text is left in the
    /// field without touching the model, like a WPF validation error.</summary>
    public void NumberEntry(Gtk.Editable editable, INotifyPropertyChanged source, string property,
        Func<double> get, Action<double> set, string format = "G")
    {
        bool pushing = false;
        Watch(source, property, () =>
        {
            if (pushing) return;
            string value = get().ToString(format, CultureInfo.InvariantCulture);
            if (!TryParse(editable.GetText(), out double current) || !current.Equals(get()))
                editable.SetText(value);
        });
        editable.OnChanged += (_, _) =>
        {
            if (pushing || !TryParse(editable.GetText(), out double parsed) || parsed.Equals(get())) return;
            pushing = true;
            try
            {
                set(parsed);
            }
            finally
            {
                pushing = false;
            }
        };
    }

    internal static bool TryParse(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    public void Switch(Adw.SwitchRow row, INotifyPropertyChanged source, string property, Func<bool> get, Action<bool> set)
    {
        bool pushing = false;
        Watch(source, property, () =>
        {
            if (!pushing && row.GetActive() != get()) row.SetActive(get());
        });
        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "active" || pushing || row.GetActive() == get()) return;
            pushing = true;
            try
            {
                set(row.GetActive());
            }
            finally
            {
                pushing = false;
            }

            if (row.GetActive() != get()) row.SetActive(get());
        };
    }

    public void Check(Gtk.CheckButton check, INotifyPropertyChanged source, string property, Func<bool> get, Action<bool> set)
    {
        bool pushing = false;
        Watch(source, property, () =>
        {
            if (!pushing && check.GetActive() != get()) check.SetActive(get());
        });
        check.OnToggled += (_, _) =>
        {
            if (pushing || check.GetActive() == get()) return;
            pushing = true;
            try
            {
                set(check.GetActive());
            }
            finally
            {
                pushing = false;
            }

            if (check.GetActive() != get()) check.SetActive(get());
        };
    }

    /// <summary>Binds a two-choice toggle group (WPF SegmentedControl) to a boolean that is
    /// true when the second option is selected.</summary>
    public void Segmented(Adw.ToggleGroup group, INotifyPropertyChanged source, string property,
        Func<bool> isSecond, Action<bool> setSecond)
    {
        bool pushing = false;
        Watch(source, property, () =>
        {
            if (pushing) return;
            uint wanted = isSecond() ? 1u : 0u;
            if (group.GetActive() != wanted) group.SetActive(wanted);
        });
        group.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "active" || pushing) return;
            bool second = group.GetActive() == 1;
            if (second == isSecond()) return;
            pushing = true;
            try
            {
                setSecond(second);
            }
            finally
            {
                pushing = false;
            }

            uint actual = isSecond() ? 1u : 0u;
            if (group.GetActive() != actual) group.SetActive(actual);
        };
    }

    public void Scale(Gtk.Scale scale, INotifyPropertyChanged source, string property, Func<double> get, Action<double> set)
    {
        bool pushing = false;
        Watch(source, property, () =>
        {
            if (!pushing && Math.Abs(scale.GetValue() - get()) > 1e-9) scale.SetValue(get());
        });
        scale.OnValueChanged += (_, _) =>
        {
            if (pushing || Math.Abs(scale.GetValue() - get()) <= 1e-9) return;
            pushing = true;
            try
            {
                set(scale.GetValue());
            }
            finally
            {
                pushing = false;
            }
        };
    }

    /// <summary>Binds a combo row to a list of items and a selected item.</summary>
    public void Combo<T>(Adw.ComboRow row, INotifyPropertyChanged source, string itemsProperty, Func<IReadOnlyList<T>> items,
        Func<T, string> display, string selectedProperty, Func<T?> getSelected, Action<T?> setSelected)
        where T : class
    {
        IReadOnlyList<T> current = [];
        bool pushing = false;

        void SyncSelection()
        {
            T? selected = getSelected();
            int index = -1;
            for (int i = 0; i < current.Count; i++)
            {
                if (ReferenceEquals(current[i], selected) || Equals(current[i], selected))
                {
                    index = i;
                    break;
                }
            }

            uint wanted = index < 0 ? Gtk.Constants.INVALID_LIST_POSITION : (uint)index;
            if (row.GetSelected() != wanted)
            {
                pushing = true;
                try
                {
                    row.SetSelected(wanted);
                }
                finally
                {
                    pushing = false;
                }
            }
        }

        Watch(source, itemsProperty, () =>
        {
            current = items().ToArray();
            pushing = true;
            try
            {
                row.SetModel(Gtk.StringList.New(current.Select(display).ToArray()));
            }
            finally
            {
                pushing = false;
            }

            SyncSelection();
        });
        if (selectedProperty != itemsProperty) Watch(source, selectedProperty, SyncSelection);
        row.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected" || pushing) return;
            uint index = row.GetSelected();
            T? item = index < current.Count ? current[(int)index] : null;
            if (ReferenceEquals(item, getSelected())) return;
            pushing = true;
            try
            {
                setSelected(item);
            }
            finally
            {
                pushing = false;
            }

            SyncSelection();
        };
    }

    /// <summary>Gtk.DropDown flavour of <see cref="Combo{T}"/>, for compact inline pickers.</summary>
    public void DropDown<T>(Gtk.DropDown dropDown, INotifyPropertyChanged source, string itemsProperty, Func<IReadOnlyList<T>> items,
        Func<T, string> display, string selectedProperty, Func<T?> getSelected, Action<T?> setSelected)
        where T : class
    {
        IReadOnlyList<T> current = [];
        bool pushing = false;

        void SyncSelection()
        {
            T? selected = getSelected();
            int index = -1;
            for (int i = 0; i < current.Count; i++)
            {
                if (ReferenceEquals(current[i], selected) || Equals(current[i], selected))
                {
                    index = i;
                    break;
                }
            }

            uint wanted = index < 0 ? Gtk.Constants.INVALID_LIST_POSITION : (uint)index;
            if (dropDown.GetSelected() == wanted) return;
            pushing = true;
            try
            {
                dropDown.SetSelected(wanted);
            }
            finally
            {
                pushing = false;
            }
        }

        Watch(source, itemsProperty, () =>
        {
            IReadOnlyList<T> next = items().ToArray();
            string[] labels = next.Select(display).ToArray();
            bool same = next.Count == current.Count && next.Select(display).SequenceEqual(current.Select(display));
            current = next;
            if (!same)
            {
                pushing = true;
                try
                {
                    dropDown.SetModel(Gtk.StringList.New(labels));
                }
                finally
                {
                    pushing = false;
                }
            }

            SyncSelection();
        });
        if (selectedProperty != itemsProperty) Watch(source, selectedProperty, SyncSelection);
        dropDown.OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() != "selected" || pushing) return;
            uint index = dropDown.GetSelected();
            T? item = index < current.Count ? current[(int)index] : null;
            if (item is null || ReferenceEquals(item, getSelected())) return;
            pushing = true;
            try
            {
                setSelected(item);
            }
            finally
            {
                pushing = false;
            }

            SyncSelection();
        };
    }

    private static void OnMain(Action action)
    {
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess()) action();
        else Dispatcher.Post(action);
    }
}
