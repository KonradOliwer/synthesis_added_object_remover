using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.RestoreWhereRemoved;

public static partial class WhereRemovedRestorer
{
    private enum HeldKind { Placed, Quarter, Cell, Navmesh }

    /// <summary>A part of a later round held as the round before left it.</summary>
    /// <param name="Change">The placed record, LAND record, cell record or navmesh.</param>
    /// <param name="Quarter">The quarter of a LAND record; null for every other kind.</param>
    private readonly record struct HeldPart(HeldKind Kind, RecordKey Change, int? Quarter);

    /// <summary>
    /// The parts of a later round held as the round before left them, each with the placed record, target, LAND record, cell record or
    /// navmesh of that round whose outcome its going back would undo: placed records refused, LAND quarters, water levels and navmesh
    /// groups kept.
    /// </summary>
    private sealed record EarlierRoundPins(ImmutableDictionary<HeldPart, EarlierRoundHold> Parts)
    {
        public static EarlierRoundPins None { get; } = new(ImmutableDictionary<HeldPart, EarlierRoundHold>.Empty);

        public bool IsEmpty => Parts.IsEmpty;

        /// <summary>These and those of <paramref name="more"/> not held yet; a part held already keeps what it was held for.</summary>
        public EarlierRoundPins Plus(EarlierRoundPins more) =>
            new(more.Parts.Aggregate(Parts, (joined, part) => joined.ContainsKey(part.Key) ? joined : joined.Add(part.Key, part.Value)));

        /// <summary>Those of these not among <paramref name="held"/>.</summary>
        public EarlierRoundPins Except(EarlierRoundPins held) => new(Parts.RemoveRange(held.Parts.Keys));

        /// <returns>What the part is held for; null when it is not held.</returns>
        public EarlierRoundHold? HeldFor(HeldKind kind, RecordKey change, int? quarter = null) =>
            Parts.TryGetValue(new HeldPart(kind, change, quarter), out var hold) ? hold : null;

        /// <summary>The parts of a kind held, each with what it is held for.</summary>
        public IEnumerable<(HeldPart Part, EarlierRoundHold Hold)> Of(HeldKind kind) =>
            Parts.Where(part => part.Key.Kind == kind).Select(part => (part.Key, part.Value));

        /// <summary>The LAND quarters, cell records and navmeshes held, in key order and quarter order.</summary>
        public ImmutableArray<KeptForEarlierRound> KeptOtherThanPlaced() =>
        [
            .. Parts.Where(part => part.Key.Kind != HeldKind.Placed)
                .Select(part => new KeptForEarlierRound(part.Key.Change, part.Key.Quarter, part.Value))
                .OrderBy(kept => kept.Change, RecordKeyOrder.Comparer)
                .ThenBy(kept => kept.Quarter),
        ];

        /// <param name="causeNamed">Whether each part's own going back undoes <paramref name="undone"/>, rather than all of this round's new parts together.</param>
        public static EarlierRoundPins Holding(HeldKind kind, IEnumerable<(RecordKey Change, int? Quarter)> parts, RecordKey undone, bool causeNamed = true) =>
            new(parts.ToImmutableDictionary(part => new HeldPart(kind, part.Change, part.Quarter), _ => new EarlierRoundHold(undone, causeNamed)));
    }

    /// <summary>
    /// What a later round holds of the round before it, as decisions: with any part held, every placed record that went back there
    /// starts with the copy it went back with, the placed records held start refused for <see cref="PlacedGuard.EarlierRoundWins"/>, the
    /// cells held keep their water level, and the LAND quarters and navmeshes held are planned on the verdicts the round before was
    /// planned on, so nothing of a quarter goes back and a navmesh joins no group; each names what it would undo. The rows show the
    /// current shares (<see cref="Marked(ImmutableArray{LandChangeJudged}, AreaVerdicts)"/>).
    /// </summary>
    /// <param name="Copies">The copy of each placed record that went back in the round before, by its key.</param>
    /// <param name="Before">The verdicts the round before was planned on; null for none.</param>
    private sealed record EarlierRound(EarlierRoundPins Pins, ImmutableDictionary<RecordKey, CloneJudged?> Copies, AreaVerdicts? Before)
    {
        public static EarlierRound None { get; } = new(EarlierRoundPins.None, ImmutableDictionary<RecordKey, CloneJudged?>.Empty, Before: null);

        public static EarlierRound Holding(EarlierRoundPins pins, RestoreRound before) =>
            new(pins, GoingBackIndexes(before).ToImmutableDictionary(index => before.Plans.Cleared[index].Key, index => before.State.Clones[index]), before.Used);

        public PlacedRefusal? RefusedFromTheStart(RecordKey placed) =>
            Pins.HeldFor(HeldKind.Placed, placed) is { } hold
                ? new PlacedRefusal(hold.CauseNamed ? PlacedGuard.EarlierRoundWins : PlacedGuard.EarlierRoundWinsTogether, hold.Undone, Failure: null)
                : null;

        /// <summary>
        /// The verdicts with the decisions held: the copies of the records that went back before and none for a record held, and the
        /// verdicts of the round before for the quarters and navmeshes held.
        /// </summary>
        public AreaVerdicts Pinned(AreaVerdicts verdicts)
        {
            if (Before is not { } before) return verdicts;
            var lands = before.Lands.ToDictionary(LandKeyOf);
            var navmeshes = before.Navmeshes.ToDictionary(NavmeshKeyOf);
            return verdicts with
            {
                Placed = [.. verdicts.Placed.Select(PinnedPlaced)],
                Lands = [.. verdicts.Lands.Select(land => land with { Quarters = [.. land.Quarters.Select((quarter, index) => PinnedQuarter(land, quarter, index, lands))] })],
                Navmeshes = [.. verdicts.Navmeshes.Select(navmesh => Pins.HeldFor(HeldKind.Navmesh, NavmeshKeyOf(navmesh)) is null ? navmesh : navmeshes[NavmeshKeyOf(navmesh)])],
            };
        }

        /// <summary>The state a round starts from with the water levels held kept.</summary>
        public RestoreState HeldIn(RestoreState start) =>
            Pins.IsEmpty
                ? start
                : start with
                {
                    Water = start.Water
                        .KeepForEarlierRound(WaterKeptReason.ForEarlierRound, HeldCells(causeNamed: true))
                        .KeepForEarlierRound(WaterKeptReason.ForEarlierRoundTogether, HeldCells(causeNamed: false)),
                };

        private IEnumerable<(RecordKey Cell, RecordKey By)> HeldCells(bool causeNamed) =>
            Pins.Of(HeldKind.Cell).Where(held => held.Hold.CauseNamed == causeNamed).Select(held => (held.Part.Change, held.Hold.Undone));

        /// <summary>
        /// The judged LAND records with each quarter held showing its current share and what it is held for, and a rest kept only for
        /// a quarter held showing that.
        /// </summary>
        /// <param name="current">The verdicts of the round's own judging.</param>
        public ImmutableArray<LandChangeJudged> Marked(ImmutableArray<LandChangeJudged> lands, AreaVerdicts current)
        {
            var currentOf = current.Lands.ToDictionary(LandKeyOf);
            return [.. lands.Select(land => Marked(land, currentOf[land.Change.LandKey]))];
        }

        /// <summary>The judged navmeshes with each one held showing what it is held for instead of the verdict of the round before.</summary>
        public ImmutableArray<NavmeshChangeJudged> Marked(ImmutableArray<NavmeshChangeJudged> navmeshes) =>
            [.. navmeshes.Select(navmesh => Pins.HeldFor(HeldKind.Navmesh, NavmeshKeyOf(navmesh)) is { } hold ? navmesh with { KeptForArea = null, HeldForEarlierRound = hold } : navmesh)];

        private LandChangeJudged Marked(LandChangeJudged land, LandAreasJudged current)
        {
            var key = land.Change.LandKey;
            var quarters = land.ChangedQuarters
                .Select(quarter => Pins.HeldFor(HeldKind.Quarter, key, quarter.Quarter) is { } hold
                    ? quarter with { Area = current.Quarters[quarter.Quarter].Area, KeptForArea = null, HeldForEarlierRound = hold }
                    : quarter)
                .ToImmutableArray();
            var heldOnly = !land.Rest && land.RestKeptForArea is not null
                && Enumerable.Range(0, current.Quarters.Length).All(index => current.Quarters[index].Verdict == AreaVerdict.Cleared || Pins.HeldFor(HeldKind.Quarter, key, index) is not null);
            var heldFor = Enumerable.Range(0, current.Quarters.Length).Select(index => Pins.HeldFor(HeldKind.Quarter, key, index)).FirstOrDefault(hold => hold is not null);
            return land with
            {
                ChangedQuarters = quarters,
                RestKeptForArea = heldOnly ? null : land.RestKeptForArea,
                RestHeldForEarlierRound = heldOnly ? heldFor : null,
            };
        }

        private PlacedChangeJudged PinnedPlaced(PlacedChangeJudged change)
        {
            if (!change.IsRestored) return change;
            if (Copies.TryGetValue(change.Change.Key, out var copy)) return change with { Cloning = copy };
            return Pins.HeldFor(HeldKind.Placed, change.Change.Key) is null ? change : change with { Cloning = null };
        }

        private LandQuarterJudged PinnedQuarter(LandAreasJudged land, LandQuarterJudged quarter, int index, Dictionary<RecordKey, LandAreasJudged> before) =>
            Pins.HeldFor(HeldKind.Quarter, LandKeyOf(land), index) is null ? quarter : before[LandKeyOf(land)].Quarters[index];
    }

    private static RecordKey CellKeyOf(CellChangeJudged cell) => cell.Change.Cell;

    private static RecordKey LandKeyOf(LandAreasJudged land) => land.Change.LandKey;

    private static RecordKey NavmeshKeyOf(NavmeshChangeJudged navmesh) => navmesh.Change.Navmesh;

    /// <summary>
    /// What of <paramref name="round"/> undoes <paramref name="before"/>, found from each part that went back there and does not now, in
    /// kind and key order: the placed records, LAND quarters, cell records and navmeshes only this round lets go back that its refusal,
    /// the water keeper or the navmesh group's refusal names, through the follow-up where it names a followed object. None when no
    /// such part is named.
    /// </summary>
    private static EarlierRoundPins Blame(StepInputs inputs, RestoreRound round, RestoreRound before)
    {
        var blame = new Blamed(inputs, round, before);
        var undone = new UndoneParts(round, before);
        var pins = EarlierRoundPins.None;
        foreach (var placed in undone.Placed) pins = Found(RefusalOf(round, placed)).Aggregate(pins, (held, named) => held.Plus(blame.Of(named, placed)));
        foreach (var cell in undone.Cells) pins = pins.Plus(blame.Of(cell.WaterKeptBy, CellKeyOf(cell)));
        foreach (var (navmesh, refusal) in undone.Navmeshes) pins = pins.Plus(blame.Of(refusal?.By, navmesh));
        foreach (var quarter in undone.Quarters) pins = pins.Plus(blame.OfQuarter(quarter, quarter.Land));
        return pins;
    }

    /// <summary>Every part only <paramref name="round"/> lets go back, held for <paramref name="undone"/>: the round before kept whole.</summary>
    private static EarlierRoundPins EverythingNew(StepInputs inputs, RestoreRound round, RestoreRound before, RecordKey undone) => new Blamed(inputs, round, before).All(undone);

    /// <summary>The first part that went back in <paramref name="before"/> and does not in <paramref name="round"/>, then the first counted removal it does not make; null when there is none.</summary>
    private static RecordKey? FirstUndone(StepInputs inputs, RestoreRound round, RestoreRound before) =>
        new UndoneParts(round, before).First() ?? (FirstCountedStanding(round) is { } target ? inputs.Input.Earlier.Collected.Targets[target.Index].Key : null);

    /// <summary>Every record a refusal names, its main one first; none for no refusal.</summary>
    private static IEnumerable<RecordKey> Found(PlacedRefusal? refusal) =>
        refusal is null ? [] : (refusal.By is { } by ? [by] : Array.Empty<RecordKey>()).Concat(refusal.AlsoFound);

    private static PlacedRefusal? RefusalOf(RestoreRound round, RecordKey placed) =>
        round.Plans.Cleared.FindIndex(change => change.Key == placed) is var index and >= 0 ? round.State.Placed[index] : null;

    /// <summary>The parts that went back in a round and do not in the round after, each kind in key order.</summary>
    private sealed class UndoneParts(RestoreRound round, RestoreRound before)
    {
        public IEnumerable<RecordKey> Placed => GoingBack(before).Except(GoingBack(round));

        /// <summary>The cells whose water level the round before wrote and this one keeps, as this round judges them.</summary>
        public IEnumerable<CellChangeJudged> Cells
        {
            get
            {
                ImmutableHashSet<RecordKey> wrote = [.. before.State.Water.Judged().Where(WaterUndoPlan.WritesWaterLevel).Select(CellKeyOf)];
                return round.State.Water.Judged().Where(cell => wrote.Contains(CellKeyOf(cell)) && !WaterUndoPlan.WritesWaterLevel(cell));
            }
        }

        /// <summary>The navmeshes whose group went back in the round before and is refused now, each with that refusal.</summary>
        public IEnumerable<(RecordKey Navmesh, NavmeshRefusal? Refusal)> Navmeshes
        {
            get
            {
                ImmutableHashSet<RecordKey> wentBack = [.. MembersGoingBack(before)];
                var refusalOf = (round.State.Navmeshes?.Groups() ?? []).SelectMany(group => group.Members.Select(member => (member, group.Refused))).ToImmutableDictionary(entry => entry.member, entry => entry.Refused);
                return wentBack.Except(MembersGoingBack(round)).Order(RecordKeyOrder.Comparer).Select(navmesh => (navmesh, refusalOf.GetValueOrDefault(navmesh)));
            }
        }

        /// <summary>The quarters whose terrain went back in the round before and does not now, in key and quarter order.</summary>
        public IEnumerable<(RecordKey Land, int Quarter)> Quarters
        {
            get
            {
                ImmutableHashSet<(RecordKey, int)> restored = [.. RestoredQuarters(round)];
                return RestoredQuarters(before).Where(quarter => !restored.Contains(quarter)).OrderBy(quarter => quarter.Land, RecordKeyOrder.Comparer).ThenBy(quarter => quarter.Quarter);
            }
        }

        public RecordKey? First() =>
            Placed.Select(key => (RecordKey?)key)
                .Concat(Cells.Select(cell => (RecordKey?)CellKeyOf(cell)))
                .Concat(Navmeshes.Select(navmesh => (RecordKey?)navmesh.Navmesh))
                .Concat(Quarters.Select(quarter => (RecordKey?)quarter.Land))
                .FirstOrDefault();

        private static IEnumerable<RecordKey> MembersGoingBack(RestoreRound round) =>
            (round.State.Navmeshes?.Groups() ?? []).Where(group => group.Write is not null).SelectMany(group => group.Members);

        private static IEnumerable<(RecordKey Land, int Quarter)> RestoredQuarters(RestoreRound round) =>
            round.State.Terrain.Judged().SelectMany(land => land.ChangedQuarters.Where(quarter => quarter.Restored.Any).Select(quarter => (land.Change.LandKey, quarter.Quarter)));
    }

    /// <summary>The parts only a round lets go back, compared with the round before, and what of them a refusal or the follow-up names.</summary>
    private sealed class Blamed(StepInputs inputs, RestoreRound round, RestoreRound before)
    {
        private readonly ImmutableHashSet<RecordKey> _newlyGoingBack = [.. GoingBack(round).Except(GoingBack(before))];

        /// <summary>The placed records the round before planned, gone back or refused there.</summary>
        private readonly ImmutableHashSet<RecordKey> _clearedBefore = [.. before.Plans.Cleared.Select(change => change.Key)];

        private readonly ImmutableDictionary<RecordKey, LandAreasJudged> _landsNow = round.Used.Lands.ToImmutableDictionary(LandKeyOf);
        private readonly ImmutableHashSet<(RecordKey Land, int Quarter)> _newQuarters = [.. ClearedQuarters(round.Used).Except(ClearedQuarters(before.Used))];
        private readonly ImmutableHashSet<RecordKey> _newCells = [.. RestoredCells(round.Used).Except(RestoredCells(before.Used))];
        private readonly ImmutableHashSet<RecordKey> _newNavmeshes = [.. ClearedNavmeshes(round.Used).Except(ClearedNavmeshes(before.Used))];
        private readonly ImmutableDictionary<RecordKey, FollowUpOtherObject> _others = round.FollowUp.Others.ToImmutableDictionary(other => other.Outcome.Object.Key);
        private readonly ImmutableDictionary<RecordKey, FollowUpTarget> _targets =
            round.FollowUp.Targets.ToImmutableDictionary(followed => inputs.Input.Earlier.Collected.Targets[followed.Target.Index].Key);

        /// <summary>What refuses each placed record and the group of each navmesh that went back in the round before and does not now.</summary>
        private readonly ImmutableDictionary<RecordKey, RecordKey> _refusalOfUndone = new UndoneParts(round, before).Navmeshes
            .Where(navmesh => navmesh.Refusal is not null)
            .Select(navmesh => (Part: navmesh.Navmesh, By: navmesh.Refusal!.By))
            .Concat(new UndoneParts(round, before).Placed
                .Select(placed => (Part: placed, By: RefusalOf(round, placed)?.By))
                .Where(entry => entry.By is not null)
                .Select(entry => (entry.Part, By: entry.By!.Value)))
            .ToImmutableDictionary(entry => entry.Part, entry => entry.By);

        /// <summary>
        /// What the key a refusal names blames, held for <paramref name="undone"/>; for a placed record or navmesh that went back in the
        /// round before and stays now, such as the support of a record arriving without it, what its own refusal names.
        /// </summary>
        public EarlierRoundPins Of(RecordKey? named, RecordKey undone)
        {
            if (named is not { } key) return EarlierRoundPins.None;
            if (Direct(key, undone) is { } direct) return direct;
            return _refusalOfUndone.TryGetValue(key, out var refusedBy) ? Direct(refusedBy, undone) ?? EarlierRoundPins.None : EarlierRoundPins.None;
        }

        /// <returns>Null when the key names no part only this round lets go back, no followed object and no LAND record.</returns>
        private EarlierRoundPins? Direct(RecordKey key, RecordKey undone)
        {
            if (_newlyGoingBack.Contains(key)) return EarlierRoundPins.Holding(HeldKind.Placed, [(key, null)], undone);
            if (_others.TryGetValue(key, out var other)) return FollowedUp(other.StartedBy, other.GroundLands, undone);
            if (_targets.TryGetValue(key, out var target)) return FollowedUp(target.StartedBy, target.GroundLands, undone);
            if (_newNavmeshes.Contains(key)) return EarlierRoundPins.Holding(HeldKind.Navmesh, [(key, null)], undone);
            if (_newCells.Contains(key)) return EarlierRoundPins.Holding(HeldKind.Cell, [(key, null)], undone);
            return QuartersOf([key], undone) is { IsEmpty: false } quarters ? quarters : null;
        }

        /// <summary>
        /// What a quarter whose terrain no longer goes back blames: the ground stays under what newly stands on it, so the placed records
        /// only this round lets go back or refuses that stand in the quarter, where they are now or go back to, with their copies, the
        /// navmeshes only this round lets go back with a vertex in it, and what started a follow-up that moved an object there.
        /// </summary>
        public EarlierRoundPins OfQuarter((RecordKey Land, int Quarter) quarter, RecordKey undone)
        {
            if (!_landsNow.TryGetValue(quarter.Land, out var land)) return EarlierRoundPins.None;
            bool InQuarter(RecordKey space, Vector3 position) => IsInQuarter(land.Change.Cell, quarter.Quarter, space, position);
            var placed = round.Plans.Cleared
                .Where(change => _newlyGoingBack.Contains(change.Key) || !_clearedBefore.Contains(change.Key))
                .Where(change => InQuarter(change.SpaceKey, ChangeJudge.WhereItEndsOf(change)) || change.WhereItIsNow is { } now && InQuarter(change.SpaceKey, now.Position))
                .Select(change => (change.Key, (int?)null));
            var navmeshes = round.Used.Navmeshes
                .Where(navmesh => _newNavmeshes.Contains(NavmeshKeyOf(navmesh)))
                .Where(navmesh => new[] { navmesh.Change.Earlier, navmesh.Change.Current }.Any(shape => shape?.Vertices.Any(vertex => InQuarter(navmesh.Change.SpaceKey, vertex)) == true))
                .Select(navmesh => (NavmeshKeyOf(navmesh), (int?)null));
            var followed = round.FollowUp.Others
                .Where(other => InQuarter(other.Outcome.Object.SpaceKey, other.Outcome.Object.Position) || InQuarter(other.Outcome.Object.SpaceKey, other.Outcome.NewPosition))
                .Select(other => FollowedUp(other.StartedBy, other.GroundLands, undone));
            return followed.Aggregate(
                EarlierRoundPins.Holding(HeldKind.Placed, placed, undone).Plus(EarlierRoundPins.Holding(HeldKind.Navmesh, navmeshes, undone)), (pins, more) => pins.Plus(more));
        }

        private bool IsInQuarter(ExteriorCell cell, int quarter, RecordKey space, Vector3 position) =>
            inputs.FindLandWorldspace(space) == cell.WorldspaceKey && TerrainQuadrants.Contains(cell.X, cell.Y, quarter, new Vector2(position.X, position.Y));

        /// <summary>Every part only this round lets go back, held together for <paramref name="undone"/>, no single cause named.</summary>
        public EarlierRoundPins All(RecordKey undone) =>
            EarlierRoundPins.Holding(HeldKind.Placed, _newlyGoingBack.Select(key => (key, (int?)null)), undone, causeNamed: false)
                .Plus(EarlierRoundPins.Holding(HeldKind.Quarter, _newQuarters.Select(quarter => (quarter.Land, (int?)quarter.Quarter)), undone, causeNamed: false))
                .Plus(EarlierRoundPins.Holding(HeldKind.Cell, _newCells.Select(key => (key, (int?)null)), undone, causeNamed: false))
                .Plus(EarlierRoundPins.Holding(HeldKind.Navmesh, _newNavmeshes.Select(key => (key, (int?)null)), undone, causeNamed: false));

        /// <summary>What started a follow-up: the placed records only this round lets go back, and the new quarters of the LAND records whose ground going down started it.</summary>
        private EarlierRoundPins FollowedUp(ImmutableArray<RecordKey> startedBy, ImmutableArray<RecordKey> groundLands, RecordKey undone) =>
            EarlierRoundPins.Holding(HeldKind.Placed, startedBy.Where(_newlyGoingBack.Contains).Select(key => (key, (int?)null)), undone).Plus(QuartersOf(groundLands, undone));

        private EarlierRoundPins QuartersOf(IEnumerable<RecordKey> lands, RecordKey undone)
        {
            var named = lands.ToImmutableHashSet();
            return EarlierRoundPins.Holding(HeldKind.Quarter, _newQuarters.Where(quarter => named.Contains(quarter.Land)).Select(quarter => (quarter.Land, (int?)quarter.Quarter)), undone);
        }

        private static IEnumerable<(RecordKey Land, int Quarter)> ClearedQuarters(AreaVerdicts verdicts) =>
            verdicts.Lands.SelectMany(land => land.Quarters.Select((quarter, index) => (Quarter: quarter, Index: index))
                .Where(entry => entry.Quarter.Verdict == AreaVerdict.Cleared)
                .Select(entry => (LandKeyOf(land), entry.Index)));

        private static IEnumerable<RecordKey> RestoredCells(AreaVerdicts verdicts) => verdicts.Cells.Where(cell => cell.IsRestored).Select(CellKeyOf);

        private static IEnumerable<RecordKey> ClearedNavmeshes(AreaVerdicts verdicts) =>
            verdicts.Navmeshes.Where(navmesh => navmesh is { Never: null, KeptForArea: null, NothingGoesBack: false }).Select(NavmeshKeyOf);
    }
}
