// Records shared between the calibration view models and the photo canvas. On Windows they
// are declared next to the WPF canvas control; the GTK canvas uses the same shapes.

using DiscImageStudio.Core.Calibration;
using DiscImageStudio.Localization;

namespace DiscImageStudio.Controls;

public enum CalibrationPhotoBoundaryKind { OuterEdge, CenterHole }

public sealed record CalibrationPhotoBoundaryPoint(CalibrationPhotoBoundaryKind Boundary, int Index, double X, double Y);

public sealed record CalibrationBoundaryEdit(CalibrationPhotoBoundaryKind Boundary, int Index, double X, double Y);

public sealed record CalibrationTracePoint(int Arm, int Index, double XMm, double YMm);

/// <summary>Index -1 adds a point before InsertBeforeIndex, or at the segment end when null.
/// Existing indices identify a point without changing its neighbours.</summary>
public sealed record CalibrationTraceEdit(int Arm, int Index, double XMm, double YMm, bool Remove = false,
    int? InsertBeforeIndex = null);

public sealed record CalibrationRingArc(int RingIndex, double RadiusMm, bool IsConfirmed,
    IReadOnlyList<CalibrationPoint>? Points = null)
{
    public static string WideEdgeLabel(int id) =>
        L.Format(id < 3 ? "BandEdges.InnerLabel" : "BandEdges.OuterLabel", id % 3 + 1);

    public string ShortEdgeLabel => L.Format(RingIndex < 3 ? "BandEdges.InnerBadge" : "BandEdges.OuterBadge", RingIndex % 3 + 1);
}

public sealed record CalibrationRingEdit(int RingIndex, double? RadiusMm);

/// <summary>Progress of one calibration step card.</summary>
public enum StepCardState
{
    Pending,
    Active,
    Done,
}
