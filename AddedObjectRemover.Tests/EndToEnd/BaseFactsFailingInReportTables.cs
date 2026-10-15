using System.Diagnostics;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>Plants a failure that only the building of the report tables meets: reading base facts fails while a frame of the table builder is on the stack.</summary>
internal sealed class BaseFactsFailingInReportTables(IBaseFacts inner) : IBaseFacts
{
    public const string FailureMessage = "tables broke";

    private const string TableBuilderTypeName = "AddedObjectRemover.Steps.BuildLogAndReports.Tables";

    public BaseFacts Of(BaseKey baseKey) =>
        IsBuildingReportTables() ? throw new InvalidOperationException(FailureMessage) : inner.Of(baseKey);

    private static bool IsBuildingReportTables() =>
        new StackTrace().GetFrames().Any(frame => frame.GetMethod()?.DeclaringType?.FullName?.StartsWith(TableBuilderTypeName, StringComparison.Ordinal) == true);
}
