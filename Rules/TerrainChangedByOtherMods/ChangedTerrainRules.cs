using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Rules.RemovalRules.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>
/// Creates the rule of target objects on terrain that another mod changed. At each vertex the version the game shows is compared
/// with the accepted height and terrain type, each from the highest version trusted for it; a difference counts, and the plugin
/// whose version first holds the shown value is named as the changer. A terrain type change also needs the set share of the area's
/// vertices. The cell's water is compared with the accepted water, made of the cell's water flag and own height and its
/// worldspace's default height, each from the highest version trusted for it, and counts when its change covered or uncovered the
/// set share of the area's vertices.
/// </summary>
public static class ChangedTerrainRules
{
    /// <summary>The terrain and water of the load order as read.</summary>
    /// <param name="records">Its terrain and water versions must have been read.</param>
    /// <param name="restored">The parts earlier runs gave back, which the patch's version holds as accepted while it keeps their values.</param>
    public static ITerrainView ViewOfLoadOrder(IPluginRecords records, ImmutableArray<RestoredPart> restored) => new LoadOrderTerrainView(records, restored);

    /// <summary>The checks over the given target objects, one for each set of settings asked.</summary>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    public static IChangedTerrainChecks CreateChecks(
        IReadOnlyList<TargetObject> targets, AcceptedWorld accepted, LoadOrderPlugins loadOrder, ITerrainView view, IBaseObjectShapes shapes) =>
        new ChangedTerrainChecks(targets, new TerrainPlugins(accepted, loadOrder), view, shapes);

    /// <summary>The rule judging single objects over the load order as read or over any other view of the terrain.</summary>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <param name="view">The terrain and water of the load order as read.</param>
    public static IChangedTerrainJudging CreateJudging(AcceptedWorld accepted, LoadOrderPlugins loadOrder, ITerrainView view, IBaseObjectShapes shapes) =>
        new ChangedTerrainJudging(new TerrainPlugins(accepted, loadOrder), view, shapes);

    /// <summary>The check of one target object at a given placement; it finds nothing when the settings could find nothing.</summary>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    public static IPlacementRule CreatePlacementRule(
        AcceptedWorld accepted, LoadOrderPlugins loadOrder, ITerrainView view, IBaseObjectShapes shapes, ChangedTerrainSettings settings) =>
        new ChangedTerrainPlacementRule(settings.Runs ? new ObjectTerrainCheck(view, shapes, new TerrainPlugins(accepted, loadOrder), settings) : null);

    /// <summary>The check of one target object at its earlier version's place, in the world without the cleaned mod.</summary>
    /// <param name="records">Its terrain and water versions must have been read.</param>
    /// <param name="loadOrder">Decides which of several changing plugins loads last.</param>
    /// <remarks>No part an earlier run gave back counts in that world, since its value may be the cleaned mod's own.</remarks>
    public static IPlacementRule CreateOldPlacePlacementRule(
        IPluginRecords records, AcceptedWorld acceptedWithoutCleanedMod, LoadOrderPlugins loadOrder, IBaseObjectShapes shapes, ChangedTerrainSettings settings) =>
        CreatePlacementRule(acceptedWithoutCleanedMod, loadOrder, ViewOfLoadOrder(records, restored: []), shapes, settings);
}
