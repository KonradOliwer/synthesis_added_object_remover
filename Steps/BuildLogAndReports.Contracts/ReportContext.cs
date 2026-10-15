using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

/// <summary>What the report builders read besides a step's result.</summary>
/// <param name="Shapes">For world boxes of target objects.</param>
/// <param name="Detailed">Whether the detailed log is on: lines and timings only it shows are left out otherwise.</param>
public sealed record ReportContext(IBaseFacts Bases, IBaseObjectShapes Shapes, bool Detailed);
