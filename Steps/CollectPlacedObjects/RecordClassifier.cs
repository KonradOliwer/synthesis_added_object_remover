using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// A record's role in the run. <see cref="SupportOnlyCopy"/> is a clone of another cleaned mod's content or a patch record with a
/// broken clone marker: it only supports.
/// </summary>
internal enum RecordRole { Target, TargetOverriddenLater, Moved, Replaced, OverriddenByTarget, OtherUnchanged, OtherChanged, SupportOnlyCopy }

/// <param name="Change">Only for <see cref="RecordRole.OtherChanged"/>.</param>
/// <param name="EarlierVersion">Only for <see cref="RecordRole.Moved"/> and <see cref="RecordRole.Replaced"/>.</param>
/// <param name="CloneOf">Only for a clone of the cleaned mod's content: the object it copies.</param>
internal readonly record struct Classification(RecordRole Role, ObjectChange? Change, PlacedVersionFact? EarlierVersion, RecordKey? CloneOf = null)
{
    /// <summary>Null for a record that is not a target object.</summary>
    public TargetOrigin? Origin => Role switch
    {
        RecordRole.Target => TargetOrigin.CleanedMod,
        RecordRole.Moved => TargetOrigin.Moved,
        RecordRole.Replaced => TargetOrigin.Replaced,
        RecordRole.TargetOverriddenLater or RecordRole.OverriddenByTarget or RecordRole.OtherUnchanged or RecordRole.OtherChanged
            or RecordRole.SupportOnlyCopy => null,
        _ => throw new UnreachableException($"Unknown record role {Role}."),
    };

    public bool IsJudged => Origin is not null;
}

/// <summary>Gives a placed record its role in the run.</summary>
internal sealed class RecordClassifier(ModTrustTable trust, AcceptedWorld world, WhatToCollect plan, PlacedRecordFacts facts)
{
    /// <summary>The plugins of the load order by the name a clone marker hashes them to; a hash two of them share is left out.</summary>
    private readonly ImmutableDictionary<string, PluginName> _pluginsByHashedName = PluginsByHashedName([trust.Patch, .. trust.Plugins.Keys]);

    /// <summary>
    /// Clones first, then the creator: the target's own records, then the records the target overrides, then the rule decides once.
    /// A clone of the cleaned mod's content is one of its own objects; a clone of another cleaned mod's content only supports, and so
    /// does a record with a broken marker, which is almost surely a clone whose EditorID was edited: no clone causes a removal.
    /// </summary>
    public Classification Classify(PlacedRecordFact record)
    {
        if (CloneMarkOf(record) is { } clone) return ClassifyClone(record, clone);
        if (HasBrokenCloneMarker(record)) return new(RecordRole.SupportOnlyCopy, null, null);
        if (IsCreatedByTarget(record)) return new(WinsInTargetOrPatch(record) ? RecordRole.Target : RecordRole.TargetOverriddenLater, null, null);
        if (facts.OverriddenRecords.Contains(record.Key)) return ClassifyOverriddenRecord(record);
        var change = OtherRecordChange.Decide(record, world);
        return new(change is null ? RecordRole.OtherUnchanged : RecordRole.OtherChanged, change, null);
    }

    public bool IsCreatedByTarget(PlacedRecordFact record) => record.Key.Plugin == trust.CleanedMod;

    /// <summary>The cleaned mod created the record, or it is a clone of the cleaned mod's content.</summary>
    public bool IsCleanedModsObject(PlacedRecordFact record) => IsCreatedByTarget(record) || CloneMarkOf(record)?.CleanedMod == trust.CleanedMod;

    /// <remarks>Only an Added Object Remover patch creates clones, so a marker in any other plugin is an ordinary EditorID.</remarks>
    public CloneMark? CloneMarkOf(PlacedRecordFact record) => IsInAnyGroupsPatch(record) ? ReadCloneMarker(record).Mark : null;

    /// <summary>A record of an Added Object Remover patch whose EditorID starts like a clone marker but is none.</summary>
    public bool HasBrokenCloneMarker(PlacedRecordFact record) => IsInAnyGroupsPatch(record) && ReadCloneMarker(record).IsBroken;

    private bool IsInAnyGroupsPatch(PlacedRecordFact record) => trust.IsAnyGroupsPatch(record.Key.Plugin);

    private CloneMarkerReading ReadCloneMarker(PlacedRecordFact record) =>
        CloneMarker.Read(record.EditorId, hashed => _pluginsByHashedName.TryGetValue(hashed, out var plugin) ? plugin : null);

    /// <remarks>A hash two plugins share names neither, so the result does not depend on the order the plugins come in.</remarks>
    private static ImmutableDictionary<string, PluginName> PluginsByHashedName(IEnumerable<PluginName> plugins)
    {
        PluginName[] distinct = [.. plugins.Distinct()];
        var shared = KeyedGroups.CountBy(distinct, CloneMarker.HashedNameOf, StringComparer.Ordinal).Where(hash => hash.Value > 1).Select(hash => hash.Key).ToHashSet();
        return distinct.Where(plugin => !shared.Contains(CloneMarker.HashedNameOf(plugin))).ToImmutableDictionary(CloneMarker.HashedNameOf);
    }

    /// <remarks>A clone is judged as its own patch or this group's patch left it, like the cleaned mod's records (<see cref="WinsInTargetOrPatch"/>).</remarks>
    private Classification ClassifyClone(PlacedRecordFact record, CloneMark clone)
    {
        if (clone.CleanedMod != trust.CleanedMod) return new(RecordRole.SupportOnlyCopy, null, null);
        var wins = record.WinningMod == record.Key.Plugin || record.WinningMod == trust.Patch;
        return new(wins ? RecordRole.Target : RecordRole.TargetOverriddenLater, null, null, clone.Original);
    }

    private Classification ClassifyOverriddenRecord(PlacedRecordFact record)
    {
        var changed = ChangedObjectKind.Of(record, trust, plan.MovedDistance);
        return changed switch
        {
            { Origin: TargetOrigin.Moved } => new(RecordRole.Moved, null, changed.Value.EarlierVersion),
            { Origin: TargetOrigin.Replaced } when plan.ProcessReplaced => new(RecordRole.Replaced, null, changed.Value.EarlierVersion),
            _ => new(RecordRole.OverriddenByTarget, null, null),
        };
    }

    /// <remarks>The patch's version holds what earlier patchers in this group wrote, so a target record it wins is judged as the patch left it.</remarks>
    private bool WinsInTargetOrPatch(PlacedRecordFact record) => record.WinningMod == trust.CleanedMod || record.WinningMod == trust.Patch;
}
