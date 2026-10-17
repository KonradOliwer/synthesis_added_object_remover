using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>
/// Compares the version of a cell's terrain the game shows with the cell's reference terrain, vertex by vertex, and finds the
/// plugin whose own edit made each difference. Thread-safe.
/// </summary>
internal sealed class CellTerrainComparison(TerrainPlugins plugins, TerrainTypes types, ChangedTerrainSettings settings)
{
    private readonly TerrainDifference _difference = new(settings);

    /// <param name="versions">The cell's versions in load order.</param>
    /// <returns>Null when the cell has no reference terrain.</returns>
    public CellTerrain? Compare(TerrainVersion[] versions)
    {
        if (plugins.ReferenceIn(versions) is not { } referenceIndex) return null;

        // Only the versions loading after the reference can change it: the reference overrides the earlier ones.
        var versionsFromReference = versions[referenceIndex..].Select(ToTraced).ToArray();
        return new CellTerrain(versionsFromReference[0].Version.Heights, versionsFromReference[^1].Version.Heights, ChangesOf(versionsFromReference));
    }

    private TracedVersion ToTraced(TerrainVersion version) =>
        new(
            new ComparedVersion(version.Plugin, version.Heights, settings.CountTerrainTypeChanges ? types.AtEveryVertex(version) : null),
            plugins.CountsAsChanging(version.Plugin) ? plugins.PositionOf(version.Plugin) : null);

    /// <param name="versionsFromReference">The reference first, then every later version in load order; the last one is the version the game shows.</param>
    private VertexChange[]? ChangesOf(TracedVersion[] versionsFromReference)
    {
        VertexChange[]? changes = null;
        for (var vertex = 0; vertex < CellTerrain.VertexCount; vertex++)
        {
            var shown = _difference.At(versionsFromReference[0].Version, versionsFromReference[^1].Version, vertex);
            if (!shown.Any) continue;
            var change = FindChangersAt(versionsFromReference, shown, vertex);
            if (!change.Any) continue;
            changes ??= new VertexChange[CellTerrain.VertexCount];
            changes[vertex] = change;
        }
        return changes;
    }

    /// <summary>
    /// Height and terrain type are traced apart, so a non-counting plugin changing one of them on top of a counting plugin's
    /// change of the other does not hide that change.
    /// </summary>
    private VertexChange FindChangersAt(TracedVersion[] versionsFromReference, TerrainChange shown, int vertex) =>
        new(
            shown.Height ? FindOriginOf(versionsFromReference, vertex, static change => change.Height).ChangerPosition : null,
            shown.TerrainType ? FindOriginOf(versionsFromReference, vertex, static change => change.TerrainType).ChangerPosition : null);

    /// <summary>
    /// The version whose own edit gave the shown value: the first of the last versions that all carry the shown change. When that
    /// version does not count and an earlier version already carried the change, it took that version's edit back in (as a patch
    /// combining several mods' changes does), and the earlier version is traced instead.
    /// </summary>
    /// <remarks>
    /// A version carries the change only when its own value differs from the reference beyond the check's tolerance, so small
    /// steps within the height tolerance cannot add up to a change that no version made. The reference never differs from
    /// itself, so the origin is always a version after it.
    /// </remarks>
    /// <param name="check">Selects the check traced: height or terrain type.</param>
    private TracedVersion FindOriginOf(TracedVersion[] versionsFromReference, int vertex, Func<TerrainChange, bool> check)
    {
        var origin = FindStartOfCarryingRun(versionsFromReference, versionsFromReference.Length - 1, vertex, check);
        while (TakesAnEarlierEditBackIn(versionsFromReference, origin, vertex, check, out var earlier))
        {
            origin = FindStartOfCarryingRun(versionsFromReference, earlier, vertex, check);
        }
        return versionsFromReference[origin];
    }

    /// <returns>The first of the versions up to <paramref name="end"/> that all carry the shown change.</returns>
    private int FindStartOfCarryingRun(TracedVersion[] versionsFromReference, int end, int vertex, Func<TerrainChange, bool> check)
    {
        var start = end;
        while (CarriesShownChange(versionsFromReference, start - 1, vertex, check)) start--;
        return start;
    }

    /// <summary>A non-counting version takes an earlier edit back in when an earlier version already carried the shown change.</summary>
    private bool TakesAnEarlierEditBackIn(TracedVersion[] versionsFromReference, int origin, int vertex, Func<TerrainChange, bool> check, out int earlier)
    {
        earlier = 0;
        if (versionsFromReference[origin].ChangerPosition != null || FindLastCarrierBefore(versionsFromReference, origin, vertex, check) is not { } carrier) return false;
        earlier = carrier;
        return true;
    }

    /// <returns>The last version after the reference and before <paramref name="end"/> that carries the shown change; null when none does.</returns>
    private int? FindLastCarrierBefore(TracedVersion[] versionsFromReference, int end, int vertex, Func<TerrainChange, bool> check)
    {
        for (var index = end - 1; index > 0; index--)
        {
            if (CarriesShownChange(versionsFromReference, index, vertex, check)) return index;
        }
        return null;
    }

    /// <summary>The version differs from the reference beyond the check's tolerance and matches the shown version within it.</summary>
    private bool CarriesShownChange(TracedVersion[] versionsFromReference, int index, int vertex, Func<TerrainChange, bool> check) =>
        check(_difference.At(versionsFromReference[0].Version, versionsFromReference[index].Version, vertex))
        && !check(_difference.At(versionsFromReference[^1].Version, versionsFromReference[index].Version, vertex));

    /// <param name="ChangerPosition">The plugin's load-order position when it counts as changing the terrain; null when it does not.</param>
    private readonly record struct TracedVersion(ComparedVersion Version, int? ChangerPosition);
}
