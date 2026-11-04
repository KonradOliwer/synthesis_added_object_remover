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
/// value the patch restores exactly counts as unchanged for the rules, so floating and sinking are checked on their own. The copies
/// the first patch added count as objects that stand where they stood.</item>
/// <item>(d) every copy the first patch added is read back as one of the cleaned mod's objects, a copy of its original;</item>
/// <item>(e) the second run adds no copy and puts back no original that went back with a copy;</item>
/// <item>(f) the changed-terrain step finds no object on ground textures or water that it did not find there in the first run;</item>
/// <item>(g) every placed record the first run put back is read back as support only: neither a target nor an object that can
/// cause removals.</item>
/// </list>
/// The second run's own restore decisions are not judged otherwise. A second run that stops because the first removed every object of
/// the cleaned mod judges nothing, so only (c) checks that scene.
/// </summary>
internal static class RestoredWorldStaysClean
{
    /// <summary>How far above or below an object's origin ground counts as reaching it: the fixtures' objects stand on their base and are crate-sized.</summary>
    private const float GroundReach = FixtureMeshes.CrateSize;

    private const int Side = TerrainGrid.VerticesPerSide;

    private const TerrainChangeKind TexturesAndWater =
        TerrainChangeKind.TerrainType | TerrainChangeKind.WaterHeight | TerrainChangeKind.WaterAdded | TerrainChangeKind.WaterRemoved;

    /// <returns>Whether the second run judged the world, so (a), (b), (d), (e), (f) and (g) were checked; false when it stopped because nothing of the cleaned mod was left.</returns>
    public static bool Check(Settings settings, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, float heightTolerance)
    {
        var (first, mods, firstPatch, second) = RunTwice(settings, createLoadOrder, settings, createLoadOrder);
        var judged = second is RunResult.Done;
        if (second is RunResult.Done { Outcome: var outcome })
        {
            AssertNothingTakenAway(outcome);
            AssertEveryCopyReadBackAsTheCleanedModsObject(first, outcome);
            AssertNoCopyAddedAgain(first, outcome);
            AssertNoNewObjectOnChangedTexturesOrWater(first, outcome);
            AssertEveryRecordThatWentBackReadAsSupportOnly(first, outcome);
        }
        else
        {
            Assert.Equal(StopKind.NoTargetObjects, ((RunResult.Stopped)second).Reason.Kind);
        }
        var moved = FindGroundMovedUnderWhatStands(first, mods, firstPatch, heightTolerance);
        Assert.True(moved.Count == 0, $"The first patch moved the ground under: {string.Join("; ", moved)}");
        return judged;
    }

    /// <summary>
    /// Runs a scene, then a step for another cleaned mod in the same group, on a load order that may add that mod: the second step removes
    /// nothing, and reads every copy the first patch added as a copy of another cleaned mod, which causes nothing.
    /// </summary>
    /// <returns>The copies the first patch added.</returns>
    public static int CheckWithAnotherCleanedMod(
        Settings settings, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, Settings secondSettings, Func<string, IReadOnlyList<ISkyrimMod>> createSecondLoadOrder)
    {
        var (first, _, _, second) = RunTwice(settings, createLoadOrder, secondSettings, createSecondLoadOrder);
        var outcome = ((RunResult.Done)second).Outcome;

        AssertNothingTakenAway(outcome);
        Assert.Equal((0, first.Written.Clones.Length), (outcome.Collected.Counts.ClonesOfCleanedMod, outcome.Collected.Counts.ClonesOfOtherCleanedMods));
        Assert.DoesNotContain(outcome.Collected.Targets, target => target.CloneOf is not null);
        return first.Written.Clones.Length;
    }

    /// <summary>Runs the first settings, then the second with the first run's patch imported as the patch it adds to.</summary>
    private static (RunOutcome First, IReadOnlyList<ISkyrimMod> Mods, ISkyrimModGetter FirstPatch, RunResult Second) RunTwice(
        Settings settings, Func<string, IReadOnlyList<ISkyrimMod>> createLoadOrder, Settings secondSettings, Func<string, IReadOnlyList<ISkyrimMod>> createSecondLoadOrder)
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
            var second = FixtureRun.Attempt(secondSettings, workers: 1, createSecondLoadOrder, earlierPatchFile: firstPatchFile);
            return (first, mods, firstPatch!, second);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>(a) and (b): nothing removed, fitted, moved or taken away.</summary>
    private static void AssertNothingTakenAway(RunOutcome second)
    {
        Assert.True(second.Removals.IsEmpty, $"The second run removed: {string.Join(", ", second.Removals.Select(removal => second.Collected.Targets[removal.TargetIndex].Key))}");
        Assert.Equal((0, 0), (second.Written.Fitted, second.Written.Moved));
        var takenAway = second.OtherModsObjects.TakenAway();
        Assert.True(takenAway.Count == 0, $"The second run took away: {string.Join(", ", takenAway.Keys.Select(id => second.Collected.Other(id).Key))}");
    }

    /// <summary>(d).</summary>
    private static void AssertEveryCopyReadBackAsTheCleanedModsObject(RunOutcome first, RunOutcome second)
    {
        var copies = second.Collected.Targets.Where(target => target.CloneOf is not null).Select(target => (Copy: target.Key, Original: target.CloneOf!.Value, target.Origin));
        Assert.Equal(
            first.Written.Clones.Select(clone => (clone.Copy, clone.Original, Origin: TargetOrigin.CleanedMod)).OrderBy(copy => copy.Copy.ToString(), StringComparer.Ordinal),
            copies.OrderBy(copy => copy.Copy.ToString(), StringComparer.Ordinal));
        Assert.Equal(first.Written.Clones.Length, second.Collected.Counts.ClonesOfCleanedMod);
    }

    /// <summary>(e).</summary>
    private static void AssertNoCopyAddedAgain(RunOutcome first, RunOutcome second)
    {
        Assert.Empty(second.Written.Clones);
        var originals = first.Written.Clones.Select(clone => clone.Original).ToHashSet();
        Assert.DoesNotContain(second.RestoreWhereRemoved.Objects, judged => judged.IsRestored && originals.Contains(judged.Change.Key));
    }

    /// <summary>(g).</summary>
    private static void AssertEveryRecordThatWentBackReadAsSupportOnly(RunOutcome first, RunOutcome second)
    {
        var judgedAgain = second.Collected.Targets.Select(target => target.Key).Concat(second.Collected.OtherModObjects.Select(other => other.Key)).ToHashSet();
        var read = first.RestoreWhereRemoved.Objects.Where(judged => judged.IsRestored && judgedAgain.Contains(judged.Change.Key)).Select(judged => judged.Change.Key).ToList();
        Assert.True(read.Count == 0, $"The second run reads as a target or a cause what the first run put back: {string.Join(", ", read)}");
    }

    /// <summary>(f).</summary>
    private static void AssertNoNewObjectOnChangedTexturesOrWater(RunOutcome first, RunOutcome second)
    {
        var before = HitsOnTexturesOrWater(first).ToHashSet();
        var found = HitsOnTexturesOrWater(second).Where(key => !before.Contains(key)).ToList();
        Assert.True(found.Count == 0, $"The second run found on changed ground textures or water: {string.Join(", ", found)}");
    }

    private static IEnumerable<RecordKey> HitsOnTexturesOrWater(RunOutcome run) =>
        run.ChangedTerrain.Passes.SelectMany(pass => pass.Hits).Where(hit => (hit.Kind & TexturesAndWater) != 0).Select(hit => run.Collected.Targets[hit.Target.Index].Key);

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
        var copyOf = first.Written.Clones.ToDictionary(clone => clone.Original, clone => clone.Copy);
        var copies = first.RestoreWhereRemoved.Objects
            .Where(judged => copyOf.ContainsKey(judged.Change.Key))
            .Select(judged => (copyOf[judged.Change.Key], judged.Change.SpaceKey, judged.Change.WhereItIsNow!.Value.Position));
        return targets.Concat(others).Concat(copies);
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
