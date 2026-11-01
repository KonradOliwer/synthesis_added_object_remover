using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>What a placed record that goes back has put back, by the first of these that applies.</summary>
internal enum PlacedRestoreKind
{
    OwnModel,
    ShownAgain,
    HiddenAgain,
    MovedBack,
    TurnedOrRescaledBack,
}

/// <summary>A navmesh the step judged and the group it goes back or stays with; the group is null for a navmesh that is no candidate.</summary>
internal sealed record NavmeshOutcome(NavmeshChangeJudged Judged, NavmeshGroupJudged? Group)
{
    public bool IsRestored => Group is { IsRestored: true };
}

/// <summary>
/// One quarter of a cell's terrain: what the cleaned mod changed in it, what goes back, what a later plugin changed again and
/// which points stay and why. A point on the line between quarters counts in each of them. In a cleared quarter every height
/// the cleaned mod last changed goes back or is kept for a reason; a quarter whose area is not cleared keeps all of it for that.
/// </summary>
/// <param name="Judged">The quarter's area; null when the land never goes back.</param>
internal sealed record LandQuarterFacts(
    int Quarter,
    LandQuarterJudged? Judged,
    int Heights,
    int Colours,
    bool Textures,
    int RestoredHeights,
    int RestoredColours,
    bool RestoredTextures,
    int LaterHeights,
    int LaterColours,
    bool LaterTextures,
    ImmutableArray<(TerrainKeptReason Reason, int Points)> Kept)
{
    /// <summary>The cleaned mod last changed a height, colour or texture layer in the quarter that could go back. The rest of the record belongs to no quarter.</summary>
    public bool HoldsChange => Heights > 0 || Colours > 0 || Textures;

    public bool Restores => RestoredHeights > 0 || RestoredColours > 0 || RestoredTextures;
}

/// <summary>What became of each change the step judged, as the log and the report files both say it.</summary>
internal static class RestoreOutcomes
{
    public static PlacedRestoreKind KindOf(RecordChangedByCleanedMod change)
    {
        var undo = change.Earlier.Undo;
        if (undo.HasFlag(PlacedParts.BaseAndData)) return PlacedRestoreKind.OwnModel;
        if (undo.HasFlag(PlacedParts.Presence)) return change.PresenceBefore.Removed ? PlacedRestoreKind.HiddenAgain : PlacedRestoreKind.ShownAgain;
        if (undo.HasFlag(PlacedParts.Position)) return PlacedRestoreKind.MovedBack;
        if (undo.HasFlag(PlacedParts.Rotation) || undo.HasFlag(PlacedParts.Scale)) return PlacedRestoreKind.TurnedOrRescaledBack;
        throw new UnreachableException($"The placed record {change.Key} goes back with {undo}, which is no restored kind.");
    }

    /// <returns>The areas the record was judged in: around its old place, and for a target where it stands now.</returns>
    public static IEnumerable<AreaShare> AreasOf(PlacedChangeJudged judged) => new[] { judged.OldPlace, judged.WhereItIsNow }.OfType<AreaShare>();

    /// <summary>The record stays only because of its area: no never-rule or guard keeps it.</summary>
    public static bool IsKeptForArea(PlacedChangeJudged judged) => !judged.IsRestored && judged.Never is null && judged.Refused is null;

    /// <returns>The verdict of the area that keeps a record that <see cref="IsKeptForArea"/>.</returns>
    public static AreaVerdict AreaKeeping(PlacedChangeJudged judged) => KeepingVerdict(AreasOf(judged).Select(area => area.Verdict), $"The placed record {judged.Change.Key}");

    /// <summary>Of the verdicts of the areas a change was judged in, the one that keeps it: a share that still stands wins over no objects around.</summary>
    /// <param name="change">Names the change in the bug message when every area is cleared.</param>
    private static AreaVerdict KeepingVerdict(IEnumerable<AreaVerdict> verdicts, string change)
    {
        var list = verdicts.ToList();
        return list.Contains(AreaVerdict.StillStanding) ? AreaVerdict.StillStanding
            : list.Contains(AreaVerdict.NoObjectsAround) ? AreaVerdict.NoObjectsAround
            : throw new UnreachableException($"{change} stays for its area with every area cleared.");
    }

    /// <summary>Names a record as "EditorId [key]" when it is a placed object of the world with an editor ID, else by its key.</summary>
    public static Func<RecordKey, string> LabelsIn(CollectedObjects world)
    {
        var editorIds = world.Targets.Select(target => (target.Key, target.EditorId))
            .Concat(world.OtherModAndSupportOnlyObjects.Select(other => (other.Key, other.EditorId)))
            .Where(placed => placed.EditorId is not null)
            .ToDictionary(placed => placed.Key, placed => placed.EditorId);
        return key => RecordLabels.Of(key, editorIds.GetValueOrDefault(key));
    }

    /// <returns>Why the record stays as the cleaned mod changed it; null when it goes back.</returns>
    /// <param name="label">Names the record a guard found.</param>
    public static string? KeptBecause(PlacedChangeJudged judged, Func<RecordKey, string> label)
    {
        if (judged.IsRestored) return null;
        if (judged.Never is { } never) return RestoreWhereRemovedText.Never(never);
        return judged.Refused is { } refusal ? RestoreWhereRemovedText.Refusal(refusal, label) : RestoreWhereRemovedText.Area(AreaKeeping(judged));
    }

    /// <returns>The parts of the cell that go back, in the order of the cell's parts, a region part once.</returns>
    public static ImmutableArray<CellPart> UndoneParts(CellChangeJudged judged) => [.. judged.Undone.Select(part => part.Name.Part).Distinct()];

    /// <returns>The parts of the cell that a later plugin changed again, each with that plugin, in the order of the cell's parts.</returns>
    public static IEnumerable<(string Part, PluginName Plugin)> LeftToLaterPlugins(CellChangedByCleanedMod change) =>
        change.Parts
            .Where(part => part.Outcome is PartOutcome.LeftToLaterPlugin)
            .Select(part => (RestoreWhereRemovedText.PartOfCell(part.Name.Part), ((PartOutcome.LeftToLaterPlugin)part.Outcome).Plugin))
            .Distinct();

    /// <summary>The cell stays only because of its area: no never-rule keeps it.</summary>
    public static bool IsKeptForArea(CellChangeJudged judged) => !judged.IsRestored && judged.Never is null && judged.Area is { Verdict: not AreaVerdict.Cleared };

    /// <returns>Why the cell keeps what the cleaned mod changed; null when something of it goes back.</returns>
    /// <param name="label">Names the record that keeps the water level.</param>
    public static string? KeptBecause(CellChangeJudged judged, Func<RecordKey, string> label)
    {
        if (judged.IsRestored) return null;
        if (judged.Never is { } never) return RestoreWhereRemovedText.Never(never);
        if (judged.Area is { Verdict: not AreaVerdict.Cleared } area) return RestoreWhereRemovedText.Area(area.Verdict);
        return judged.WaterKeptBy is { } by
            ? RestoreWhereRemovedText.WaterKept(by, label)
            : throw new UnreachableException($"The cell {judged.Change.Cell} stays with its area cleared, no water level kept and no never-rule.");
    }

    /// <returns>The four quarters of the land, in the order of <see cref="TerrainQuadrants.IndexOf"/>.</returns>
    public static ImmutableArray<LandQuarterFacts> QuartersOf(LandChangeJudged land)
    {
        var change = land.Change;
        var candidateHeights = PointsPerQuarter(change.UndoPoints);
        var candidateColours = PointsPerQuarter(change.UndoColours);
        var restoredHeights = PointsPerQuarter(land.Heights);
        var restoredColours = PointsPerQuarter(land.Colours);
        var laterHeights = PointsPerQuarter(change.LeftToLater.Points);
        var laterColours = PointsPerQuarter(change.LeftToLater.Colours);
        var kept = land.Kept.Select(entry => (entry.Reason, Counts: PointsPerQuarter(entry.Points))).ToList();
        return
        [
            .. Enumerable.Range(0, TerrainQuadrants.PerSide * TerrainQuadrants.PerSide).Select(quarter =>
            {
                var judged = land.Quarters.IsEmpty ? null : land.Quarters[quarter];
                var keptHere = judged is { Verdict: AreaVerdict.Cleared } ? kept.Where(entry => entry.Counts[quarter] > 0) : [];
                return new LandQuarterFacts(
                    quarter,
                    judged,
                    candidateHeights[quarter],
                    candidateColours[quarter],
                    change.UndoQuarters.Contains(quarter),
                    restoredHeights[quarter],
                    restoredColours[quarter],
                    land.Textures.Contains(quarter),
                    laterHeights[quarter],
                    laterColours[quarter],
                    change.LeftToLater.Quarters.Contains(quarter),
                    [.. keptHere.Select(entry => (entry.Reason, entry.Counts[quarter]))]);
            }),
        ];
    }

    /// <returns>Why the quarter keeps what the cleaned mod changed in it; null when something of it goes back.</returns>
    public static string? KeptBecause(LandChangeJudged land, LandQuarterFacts quarter)
    {
        if (quarter.Restores) return null;
        if (land.Never is { } never) return RestoreWhereRemovedText.Never(never);
        var verdict = quarter.Judged?.Verdict ?? throw new UnreachableException($"The land {land.Change.LandKey} has a quarter with no area and no never-rule.");
        if (verdict != AreaVerdict.Cleared) return RestoreWhereRemovedText.Area(verdict);
        return quarter.Kept.IsEmpty
            ? RestoreWhereRemovedText.ColoursSharedWithGroundThatStays
            : string.Join(Describe.ListSeparator, quarter.Kept.Select(entry => RestoreWhereRemovedText.TerrainKept(entry.Reason)));
    }

    /// <summary>The only part of the land the cleaned mod last changed that could go back is the rest of the record.</summary>
    public static bool HoldsOnlyTheRest(LandJudgement land) => land.Land.Change.UndoRest && !land.Quarters.Any(quarter => quarter.HoldsChange);

    /// <returns>Why the land keeps the rest of the record the cleaned mod changed; null when it goes back.</returns>
    public static string? KeptBecauseOfRest(LandChangeJudged land)
    {
        if (land.Rest) return null;
        return land.Never is { } never ? RestoreWhereRemovedText.Never(never) : RestoreWhereRemovedText.Area(RestAreaKeeping(land));
    }

    /// <summary>The rest of the record stays only because of the area: no never-rule keeps it.</summary>
    public static bool IsRestKeptForArea(LandChangeJudged land) => land.Change.UndoRest && !land.Rest && land.Never is null;

    /// <returns>The verdict of the area that keeps a rest that <see cref="IsRestKeptForArea"/>.</returns>
    /// <remarks>The rest goes back only when every quarter is cleared, so a quarter that is not names the reason.</remarks>
    public static AreaVerdict RestAreaKeeping(LandChangeJudged land) =>
        KeepingVerdict(land.Quarters.Select(quarter => quarter.Verdict), $"The rest of the land record {land.Change.LandKey}");

    /// <returns>The area of a quarter, added up over the spaces it is judged in; null when it is not judged.</returns>
    public static AreaShare? AreaOf(LandQuarterFacts quarter) => quarter.Judged is { } judged ? ClearedAreaText.Sum(judged.Areas, judged.Verdict) : null;

    public static ImmutableArray<NavmeshOutcome> NavmeshOutcomes(RestoreWhereRemovedResult result)
    {
        var groupOf = result.NavmeshGroups.SelectMany(group => group.Members.Select(member => (Member: member, Group: group))).ToDictionary(entry => entry.Member, entry => entry.Group);
        return [.. result.Navmeshes.Select(navmesh => new NavmeshOutcome(navmesh, groupOf.GetValueOrDefault(navmesh.Change.Navmesh)))];
    }

    /// <returns>The parts of the navmesh that go back to what they were, in the order shape, existence, map entry.</returns>
    public static IEnumerable<string> UndoneParts(NavmeshChangedByCleanedMod change) =>
        NavmeshParts(change).Where(part => part.Outcome is PartOutcome.Undo).Select(part => part.Name);

    /// <returns>The parts of the navmesh that a later plugin changed again, each with that plugin.</returns>
    public static IEnumerable<(string Part, PluginName Plugin)> LeftToLaterPlugins(NavmeshChangedByCleanedMod change) =>
        NavmeshParts(change)
            .Where(part => part.Outcome is PartOutcome.LeftToLaterPlugin)
            .Select(part => (part.Name, ((PartOutcome.LeftToLaterPlugin)part.Outcome).Plugin));

    /// <summary>The navmesh stays only because of its area: no never-rule or guard keeps it.</summary>
    public static bool IsKeptForArea(NavmeshOutcome outcome) =>
        !outcome.IsRestored && outcome.Judged.Never is null && outcome.Group?.Refused is null && outcome.Judged.Area is { Verdict: not AreaVerdict.Cleared };

    /// <returns>Why the navmesh keeps what the cleaned mod changed; null when it goes back.</returns>
    /// <param name="label">Names the record a guard found.</param>
    public static string? KeptBecause(NavmeshOutcome outcome, Func<RecordKey, string> label)
    {
        if (outcome.IsRestored) return null;
        if (outcome.Judged.Never is { } never) return RestoreWhereRemovedText.Never(never);
        if (outcome.Group?.Refused is { } refusal) return RestoreWhereRemovedText.Refusal(refusal, label);
        return outcome.Judged.Area is { Verdict: not AreaVerdict.Cleared } area
            ? RestoreWhereRemovedText.Area(area.Verdict)
            : RestoreWhereRemovedText.NothingGoesBack;
    }

    private static IEnumerable<(string Name, PartOutcome Outcome)> NavmeshParts(NavmeshChangedByCleanedMod change) =>
    [
        ("shape", change.Geometry),
        ("existence", change.Presence),
        ("map entry", change.MapEntry),
    ];

    private static int[] PointsPerQuarter(PointSet points)
    {
        var counts = new int[TerrainQuadrants.PerSide * TerrainQuadrants.PerSide];
        foreach (var quarter in points.Points.SelectMany(TerrainQuadrants.Holding)) counts[quarter]++;
        return counts;
    }
}
