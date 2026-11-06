using System.Collections.Immutable;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

/// <summary>An object a changed-terrain check is made for: a copy carries its original's key.</summary>
internal readonly record struct TerrainChecked(RecordKey Key, bool IsCopy);

/// <summary>What the changed-terrain rule says about one pass's plan that the plan must keep, each list in the order of the objects.</summary>
/// <param name="Heights">The points whose height goes back that a finding counts, or that a stopped check reads.</param>
/// <param name="Textures">The quarters whose texture layers go back that hold a vertex a finding counts the type of, or that a stopped check reads.</param>
/// <param name="Water">
/// The cells whose water level goes back whose water a finding counts or a stopped check reads, each with the object of the first such
/// check.
/// </param>
/// <param name="Stops">Each object whose check stopped, with the failure.</param>
/// <param name="Findings">How many objects have a finding that counts.</param>
internal sealed record TerrainKeeping(
    List<LandGridPoint> Heights, List<(ExteriorCell Cell, int Quarter)> Textures, List<(RecordKey Cell, RecordKey By)> Water, List<(TerrainChecked Object, PartFailure Failure)> Stops, int Findings);

/// <summary>
/// Judges the cleaned mod's objects that stay and the copies the patch adds by the shared changed-terrain rule as the next patcher
/// reads the world: each with the settings it is judged with (a copy with those of the cleaned mod's own objects), over the load order
/// with the patch's terrain and water replaced by what a pass plans to write (<see cref="PlannedTerrainView"/>). A finding counts
/// only when the same check over the load order as read, with nothing restored, ran and found nothing; that baseline check is made
/// once per object, on its first ask. A check that stops, over the plan or the baseline, keeps everything the pass writes where the
/// check reads, and its failure is reported. Thread-safe.
/// </summary>
/// <param name="settingsOf">The settings the changed-terrain rule judges each target with, by its key.</param>
/// <param name="copySettings">The settings the step judges the cleaned mod's own objects with, and so a copy.</param>
internal sealed class StandingTerrainChecks(
    IChangedTerrainJudging rule,
    PluginName patch,
    PluginName cleanedMod,
    IReadOnlyDictionary<RecordKey, ChangedTerrainSettings> settingsOf,
    ChangedTerrainSettings copySettings,
    Execution execution)
{
    private readonly IObjectTerrainChecks _baselineChecks = rule.Over(rule.LoadOrder);

    /// <remarks>The settings are not part of the key: an object is always judged with the same ones, its group's or a copy's.</remarks>
    private readonly ComputedOncePerKey<TerrainChecked, TerrainCheckOutcome> _baseline = new(Publication.BuiltOnce, EqualityComparer<TerrainChecked>.Default);

    /// <summary>
    /// Each planned copy judged where its original stands now, on the baseline: the next run for the same cleaned mod would remove a
    /// copy the rule finds there, whatever the restore keeps, so such a copy, or one whose check stops, is refused.
    /// </summary>
    /// <param name="judged">Every placed record judged by the never-rules, its areas and the copy rule, in key order.</param>
    /// <returns><paramref name="judged"/> with each refused copy's refusal, in the same order.</returns>
    public PlacedChangeJudged[] JudgeCopiesOnTheBaseline(PlacedChangeJudged[] judged, IReadOnlyList<TargetObject> targets)
    {
        int[] planned = [.. Enumerable.Range(0, judged.Length).Where(index => judged[index].Cloning is { RefusedBy: null })];
        var checks = ParallelMap.Run(
            execution, planned.Length, position => Baseline(CloneCandidates.AsClone(targets[judged[planned[position]].Change.Target!.Value.Index]), copySettings), ParallelMap.AutomaticRangeSize);
        var result = judged.ToArray();
        for (var position = 0; position < planned.Length; position++)
        {
            var (index, check) = (planned[position], checks[position]);
            if (check is { Finding: null, Failure: null }) continue;
            result[index] = ChangeJudge.WithCloning(judged[index], judged[index].Cloning! with { RefusedBy = CloneRefusal.OnChangedTerrainAsCleanedModsObject, Failure = check.Failure });
        }
        return result;
    }

    /// <summary>
    /// The parts the pass's plan changes that the rule counts in a finding the baseline does not have, or that a stopped check reads,
    /// for the objects whose check reads a cell the plan writes.
    /// </summary>
    /// <param name="standing">The objects a later run judges where they stand, in the order findings are applied.</param>
    /// <param name="lands">Every LAND record with what the pass's plan writes.</param>
    /// <param name="cells">Every cell record with what the pass's plan writes.</param>
    public TerrainKeeping Find(IEnumerable<TargetObject> standing, ImmutableArray<LandChangeJudged> lands, ImmutableArray<CellChangeJudged> cells)
    {
        var written = new WrittenTerrain(lands, cells);
        var view = new PlannedTerrainView(rule.LoadOrder, patch, cleanedMod, written.PlannedLands(), written.PlannedWater(), written.PartsGivenBack(cleanedMod));
        List<(TargetObject Object, ChangedTerrainSettings Settings)> judged = [.. standing.Select(JudgedWith).OfType<(TargetObject, ChangedTerrainSettings)>()];
        var reading = rule.FindObjectsReading(judged, view.CellsWritten());
        var planned = rule.Over(view);
        var verdicts = ParallelMap.Run(execution, reading.Length, position => Judge(planned, judged[reading[position]]), ParallelMap.AutomaticRangeSize);
        var keeping = new TerrainKeeping([], [], [], [], verdicts.Count(verdict => verdict.Finding is not null));
        for (var position = 0; position < reading.Length; position++)
        {
            var (placed, settings) = judged[reading[position]];
            var keeper = placed.CloneOf ?? placed.Key;
            if (verdicts[position].Finding is { } finding) written.KeepCounted(keeping, finding, keeper);
            if (verdicts[position].Failure is { } failure)
            {
                written.KeepEverythingIn(keeping, rule.FindCellsRead(placed, settings), keeper);
                keeping.Stops.Add((KeyOf(placed), new PartFailure(RestoreWhereRemovedParts.StandingTerrainCheck, failure, SubjectOf(placed))));
            }
        }
        return keeping;
    }

    /// <summary>A copy carries its original's key, whose settings are those of another group; an object no group judges has none.</summary>
    private (TargetObject, ChangedTerrainSettings)? JudgedWith(TargetObject placed) =>
        (placed.CloneOf is not null ? copySettings : settingsOf.GetValueOrDefault(placed.Key)) is { Runs: true } settings ? (placed, settings) : null;

    /// <returns>
    /// The planned check's finding when the baseline check ran and found nothing; the failure of the planned check, or of the baseline
    /// check asked after a planned finding; neither otherwise.
    /// </returns>
    private TerrainCheckOutcome Judge(IObjectTerrainChecks planned, (TargetObject Object, ChangedTerrainSettings Settings) judged)
    {
        var check = planned.Check(judged.Object, judged.Settings);
        if (check.Finding is null) return check;
        return Baseline(judged.Object, judged.Settings) switch
        {
            { Failure: { } failure } => new TerrainCheckOutcome(Finding: null, failure),
            { Finding: not null } => default,
            _ => check,
        };
    }

    private TerrainCheckOutcome Baseline(TargetObject placed, ChangedTerrainSettings settings) => _baseline.Get(KeyOf(placed), () => _baselineChecks.Check(placed, settings));

    private static TerrainChecked KeyOf(TargetObject placed) => new(placed.Key, placed.CloneOf is not null);

    private static FailureSubject SubjectOf(TargetObject placed) => new(placed.Key.ToString(), placed.EditorId, placed.Id.Index);

    /// <summary>What the pass's plan writes into the LAND records and the exterior cells' water, by their cell.</summary>
    private sealed class WrittenTerrain(ImmutableArray<LandChangeJudged> lands, ImmutableArray<CellChangeJudged> cells)
    {
        /// <summary>By the cell of the land worldspace that holds the LAND record.</summary>
        private readonly ILookup<ExteriorCell, LandChangeJudged> _lands = lands.ToLookup(land => land.Change.Cell);

        /// <summary>The exterior cell records whose water level the plan writes, by their cell in their own worldspace.</summary>
        private readonly ILookup<ExteriorCell, CellChangeJudged> _water = cells
            .Where(cell => cell.Change.Grid is not null && WaterUndoPlan.WritesWaterLevel(cell))
            .ToLookup(cell => new ExteriorCell(cell.Change.SpaceKey, cell.Change.Grid!.Value.X, cell.Change.Grid.Value.Y));

        /// <remarks>Two LAND records at one cell never go back (<see cref="NeverRestoredReason.SharesItsGrid"/>), so a written cell has one.</remarks>
        public Dictionary<ExteriorCell, PlannedLand> PlannedLands() =>
            WrittenLands().ToDictionary(land => land.Change.Cell, land => new PlannedLand(land.FinalHeights, land.Textures, land.Rest));

        /// <summary>The parts the plan gives back of the LAND records and of the cells whose water level it writes, by their cell.</summary>
        public ILookup<ExteriorCell, RestoredPart> PartsGivenBack(PluginName cleanedMod) =>
            WrittenLands()
                .SelectMany(land => RestoredPartFacts.ListLand(land.Change, land.Heights, land.Textures, cleanedMod).Select(part => (land.Change.Cell, Part: part)))
                .Concat(_water.SelectMany(group => group.SelectMany(cell => RestoredPartFacts.ListCell(cell.Change, cell.Undone, cleanedMod)).Select(part => (Cell: group.Key, Part: part))))
                .ToLookup(given => given.Cell, given => given.Part);

        private IEnumerable<LandChangeJudged> WrittenLands() =>
            lands.Where(land => land.Never is null && (land.FinalHeights is not null || !land.Textures.IsEmpty || land.Rest));

        /// <remarks>Two cell records at one grid never go back (<see cref="NeverRestoredReason.SharesItsGrid"/>), so a written cell has one.</remarks>
        public Dictionary<ExteriorCell, CellWaterCopy> PlannedWater() => _water.ToDictionary(group => group.Key, group => group.Single().Change.EarlierWater);

        /// <summary>
        /// Keeps, in the finding's area, the restored heights it counted, the quarters going back holding a vertex whose type it counted
        /// and the water levels it counted.
        /// </summary>
        /// <remarks>
        /// Where the accepted height is the restored one (the ground as shown, a trusted earlier version, or the plan's own given-back
        /// heights), the restored heights alone can put a point under another mod's water or out of it, and the finding then holds no
        /// height. The baseline has no such finding, so each covered or uncovered point comes from the restored heights, the restored
        /// water level, or both. While the plan writes the cell's water level, only that level is kept; a later pass judges the plan
        /// with it kept, and the restored heights at the points still covered or uncovered are kept then.
        /// </remarks>
        public void KeepCounted(TerrainKeeping keeping, ObjectOnChangedTerrain finding, RecordKey keeper)
        {
            foreach (var counted in finding.Area)
            {
                var waterWritten = _water[counted.WaterCell].ToList();
                ImmutableArray<int> flippedByHeights = waterWritten.Count == 0 ? counted.WaterFlipped : [];
                foreach (var land in _lands[counted.LandCell])
                {
                    keeping.Heights.AddRange(
                        counted.HeightVertices.Concat(flippedByHeights).Distinct().Where(land.Heights.Contains).Select(vertex => new LandGridPoint(counted.LandCell, vertex)));
                    keeping.Textures.AddRange(counted.TypeVertices.SelectMany(TerrainQuadrants.Holding).Distinct().Where(land.Textures.Contains).Select(quarter => (counted.LandCell, quarter)));
                }
                if (!counted.WaterFlipped.IsEmpty) keeping.Water.AddRange(waterWritten.Select(cell => (cell.Change.Cell, keeper)));
            }
        }

        /// <summary>Keeps every height, quarter texture and water level the plan writes in the cells.</summary>
        /// <remarks>The cells' order does not matter: every part kept here has the same reason and keeper.</remarks>
        public void KeepEverythingIn(TerrainKeeping keeping, IReadOnlySet<ExteriorCell> read, RecordKey keeper)
        {
            foreach (var cell in read)
            {
                foreach (var land in _lands[cell])
                {
                    keeping.Heights.AddRange(land.Heights.Points.Select(point => new LandGridPoint(cell, point)));
                    keeping.Textures.AddRange(land.Textures.Quarters.Select(quarter => (cell, quarter)));
                }
                keeping.Water.AddRange(_water[cell].Select(written => (written.Change.Cell, keeper)));
            }
        }
    }
}
