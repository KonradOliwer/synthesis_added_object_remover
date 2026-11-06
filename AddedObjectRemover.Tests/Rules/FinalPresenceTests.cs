using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>Whether a placed record stands in the final world: its own flags, this run's removals, the restores that go back and its whole enable-parent chain.</summary>
public sealed class FinalPresenceTests
{
    private static readonly PluginName BaseGame = new("Base.esm");
    private static readonly RecordKey Record = new(BaseGame, 0x900);
    private static readonly RecordKey Parent = new(BaseGame, 0x901);
    private static readonly RecordKey Grandparent = new(BaseGame, 0x902);

    private static readonly PlacedPresence Removed = new(Removed: true, Parent: null, OppositeOfParent: false);

    [Fact]
    public void ARecordNoFactReadsAnythingAboutStands() =>
        Assert.False(Presence().EndsRemoved(Record, _ => true));

    [Fact]
    public void ARecordTheRunRemovesEndsRemovedAndSoDoesARecordEnabledWithIt()
    {
        var presence = Presence(removedByRun: new HashSet<RecordKey> { Parent }, winning: new() { [Record] = Follows(Parent) });

        Assert.True(presence.EndsRemoved(Parent, _ => true));
        Assert.True(presence.EndsRemoved(Record, _ => true));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void ARecordEndsRemovedWhenItsParentIsRemovedAndItFollowsItOrWhenTheParentStandsAndItIsTheOppositeOfIt(bool parentRemoved, bool opposite, bool endsRemoved)
    {
        var winning = new Dictionary<RecordKey, PlacedPresence> { [Record] = Follows(Parent, opposite) };
        if (parentRemoved) winning.Add(Parent, Removed);

        Assert.Equal(endsRemoved, Presence(winning: winning).EndsRemoved(Record, _ => true));
    }

    [Fact]
    public void ARecordIsRemovedItselfWhateverItsParentSays()
    {
        var winning = new Dictionary<RecordKey, PlacedPresence> { [Record] = Removed with { Parent = Parent, OppositeOfParent = true } };

        Assert.True(Presence(winning: winning).EndsRemoved(Record, _ => true));
    }

    /// <summary>The grandparent is removed. Each link follows its parent or is the opposite of it, so a record ends removed when an even number of links are opposites.</summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ARecordFollowsEveryParentOfItsChain(bool recordOppositeOfParent, bool parentOppositeOfGrandparent, bool endsRemoved)
    {
        var winning = new Dictionary<RecordKey, PlacedPresence>
        {
            [Record] = Follows(Parent, recordOppositeOfParent),
            [Parent] = Follows(Grandparent, parentOppositeOfGrandparent),
            [Grandparent] = Removed,
        };

        Assert.Equal(endsRemoved, Presence(winning: winning).EndsRemoved(Record, _ => true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AParentNoFactReadsIsTakenAsEnabled(bool opposite, bool endsRemoved)
    {
        var winning = new Dictionary<RecordKey, PlacedPresence> { [Record] = Follows(Parent, opposite) };

        Assert.Equal(endsRemoved, Presence(winning: winning).EndsRemoved(Record, _ => true));
    }

    [Fact]
    public void ACycleOfEnableParentsIsCutOffSoItsRecordsStand()
    {
        var winning = new Dictionary<RecordKey, PlacedPresence> { [Record] = Follows(Parent), [Parent] = Follows(Record) };
        var presence = Presence(winning: winning);

        Assert.False(presence.EndsRemoved(Record, _ => true));
        Assert.False(presence.EndsRemoved(Parent, _ => true));
    }

    /// <summary>The parent is disabled now; its presence restore brings back a version that was enabled, and goes back or not.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AParentWhosePresenceRestoreGoesBackStandsAsItsVersionBelowTheCleanedModDid(bool parentGoesBack, bool recordEndsRemoved)
    {
        var presence = Presence(
            winning: new() { [Record] = Follows(Parent), [Parent] = Removed },
            changed: [ChangedWithPresenceBefore(Parent, default)]);

        Assert.Equal(recordEndsRemoved, presence.EndsRemoved(Record, index => index == 0 && parentGoesBack));
    }

    /// <summary>The parent's restore does not bring back its presence part, so the disable stays whatever goes back.</summary>
    [Fact]
    public void AParentWhoseRestoreLeavesItsPresenceAloneStaysDisabled()
    {
        var restore = ChangedWithPresenceBefore(Parent, default) with { Earlier = ChangedWithPresenceBefore(Parent, default).Earlier with { Undo = PlacedParts.Position } };
        var presence = Presence(winning: new() { [Record] = Follows(Parent), [Parent] = Removed }, changed: [restore]);

        Assert.True(presence.EndsRemoved(Record, _ => true));
    }

    /// <summary>The record's winning version has no parent; the version its restore brings back follows a disabled one, or is the opposite of it.</summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ARecordWhosePresenceRestoreGoesBackFollowsTheParentOfTheVersionBelowTheCleanedMod(bool goesBack, bool oppositeBefore, bool endsRemoved)
    {
        var presence = Presence(winning: new() { [Parent] = Removed }, changed: [ChangedWithPresenceBefore(Record, Follows(Parent, oppositeBefore))]);

        Assert.Equal(endsRemoved, presence.EndsRemoved(Record, _ => goesBack));
    }

    [Fact]
    public void TheDoorsEndingRemovedAreThoseDisabledThroughTheirParentToo()
    {
        var other = new RecordKey(BaseGame, 0x903);
        var presence = new FinalPresence(
            new HashSet<RecordKey>(),
            new Dictionary<RecordKey, PlacedPresence> { [Record] = Follows(Parent), [Parent] = Removed },
            new Dictionary<RecordKey, RecordChangedByCleanedMod>(),
            new Dictionary<RecordKey, int>(),
            [Record, other]);

        Assert.Equal([Record], presence.DoorsEndingRemoved(_ => true).ToList());
    }

    private static PlacedPresence Follows(RecordKey parent, bool opposite = false) => new(Removed: false, parent, opposite);

    private static FinalPresence Presence(
        IReadOnlySet<RecordKey>? removedByRun = null, Dictionary<RecordKey, PlacedPresence>? winning = null, ImmutableArray<RecordChangedByCleanedMod> changed = default) =>
        new(
            removedByRun ?? new HashSet<RecordKey>(),
            winning ?? new Dictionary<RecordKey, PlacedPresence>(),
            changed.IsDefault ? new Dictionary<RecordKey, RecordChangedByCleanedMod>() : changed.ToDictionary(record => record.Key),
            changed.IsDefault ? new Dictionary<RecordKey, int>() : changed.Select((record, index) => KeyValuePair.Create(record.Key, index)).ToDictionary(),
            []);

    /// <summary>A record the cleaned mod moved and disabled, whose version below the cleaned mod's has the given presence, and whose restore brings back its presence.</summary>
    private static RecordChangedByCleanedMod ChangedWithPresenceBefore(RecordKey key, PlacedPresence before) =>
        new(
            key,
            EditorId: null,
            TestTargets.Space,
            new CellFact(TestTargets.Space, null),
            TestTargets.TargetName,
            WinningModPastPatches: TestTargets.TargetName,
            WhereItIsNow: null,
            BaseNow: null,
            TestPlacedRecords.PlainReference,
            IsPersistent: false,
            TestTargets.EarlierVersionAt(BaseGame, new EulerPlacement(Vector3.Zero, Vector3.Zero, 1f)) with { Undo = PlacedParts.Position | PlacedParts.Presence },
            Target: null,
            LinkedFromCleanedMod: false,
            before);
}
