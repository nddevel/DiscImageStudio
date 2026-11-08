using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using DiscImageStudio.Gnome.Imaging;
using DiscImageStudio.Localization;
using DiscImageStudio.ViewModels;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>The design page's interactive disc view, ported from the WPF DiscDesignCanvas:
/// layers drawn in millimetre space on the disc, a dimming mask outside the content ring,
/// the data-region guide, and move / corner-scale / rotate handles for the selected layer.
/// Ctrl+scroll zooms around the pointer, middle-drag pans, double-click on empty space fits.</summary>
internal sealed class DiscDesignCanvas : IDisposable
{
    private const double ViewMarginMm = 3.0;
    private const double HandleSize = 8.0;
    private const double RotateHandleDistance = 26.0;
    private const double MinScale = 0.05;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 12.0;
    private const double ZoomStep = 1.25;
    private const double WheelZoomStep = 1.15;

    private readonly DesignViewModel _design;
    private readonly Gtk.DrawingArea _area;
    private readonly Dictionary<DesignLayerViewModel, (object? Image, Cairo.ImageSurface Surface)> _surfaces = [];
    private DragMode _dragMode;
    private Point _dragStartMm;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;
    private double _dragStartScale = 1.0;
    private double _dragStartDistanceMm = 1.0;
    private double _dragStartRotationDeg;
    private double _dragStartMouseAngleDeg;
    private bool _isPanning;
    private (double X, double Y) _panOffset;
    private (double X, double Y) _panStartOffset;
    private double _zoom = 1.0;
    private bool _shiftDown;

    private enum DragMode
    {
        None,
        Move,
        Scale,
        Rotate,
    }

    public DiscDesignCanvas(DesignViewModel design)
    {
        _design = design;
        _area = Gtk.DrawingArea.New();
        _area.SetHexpand(true);
        _area.SetVexpand(true);
        _area.SetFocusable(true);
        _area.SetDrawFunc(Draw);
        _area.SetSizeRequest(320, 320);

        design.CanvasChanged += QueueDraw;
        design.PropertyChanged += OnDesignPropertyChanged;
        design.Layers.CollectionChanged += OnLayersChanged;
        foreach (DesignLayerViewModel layer in design.Layers) layer.PropertyChanged += OnLayerPropertyChanged;
        Adw.StyleManager.GetDefault().OnNotify += OnStyleChanged;

        Gtk.GestureClick click = Gtk.GestureClick.New();
        click.SetButton(1);
        click.OnPressed += (_, args) => OnPrimaryPressed(args.NPress, args.X, args.Y);
        click.OnReleased += (_, _) => EndDrag();
        _area.AddController(click);

        Gtk.GestureDrag drag = Gtk.GestureDrag.New();
        drag.SetButton(0);
        drag.OnDragBegin += (_, args) => OnDragBegin(drag, args.StartX, args.StartY);
        drag.OnDragUpdate += (_, args) => OnDragUpdate(drag, args.OffsetX, args.OffsetY);
        drag.OnDragEnd += (_, _) => EndDrag();
        _area.AddController(drag);

        Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New();
        motion.OnMotion += (_, args) => UpdateHoverCursor(args.X, args.Y);
        _area.AddController(motion);

        Gtk.EventControllerScroll scroll = Gtk.EventControllerScroll.New(Gtk.EventControllerScrollFlags.Vertical);
        scroll.OnScroll += (controller, args) =>
        {
            Gdk.ModifierType state = controller.GetCurrentEventState();
            if ((state & (Gdk.ModifierType.ControlMask | Gdk.ModifierType.AltMask)) == 0) return false;
            (double x, double y) = _pointer;
            ZoomAt(args.Dy < 0 ? WheelZoomStep : 1.0 / WheelZoomStep, new Point(x, y));
            return true;
        };
        _area.AddController(scroll);
        motion.OnMotion += (_, args) => _pointer = (args.X, args.Y);

        Gtk.EventControllerKey keys = Gtk.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) =>
        {
            _shiftDown = (args.State & Gdk.ModifierType.ShiftMask) != 0
                         || args.Keyval is Gdk.Constants.KEY_Shift_L or Gdk.Constants.KEY_Shift_R;
            if (args.Keyval is Gdk.Constants.KEY_Delete or Gdk.Constants.KEY_KP_Delete
                && _design.RemoveSelectedCommand.CanExecute(null))
            {
                _design.RemoveSelectedCommand.Execute(null);
                return true;
            }

            return false;
        };
        keys.OnKeyReleased += (_, args) => _shiftDown = (args.State & Gdk.ModifierType.ShiftMask) != 0
                                                       && args.Keyval is not (Gdk.Constants.KEY_Shift_L or Gdk.Constants.KEY_Shift_R);
        _area.AddController(keys);
    }

    private (double X, double Y) _pointer;

    public Gtk.Widget Root => _area;

    /// <summary>Raised when the zoom changes, with the new "NNN%" text.</summary>
    public event Action<string>? ZoomChanged;

    public string ZoomPercent => $"{Math.Round(_zoom * 100)}%";

    public void ResetView()
    {
        SetZoom(1.0);
        _panOffset = default;
        QueueDraw();
    }

    public void ZoomIn() => ZoomAt(ZoomStep, ViewCenter);

    public void ZoomOut() => ZoomAt(1.0 / ZoomStep, ViewCenter);

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ZoomChanged?.Invoke(ZoomPercent);
    }

    private void QueueDraw() => App.RunOnMain(_area.QueueDraw);

    private void OnStyleChanged(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        if (args.Pspec.GetName() == "dark") QueueDraw();
    }

    private void OnDesignPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueDraw();

    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DesignLayerViewModel layer in e.OldItems)
            {
                layer.PropertyChanged -= OnLayerPropertyChanged;
                DropSurface(layer);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DesignLayerViewModel layer in e.NewItems) layer.PropertyChanged += OnLayerPropertyChanged;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (DesignLayerViewModel stale in _surfaces.Keys.Where(layer => !_design.Layers.Contains(layer)).ToArray())
                DropSurface(stale);
            foreach (DesignLayerViewModel layer in _design.Layers)
            {
                layer.PropertyChanged -= OnLayerPropertyChanged;
                layer.PropertyChanged += OnLayerPropertyChanged;
            }
        }

        QueueDraw();
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueDraw();

    private void DropSurface(DesignLayerViewModel layer)
    {
        if (_surfaces.Remove(layer, out var entry)) entry.Surface.Dispose();
    }

    private Cairo.ImageSurface? SurfaceFor(DesignLayerViewModel layer)
    {
        if (layer.Image is null) return null;
        if (_surfaces.TryGetValue(layer, out var entry) && ReferenceEquals(entry.Image, layer.Image)) return entry.Surface;
        DropSurface(layer);
        Cairo.ImageSurface surface = CairoImages.FromBitmap(layer.Image);
        _surfaces[layer] = (layer.Image, surface);
        return surface;
    }

    // ---- Coordinate mapping ----

    private double Width => _area.GetWidth();

    private double Height => _area.GetHeight();

    private double FitScale
    {
        get
        {
            double radiusMm = DesignViewModel.DiscRadiusMm + ViewMarginMm;
            return Math.Min(Width / (2 * radiusMm), Height / (2 * radiusMm));
        }
    }

    private double ViewScale => FitScale * _zoom;

    private Point ViewCenterBase => new(Width / 2, Height / 2);

    private Point ViewCenter => new(ViewCenterBase.X + _panOffset.X, ViewCenterBase.Y + _panOffset.Y);

    private Point ScreenFromMm(double xMm, double yMm)
    {
        Point center = ViewCenter;
        return new Point(center.X + xMm * ViewScale, center.Y + yMm * ViewScale);
    }

    private Point MmFromScreen(Point point)
    {
        Point center = ViewCenter;
        return new Point((point.X - center.X) / ViewScale, (point.Y - center.Y) / ViewScale);
    }

    private double ScreenLength(double mm) => mm * ViewScale;

    private static double SizeWidthMm(DesignLayerViewModel layer) => layer.BaseWidthMm * layer.Scale;

    private static double SizeHeightMm(DesignLayerViewModel layer) => layer.BaseHeightMm * layer.Scale;

    /// <summary>The layer's four corners in mm space, in the order NW, NE, SE, SW.</summary>
    private static Point[] LayerCorners(DesignLayerViewModel layer)
    {
        double halfWidth = SizeWidthMm(layer) / 2;
        double halfHeight = SizeHeightMm(layer) / 2;
        double radians = layer.RotationDeg * Math.PI / 180.0;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        (double dx, double dy)[] locals = [(-halfWidth, -halfHeight), (halfWidth, -halfHeight), (halfWidth, halfHeight), (-halfWidth, halfHeight)];
        return locals
            .Select(local => new Point(
                layer.TranslateXMm + local.dx * cos - local.dy * sin,
                layer.TranslateYMm + local.dx * sin + local.dy * cos))
            .ToArray();
    }

    private static bool PointInPolygon(Point point, Point[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            bool crosses = (polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)
                && point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X;
            if (crosses) inside = !inside;
        }

        return inside;
    }

    // ---- Rendering ----

    private void Draw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
    {
        DiscPalette palette = DiscPalette.Current;
        palette.Backdrop.Apply(cr);
        cr.Paint();
        if (width <= 0 || height <= 0) return;

        double discRadius = ScreenLength(DesignViewModel.DiscRadiusMm);
        double hubRadius = ScreenLength(DesignViewModel.HubRadiusMm);
        double innerRadius = ScreenLength(_design.ContentInnerRadiusMm);
        double outerRadius = ScreenLength(Math.Min(_design.ContentOuterRadiusMm, DesignViewModel.DiscRadiusMm));
        Point center = ViewCenter;

        // Disc body and hub.
        palette.DiscBody.Apply(cr);
        cr.Arc(center.X, center.Y, discRadius, 0, 2 * Math.PI);
        cr.Fill();
        palette.Hub.Apply(cr);
        cr.Arc(center.X, center.Y, hubRadius, 0, 2 * Math.PI);
        cr.Fill();

        // Guide circles and origin cross.
        palette.Guide.Apply(cr);
        cr.LineWidth = 1;
        cr.Arc(center.X, center.Y, discRadius, 0, 2 * Math.PI);
        cr.Stroke();
        cr.Arc(center.X, center.Y, outerRadius, 0, 2 * Math.PI);
        cr.Stroke();
        if (innerRadius > 0)
        {
            cr.Arc(center.X, center.Y, innerRadius, 0, 2 * Math.PI);
            cr.Stroke();
        }

        cr.MoveTo(center.X - 5, center.Y + 0.5);
        cr.LineTo(center.X + 5, center.Y + 0.5);
        cr.MoveTo(center.X + 0.5, center.Y - 5);
        cr.LineTo(center.X + 0.5, center.Y + 5);
        cr.Stroke();

        // Bottom-up: index 0 is the topmost layer.
        for (int index = _design.Layers.Count - 1; index >= 0; index--) DrawLayer(cr, palette, _design.Layers[index]);

        // Dim everything outside the content ring.
        palette.Mask.Apply(cr);
        cr.FillRule = Cairo.FillRule.EvenOdd;
        cr.Rectangle(0, 0, width, height);
        cr.Arc(center.X, center.Y, outerRadius, 0, 2 * Math.PI);
        cr.NewSubPath();
        cr.Arc(center.X, center.Y, Math.Max(0, innerRadius), 0, 2 * Math.PI);
        cr.Fill();
        cr.FillRule = Cairo.FillRule.Winding;

        if (_design.SelectedLayer is { } selected) DrawSelection(cr, palette, selected);
        if (!_design.HasLayers) DrawEmptyHint(cr, palette, center);

        // Last, so no artwork or adorner hides the data-region guide.
        if (_design.DataRegions is { } regions)
        {
            palette.DataRegion.Apply(cr);
            cr.FillRule = Cairo.FillRule.EvenOdd;
            foreach (DesignViewModel.DataRegionMm region in regions)
            {
                double outer = ScreenLength(region.OuterRadiusMm), inner = ScreenLength(region.InnerRadiusMm);
                if (outer <= 0 || inner < 0) continue;
                cr.NewPath();
                cr.Arc(center.X, center.Y, outer, 0, 2 * Math.PI);
                cr.NewSubPath();
                cr.Arc(center.X, center.Y, inner, 0, 2 * Math.PI);
                cr.Fill();
            }

            cr.FillRule = Cairo.FillRule.Winding;
        }
    }

    private void DrawLayer(Cairo.Context cr, DiscPalette palette, DesignLayerViewModel layer)
    {
        double widthPx = ScreenLength(SizeWidthMm(layer));
        double heightPx = ScreenLength(SizeHeightMm(layer));
        if (widthPx <= 0 || heightPx <= 0) return;

        if (!layer.IsVisible || layer.Image is null)
        {
            // Hidden layers stay faintly visible so they can be re-selected; missing images
            // keep their footprint findable.
            (layer.IsVisible ? palette.MissingImage : palette.Guide).Apply(cr);
            cr.LineWidth = 1;
            cr.SetDash([4, 3], 0);
            TracePolygon(cr, LayerCorners(layer).Select(corner => ScreenFromMm(corner.X, corner.Y)).ToArray());
            cr.Stroke();
            cr.SetDash([], 0);
            return;
        }

        if (SurfaceFor(layer) is not { } surface) return;
        Point centerPx = ScreenFromMm(layer.TranslateXMm, layer.TranslateYMm);
        cr.Save();
        cr.Translate(centerPx.X, centerPx.Y);
        cr.Rotate(layer.RotationDeg * Math.PI / 180.0);
        cr.Translate(-widthPx / 2, -heightPx / 2);
        cr.Scale(widthPx / surface.Width, heightPx / surface.Height);
        cr.SetSourceSurface(surface, 0, 0);
        using (Cairo.Pattern pattern = cr.GetSource()) pattern.Filter = Cairo.Filter.Good;
        cr.Rectangle(0, 0, surface.Width, surface.Height);
        cr.Fill();
        cr.Restore();
    }

    private static void TracePolygon(Cairo.Context cr, Point[] points)
    {
        cr.NewPath();
        cr.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++) cr.LineTo(points[i].X, points[i].Y);
        cr.ClosePath();
    }

    private void DrawSelection(Cairo.Context cr, DiscPalette palette, DesignLayerViewModel layer)
    {
        Point[] cornersMm = LayerCorners(layer);
        Point[] corners = cornersMm.Select(corner => ScreenFromMm(corner.X, corner.Y)).ToArray();
        palette.Selection.Apply(cr);
        cr.LineWidth = 1.5;
        TracePolygon(cr, corners);
        cr.Stroke();

        foreach (Point corner in corners)
        {
            cr.Rectangle(corner.X - HandleSize / 2, corner.Y - HandleSize / 2, HandleSize, HandleSize);
            cr.SetSourceRgb(1, 1, 1);
            cr.FillPreserve();
            palette.Selection.Apply(cr);
            cr.Stroke();
        }

        if (RotateHandle(layer) is { } handle)
        {
            Point topMid = ScreenFromMm((cornersMm[0].X + cornersMm[1].X) / 2, (cornersMm[0].Y + cornersMm[1].Y) / 2);
            palette.Selection.Apply(cr);
            cr.MoveTo(topMid.X, topMid.Y);
            cr.LineTo(handle.X, handle.Y);
            cr.Stroke();
            cr.Arc(handle.X, handle.Y, HandleSize / 2, 0, 2 * Math.PI);
            cr.SetSourceRgb(1, 1, 1);
            cr.FillPreserve();
            palette.Selection.Apply(cr);
            cr.Stroke();
        }
    }

    private void DrawEmptyHint(Cairo.Context cr, DiscPalette palette, Point center)
    {
        Pango.Layout layout = _area.CreatePangoLayout(L.T("点击左侧「导入图片」开始设计"));
        layout.GetPixelSize(out int textWidth, out int textHeight);
        palette.Text.Apply(cr);
        cr.MoveTo(center.X - textWidth / 2.0, center.Y - textHeight / 2.0);
        PangoCairo.Functions.ShowLayout(cr, layout);
    }

    // ---- Hit testing ----

    private static bool Near(Point a, Point b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private int? HitScaleHandle(Point mouse)
    {
        if (_design.SelectedLayer is not { } layer) return null;
        Point[] cornersMm = LayerCorners(layer);
        for (int i = 0; i < cornersMm.Length; i++)
        {
            if (Near(mouse, ScreenFromMm(cornersMm[i].X, cornersMm[i].Y), HandleSize)) return i;
        }

        return null;
    }

    private Point? RotateHandle(DesignLayerViewModel layer)
    {
        Point[] cornersMm = LayerCorners(layer);
        double dx = (cornersMm[0].X + cornersMm[1].X) / 2 - layer.TranslateXMm;
        double dy = (cornersMm[0].Y + cornersMm[1].Y) / 2 - layer.TranslateYMm;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return null;
        double extensionMm = RotateHandleDistance / Math.Max(0.001, ViewScale);
        return ScreenFromMm(
            layer.TranslateXMm + dx / length * (length + extensionMm),
            layer.TranslateYMm + dy / length * (length + extensionMm));
    }

    private DesignLayerViewModel? HitTestLayer(Point mouseMm)
    {
        // Topmost first; visible layers win over hidden ones.
        DesignLayerViewModel? hidden = null;
        foreach (DesignLayerViewModel layer in _design.Layers)
        {
            if (!PointInPolygon(mouseMm, LayerCorners(layer))) continue;
            if (layer.IsVisible) return layer;
            hidden ??= layer;
        }

        return hidden;
    }

    // ---- Interaction ----

    private void ZoomAt(double factor, Point anchor)
    {
        double fit = FitScale;
        if (fit <= 0 || Width <= 0 || Height <= 0) return;
        Point anchorMm = MmFromScreen(anchor);
        SetZoom(_zoom * factor);
        _panOffset = _zoom > 1.0001
            ? ClampPan((anchor.X - ViewCenterBase.X - anchorMm.X * fit * _zoom, anchor.Y - ViewCenterBase.Y - anchorMm.Y * fit * _zoom))
            : default;
        QueueDraw();
    }

    private (double X, double Y) ClampPan((double X, double Y) pan)
    {
        double fit = FitScale;
        if (fit <= 0 || _zoom <= 1.0001) return default;
        double maxLength = fit * _zoom * DesignViewModel.DiscRadiusMm;
        double length = Math.Sqrt(pan.X * pan.X + pan.Y * pan.Y);
        return length > maxLength ? (pan.X * maxLength / length, pan.Y * maxLength / length) : pan;
    }

    private void OnPrimaryPressed(int presses, double x, double y)
    {
        _area.GrabFocus();
        Point mouse = new(x, y);
        if (presses == 2 && _design.SelectedLayer is null && HitScaleHandle(mouse) is null
            && HitTestLayer(MmFromScreen(mouse)) is null)
        {
            ResetView();
        }
    }

    private void OnDragBegin(Gtk.GestureDrag gesture, double x, double y)
    {
        uint button = gesture.GetCurrentButton();
        Point mouse = new(x, y);
        Point mouseMm = MmFromScreen(mouse);
        if (button == 2)
        {
            if (_zoom <= 1.0001) return;
            _isPanning = true;
            _panStartOffset = _panOffset;
            _area.SetCursorFromName("move");
            return;
        }

        if (button != 1) return;
        if (HitScaleHandle(mouse) is not null && _design.SelectedLayer is { } scaleLayer)
        {
            _dragMode = DragMode.Scale;
            _dragStartMm = mouseMm;
            _dragStartScale = scaleLayer.Scale;
            double dx = mouseMm.X - scaleLayer.TranslateXMm, dy = mouseMm.Y - scaleLayer.TranslateYMm;
            _dragStartDistanceMm = Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
            return;
        }

        if (_design.SelectedLayer is { } selected && RotateHandle(selected) is { } rotate && Near(mouse, rotate, HandleSize + 2))
        {
            _dragMode = DragMode.Rotate;
            _dragStartRotationDeg = selected.RotationDeg;
            _dragStartMouseAngleDeg = AngleFromCenter(selected, mouseMm);
            return;
        }

        if (HitTestLayer(mouseMm) is { } hit)
        {
            _design.SelectedLayer = hit;
            _dragMode = DragMode.Move;
            _dragStartMm = mouseMm;
            _dragStartTranslateX = hit.TranslateXMm;
            _dragStartTranslateY = hit.TranslateYMm;
            return;
        }

        _design.SelectedLayer = null;
        _dragMode = DragMode.None;
    }

    private void OnDragUpdate(Gtk.GestureDrag gesture, double offsetX, double offsetY)
    {
        gesture.GetStartPoint(out double startX, out double startY);
        if (_isPanning)
        {
            _panOffset = ClampPan((_panStartOffset.X + offsetX, _panStartOffset.Y + offsetY));
            QueueDraw();
            return;
        }

        if (_dragMode == DragMode.None || _design.SelectedLayer is not { } layer) return;
        Point mouseMm = MmFromScreen(new Point(startX + offsetX, startY + offsetY));
        switch (_dragMode)
        {
            case DragMode.Move:
                layer.TranslateXMm = _dragStartTranslateX + (mouseMm.X - _dragStartMm.X);
                layer.TranslateYMm = _dragStartTranslateY + (mouseMm.Y - _dragStartMm.Y);
                break;
            case DragMode.Scale:
            {
                double dx = mouseMm.X - layer.TranslateXMm, dy = mouseMm.Y - layer.TranslateYMm;
                layer.Scale = Math.Max(MinScale, _dragStartScale * Math.Sqrt(dx * dx + dy * dy) / _dragStartDistanceMm);
                break;
            }

            case DragMode.Rotate:
            {
                double delta = AngleFromCenter(layer, mouseMm) - _dragStartMouseAngleDeg;
                while (delta <= -180) delta += 360;
                while (delta > 180) delta -= 360;
                double angle = _dragStartRotationDeg + delta;
                if (_shiftDown || (gesture.GetCurrentEventState() & Gdk.ModifierType.ShiftMask) != 0)
                {
                    // Snap to right angles; 360 folds back to 0.
                    angle = Math.Round(angle / 90.0) * 90.0;
                    if (angle == 360) angle = 0;
                }

                layer.RotationDeg = Math.Round(angle, 1);
                break;
            }
        }
    }

    private void EndDrag()
    {
        _dragMode = DragMode.None;
        if (_isPanning)
        {
            _isPanning = false;
            _area.SetCursorFromName(null);
        }
    }

    private static double AngleFromCenter(DesignLayerViewModel layer, Point mouseMm) =>
        Math.Atan2(mouseMm.Y - layer.TranslateYMm, mouseMm.X - layer.TranslateXMm) * 180.0 / Math.PI;

    private void UpdateHoverCursor(double x, double y)
    {
        if (_isPanning || _dragMode != DragMode.None) return;
        Point mouse = new(x, y);
        bool onHandle = HitScaleHandle(mouse) is not null
                        || _design.SelectedLayer is { } selected && RotateHandle(selected) is { } rotate && Near(mouse, rotate, HandleSize + 2);
        _area.SetCursorFromName(onHandle ? "move" : null);
    }

    public void Dispose()
    {
        _design.CanvasChanged -= QueueDraw;
        _design.PropertyChanged -= OnDesignPropertyChanged;
        _design.Layers.CollectionChanged -= OnLayersChanged;
        foreach (DesignLayerViewModel layer in _design.Layers) layer.PropertyChanged -= OnLayerPropertyChanged;
        Adw.StyleManager.GetDefault().OnNotify -= OnStyleChanged;
        foreach ((object? _, Cairo.ImageSurface surface) in _surfaces.Values) surface.Dispose();
        _surfaces.Clear();
    }
}
