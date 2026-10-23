using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Compares the water of a cell the game shows with the accepted water, and names the plugin that changed it. Thread-safe.
/// </summary>
/// <remarks>
/// A cell's water is made of three parts: the cell's "has water" flag and its own height, held by the cell's versions, and the
/// default height, held by its worldspace's versions. Each part is accepted on its own, since a plugin's version of the worldspace
/// says nothing about the cell and the other way round. The accepted water is made of the accepted parts by the same rule as the
/// water the game shows is made of the parts it shows.
/// </remarks>
internal sealed class CellWaterComparison(TerrainPlugins plugins, ChangedTerrainSettings settings)
{
    /// <summary>The parts in the order the game's rule reads them, as <see cref="DecideParts"/> returns them.</summary>
    private const int FlagPart = 0;
    private const int OwnLevelPart = 1;
    private const int DefaultLevelPart = 2;

    /// <summary>The positions of the cell's properties in the results of <see cref="AcceptedVersions.Decide"/>.</summary>
    private const int FlagProperty = 0;
    private const int OwnLevelProperty = 1;

    /// <returns>
    /// Null when the cell's water is not checked, because a part it is made of has no accepted version or no version at all, or
    /// when the water the game shows differs from the accepted water by no counted change.
    /// </returns>
    public CellWaterChange? Compare(WaterCopies copies)
    {
        var parts = DecideParts(copies);
        if (Combine(copies, parts, static part => part.Acceptance.Accepted) is not { } accepted
            || Combine(copies, parts, static part => part.Shown) is not { } shown)
        {
            return null;
        }

        var kind = FindChangeBetween(accepted.Level, shown.Level);
        return IsCounted(kind) ? new CellWaterChange(accepted.Level, shown.Level, FindChanger(parts, shown), kind) : null;
    }

    /// <returns>The flag, the own level and the default level, in that order.</returns>
    private WaterPart[] DecideParts(WaterCopies copies)
    {
        PluginName[] cellPlugins = [.. copies.Cell.Select(static copy => copy.Plugin)];
        PluginName[] worldspacePlugins = [.. copies.Worldspace.Select(static copy => copy.Plugin)];
        var cell = AcceptedVersions.Decide(
            TrustsOf(cellPlugins),
            [
                (one, other) => copies.Cell[one].HasWater != copies.Cell[other].HasWater,
                (one, other) => LevelsDiffer(copies.Cell[one].OwnLevel, copies.Cell[other].OwnLevel),
            ]);
        var worldspace = AcceptedVersions.Decide(
            TrustsOf(worldspacePlugins),
            [(one, other) => LevelsDiffer(copies.Worldspace[one].DefaultLevel, copies.Worldspace[other].DefaultLevel)]);
        return [new(cellPlugins, cell[FlagProperty]), new(cellPlugins, cell[OwnLevelProperty]), new(worldspacePlugins, worldspace.Single())];
    }

    private VersionTrust[] TrustsOf(PluginName[] versionPlugins) => [.. versionPlugins.Select(plugins.TrustOf)];

    /// <summary>
    /// The game's rule, reading the parts in order: a cell without the "has water" flag has no water, whatever height it stores;
    /// with it, the cell's own height, else its worldspace's default height. A worldspace version that stores no default height has
    /// no default water, so a cell using it has no water.
    /// </summary>
    /// <param name="versionOf">The index of the version a part is read from; null where there is none.</param>
    /// <returns>Null when a part the rule reads has no version.</returns>
    private Water? Combine(WaterCopies copies, WaterPart[] parts, Func<WaterPart, int?> versionOf)
    {
        if (versionOf(parts[FlagPart]) is not { } flag) return null;
        if (!copies.Cell[flag].HasWater) return new Water(Level: null, FlagPart);
        if (versionOf(parts[OwnLevelPart]) is not { } own) return null;
        if (copies.Cell[own].OwnLevel is { } ownLevel) return new Water(ownLevel, OwnLevelPart);
        return versionOf(parts[DefaultLevelPart]) is { } defaultLevel ? new Water(copies.Worldspace[defaultLevel].DefaultLevel, DefaultLevelPart) : null;
    }

    /// <summary>
    /// The changed parts the shown water is made of each name a plugin, and the later loading one is named; a part whose shown value
    /// only the patch holds names the patch.
    /// </summary>
    /// <returns>The load-order position of the plugin named for the change.</returns>
    private int FindChanger(WaterPart[] parts, Water shown) =>
        parts.Take(shown.FixingPart + 1).Where(static part => part.Acceptance.Changed).Max(part => plugins.PositionOf(part.ChangingPlugin));

    private bool LevelsDiffer(float? one, float? other) => FindChangeBetween(one, other) != TerrainChangeKind.None;

    /// <summary>A water height change counts only beyond the tolerance; water added or removed always differs.</summary>
    /// <returns><see cref="TerrainChangeKind.None"/> when the levels do not differ.</returns>
    private TerrainChangeKind FindChangeBetween(float? before, float? after) => (before, after) switch
    {
        (null, null) => TerrainChangeKind.None,
        (null, _) => TerrainChangeKind.WaterAdded,
        (_, null) => TerrainChangeKind.WaterRemoved,
        ({ } beforeLevel, { } afterLevel) =>
            MathF.Abs(afterLevel - beforeLevel) > settings.WaterHeightTolerance ? TerrainChangeKind.WaterHeight : TerrainChangeKind.None,
    };

    private bool IsCounted(TerrainChangeKind kind) => kind switch
    {
        TerrainChangeKind.WaterHeight => settings.CountWaterHeightChanges,
        TerrainChangeKind.WaterAdded or TerrainChangeKind.WaterRemoved => settings.CountWaterAddedOrRemoved,
        _ => false,
    };

    /// <summary>One part of a cell's water, decided over the versions that hold it.</summary>
    /// <param name="Plugins">The plugins of the versions, in load order; the last one's value is the one the game shows.</param>
    private sealed record WaterPart(PluginName[] Plugins, PropertyAcceptance Acceptance)
    {
        /// <summary>The index of the version the game shows; null where no plugin has a version.</summary>
        public int? Shown => Plugins.Length == 0 ? null : Plugins.Length - 1;

        /// <summary>
        /// The plugin of the changing version; where only the patch holds the shown value (an earlier patcher of the group made the
        /// change), the patch, the plugin of the version the game shows.
        /// </summary>
        public PluginName ChangingPlugin => Acceptance.Changing is { } changing ? Plugins[changing] : Plugins[^1];
    }

    /// <summary>A cell's water as made of its parts.</summary>
    /// <param name="Level">The height of the water surface; null where the cell has no water.</param>
    /// <param name="FixingPart">The last part the rule read, which fixes the level; the water is made of it and the parts before it.</param>
    private readonly record struct Water(float? Level, int FixingPart);
}
