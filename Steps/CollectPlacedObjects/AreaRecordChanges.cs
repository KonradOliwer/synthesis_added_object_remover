using System.Collections.Immutable;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Steps.CollectPlacedObjects;

/// <summary>
/// What the cleaned mod last changed in the cell, LAND and navmesh records of other plugins it changed, part by part
/// (<see cref="PartHistory"/>), from how their parts differ across the versions read.
/// </summary>
/// <param name="trust">Names the patch, whose version holds what earlier patchers of the group wrote, and the plugins that need the cleaned mod.</param>
/// <param name="placedDifferences">How the parts of each placed record the cleaned mod overrides differ across its versions, by key.</param>
/// <param name="navmeshReplacements">The replacements of each deleted navmesh that has some (<see cref="NavmeshReplacements"/>), by key.</param>
internal sealed class AreaRecordChanges(
    ModTrustTable trust, IReadOnlyDictionary<RecordKey, PlacedPartDifferences> placedDifferences, IReadOnlyDictionary<RecordKey, ImmutableArray<RecordKey>> navmeshReplacements)
{
    /// <returns>
    /// Null when no part is listed: every part that is not <see cref="PartOutcome.Unchanged"/> is, in the order of the cell's part
    /// table, but when any version deletes the cell nothing goes back, so only the parts left to later plugins are.
    /// </returns>
    public CellChangedByCleanedMod? CellOf(CellRecordFact cell)
    {
        ImmutableArray<PartChange> parts =
        [
            .. cell.Parts
                .Select(part => new PartChange(part.Name, PartHistory.Of(part.Differences, trust)))
                .Where(change => change.Outcome is PartOutcome.LeftToLaterPlugin || (!cell.DeletedInAVersion && change.Outcome is not PartOutcome.Unchanged)),
        ];
        return parts.IsEmpty
            ? null
            : new(cell.Key, cell.SpaceKey, cell.Grid, parts, LinkedFromCleanedMod: false, cell.WinningMod, cell.EarlierWater, cell.CurrentWater, cell.DefaultWaterLevel);
    }

    /// <returns>Null when no part goes back or was changed again; when any version deletes the LAND record, nothing goes back.</returns>
    public LandChangedByCleanedMod? LandOf(LandRecordFact land)
    {
        var plugins = PartHistory.PluginsOf(land.Versions);
        // Every part the cleaned mod's version does not change from the one below it is unchanged.
        var changed = VersionComparison.NamedPluginVersionOf(land.Versions).FromBelow;
        var points = OutcomesOf(land, plugins, changed.Points.Points, point => parts => parts.Points.Contains(point));
        var colours = OutcomesOf(land, plugins, changed.Colours.Points, point => parts => parts.Colours.Contains(point));
        var quarters = OutcomesOf(land, plugins, changed.Quarters.Quarters, quarter => parts => parts.Quarters.Contains(quarter));
        var rest = changed.Rest ? PartHistory.Of(land.Versions, plugins, trust, parts => parts.Rest) : new PartOutcome.Unchanged();

        var pointsOfNeedingPlugins = PointSet.Of(points.Where(entry => entry.Outcome is PartOutcome.LeftToLaterPlugin later && trust.NeedsCleanedMod(later.Plugin)).Select(entry => entry.Index));
        var leftToLater = new LandLeftToLater(
            PointSet.Of(IndicesOf<PartOutcome.LeftToLaterPlugin>(points)),
            PointSet.Of(IndicesOf<PartOutcome.LeftToLaterPlugin>(colours)),
            QuarterSet.Of(IndicesOf<PartOutcome.LeftToLaterPlugin>(quarters)),
            rest is PartOutcome.LeftToLaterPlugin,
            LastChangersInLoadOrder([.. points.Concat(colours).Concat(quarters).Select(entry => entry.Outcome), rest], plugins),
            pointsOfNeedingPlugins,
            pointsOfNeedingPlugins.IsEmpty ? null : HeightsOfPluginLoadingWithoutCleanedMod(land, plugins));
        var deleted = land.DeletedInAVersion;
        var undoPoints = PointSet.Of(deleted ? [] : IndicesOf<PartOutcome.Undo>(points));
        var undoColours = PointSet.Of(deleted ? [] : IndicesOf<PartOutcome.Undo>(colours));
        var undoQuarters = QuarterSet.Of(deleted ? [] : IndicesOf<PartOutcome.Undo>(quarters));
        var undoRest = !deleted && rest is PartOutcome.Undo;
        var isEmpty = undoPoints.IsEmpty && undoColours.IsEmpty && undoQuarters.IsEmpty && !undoRest
            && leftToLater.Points.IsEmpty && leftToLater.Colours.IsEmpty && leftToLater.Quarters.IsEmpty && !leftToLater.Rest;
        return isEmpty
            ? null
            : new(land.Cell, land.Key, undoPoints, undoColours, undoQuarters, undoRest, leftToLater, land.WinningMod, land.Heights, land.CellDeletedInAVersion);
    }

    /// <remarks>A part is left to a plugin that needs the cleaned mod only above a plugin that loads without it, so there is one.</remarks>
    private float[]? HeightsOfPluginLoadingWithoutCleanedMod(LandRecordFact land, IReadOnlyList<PluginName> plugins) =>
        land.HeightsOfVersion(PartHistory.LastLoadingWithoutCleanedModAbove(plugins, VersionComparison.NamedPluginIndex, trust)!.Value);

    /// <returns>Null when no part goes back, was changed again or stays for its record format.</returns>
    /// <remarks>
    /// When a version above the cleaned mod's deletes a navmesh the cleaned mod keeps, nothing in it goes back. A navmesh with a part
    /// a later plugin changed again never goes back, so it has no neighbours.
    /// </remarks>
    public NavmeshChangedByCleanedMod? NavmeshOf(NavmeshRecordFact navmesh)
    {
        var geometry = WhatGoesBack(navmesh, PartHistory.Of(navmesh.Geometry, trust));
        var presence = WhatGoesBack(navmesh, PartHistory.Of(navmesh.Presence, trust));
        var mapEntry = WhatGoesBack(navmesh, navmesh.MapEntry is { } entry ? PartHistory.Of(entry, trust) : new PartOutcome.Unchanged());
        PartOutcome[] outcomes = [geometry, presence, mapEntry];
        if (outcomes.All(outcome => outcome is PartOutcome.Unchanged)) return null;
        return new(
            navmesh.Key,
            navmesh.SpaceKey,
            navmesh.Cell,
            navmesh.WinningMod,
            navmesh.Case,
            geometry,
            presence,
            mapEntry,
            navmesh.Earlier,
            navmesh.Current,
            navmeshReplacements.TryGetValue(navmesh.Key, out var replacedBy) ? replacedBy : [],
            outcomes.Any(outcome => outcome is PartOutcome.LeftToLaterPlugin) ? [] : navmesh.Neighbours,
            [.. navmesh.Doors.Select(DoorOf)],
            navmesh.NamedInPreferredPaths,
            navmesh.WinningMapMod,
            navmesh.CellDeletedInAVersion,
            navmesh.LaterWinner,
            navmesh.WinningMapEntry);
    }

    /// <summary>The door's navmesh link with what the cleaned mod last changed in it; unchanged for a door it does not override.</summary>
    public DoorLinkChange DoorOf(NavDoorLinkVersions door) => new(
        door.Door,
        placedDifferences.TryGetValue(door.Door, out var differences) ? PlacedPartChanges.NavDoorLinkOf(differences, trust) : new PartOutcome.Unchanged(),
        door.Earlier,
        door.Current,
        door.InDeletedCell);

    private static PartOutcome WhatGoesBack(NavmeshRecordFact navmesh, PartOutcome outcome) =>
        navmesh.DeletedAboveNamedPlugin && outcome is PartOutcome.Undo or PartOutcome.KeptForRecordFormat ? new PartOutcome.Unchanged() : outcome;

    /// <param name="includes">For each index, whether a set of parts includes the part at that index.</param>
    private List<(int Index, PartOutcome Outcome)> OutcomesOf(
        LandRecordFact land, IReadOnlyList<PluginName> plugins, IEnumerable<int> indices, Func<int, Func<LandParts, bool>> includes) =>
        [.. indices.Select(index => (index, PartHistory.Of(land.Versions, plugins, trust, includes(index))))];

    private static IEnumerable<int> IndicesOf<TOutcome>(IEnumerable<(int Index, PartOutcome Outcome)> outcomes)
        where TOutcome : PartOutcome =>
        outcomes.Where(entry => entry.Outcome is TOutcome).Select(entry => entry.Index);

    private static ImmutableArray<PluginName> LastChangersInLoadOrder(IEnumerable<PartOutcome> outcomes, IReadOnlyList<PluginName> plugins)
    {
        var lastChangers = outcomes.OfType<PartOutcome.LeftToLaterPlugin>().Select(left => left.Plugin).ToHashSet();
        return [.. plugins.Where(lastChangers.Contains).Distinct()];
    }
}
