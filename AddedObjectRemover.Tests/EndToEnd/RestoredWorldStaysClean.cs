using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>
/// Runs a scene, then runs it again as the next patcher of the group, with the first run's patch imported as the patch it adds to and
/// the same settings, and checks that what the first run restored gives no removal rule a reason to act (decision 20, rule 3):
/// <list type="bullet">
/// <item>(a) the second run removes nothing: every target that stood after the first run still stands;</item>
/// <item>(b) the step for other mods' objects takes away nothing in the second run;</item>
/// <item>(c) the ground the first patch wrote does not move farther from an object that stands where it stood and was placed for the
/// ground as it is (the cleaned mod's and later plugins' objects): ground within reach of the object before the patch is not moved
/// by more than the height tolerance to a height farther from the object, or through the object to its other side. Ground moving
/// toward the object, to its height or short of it, is the restore's aim (a plugin that loads without the cleaned mod placed its
/// objects for the base game's ground, which the restore gives back). A
/// value the patch restores exactly counts as unchanged for the rules, so floating and sinking are checked on their own.</item>
/// </list>
/// The second run's own restore decisions are not judged. A second run that stops because the first removed every object of the
/// cleaned mod judges nothing, so only (c) checks that scene.
/// </summary>
internal static class RestoredWorldStaysClean
{
    /// <summary>How far above or below an object's origin ground counts as reaching it: the fixtures' objects stand on their base and are crate-sized.</summary>
    private const float GroundReach = FixtureMeshes.CrateSize;

    private const int Side = TerrainGrid.VerticesPerSide;

    /// <returns>Whether the second run judged the world, so (a) and (b) were checked; false when it stopped because nothing of the cleaned mod was left.</returns>
    public static bool Check(Settings settings, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, float heightTolerance)
    {
        var folder = Directory.CreateTempSubdirectory("aor-second-run-");
        try
        {
            var firstPatchFile = Path.Combine(folder.FullName, FixturePatcherState.PatchModKey.FileName);
            IReadOnlyList<ISkyrimMod> mods = [];
            ISkyrimModGetter? firstPatch = null;
            var first = FixtureRun.Execute(
                settings,
                workers: 1,
                dataFolder => mods = createLoadOrder(dataFolder),
                inspectPatch: patch =>
                {
                    firstPatch = patch;
                    patch.WriteToBinary(firstPatchFile);
                });
            var second = FixtureRun.Attempt(settings, workers: 1, createLoadOrder, earlierPatchFile: firstPatchFile);

            var judged = second is RunResult.Done;
            if (second is RunResult.Done { Outcome: var outcome }) AssertNothingTakenAway(outcome);
            else Assert.Equal(StopKind.NoTargetObjects, ((RunResult.Stopped)second).Reason.Kind);
            var moved = FindGroundMovedUnderWhatStands(first, mods, firstPatch!, heightTolerance);
            Assert.True(moved.Count == 0, $"The first patch moved the ground under: {string.Join("; ", moved)}");
            return judged;
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>(a) and (b).</summary>
    private static void AssertNothingTakenAway(RunOutcome second)
    {
        Assert.True(second.Removals.IsEmpty, $"The second run removed: {string.Join(", ", second.Removals.Select(removal => second.Collected.Targets[removal.TargetIndex].Key))}");
        var takenAway = second.OtherModsObjects.TakenAway();
        Assert.True(takenAway.Count == 0, $"The second run took away: {string.Join(", ", takenAway.Keys.Select(id => second.Collected.Other(id).Key))}");
    }

    /// <returns>One line for each object under which the ground moved, naming the point and both heights.</returns>
    private static List<string> FindGroundMovedUnderWhatStands(RunOutcome first, IReadOnlyList<ISkyrimMod> mods, ISkyrimModGetter patch, float heightTolerance)
    {
        var lands = WrittenLands(patch).ToList();
        if (lands.Count == 0) return [];
        var standing = ObjectsPlacedForTheGroundAsItIs(first, mods).ToList();
        List<string> moved = [];
        foreach (var (worldspace, cellX, cellY, land) in lands)
        {
            var after = LandHeightReader.Read(land);
            var before = InputWinnerOf(mods, land.FormKey) is { } winner ? LandHeightReader.Read(winner) : null;
            if (after is null || before is null) continue;
            foreach (var (key, _, position) in standing.Where(entry => entry.Space == worldspace))
            {
                foreach (var point in PointsAround(position, cellX, cellY))
                {
                    var (was, now) = (before[point], after[point]);
                    if (MathF.Abs(now - was) > heightTolerance && MathF.Abs(was - position.Z) <= GroundReach && LeftTheObject(was - position.Z, now - position.Z, heightTolerance))
                    {
                        moved.Add($"{key} at point {point} of cell {cellX},{cellY}: {was} to {now}");
                    }
                }
            }
        }
        return moved;
    }

    /// <summary>
    /// Whether ground that lay the first height above the object (negative: below it) lies farther from it afterwards, or passed
    /// through it and ends more than the tolerance on the other side, such as buried 20 and then floating 15.
    /// </summary>
    internal static bool LeftTheObject(float heightBefore, float heightNow, float heightTolerance) =>
        MathF.Abs(heightNow) > MathF.Abs(heightBefore) || (MathF.Sign(heightBefore) != MathF.Sign(heightNow) && MathF.Abs(heightNow) > heightTolerance);

    /// <summary>
    /// The cleaned mod's objects that stand after the first run where they stood, and the objects of plugins loading after it that
    /// stand where they stood: each with its space and position. A candidate the restore moved stands elsewhere, so it is not among them.
    /// </summary>
    private static IEnumerable<(RecordKey Key, RecordKey Space, Vector3 Position)> ObjectsPlacedForTheGroundAsItIs(RunOutcome first, IReadOnlyList<ISkyrimMod> mods)
    {
        var final = first.Final;
        var moves = final.Moves.Select(move => move.Target).ToHashSet();
        var restored = first.RestoreWhereRemoved.Objects.Where(judged => judged.IsRestored).Select(judged => judged.Change.Key).ToHashSet();
        var takenAway = first.OtherModsObjects.TakenAway();
        var cleanedMod = new PluginName(FixtureWorld.TargetName);
        var after = mods.Select(mod => mod.ModKey.ToPluginName()).SkipWhile(plugin => plugin != cleanedMod).Skip(1).ToHashSet();
        var targets = first.Collected.Targets
            .Where(target => target.Origin == TargetOrigin.CleanedMod && !final.IsRemoved(target.Id) && !moves.Contains(target.Id))
            .Select(target => (target.Key, target.SpaceKey, target.Transform.Position));
        var others = first.Collected.OtherModAndSupportOnlyObjects
            .Where(other => after.Contains(other.Key.Plugin) && !takenAway.ContainsKey(other.Id) && !restored.Contains(other.Key))
            .Select(other => (other.Key, other.SpaceKey, other.Position));
        return targets.Concat(others);
    }

    /// <summary>Each LAND record the patch writes, with the worldspace and grid of its cell.</summary>
    private static IEnumerable<(RecordKey Worldspace, int CellX, int CellY, ILandscapeGetter Land)> WrittenLands(ISkyrimModGetter patch) =>
        from worldspace in patch.Worldspaces
        from block in worldspace.SubCells
        from subBlock in block.Items
        from cell in subBlock.Items
        where cell.Landscape is not null && cell.Grid is not null
        select (worldspace.FormKey.ToRecordKey(), cell.Grid!.Point.X, cell.Grid.Point.Y, cell.Landscape!);

    /// <summary>The version of the LAND record that wins among the listed mods, before the patch.</summary>
    private static ILandscapeGetter? InputWinnerOf(IReadOnlyList<ISkyrimMod> mods, FormKey land) =>
        mods.Reverse().SelectMany(mod => mod.EnumerateMajorRecords<ILandscapeGetter>()).FirstOrDefault(version => version.FormKey == land);

    /// <summary>The grid points of the cell at the corners of the square of the grid the position lies in; none when it lies outside the cell.</summary>
    private static IEnumerable<int> PointsAround(Vector3 position, int cellX, int cellY)
    {
        var x = (position.X - cellX * ExteriorGrid.CellSize) / TerrainGrid.VertexSpacing;
        var y = (position.Y - cellY * ExteriorGrid.CellSize) / TerrainGrid.VertexSpacing;
        if (x < 0 || y < 0 || x > Side - 1 || y > Side - 1) return [];
        var (left, bottom) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
        var (right, top) = (Math.Min(left + 1, Side - 1), Math.Min(bottom + 1, Side - 1));
        return new[] { (left, bottom), (right, bottom), (left, top), (right, top) }.Distinct().Select(corner => corner.Item2 * Side + corner.Item1);
    }
}
