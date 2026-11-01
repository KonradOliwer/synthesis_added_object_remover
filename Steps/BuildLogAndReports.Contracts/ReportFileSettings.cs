using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports.Contracts;

public sealed record ReportFileSettings(string Folder)
{
    /// <summary>The detailed log and the report files list other mods' objects by Editor ID.</summary>
    public ScanNeeds Needs => new(OtherModDetails: true);
}
