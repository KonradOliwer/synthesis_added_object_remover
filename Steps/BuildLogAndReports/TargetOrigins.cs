using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// Counts and words target objects by origin. The target plugin's own objects are the base; every other origin is counted and
/// named beside them only when it has objects, so a new origin needs only its wording in <see cref="Phrase"/> and <see cref="KindName"/>.
/// </summary>
internal static class TargetOrigins
{
    /// <summary>The target objects with the given indexes, in the given order.</summary>
    public static List<TargetObject> At(CollectedObjects world, IEnumerable<int> targetIndexes) => [.. targetIndexes.Select(index => world.Targets[index])];

    public static IEnumerable<TargetObject> Of(IEnumerable<TargetObject> targets, TargetOrigin origin) =>
        targets.Where(target => target.Origin == origin);

    public static int CountOwn(IEnumerable<TargetObject> targets) => Of(targets, TargetOrigin.CleanedMod).Count();

    /// <summary>"Mod.esp objects", or "objects of other mods that Mod.esp moved".</summary>
    public static string Phrase(TargetOrigin origin, PluginName target) => origin switch
    {
        TargetOrigin.CleanedMod => $"{target} objects",
        TargetOrigin.Moved => $"objects of other mods that {target} moved",
        TargetOrigin.Replaced => $"objects of other mods that {target} gave another model",
        _ => throw new UnreachableException($"Unknown target origin {origin}."),
    };

    /// <summary>"objects of other mods that Mod.esp changed": the objects of every origin but the own one together.</summary>
    public static string ChangedPhrase(PluginName target) => $"objects of other mods that {target} changed";

    public static string ChangedHeading(PluginName target)
    {
        var phrase = ChangedPhrase(target);
        return char.ToUpperInvariant(phrase[0]) + phrase[1..];
    }

    /// <summary>The origin as the changed-objects line and the report file name it: "own", "moved" or "replaced".</summary>
    public static string KindName(TargetOrigin origin) => origin switch
    {
        TargetOrigin.CleanedMod => "own",
        TargetOrigin.Moved => "moved",
        TargetOrigin.Replaced => "replaced",
        _ => throw new UnreachableException($"Unknown target origin {origin}."),
    };

    /// <summary>"2 objects of other mods that Mod.esp moved" for each origin other than the own one that has objects, in origin order.</summary>
    public static IReadOnlyList<string> CountedOtherOrigins(IReadOnlyCollection<TargetObject> targets, PluginName target) =>
        [.. CountOtherOrigins(targets).Select(entry => $"{TextFormat.Count(entry.Count)} {Phrase(entry.Origin, target)}")];

    /// <summary>"7 moved, 2 replaced" for each origin other than the own one that has objects, in origin order.</summary>
    public static string CountedKinds(IReadOnlyCollection<TargetObject> targets) =>
        string.Join(", ", CountOtherOrigins(targets).Select(entry => $"{TextFormat.Count(entry.Count)} {KindName(entry.Origin)}"));

    private static IEnumerable<(TargetOrigin Origin, int Count)> CountOtherOrigins(IReadOnlyCollection<TargetObject> targets) =>
        Enum.GetValues<TargetOrigin>()
            .Where(origin => origin != TargetOrigin.CleanedMod)
            .Select(origin => (Origin: origin, Count: Of(targets, origin).Count()))
            .Where(entry => entry.Count > 0);
}
