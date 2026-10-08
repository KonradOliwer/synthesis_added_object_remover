using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>Labels for the target's objects and their base objects.</summary>
internal static class RecordNames
{
    public static string Describe(TargetObject target) => RecordLabels.Of(target.Key, target.EditorId);

    public static string Describe(OtherObject other) => RecordLabels.Of(other.Key, other.EditorId);

    public static string DescribeBase(IBaseFacts bases, BaseKey? baseKey)
    {
        if (baseKey is not { } key) return "(none)";
        var facts = bases.Of(key);
        return facts.Resolved ? RecordLabels.Of(facts.Record, facts.EditorId) : key.Record.ToString();
    }
}
