using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DiscImageStudio.Controls;
using DiscImageStudio.Core.Calibration;
using DiscImageStudio.Gnome.Imaging;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Gnome.Widgets;

/// <summary>Photo and pattern view of the calibration workspace, ported from the WPF
/// CalibrationPhotoCanvas. Boundaries on the raw photo use source pixels; the rectified
/// photo and all measurements use disc-centred millimetres with Y pointing down.
/// Scroll zooms, right/middle-drag pans; clicks and drags edit the active step's marks.</summary>
internal sealed class CalibrationPhotoCanvas
{
    private const double ArcHalfAngle = Math.PI / 6;
    private readonly Gtk.DrawingArea _area;
    private BitmapSource? _image;
    private BitmapSource? _fallbackImage;
    private Cairo.ImageSurface? _imageSurface;
    private Cairo.ImageSurface? _fallbackSurface;
    private double _zoom = 1;
    private (double X, double Y) _pan;
    private bool _panning;
    private (double X, double Y) _panBefore;
    private CalibrationPhotoBoundaryPoint? _boundaryDrag;
    private CalibrationTracePoint? _traceDrag;
    private CalibrationRingArc? _ringDrag;
    private double _ringDragRadius;
    private int? _selectedRing;
    private Point _dragStart;
    private Point _dragOriginal;
    private Point _dragPosition;
    private bool _hasMoved;
    private (int Arm, int Index)? _selectedTrace;
    private (int Arm, int Index)? _traceContinuation;
    private (int Arm, int Index)? _traceLastExtension;
    private Point? _traceHover;
    private (CalibrationPhotoBoundaryKind Boundary, int Index)? _selectedBoundary;
    private string _selectionDescription = string.Empty;

    public CalibrationPhotoCanvas()
    {
        _area = Gtk.DrawingArea.New();
        _area.SetHexpand(true);
        _area.SetVexpand(true);
        _area.SetFocusable(true);
        _area.SetSizeRequest(360, 360);
        _area.SetDrawFunc(Draw);
        _area.SetCursorFromName("crosshair");

        Gtk.GestureDrag primary = Gtk.GestureDrag.New();
        primary.SetButton(1);
        primary.OnDragBegin += (_, args) => OnPrimaryDown(new Point(args.StartX, args.StartY));
        primary.OnDragUpdate += (gesture, args) =>
        {
            gesture.GetStartPoint(out double x, out double y);
            if (Dragging) DragTo(new Point(x + args.OffsetX, y + args.OffsetY));
        };
        primary.OnDragEnd += (_, _) =>
        {
            if (Dragging) CompleteDrag();
        };
        _area.AddController(primary);

        Gtk.GestureDrag pan = Gtk.GestureDrag.New();
        pan.SetButton(0);
        pan.OnDragBegin += (gesture, _) =>
        {
            if (gesture.GetCurrentButton() is not (2 or 3)) return;
            _panning = true;
            _panBefore = _pan;
        };
        pan.OnDragUpdate += (_, args) =>
        {
            if (!_panning) return;
            _pan = (_panBefore.X + args.OffsetX, _panBefore.Y + args.OffsetY);
            _area.QueueDraw();
        };
        pan.OnDragEnd += (_, _) => _panning = false;
        _area.AddController(pan);

        Gtk.EventControllerMotion motion = Gtk.EventControllerMotion.New();
        motion.OnMotion += (_, args) =>
        {
            _traceHover = new Point(args.X, args.Y);
            if (IsBoundarySegmentMode && CanEditTrace) _area.QueueDraw();
        };
        motion.OnLeave += (_, _) =>
        {
            _traceHover = null;
            _area.QueueDraw();
        };
        _area.AddController(motion);

        Gtk.EventControllerScroll scroll = Gtk.EventControllerScroll.New(Gtk.EventControllerScrollFlags.Vertical);
        scroll.OnScroll += (_, args) =>
        {
            ZoomAt(args.Dy < 0 ? 1.2 : 1 / 1.2, _traceHover ?? new Point(Width / 2, Height / 2));
            return true;
        };
        _area.AddController(scroll);

        Gtk.EventControllerKey keys = Gtk.EventControllerKey.New();
        keys.OnKeyPressed += (_, args) => OnKey(args.Keyval);
        _area.AddController(keys);
        Adw.StyleManager.GetDefault().OnNotify += (_, args) =>
        {
            if (args.Pspec.GetName() == "dark") _area.QueueDraw();
        };
    }

    public Gtk.Widget Root => _area;

    public event Action? DescriptionsChanged;

    public string ViewDescription => $"{_zoom * 100:0}%";

    public string SelectionDescription => _selectionDescription;

    // ---- Inputs from the view model (set by the page) ----

    public BitmapSource? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            _imageSurface?.Dispose();
            _imageSurface = value is null ? null : CairoImages.FromBitmap(value);
            ViewportChanged(resetView: true);
        }
    }

    public BitmapSource? FallbackImage
    {
        get => _fallbackImage;
        set
        {
            if (ReferenceEquals(_fallbackImage, value)) return;
            _fallbackImage = value;
            _fallbackSurface?.Dispose();
            _fallbackSurface = value is null ? null : CairoImages.FromBitmap(value);
            _area.QueueDraw();
        }
    }

    public bool IsPhotoStep { get; set; } = true;
    public bool IsRectificationPreview { get; set; }
    public bool IsTargetPreview { get; set; }
    public double CanvasRadiusMm { get; set; } = 63;
    public double TargetCanvasRadiusMm { get; set; } = 60;
    public IEnumerable<CalibrationPhotoBoundaryPoint>? BoundaryPoints { get; set; }
    public IReadOnlyList<Point[]>? BoundaryContours { get; set; }
    public IEnumerable<CalibrationTracePoint>? TracePoints { get; set; }
    public IReadOnlyList<Point[]>? PredictionContours { get; set; }
    public IReadOnlyList<int>? OutlierTracePointIndices { get; set; }
    public int SelectedTraceArm { get; set; }
    public Action<int>? SelectedTraceArmChanged { get; set; }
    public bool IsBoundarySegmentMode { get; set; }
    public ICommand? BoundaryEditedCommand { get; set; }
    public ICommand? TraceEditedCommand { get; set; }
    public bool IsCoarseMode { get; set; }
    public bool HasRadialArcTarget { get; set; }
    public bool HasWideBandTarget { get; set; }
    public bool IsBandRayMode { get; set; }
    public double? BandRayAngleDegrees { get; set; }
    public ICommand? BandRayPointCommand { get; set; }
    public IReadOnlyList<CalibrationPoint>? ArcLengthPoints { get; set; }
    public ICommand? ArcLengthPointCommand { get; set; }
    public ICommand? UndoArcLengthPointCommand { get; set; }
    public bool HasClockTarget { get; set; }
    public IReadOnlyList<double>? ClockReadAngles { get; set; }
    public double? PreferredClockAngle { get; set; }
    public ICommand? PreferClockAngleCommand { get; set; }
    public IEnumerable<CalibrationRingArc>? RingArcs { get; set; }
    public bool ThreePointMode { get; set; }
    public int SelectedRingIndex { get; set; } = -1;
    public IReadOnlyList<CalibrationPoint>? RingPointMarkers { get; set; }
    public ICommand? RingPointCommand { get; set; }
    public ICommand? UndoRingPointCommand { get; set; }
    public ICommand? RingEditedCommand { get; set; }
    public double RingAngleDegrees { get; set; }
    public double PhysicalRadiusMm { get; set; } = 60;
    public bool ShowPrediction { get; set; } = true;

    /// <summary>Called after a batch of input changes. A changed step or mode drops the
    /// in-progress selection and (outside mode switches) resets the view, as WPF does.</summary>
    public void ViewportChanged(bool resetView)
    {
        CompleteDrag(false);
        _selectedTrace = null;
        _traceContinuation = null;
        _traceLastExtension = null;
        _selectedBoundary = null;
        _selectedRing = null;
        if (resetView) ResetView();
        SetDescription(DefaultSelectionDescription());
        _area.QueueDraw();
    }

    public void PointsChanged()
    {
        ValidateTraceSelection();
        if (CanEditTrace) UpdateTraceDescription();
        _area.QueueDraw();
    }

    public void SelectedArmChanged()
    {
        if (_selectedTrace?.Arm != SelectedTraceArm) _selectedTrace = null;
        _traceContinuation = null;
        _traceLastExtension = null;
        if (CanEditTrace) UpdateTraceDescription();
        _area.QueueDraw();
    }

    public void Redraw() => _area.QueueDraw();

    // ---- Modes ----

    private bool ShowingTarget => IsTargetPreview || Image is null;
    private bool ShowingRawPhoto => !ShowingTarget && IsPhotoStep && !IsRectificationPreview;
    private bool ShowingEditablePhoto => !ShowingTarget && !IsPhotoStep && !IsRectificationPreview;
    private bool CanEditTrace => ShowingEditablePhoto && !IsCoarseMode && !HasClockTarget && !HasRadialArcTarget && !HasWideBandTarget;
    private bool CanEditRings => ShowingEditablePhoto && IsCoarseMode && !HasClockTarget && !HasRadialArcTarget && !CanMarkBandRay;
    private bool CanMarkBandRay => ShowingEditablePhoto && IsCoarseMode && HasWideBandTarget && IsBandRayMode;
    private bool ShowingClockPhoto => ShowingEditablePhoto && IsCoarseMode && HasClockTarget;
    private bool CanMarkArcLength => ShowingEditablePhoto && IsCoarseMode && HasRadialArcTarget;
    private bool Dragging => _boundaryDrag is not null || _traceDrag is not null || _ringDrag is not null;

    private double Width => _area.GetWidth();

    private double Height => _area.GetHeight();

    private (double X, double Y, double W, double H) WorldBounds
    {
        get
        {
            if (ShowingRawPhoto && Image is { } photo) return (-.5, -.5, photo.PixelWidth, photo.PixelHeight);
            double radius = ShowingTarget ? TargetCanvasRadiusMm : CanvasRadiusMm;
            radius = double.IsFinite(radius) && radius > 0 ? radius : 63;
            return (-radius, -radius, radius * 2, radius * 2);
        }
    }

    private double Scale
    {
        get
        {
            var bounds = WorldBounds;
            return Math.Max(.0001, Math.Min(Width / bounds.W, Height / bounds.H) * .92 * _zoom);
        }
    }

    private Point WorldToView(Point point)
    {
        var bounds = WorldBounds;
        double scale = Scale;
        return new Point(
            (point.X - bounds.X - bounds.W / 2) * scale + Width / 2 + _pan.X,
            (point.Y - bounds.Y - bounds.H / 2) * scale + Height / 2 + _pan.Y);
    }

    private Point ViewToWorld(Point point)
    {
        var bounds = WorldBounds;
        double scale = Scale;
        return new Point(
            (point.X - Width / 2 - _pan.X) / scale + bounds.X + bounds.W / 2,
            (point.Y - Height / 2 - _pan.Y) / scale + bounds.Y + bounds.H / 2);
    }

    private bool WorldContains(Point point)
    {
        var bounds = WorldBounds;
        return point.X >= bounds.X && point.X <= bounds.X + bounds.W && point.Y >= bounds.Y && point.Y <= bounds.Y + bounds.H;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(DistanceSquared(a, b));

    private static double DistanceSquared(Point a, Point b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static double Length(Point vector) => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    public void ResetView()
    {
        _zoom = 1;
        _pan = default;
        DescriptionsChanged?.Invoke();
        _area.QueueDraw();
    }

    public void ZoomBy(double factor) => ZoomAt(factor, new Point(Width / 2, Height / 2));

    private void ZoomAt(double factor, Point screen)
    {
        if (!double.IsFinite(factor) || factor <= 0 || Dragging) return;
        Point anchor = ViewToWorld(screen);
        _zoom = Math.Clamp(_zoom * factor, .5, 30);
        Point moved = WorldToView(anchor);
        _pan = (_pan.X + screen.X - moved.X, _pan.Y + screen.Y - moved.Y);
        DescriptionsChanged?.Invoke();
        _area.QueueDraw();
    }

    private void SetDescription(string text)
    {
        _selectionDescription = text;
        DescriptionsChanged?.Invoke();
    }

    private string DefaultSelectionDescription() => IsPhotoStep
        ? L.T("滚轮缩放，右键平移；拖动控制点贴合边缘。")
        : HasRadialArcTarget ? L.T(IsCoarseMode ? "Arc.CanvasHint" : "Arc.FineHint")
        : HasClockTarget ? L.T(IsCoarseMode ? "Clock.DirectionHint" : "Clock.FineHint")
        : HasWideBandTarget ? L.T(!IsCoarseMode ? "WideBands.FineHint" : IsBandRayMode
            ? "BandEdges.RayHint" : ThreePointMode ? "BandEdges.PointHint" : "BandEdges.DragHint")
        : IsCoarseMode ? L.T(ThreePointMode ? "Xaml.CoarseRingPointStatus" : "Xaml.CoarseRingStatusHint")
        : IsBoundarySegmentMode ? L.Format("HalfDisc.CanvasHint", SelectedTraceArm + 1)
        : L.T("滚轮缩放，右键平移；点击添加点，拖动已有点修正位置。");

    // ---- Editing ----

    private void OnPrimaryDown(Point position)
    {
        _area.GrabFocus();
        if (CanMarkArcLength) AddArcLengthPointAt(position);
        else if (ShowingClockPhoto)
        {
            Point world = ViewToWorld(position);
            double radius = Length(world);
            if (radius >= 7.5 && radius <= PhysicalRadiusMm)
                Execute(PreferClockAngleCommand, (Math.Atan2(world.Y, world.X) * 180 / Math.PI + 360) % 360);
        }
        else if (CanMarkBandRay || CanEditRings && ThreePointMode) AddRingPointAt(position);
        else if (!BeginDragAt(position)) AddTraceAt(position);
    }

    private bool OnKey(uint keyval)
    {
        bool delete = keyval is Gdk.Constants.KEY_Delete or Gdk.Constants.KEY_KP_Delete;
        bool back = keyval == Gdk.Constants.KEY_BackSpace;
        if (CanMarkArcLength && (delete || back))
        {
            Execute(UndoArcLengthPointCommand, null);
            return true;
        }

        if (CanEditRings && ThreePointMode && (delete || back))
        {
            Execute(UndoRingPointCommand, null);
            return true;
        }

        if (delete)
        {
            if (CanEditRings) RemoveSelectedRing();
            else RemoveSelectedTracePoint();
            return true;
        }

        if (keyval == Gdk.Constants.KEY_Escape)
        {
            CompleteDrag(false);
            _traceContinuation = null;
            UpdateTraceDescription();
            _area.QueueDraw();
            return true;
        }

        return false;
    }

    private bool BeginDragAt(Point screen)
    {
        if (Dragging) return false;
        if (ShowingRawPhoto)
        {
            CalibrationPhotoBoundaryPoint? point = BoundaryPoints?
                .OrderBy(p => DistanceSquared(WorldToView(new Point(p.X, p.Y)), screen))
                .FirstOrDefault(p => Distance(WorldToView(new Point(p.X, p.Y)), screen) <= 11);
            if (point is null) return false;
            _boundaryDrag = point;
            _selectedBoundary = (point.Boundary, point.Index);
            _dragOriginal = new Point(point.X, point.Y);
            SetDescription(point.Boundary == CalibrationPhotoBoundaryKind.OuterEdge
                ? L.T("拖到盘片实体外缘；不要沿刻录区边界。") : L.T("拖到实体内缘；不要沿透明塑料环。"));
        }
        else if (CanEditTrace)
        {
            CalibrationTracePoint? point = TracePoints?
                .OrderBy(p => DistanceSquared(WorldToView(Position(p)), screen))
                .ThenBy(p => p.Arm == SelectedTraceArm ? 0 : 1)
                .FirstOrDefault(p => Distance(WorldToView(Position(p)), screen) <= 10);
            if (point is null) return false;
            _traceDrag = point;
            _selectedTrace = (point.Arm, point.Index);
            if (SelectedTraceArm != point.Arm)
            {
                SelectedTraceArm = point.Arm;
                SelectedTraceArmChanged?.Invoke(point.Arm);
            }

            _dragOriginal = Position(point);
            UpdateTraceDescription();
        }
        else if (CanEditRings && !ThreePointMode)
        {
            CalibrationRingArc? ring = HitRing(screen);
            if (ring is null || RingEditedCommand?.CanExecute(new CalibrationRingEdit(ring.RingIndex, ring.RadiusMm)) != true) return false;
            _ringDrag = ring;
            _ringDragRadius = ring.RadiusMm;
            _selectedRing = ring.RingIndex;
            UpdateRingDescription(ring.RingIndex, ring.RadiusMm);
        }
        else
        {
            return false;
        }

        _dragStart = ViewToWorld(screen);
        _dragPosition = _dragOriginal;
        _hasMoved = false;
        _area.QueueDraw();
        return true;
    }

    private void DragTo(Point screen)
    {
        if (!Dragging) return;
        Point world = ViewToWorld(screen);
        if (_ringDrag is { } ring)
        {
            double delta = Length(world) - Length(_dragStart);
            _ringDragRadius = Math.Clamp(ring.RadiusMm + delta, 7.5, PhysicalRadiusMm);
            UpdateRingDescription(ring.RingIndex, _ringDragRadius);
            _area.QueueDraw();
            return;
        }

        _dragPosition = new Point(_dragOriginal.X + world.X - _dragStart.X, _dragOriginal.Y + world.Y - _dragStart.Y);
        (double left, double top, double right, double bottom) = ShowingRawPhoto && Image is { } photo
            ? (0, 0, photo.PixelWidth - 1, photo.PixelHeight - 1)
            : (WorldBounds.X, WorldBounds.Y, WorldBounds.X + WorldBounds.W, WorldBounds.Y + WorldBounds.H);
        _dragPosition = new Point(Math.Clamp(_dragPosition.X, left, right), Math.Clamp(_dragPosition.Y, top, bottom));
        _hasMoved |= IsBoundarySegmentMode && _traceDrag is not null
            ? Distance(WorldToView(_dragPosition), WorldToView(_dragOriginal)) > 2
            : Distance(_dragPosition, _dragOriginal) > 1e-8;
        _area.QueueDraw();
    }

    private void CompleteDrag(bool commit = true)
    {
        CalibrationPhotoBoundaryPoint? boundary = _boundaryDrag;
        CalibrationTracePoint? trace = _traceDrag;
        CalibrationRingArc? ring = _ringDrag;
        Point position = _dragPosition;
        bool changed = _hasMoved && commit;
        _boundaryDrag = null;
        _traceDrag = null;
        _ringDrag = null;
        _hasMoved = false;
        if (changed && boundary is not null)
            Execute(BoundaryEditedCommand, new CalibrationBoundaryEdit(boundary.Boundary, boundary.Index, position.X, position.Y));
        if (changed && trace is not null)
        {
            Execute(TraceEditedCommand, new CalibrationTraceEdit(trace.Arm, trace.Index, position.X, position.Y));
            if (IsBoundarySegmentMode)
            {
                _selectedTrace = (trace.Arm, trace.Index);
                ValidateTraceSelection();
                UpdateTraceDescription();
            }
            else
            {
                SelectTraceNear(trace.Arm, position);
            }
        }
        else if (commit && trace is not null && IsBoundarySegmentMode && IsTraceEndpoint(trace.Arm, trace.Index))
        {
            _traceContinuation = (trace.Arm, trace.Index);
            UpdateTraceDescription();
        }

        if (ring is not null)
        {
            // A click is an explicit confirmation for a guide that already lines up.
            if (commit && (!ring.IsConfirmed || ring.RadiusMm != _ringDragRadius))
                Execute(RingEditedCommand, new CalibrationRingEdit(ring.RingIndex, _ringDragRadius));
            UpdateRingDescription(ring.RingIndex, commit ? _ringDragRadius : ring.RadiusMm);
        }

        _area.QueueDraw();
    }

    private void AddTraceAt(Point screen)
    {
        if (!CanEditTrace || Dragging) return;
        Point point = ViewToWorld(screen);
        if (!WorldContains(point) || SelectedTraceArm < 0 || (!IsBoundarySegmentMode && SelectedTraceArm > 3)) return;
        TraceInsertion insertion = FindTraceInsertion(screen);
        bool continueFixedEnd = IsBoundarySegmentMode && _traceContinuation is not null;
        HashSet<int>? previousIds = IsBoundarySegmentMode ? (TracePoints ?? []).Select(p => p.Index).ToHashSet() : null;
        CalibrationTraceEdit edit = new(SelectedTraceArm, -1, point.X, point.Y,
            InsertBeforeIndex: IsBoundarySegmentMode ? insertion.BeforeIndex : null);
        if (TraceEditedCommand?.CanExecute(edit) != true) return;
        TraceEditedCommand.Execute(edit);
        if (IsBoundarySegmentMode)
        {
            CalibrationTracePoint? added = TracePoints?.FirstOrDefault(p => p.Arm == SelectedTraceArm && !previousIds!.Contains(p.Index));
            if (added is null) return;
            _selectedTrace = (added.Arm, added.Index);
            _traceLastExtension = IsTraceEndpoint(added.Arm, added.Index) ? _selectedTrace : null;
            if (continueFixedEnd && IsTraceEndpoint(added.Arm, added.Index)) _traceContinuation = _selectedTrace;
            UpdateTraceDescription();
        }
        else
        {
            SelectTraceNear(SelectedTraceArm, point);
        }

        _area.QueueDraw();
    }

    private readonly record struct TraceInsertion(int? BeforeIndex, CalibrationTracePoint? Previous, CalibrationTracePoint? Next);

    private TraceInsertion FindTraceInsertion(Point screen)
    {
        if (!IsBoundarySegmentMode) return default;
        CalibrationTracePoint[] points = (TracePoints ?? []).Where(p => p.Arm == SelectedTraceArm).ToArray();
        if (points.Length == 0) return default;
        ValidateTraceSelection();
        if (_traceContinuation == (SelectedTraceArm, points[0].Index)) return new(points[0].Index, null, points[0]);
        if (_traceContinuation == (SelectedTraceArm, points[^1].Index)) return new(null, points[^1], null);
        if (points.Length == 1) return new(null, points[0], null);
        double closest = 7 * 7;
        int insertion = -1;
        for (int i = 1; i < points.Length; i++)
        {
            Point a = WorldToView(Position(points[i - 1])), b = WorldToView(Position(points[i]));
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double t = ((screen.X - a.X) * ex + (screen.Y - a.Y) * ey) / Math.Max(1e-20, ex * ex + ey * ey);
            if (t <= 0 || t >= 1 || Distance(screen, a) <= 10 || Distance(screen, b) <= 10) continue;
            double distance = DistanceSquared(screen, new Point(a.X + t * ex, a.Y + t * ey));
            if (distance <= closest)
            {
                closest = distance;
                insertion = i;
            }
        }

        if (insertion >= 0) return new(points[insertion].Index, points[insertion - 1], points[insertion]);
        Point first = WorldToView(Position(points[0])), last = WorldToView(Position(points[^1]));
        Point fromFirst = new(screen.X - first.X, screen.Y - first.Y), fromLast = new(screen.X - last.X, screen.Y - last.Y);
        Point second = WorldToView(Position(points[1])), penultimate = WorldToView(Position(points[^2]));
        static double Alignment(Point outward, Point next) =>
            (outward.X * next.X + outward.Y * next.Y) / Math.Max(1e-20, Length(outward) * Length(next));
        double firstAlignment = Alignment(new Point(first.X - second.X, first.Y - second.Y), fromFirst);
        double lastAlignment = Alignment(new Point(last.X - penultimate.X, last.Y - penultimate.Y), fromLast);
        bool bothTurn = firstAlignment < .2 && lastAlignment < .2;
        double firstCost = Length(fromFirst) / (bothTurn ? 1 : Math.Max(.15, firstAlignment));
        double lastCost = Length(fromLast) / (bothTurn ? 1 : Math.Max(.15, lastAlignment));
        if (Math.Abs(firstCost - lastCost) <= Math.Max(1, .15 * Math.Min(firstCost, lastCost)))
        {
            if (_traceLastExtension == (SelectedTraceArm, points[0].Index)) return new(points[0].Index, null, points[0]);
            if (_traceLastExtension == (SelectedTraceArm, points[^1].Index)) return new(null, points[^1], null);
        }

        return firstCost < lastCost ? new(points[0].Index, null, points[0]) : new(null, points[^1], null);
    }

    private bool IsTraceEndpoint(int arm, int index)
    {
        CalibrationTracePoint[] points = (TracePoints ?? []).Where(p => p.Arm == arm).ToArray();
        return points.Length > 0 && (points[0].Index == index || points[^1].Index == index);
    }

    private void ValidateTraceSelection()
    {
        if (_selectedTrace is { } selected && !(TracePoints ?? []).Any(p => p.Arm == selected.Arm && p.Index == selected.Index))
            _selectedTrace = null;
        if (_traceContinuation is { } endpoint && (endpoint.Arm != SelectedTraceArm || !IsTraceEndpoint(endpoint.Arm, endpoint.Index)))
            _traceContinuation = null;
        if (_traceLastExtension is { } last && (last.Arm != SelectedTraceArm || !IsTraceEndpoint(last.Arm, last.Index)))
            _traceLastExtension = null;
    }

    public void RemoveSelectedTracePoint()
    {
        if (!CanEditTrace || Dragging || _selectedTrace is not { } selected) return;
        CalibrationTracePoint? point = TracePoints?.FirstOrDefault(p => p.Arm == selected.Arm && p.Index == selected.Index);
        if (point is null) return;
        CalibrationTraceEdit edit = new(point.Arm, point.Index, point.XMm, point.YMm, true);
        if (TraceEditedCommand?.CanExecute(edit) != true) return;
        TraceEditedCommand.Execute(edit);
        _selectedTrace = null;
        ValidateTraceSelection();
        SetDescription(L.T("已删除此点；点击照片继续描当前曲线。"));
        _area.QueueDraw();
    }

    public void RemoveSelectedRing()
    {
        if (!CanEditRings || ThreePointMode || Dragging || _selectedRing is not { } index) return;
        CalibrationRingEdit edit = new(index, null);
        if (RingEditedCommand?.CanExecute(edit) != true) return;
        RingEditedCommand.Execute(edit);
        _selectedRing = null;
        SetDescription(L.T(HasWideBandTarget ? "BandEdges.DragHint" : "Xaml.CoarseRingStatusHint"));
        _area.QueueDraw();
    }

    private void AddRingPointAt(Point screen)
    {
        if ((!CanMarkBandRay && !(CanEditRings && ThreePointMode)) || Dragging || SelectedRingIndex < 0) return;
        Point world = ViewToWorld(screen);
        double radius = Length(world);
        if (!double.IsFinite(radius) || radius < 7.5 || radius > PhysicalRadiusMm) return;
        Execute(CanMarkBandRay ? BandRayPointCommand : RingPointCommand, new CalibrationPoint(world.X, world.Y));
    }

    private void AddArcLengthPointAt(Point screen)
    {
        if (!CanMarkArcLength || Dragging) return;
        Point world = ViewToWorld(screen);
        double radius = Length(world);
        if (!double.IsFinite(radius) || radius < 7.5 || radius > PhysicalRadiusMm) return;
        Execute(ArcLengthPointCommand, new CalibrationPoint(world.X, world.Y));
    }

    private void SelectTraceNear(int arm, Point position)
    {
        CalibrationTracePoint? point = TracePoints?.Where(p => p.Arm == arm).MinBy(p => DistanceSquared(Position(p), position));
        _selectedTrace = point is null || Distance(Position(point), position) > 1e-6 ? null : (point.Arm, point.Index);
        UpdateTraceDescription();
    }

    private void UpdateTraceDescription() => SetDescription(
        IsBoundarySegmentMode
            ? L.Format(_traceContinuation is not null ? "HalfDisc.EndpointFixed" : "HalfDisc.CanvasHint", SelectedTraceArm + 1)
            : _selectedTrace is { } point ? L.F($"已选曲线上的点 {point.Index + 1} · 直接拖动修正，Delete 删除")
            : L.T("点击照片上同一条可见曲线，由内向外连续描点。"));

    private void UpdateRingDescription(int index, double radius) => SetDescription(HasWideBandTarget
        ? L.Format("BandEdges.Selected", CalibrationRingArc.WideEdgeLabel(index), radius)
        : L.Format("Xaml.CoarseRingSelected", index + 1, radius));

    private static void Execute(ICommand? command, object? argument)
    {
        if (command?.CanExecute(argument) == true) command.Execute(argument);
    }

    private static Point Position(CalibrationTracePoint point) => new(point.XMm, point.YMm);

    private Point BoundaryDisplayPosition(CalibrationPhotoBoundaryPoint point) =>
        _boundaryDrag?.Boundary == point.Boundary && _boundaryDrag.Index == point.Index ? _dragPosition : new Point(point.X, point.Y);

    private Point TraceDisplayPosition(CalibrationTracePoint point) =>
        _traceDrag?.Arm == point.Arm && _traceDrag.Index == point.Index ? _dragPosition : Position(point);

    private double RingAngle => RingAngleDegrees * Math.PI / 180;

    private Point OnRing(double radius, double angle) => WorldToView(new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)));

    private Point RingBadge(CalibrationRingArc arc, double radius) => OnRing(radius, RingAngle + (arc.RingIndex % 2 == 0 ? .22 : -.22));

    private CalibrationRingArc? HitRing(Point screen)
    {
        if (RingArcs is not { } arcs) return null;
        CalibrationRingArc? badge = arcs.MinBy(arc => DistanceSquared(RingBadge(arc, arc.RadiusMm), screen));
        if (badge is not null && Distance(RingBadge(badge, badge.RadiusMm), screen) <= 8) return badge;
        Point world = ViewToWorld(screen);
        double angle = Math.Atan2(world.Y, world.X);
        if (Math.Abs(Math.IEEERemainder(angle - RingAngle, Math.PI)) > ArcHalfAngle) return null;
        CalibrationRingArc? closest = arcs.MinBy(arc => DistanceSquared(OnRing(arc.RadiusMm, angle), screen));
        return closest is not null && Distance(OnRing(closest.RadiusMm, angle), screen) <= 8 ? closest : null;
    }

    // ---- Rendering ----

    private void Draw(Gtk.DrawingArea area, Cairo.Context cr, int width, int height)
    {
        DiscPalette palette = DiscPalette.Current;
        palette.PhotoBackdrop.Apply(cr);
        cr.Paint();
        Cairo.ImageSurface? surface = ShowingTarget ? _fallbackSurface : _imageSurface;
        var bounds = WorldBounds;
        Point topLeft = WorldToView(new Point(bounds.X, bounds.Y));
        Point bottomRight = WorldToView(new Point(bounds.X + bounds.W, bounds.Y + bounds.H));
        if (surface is not null)
        {
            cr.Save();
            cr.Translate(topLeft.X, topLeft.Y);
            cr.Scale((bottomRight.X - topLeft.X) / surface.Width, (bottomRight.Y - topLeft.Y) / surface.Height);
            cr.SetSourceSurface(surface, 0, 0);
            using (Cairo.Pattern pattern = cr.GetSource()) pattern.Filter = Cairo.Filter.Good;
            cr.Paint();
            cr.Restore();
        }
        else
        {
            DrawPlaceholder(cr, palette);
        }

        if (ShowingRawPhoto) DrawBoundaries(cr, palette);
        else if (CanMarkArcLength) DrawArcLengthPoints(cr, palette);
        else if (CanMarkBandRay) DrawBandRay(cr, palette);
        else if (ShowingClockPhoto)
        {
            double radius = PhysicalRadiusMm + .7;
            foreach (double angle in ClockReadAngles ?? [])
                DrawRingSegment(cr, radius, (angle - 22.5) * Math.PI / 180, Math.PI / 4, palette.Trace, 3);
            if (PreferredClockAngle is { } preferred) Dot(cr, OnRing(radius, preferred * Math.PI / 180), 3.5, palette.Handle);
        }
        else if (CanEditRings && ThreePointMode) DrawMarkers(cr, palette, RingPointMarkers, ringClosure: true);
        else if (CanEditRings) DrawRings(cr, palette);
        else if (CanEditTrace) DrawTraces(cr, palette);

        if (ShowingTarget && surface is null) DrawCaption(cr, palette, L.T("CalibrationArt.Empty"));
        else if (ShowingTarget && HasClockTarget) DrawCaption(cr, palette, L.T("Clock.PreviewCaption"));
        else if (ShowingTarget)
            DrawCaption(cr, palette, Image is null ? L.T("标靶预览 · 导入照片后开始矫正") : L.T("刻录图案预览 · 关闭预览后继续照片标定"));
        else if (IsRectificationPreview) DrawCaption(cr, palette, L.T("Xaml.RectifiedCalibrationCaption"));
    }

    private void DrawTraces(Cairo.Context cr, DiscPalette palette)
    {
        if (ShowPrediction && PredictionContours is { } predictions)
        {
            foreach (Point[] prediction in predictions) DrawPath(cr, prediction, palette.Prediction, 1.8, false);
        }

        foreach (int arm in (TracePoints ?? []).Select(point => point.Arm).Distinct())
        {
            CalibrationTracePoint[] points = TracePoints?.Where(p => p.Arm == arm).ToArray() ?? [];
            cr.SetDash([1.5, 3], 0);
            DrawPath(cr, points.Select(TraceDisplayPosition).ToArray(), palette.Trace,
                IsBoundarySegmentMode && arm == SelectedTraceArm ? 2 : 1.2, false);
            cr.SetDash([], 0);
            if (IsBoundarySegmentMode && points.Length > 0)
            {
                Point at = WorldToView(TraceDisplayPosition(points[0]));
                DrawText(cr, L.Format("HalfDisc.SegmentBadge", arm + 1), at.X + 8, at.Y + 8, palette.Trace, 11);
            }

            foreach (CalibrationTracePoint point in points)
            {
                bool selected = _selectedTrace == (point.Arm, point.Index);
                bool outlier = OutlierTracePointIndices?.Contains(point.Index) == true;
                Rgba fill = outlier ? palette.MissingImage : palette.Trace;
                Point screen = WorldToView(TraceDisplayPosition(point));
                Dot(cr, screen, selected ? 6 : 4, fill, 1.4);
                if (IsBoundarySegmentMode && arm == SelectedTraceArm
                    && (point.Index == points[0].Index || point.Index == points[^1].Index))
                {
                    bool fixedEnd = _traceContinuation == (point.Arm, point.Index);
                    (fixedEnd ? palette.Prediction : palette.Trace).Apply(cr);
                    cr.LineWidth = fixedEnd ? 2.3 : 1.3;
                    cr.NewPath();
                    cr.Arc(screen.X, screen.Y, fixedEnd ? 10 : 7.5, 0, 2 * Math.PI);
                    cr.Stroke();
                    if (fixedEnd) DrawText(cr, L.T("HalfDisc.ContinueBadge"), screen.X + 10, screen.Y + 7, palette.Prediction, 11);
                }

                if (selected) DrawText(cr, L.F($"点 {point.Index + 1}"), screen.X + 9, screen.Y - 16, fill, 12);
            }
        }

        // Insertion preview for boundary-segment tracing.
        if (!IsBoundarySegmentMode || Dragging || _panning || _traceHover is not { } hover) return;
        Point world = ViewToWorld(hover);
        double radius = Length(world);
        if (!WorldContains(world) || radius < 7.5 || radius > PhysicalRadiusMm
            || (TracePoints ?? []).Any(p => Distance(WorldToView(Position(p)), hover) <= 10)) return;
        TraceInsertion edit = FindTraceInsertion(hover);
        palette.Trace.Apply(cr);
        cr.LineWidth = 1.6;
        cr.SetDash([5, 4], 0);
        if (edit.Previous is { } previous)
        {
            Point from = WorldToView(Position(previous));
            cr.MoveTo(from.X, from.Y);
            cr.LineTo(hover.X, hover.Y);
        }

        if (edit.Next is { } next)
        {
            Point to = WorldToView(Position(next));
            cr.MoveTo(hover.X, hover.Y);
            cr.LineTo(to.X, to.Y);
        }

        cr.Stroke();
        cr.SetDash([], 0);
        cr.Arc(hover.X, hover.Y, 4.5, 0, 2 * Math.PI);
        cr.Stroke();
    }

    private void DrawMarkers(Cairo.Context cr, DiscPalette palette, IReadOnlyList<CalibrationPoint>? points, bool ringClosure)
    {
        if (points is not { Count: > 0 }) return;
        if (ringClosure)
        {
            CalibrationRingArc? ring = RingArcs?.FirstOrDefault(r => r.RingIndex == SelectedRingIndex);
            if (ring is { IsConfirmed: true, Points.Count: 3 } && ring.Points.SequenceEqual(points))
            {
                double[] angles = points.Select(p => Math.Atan2(p.Y, p.X)).Order().ToArray();
                double start = angles[0], sweep = angles[^1] - start, largestGap = 2 * Math.PI - sweep;
                for (int i = 1; i < angles.Length; i++)
                {
                    double gap = angles[i] - angles[i - 1];
                    if (gap <= largestGap) continue;
                    largestGap = gap;
                    start = angles[i];
                    sweep = 2 * Math.PI - gap;
                }

                const double pad = Math.PI / 36;
                DrawRingSegment(cr, ring.RadiusMm, start - pad, sweep + 2 * pad, palette.Trace, 1.6);
            }
        }

        for (int i = 0; i < points.Count; i++)
        {
            Point position = WorldToView(new Point(points[i].X, points[i].Y));
            Dot(cr, position, 3.5, palette.HubOutline);
            DrawLabel(cr, palette, (i + 1).ToString(CultureInfo.CurrentCulture), position.X + 5, position.Y - 14, palette.Handle);
        }
    }

    private void DrawArcLengthPoints(Cairo.Context cr, DiscPalette palette)
    {
        if (ArcLengthPoints is not { Count: > 0 } points) return;
        if (CalibrationArcLengthMark.TrySpan(points, out double signedDegrees))
        {
            double first = Math.Atan2(points[0].Y, points[0].X);
            double last = Math.Atan2(points[2].Y, points[2].X);
            double start = signedDegrees >= 0 ? first : last;
            double radius = points.Average(p => double.Hypot(p.X, p.Y));
            DrawRingSegment(cr, radius, start, Math.Abs(signedDegrees) * Math.PI / 180, palette.Trace, 1.6);
        }

        DrawMarkers(cr, palette, points, ringClosure: false);
    }

    private void DrawRings(Cairo.Context cr, DiscPalette palette)
    {
        if (RingArcs is null) return;
        foreach (CalibrationRingArc arc in RingArcs)
        {
            double r = _ringDrag?.RingIndex == arc.RingIndex ? _ringDragRadius : arc.RadiusMm;
            bool selected = _selectedRing == arc.RingIndex;
            Rgba color = selected ? palette.HubOutline : arc.IsConfirmed ? palette.Trace : palette.Edge;
            if (!arc.IsConfirmed) cr.SetDash([5, 4], 0);
            DrawRingSegment(cr, r, RingAngle - ArcHalfAngle, 2 * ArcHalfAngle, color, selected ? 2.2 : 1.4);
            DrawRingSegment(cr, r, RingAngle + Math.PI - ArcHalfAngle, 2 * ArcHalfAngle, color, selected ? 2.2 : 1.4);
            cr.SetDash([], 0);
            Point badge = RingBadge(arc, r);
            string label = HasWideBandTarget ? arc.ShortEdgeLabel : (arc.RingIndex + 1).ToString(CultureInfo.CurrentCulture);
            Pango.Layout layout = Layout(label, 10);
            layout.GetPixelSize(out int w, out int h);
            if (HasWideBandTarget)
            {
                RoundedRectangle(cr, badge.X - w / 2.0 - 3, badge.Y - h / 2.0 - 1, w + 6, h + 2, 3);
            }
            else
            {
                cr.NewPath();
                cr.Arc(badge.X, badge.Y, 7.5, 0, 2 * Math.PI);
            }

            palette.PhotoBackdrop.Apply(cr);
            cr.FillPreserve();
            color.Apply(cr);
            cr.LineWidth = 1;
            cr.Stroke();
            cr.MoveTo(badge.X - w / 2.0, badge.Y - h / 2.0);
            PangoCairo.Functions.ShowLayout(cr, layout);
        }
    }

    private void DrawBandRay(Cairo.Context cr, DiscPalette palette)
    {
        if (BandRayAngleDegrees is not double degrees || RingArcs is null) return;
        double angle = degrees * Math.PI / 180;
        Point from = OnRing(7.5, angle), to = OnRing(PhysicalRadiusMm, angle);
        palette.Edge.Apply(cr);
        cr.LineWidth = 1;
        cr.SetDash([5, 4], 0);
        cr.MoveTo(from.X, from.Y);
        cr.LineTo(to.X, to.Y);
        cr.Stroke();
        cr.SetDash([], 0);
        (double nx, double ny) = (-Math.Sin(angle), Math.Cos(angle));
        foreach (CalibrationRingArc edge in RingArcs)
        {
            if (edge.Points is not { Count: 1 } points) continue;
            Point mark = WorldToView(new Point(points[0].X, points[0].Y));
            Dot(cr, mark, 3.5, palette.Handle);
            Pango.Layout layout = Layout(edge.ShortEdgeLabel, 10);
            layout.GetPixelSize(out int w, out int h);
            double offset = 5 + (Math.Abs(nx) * w + Math.Abs(ny) * h) / 2;
            double sign = edge.RingIndex < 3 ? -1 : 1;
            DrawLabel(cr, palette, edge.ShortEdgeLabel, mark.X + nx * offset * sign - w / 2.0, mark.Y + ny * offset * sign - h / 2.0, palette.Trace);
        }
    }

    private void DrawBoundaries(Cairo.Context cr, DiscPalette palette)
    {
        foreach (CalibrationPhotoBoundaryKind kind in new[] { CalibrationPhotoBoundaryKind.OuterEdge, CalibrationPhotoBoundaryKind.CenterHole })
        {
            Rgba color = kind == CalibrationPhotoBoundaryKind.OuterEdge ? palette.Edge : palette.HubOutline;
            CalibrationPhotoBoundaryPoint[] points = BoundaryPoints?.Where(p => p.Boundary == kind).OrderBy(p => p.Index).ToArray() ?? [];
            Point[]? outline = BoundaryContours?.ElementAtOrDefault((int)kind);
            if (_boundaryDrag?.Boundary == kind || outline is not { Length: > 2 })
            {
                cr.SetDash([5, 4], 0);
                DrawPath(cr, points.Select(BoundaryDisplayPosition).ToArray(), color, 1.3, true);
                cr.SetDash([], 0);
            }
            else
            {
                DrawPath(cr, outline, color, 1.6, true);
            }

            foreach (CalibrationPhotoBoundaryPoint point in points)
            {
                bool selected = _selectedBoundary == (point.Boundary, point.Index);
                Dot(cr, WorldToView(BoundaryDisplayPosition(point)), selected ? 6 : 4.5, color, 1.2);
            }
        }
    }

    private void DrawPlaceholder(Cairo.Context cr, DiscPalette palette)
    {
        var bounds = WorldBounds;
        Point center = WorldToView(new Point());
        double radius = Math.Min(bounds.W, bounds.H) * .44;
        palette.Handle.Apply(cr);
        cr.LineWidth = 1.5;
        foreach (double r in new[] { radius, radius / 8 })
        {
            double viewRadius = Distance(WorldToView(new Point(r, 0)), center);
            cr.NewPath();
            cr.Arc(center.X, center.Y, viewRadius, 0, 2 * Math.PI);
            cr.Stroke();
        }
    }

    private void DrawRingSegment(Cairo.Context cr, double radius, double start, double sweep, Rgba color, double width)
    {
        Point center = WorldToView(new Point());
        double pixels = Distance(OnRing(radius, 0), center);
        color.Apply(cr);
        cr.LineWidth = width;
        cr.NewPath();
        cr.Arc(center.X, center.Y, pixels, start, start + sweep);
        cr.Stroke();
    }

    private void DrawPath(Cairo.Context cr, IReadOnlyList<Point> points, Rgba color, double width, bool closed)
    {
        if (points.Count < 2) return;
        cr.NewPath();
        bool started = false;
        foreach (Point point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                started = false;
                continue;
            }

            Point screen = WorldToView(point);
            if (!started)
            {
                cr.MoveTo(screen.X, screen.Y);
                started = true;
            }
            else
            {
                cr.LineTo(screen.X, screen.Y);
            }
        }

        if (closed) cr.ClosePath();
        color.Apply(cr);
        cr.LineWidth = width;
        cr.Stroke();
    }

    private static void Dot(Cairo.Context cr, Point at, double radius, Rgba fill, double outline = 1)
    {
        cr.NewPath();
        cr.Arc(at.X, at.Y, radius, 0, 2 * Math.PI);
        fill.Apply(cr);
        cr.FillPreserve();
        cr.SetSourceRgb(0, 0, 0);
        cr.LineWidth = outline;
        cr.Stroke();
    }

    private Pango.Layout Layout(string text, double size)
    {
        Pango.Layout layout = _area.CreatePangoLayout(text);
        Pango.FontDescription font = Pango.FontDescription.New();
        font.SetAbsoluteSize(size * Pango.Constants.SCALE);
        layout.SetFontDescription(font);
        return layout;
    }

    private void DrawText(Cairo.Context cr, string text, double x, double y, Rgba color, double size)
    {
        Pango.Layout layout = Layout(text, size);
        color.Apply(cr);
        cr.MoveTo(x, y);
        PangoCairo.Functions.ShowLayout(cr, layout);
    }

    private void DrawLabel(Cairo.Context cr, DiscPalette palette, string text, double x, double y, Rgba color)
    {
        Pango.Layout layout = Layout(text, 11);
        layout.GetPixelSize(out int w, out int h);
        RoundedRectangle(cr, x - 2, y, w + 4, h, 2);
        palette.PhotoBackdrop.Apply(cr);
        cr.Fill();
        color.Apply(cr);
        cr.MoveTo(x, y);
        PangoCairo.Functions.ShowLayout(cr, layout);
    }

    private void DrawCaption(Cairo.Context cr, DiscPalette palette, string text)
    {
        Pango.Layout layout = Layout(text, 11);
        layout.GetPixelSize(out int w, out int h);
        double top = Math.Max(4, Height - h - 12);
        RoundedRectangle(cr, 8, top, w + 12, h + 6, 4);
        palette.PhotoBackdrop.Apply(cr);
        cr.Fill();
        palette.Handle.Apply(cr);
        cr.MoveTo(14, top + 3);
        PangoCairo.Functions.ShowLayout(cr, layout);
    }

    private static void RoundedRectangle(Cairo.Context cr, double x, double y, double w, double h, double r)
    {
        cr.NewPath();
        cr.Arc(x + w - r, y + r, r, -Math.PI / 2, 0);
        cr.Arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
        cr.Arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
        cr.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        cr.ClosePath();
    }
}
