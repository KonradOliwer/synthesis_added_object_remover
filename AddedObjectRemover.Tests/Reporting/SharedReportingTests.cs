using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Mutagen.Bethesda.Plugins;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Reporting;

public sealed class SharedReportingTests
{
    private const int TargetCount = 5;
    private const int Protected = 3;
    private const int ProtectedTouching = 4;

    private static readonly KeepReason QuestReason = TestKeepReasons.Quest;
    private static readonly TooCloseCause BySize = new TooCloseCause.ObjectSize();
    private static readonly TooCloseCause WithinDistance = new TooCloseCause.Distance(32f);

    private static readonly ReportContext Normal = new(null!, null!, Detailed: false);
    private static readonly ReportContext Detailed = new(null!, null!, Detailed: true);

    [Fact]
    public void Removals_ListRoundByRoundDirectRemovalsBeforeLinkedOnes()
    {
        var (world, decisions) = RunRounds();

        var removals = RemovalList.WithReasons(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestVisibleLeftBehind.SwitchedOff, TestLeftBehind.PerformedWithoutFindings);

        Assert.Equal(
            [
                (typeof(ObjectWithReason.TooClose), 0),
                (typeof(ObjectWithReason.Touching), 1),
                (typeof(ObjectWithReason.LinkedTo), 2),
            ],
            removals.Select(removal => (removal.GetType(), removal.TargetIndex)));
        Assert.Equal(world.OtherModObjects[0], Assert.IsType<ObjectWithReason.TooClose>(removals[0]).TooCloseTo);
        Assert.Equal(1, Assert.IsType<ObjectWithReason.LinkedTo>(removals[2]).LinkedToTargetIndex);
    }

    [Fact]
    public void ALostSupportReasonNamesWhatBecameOfTheMainSupporterOnlyWhenRestoredObjectsHeldPartOfTheSupport()
    {
        var (world, decisions) = RunRounds();
        var main = RecordNames.Describe(world.Targets[0]);

        Assert.Equal($"100 % of its support was removed (mostly {main})", Describe.RemovalReason(world, new ObjectWithReason.LostSupport(2, 1f, 0f, 0), decisions));
        Assert.Equal($"100 % of its support left its place (mostly {main}, removed)", Describe.RemovalReason(world, new ObjectWithReason.LostSupport(2, 1f, 0.25f, 0), decisions));
    }

    [Fact]
    public void Removals_NameTheChangedTerrainAndTheDistanceCauses()
    {
        var world = CreateWorld();
        var changedBy = new PluginName("Other.esp");
        var decisions = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(world.Targets, [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved), TargetCount)
            .Apply(RoundKind.ChangedTerrain, [Propose(0, new RemovalReason.TerrainChanged(changedBy, TerrainChangeKind.Height | TerrainChangeKind.TerrainType))])
            .Apply(RoundKind.TooClose, [Propose(1, new RemovalReason.TooClose(new OtherId(0), BySize)), Propose(2, new RemovalReason.TooClose(new OtherId(0), WithinDistance))]);

        var removals = RemovalList.WithReasons(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestVisibleLeftBehind.SwitchedOff, TestLeftBehind.PerformedWithoutFindings);

        var other = world.OtherModObjects[0];
        Assert.Equal(
            [new ObjectWithReason.TerrainChanged(0, changedBy, TerrainChangeKind.Height | TerrainChangeKind.TerrainType), new ObjectWithReason.TooClose(1, other, BySize), new ObjectWithReason.TooClose(2, other, WithinDistance)],
            removals);
    }

    [Fact]
    public void TheSummaryCountsBothTooCloseCausesAsTooClose()
    {
        ObjectWithReason[] removals = [new ObjectWithReason.TooClose(0, CreateOtherModObject(), BySize), new ObjectWithReason.TooClose(1, CreateOtherModObject(), WithinDistance)];

        var summary = BuildRemovalSummary(removals, StepOutcome.SwitchedOff);

        Assert.Equal(
            "Removed 2 objects (2 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects).",
            Assert.Single(summary.Lines));
    }

    /// <summary>Target 1 is shrunk to fit, then removed for touching target 0, which is removed too close.</summary>
    [Fact]
    public void TheSummaryCountsTheFollowUpRemovalsThatWereFirstShrunkOrTurned()
    {
        var world = CreateWorld();
        var decisions = TestSeededDecisions.StartDisablingAll(ObjectsToKeep.Build(world.Targets, [], TestTargets.References(TargetCount), TestBaseFacts.Unresolved), TargetCount)
            .ApplyFits([new ProposedFit(new TargetId(1), new RemovalReason.TooClose(new OtherId(0), BySize), new EulerPlacement(Vector3.Zero, Vector3.Zero, 0.8f))])
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0), BySize))])
            .Apply(RoundKind.AlsoRemove, [Propose(1, new RemovalReason.Touching(new TargetId(0)))]);
        var removals = RemovalList.WithReasons(PatchWriter.ListRemovalsInWriteOrder(decisions), decisions, world, TestVisibleLeftBehind.SwitchedOff, TestLeftBehind.PerformedWithoutFindings);

        var summary = LogSections.BuildRemovalSummarySection(world, removals, decisions, TestTargets.TargetName, StepOutcome.SwitchedOff, StepOutcome.SwitchedOff);

        Assert.Equal(
            "Removed 2 objects (1 too close, 1 follow-up removals (1 of them first shrunk or turned), 0 invisible objects left behind, 0 linked to removed objects).",
            Assert.Single(summary.Lines));
    }

    [Fact]
    public void TheSummarySplitsTheRemovalsIntoTheCleanedModsObjectsAndTheOnesItChangedOfOtherMods()
    {
        var world = CreateWorld();
        var withChanged = world with { Targets = [.. world.Targets.Select(target => target with { Origin = target.Id.Index == 1 ? TargetOrigin.Moved : TargetOrigin.CleanedMod })] };
        ObjectWithReason[] removals = [new ObjectWithReason.TooClose(0, CreateOtherModObject(), BySize), new ObjectWithReason.TooClose(1, CreateOtherModObject(), BySize)];

        var summary = LogSections.BuildRemovalSummarySection(withChanged, removals, StartDecisions(world), TestTargets.TargetName, StepOutcome.SwitchedOff, StepOutcome.SwitchedOff);

        Assert.Equal(
            "Removed 2 objects (2 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects); "
            + $"1 of them {TestTargets.TargetName} objects, 1 objects of other mods that {TestTargets.TargetName} changed.",
            Assert.Single(summary.Lines));
    }

    [Fact]
    public void TheSummaryCountsTheRemovedMovedAndReplacedObjectsTogetherAsChanged()
    {
        var world = CreateWorld();
        var origins = new[] { TargetOrigin.CleanedMod, TargetOrigin.Moved, TargetOrigin.Replaced };
        var withChanged = world with { Targets = [.. world.Targets.Select(target => target with { Origin = origins[Math.Min(target.Id.Index, origins.Length - 1)] })] };
        ObjectWithReason[] removals = [.. Enumerable.Range(0, origins.Length).Select(index => new ObjectWithReason.TooClose(index, CreateOtherModObject(), BySize))];

        var summary = LogSections.BuildRemovalSummarySection(withChanged, removals, StartDecisions(world), TestTargets.TargetName, StepOutcome.SwitchedOff, StepOutcome.SwitchedOff);

        Assert.EndsWith($"; 1 of them {TestTargets.TargetName} objects, 2 objects of other mods that {TestTargets.TargetName} changed.", Assert.Single(summary.Lines));
    }

    [Theory]
    [InlineData(StepOutcome.Performed, 1, "Removed 2 objects (1 on changed terrain, 1 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects).")]
    [InlineData(StepOutcome.Performed, 0, "Removed 1 objects (0 on changed terrain, 1 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects).")]
    [InlineData(StepOutcome.SwitchedOff, 0, "Removed 1 objects (1 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects).")]
    [InlineData(StepOutcome.StoppedByUnexpectedError, 0, "Removed 1 objects (1 too close, 0 follow-up removals, 0 invisible objects left behind, 0 linked to removed objects).")]
    public void TheSummaryShowsTheChangedTerrainItemOnlyWhenTheStepWasPerformed(StepOutcome outcome, int onChangedTerrain, string expected)
    {
        List<ObjectWithReason> removals = [new ObjectWithReason.TooClose(0, CreateOtherModObject(), BySize)];
        if (onChangedTerrain > 0) removals.Add(new ObjectWithReason.TerrainChanged(1, new PluginName("Other.esp"), TerrainChangeKind.Height));

        Assert.Equal(expected, Assert.Single(BuildRemovalSummary(removals, outcome).Lines));
    }

    [Theory]
    [InlineData(StepOutcome.Performed, "Removed 4 objects (1 too close, 0 follow-up removals, 1 visible objects left behind, 1 invisible objects left behind, 1 linked to removed objects).")]
    [InlineData(StepOutcome.SwitchedOff, "Removed 3 objects (1 too close, 0 follow-up removals, 1 invisible objects left behind, 1 linked to removed objects).")]
    public void TheSummaryShowsTheVisibleLeftBehindItemOnlyWhenTheStepWasPerformedAndTheItemsAddUp(StepOutcome visibleLeftBehind, string expected)
    {
        var surroundings = new SectorAreaTally(50).Build();
        List<ObjectWithReason> removals =
        [
            new ObjectWithReason.TooClose(0, CreateOtherModObject(), BySize),
            new ObjectWithReason.LeftBehind(2, new LeftBehindCheck(2, InvisibleObjectKind.XMarkers, 100f, null, surroundings, LeftBehindOutcome.RemovedSurroundingsRemoved, KeepReason: null)),
            new ObjectWithReason.LinkedTo(3, 0),
        ];
        if (visibleLeftBehind == StepOutcome.Performed)
        {
            removals.Add(new ObjectWithReason.VisibleLeftBehind(1, new VisibleLeftBehindCheck(1, null, surroundings, VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, KeepReason: null)));
        }

        var world = CreateWorld();
        var summary = LogSections.BuildRemovalSummarySection(world, removals, StartDecisions(world), TestTargets.TargetName, StepOutcome.SwitchedOff, visibleLeftBehind);

        Assert.Equal(expected, Assert.Single(summary.Lines));
    }

    [Fact]
    public void AVisibleLeftBehindRemovalIsDescribedAsVisibleWithItsSurroundingsRemoved()
    {
        var check = new VisibleLeftBehindCheck(1, VisibleObjectKind.Containers, new SectorAreaTally(50).Build(), VisibleLeftBehindOutcome.RemovedSurroundingsRemoved, KeepReason: null);

        Assert.Equal("visible, surroundings removed", Describe.RemovalReason(null!, new ObjectWithReason.VisibleLeftBehind(1, check), null!));
    }

    /// <summary>The summary of a world with the cleaned mod's objects only, where no object was shrunk or turned.</summary>
    private static LogSection BuildRemovalSummary(IReadOnlyList<ObjectWithReason> removals, StepOutcome changedTerrain)
    {
        var world = CreateWorld();
        return LogSections.BuildRemovalSummarySection(world, removals, StartDecisions(world), TestTargets.TargetName, changedTerrain, StepOutcome.SwitchedOff);
    }

    private static RemovalDecisions StartDecisions(CollectedObjects world) => TestSeededDecisions.StartDisablingAll(CreateProtection(world.Targets), TargetCount);

    [Theory]
    [InlineData(TerrainChangeKind.Height, "terrain changed by Other.esp (height)")]
    [InlineData(TerrainChangeKind.TerrainType, "terrain changed by Other.esp (ground type)")]
    [InlineData(TerrainChangeKind.Height | TerrainChangeKind.TerrainType, "terrain changed by Other.esp (height and ground type)")]
    [InlineData(TerrainChangeKind.WaterHeight, "terrain changed by Other.esp (water height)")]
    [InlineData(TerrainChangeKind.WaterAdded, "terrain changed by Other.esp (water added)")]
    [InlineData(TerrainChangeKind.WaterRemoved, "terrain changed by Other.esp (water removed)")]
    [InlineData(TerrainChangeKind.Height | TerrainChangeKind.TerrainType | TerrainChangeKind.WaterHeight, "terrain changed by Other.esp (height, ground type and water height)")]
    public void ATerrainChangeNamesTheModAndWhatChanged(TerrainChangeKind kind, string expected)
    {
        Assert.Equal(expected, Describe.RemovalReason(null!, new ObjectWithReason.TerrainChanged(0, new PluginName("Other.esp"), kind), null!));
    }

    [Fact]
    public void ATerrainChangeOnlyThePatchHoldsNamesAnEarlierPatcher() =>
        Assert.Equal(
            "terrain changed by an earlier patcher in this group (height)",
            Describe.RemovalReason(null!, new ObjectWithReason.TerrainChanged(0, ChangedBy: null, TerrainChangeKind.Height), null!));

    [Fact]
    public void ADistanceRemovalNamesTheDistanceAndTheOtherObject()
    {
        var other = CreateOtherModObject();

        var text = Describe.RemovalReason(null!, new ObjectWithReason.TooClose(0, other, WithinDistance), null!);

        Assert.Equal($"within 32 units of {Describe.OtherObject(other)}, changed by Other.esp (new object)", text);
    }

    [Fact]
    public void ASizeRemovalNamesTheOtherObjectWithoutADistance()
    {
        var other = CreateOtherModObject();

        Assert.Equal(
            $"too close to {Describe.OtherObject(other)}, changed by Other.esp (new object)",
            Describe.RemovalReason(null!, new ObjectWithReason.TooClose(0, other, BySize), null!));
    }

    [Theory]
    [InlineData("CityMod.esp", "Skyrim.esm", "changed by CityMod.esp (accepted value from Skyrim.esm)")]
    [InlineData("CityMod.esp", null, "changed by CityMod.esp (new object)")]
    [InlineData(null, "Skyrim.esm", "changed by an earlier patcher in this group (accepted value from Skyrim.esm)")]
    [InlineData(null, null, "changed by an earlier patcher in this group (new object)")]
    public void ATooCloseRemovalNamesHowTheOtherObjectChanged(string? changingMod, string? acceptedFrom, string expected)
    {
        var other = CreateOtherModObject() with { Change = new ObjectChange(PluginOrNull(changingMod), PluginOrNull(acceptedFrom)) };

        Assert.Equal($"too close to {Describe.OtherObject(other)}, {expected}", Describe.RemovalReason(null!, new ObjectWithReason.TooClose(0, other, BySize), null!));
    }

    [Fact]
    public void ATooCloseRemovalByAnObjectWithoutAChangeIsABug() =>
        Assert.Throws<UnreachableException>(() => Describe.RemovalReason(null!, new ObjectWithReason.TooClose(0, CreateOtherModObject() with { Change = null }, BySize), null!));

    private static PluginName? PluginOrNull(string? name) => name is null ? null : new PluginName(name);

    [Fact]
    public void Removals_ComeInTheOrderTheStepNineResultGives()
    {
        var (world, decisions) = RunRounds();
        TargetId[] writeOrder = [new(2), new(0), new(1)];

        var removals = RemovalList.WithReasons(writeOrder, decisions, world, TestVisibleLeftBehind.SwitchedOff, TestLeftBehind.PerformedWithoutFindings);

        Assert.Equal([2, 0, 1], removals.Select(removal => removal.TargetIndex));
    }

    [Fact]
    public void RemovalWarnings_NameEachRemovedUniqueNpcAndCountTheRemovedLargeReferences()
    {
        var world = CreateWorld();
        var questKey = new RecordKey(new PluginName("Other.esp"), 0x950);
        var quest = new LinkFact(questKey, "SomeQuest", "Quest", TestTargets.Key(7), RelationKind.UniqueActor, SourceIsPlaced: false, SourceIsWorldspace: false, AliasFillFlags.None);
        var risks = new RemovalRisks(
            [new UniqueActorRole(new TargetId(0), quest), new UniqueActorRole(new TargetId(3), quest)],
            [new TargetId(1), new TargetId(2), new TargetId(4)]);
        ObjectWithReason[] removals = [.. Enumerable.Range(0, 3).Select(index => new ObjectWithReason.TooClose(index, CreateOtherModObject(), BySize))];

        var section = LogSections.BuildRemovalWarningsSection(world, removals, risks);

        var npc = world.Targets[0];
        Assert.Equal(
            [
                $"Warning: removed unique NPC {RecordNames.Describe(npc)} in {Describe.Location(world, npc)}: Quest SomeQuest [{questKey}] fills an alias "
                + "with it as a Unique Actor; a disabled actor cannot fill that alias.",
                "Warning: 2 removed objects are in their worldspace's large-reference list; distant LOD made before this run still shows them.",
            ],
            section.Lines.ToArray());
        Assert.Empty(LogSections.BuildRemovalWarningsSection(world, [], risks).Lines);
    }

    [Fact]
    public void Kept_ListsHeldObjectsWithTheRemovedObjectTheyTouch()
    {
        var (_, decisions) = RunRounds();

        var kept = RemovalList.Kept(decisions);

        Assert.Equal([(Protected, (int?)null), (ProtectedTouching, 0)], kept.Select(entry => (entry.TargetIndex, entry.TouchedTargetIndex)));
    }

    [Fact]
    public void CountLinkedIn_CountsOnlyTheRoundsLinkedRemovals()
    {
        var (_, decisions) = RunRounds();

        Assert.Equal([0, 1], decisions.Rounds.Select(round => RemovalList.CountLinkedIn(decisions, round)));
    }

    [Fact]
    public void Hints_ReportRemovedUsedMarkersAndKeptForNonPlacedReferences()
    {
        var (world, decisions) = RunRounds();
        var tagged = world with
        {
            Targets =
            [
                .. world.Targets.Select((target, index) => target with
                {
                    Base = TestVisibility.TaggedBase(index == 0 ? ObjectVisibility.Invisible(InvisibleObjectKind.DoorMarkers) : ObjectVisibility.Visible),
                }),
            ],
        };
        var protection = CreateProtection(tagged.Targets);
        var decided = new StepResults(
            tagged, protection, new RemovalRisks([], []), null!, OldPlaceWorld: null, new ChangedTerrainResult([]), null!, TestVisibleLeftBehind.SwitchedOff, TestLeftBehind.PerformedWithoutFindings, null!, null!, decisions);

        var hints = ManualPatchHints.FindManualPatchHints(decided, TestVisibility.OfTaggedBasesOnly(), RemovalList.WithReasons(PatchWriter.ListRemovalsInWriteOrder(decided.Final), decided.Final, decided.Collected, decided.VisibleLeftBehind, decided.LeftBehind));

        Assert.Equal(
            [
                (ManualPatchHintType.RemovedMarker, 0),
                (ManualPatchHintType.KeptForNonPlacedReference, Protected),
                (ManualPatchHintType.KeptForNonPlacedReference, ProtectedTouching),
            ],
            hints.Select(hint => (hint.Type, hint.TargetIndex)));
    }

    [Fact]
    public void Hints_ListAnObjectALocationListsWithARefTypeOnlyAsKeptForANonPlacedReference()
    {
        var (world, decisions) = RunRounds();
        var visible = world with { Targets = [.. world.Targets.Select(target => target with { Base = TestVisibility.TaggedBase(ObjectVisibility.Visible) })] };
        var location = new LinkFact(
            new RecordKey(new PluginName("Other.esp"), 0x960), "SomeLocation", "Location", TestTargets.Key(ProtectedTouching), RelationKind.LocationRefType,
            SourceIsPlaced: false, SourceIsWorldspace: false, AliasFillFlags.None);
        var listed = LinkRule.DecideLinkOutcome(visible.Targets, [location]).References[ProtectedTouching]!;
        var protection = ObjectsToKeep.Build(
            visible.Targets,
            [TestTargets.Link(1, 2)],
            TestTargets.References(TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedTouching] = listed }),
            TestBaseFacts.Unresolved);
        var decided = new StepResults(
            visible, protection, new RemovalRisks([], []), null!, OldPlaceWorld: null, new ChangedTerrainResult([]), null!, TestVisibleLeftBehind.SwitchedOff, TestLeftBehind.PerformedWithoutFindings, null!, null!, decisions);

        var hints = ManualPatchHints.FindManualPatchHints(decided, TestVisibility.OfTaggedBasesOnly(), RemovalList.WithReasons(PatchWriter.ListRemovalsInWriteOrder(decided.Final), decided.Final, decided.Collected, decided.VisibleLeftBehind, decided.LeftBehind));

        Assert.Equal(
            [(ManualPatchHintType.KeptForNonPlacedReference, Protected), (ManualPatchHintType.KeptForNonPlacedReference, ProtectedTouching)],
            hints.Select(hint => (hint.Type, hint.TargetIndex)));
        Assert.Empty(LogSections.BuildKeptOnPurposeSection(visible, [new KeptObject(ProtectedTouching, listed, null)]).Lines);
    }

    [Fact]
    public void Kept_PrintsOneLinePerObject()
    {
        var (world, decisions) = RunRounds();
        var kept = RemovalList.Kept(decisions);

        var expected = new[]
        {
            $"  Kept {RecordNames.Describe(world.Targets[Protected])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail}.",
            $"  Kept {RecordNames.Describe(world.Targets[ProtectedTouching])} in {Describe.Space(world, TestTargets.Space)}: {TestKeepReasons.QuestDetail} "
                + $"(touches removed {RecordNames.Describe(world.Targets[0])}).",
        };
        Assert.Equal(expected, LogSections.BuildKeptLines(world, kept));
    }

    [Fact]
    public void Kept_NamesATouchedObjectThatWasRestoredAsRestored()
    {
        var (world, decisions) = RunRounds();
        var restored = new Decision.Restored(
            new Round(1, RoundKind.TooClose),
            new RemovalReason.TooClose(new OtherId(0), new TooCloseCause.ObjectSize()),
            TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), world.Targets[0].Placement));
        var kept = RemovalList.Kept(decisions).Single(entry => entry.TouchedTargetIndex is not null) with { TouchedDecision = restored };

        var line = Assert.Single(LogSections.BuildKeptLines(world, [kept]));

        Assert.EndsWith($"(touches restored {RecordNames.Describe(world.Targets[0])}).", line, StringComparison.Ordinal);
    }

    [Fact]
    public void KeptOnPurpose_ListsOnlyTheScriptAndLocationRoleKeepsUnderTheirOwnHeaders()
    {
        var (world, _) = RunRounds();
        var locationRole = new KeepReason(KeepKind.LocationRole);
        var script = new KeepReason(
            KeepKind.ScriptNamesQuestOrScene,
            Script: new ScriptFinding(new ScriptNamedRecord(new RecordKey(new PluginName("Other.esp"), 0x970), "SomeQuest", ScriptNamedKind.Quest), OnBase: false));
        KeptObject[] kept = [new(Protected, TestKeepReasons.Quest, null), new(ProtectedTouching, locationRole, null), new(1, script, null)];

        var lines = LogSections.BuildKeptOnPurposeSection(world, kept).Lines;

        Assert.Equal(
            [
                $"  Kept, script names a quest or scene: {RecordNames.Describe(world.Targets[1])} in {Describe.Location(world, world.Targets[1])}: "
                    + $"{KeepReasonText.Detail(script)}.",
                $"  Kept, location role: {RecordNames.Describe(world.Targets[ProtectedTouching])} in {Describe.Location(world, world.Targets[ProtectedTouching])}: "
                    + $"{KeepReasonText.Detail(locationRole)}.",
            ],
            lines.ToArray());
    }

    [Fact]
    public void LinkedRemovals_IsPrintedOnlyInTheDetailedLog()
    {
        Assert.Empty(LogSections.BuildLinkedRemovalLines(3, 0, 0, "follow-up", Normal));
        Assert.Equal(
            new[] { "Linked groups: 3 more objects removed with the follow-up removals they are linked to." },
            LogSections.BuildLinkedRemovalLines(3, 0, 0, "follow-up", Detailed).ToArray());
    }

    [Fact]
    public void LinkedRemovals_NameTheRestoredMembersOnlyWhenThereAreAny()
    {
        Assert.Equal(
            new[] { "Linked groups: 3 more objects removed and 2 restored with the follow-up removals and restorations they are linked to." },
            LogSections.BuildLinkedRemovalLines(3, 2, 0, "follow-up", Detailed).ToArray());
    }

    [Fact]
    public void LinkedRemovals_NameRestorationsWhenARemovedMemberFollowedARestoredOne()
    {
        Assert.Equal(
            new[] { "Linked groups: 3 more objects removed with the follow-up removals and restorations they are linked to." },
            LogSections.BuildLinkedRemovalLines(3, 0, 1, "follow-up", Detailed).ToArray());
    }

    [Fact]
    public void CountLinkedToRestoredIn_CountsALinkedRemovalWhoseGroupMemberWasRestored()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        var restoring = new WhenRemoved.Restore(TestTargets.EarlierVersionAt(new PluginName("Earlier.esp"), targets[1].Placement));
        var decisions = RemovalDecisions.Start(
                CreateProtection(targets),
                [.. targets.Select((_, index) => index == 1 ? (WhenRemoved)restoring : new WhenRemoved.Disable())])
            .Apply(RoundKind.TooClose, [Propose(1, new RemovalReason.TooClose(new OtherId(0), BySize))]);

        var round = Assert.Single(decisions.Rounds);

        Assert.Equal(
            (1, 1, 0), (RemovalList.CountLinkedToRestoredIn(decisions, round), RemovalList.CountLinkedIn(decisions, round), RemovalList.CountRestoredLinkedIn(decisions, round)));
    }

    [Fact]
    public void MeshProblems_ShowTheMeshProblemsOnlyInTheDetailedLogAndTheMeshErrorsInBoth()
    {
        ImmutableArray<AssetProblem> problems = [AssetProblem.NotFound("m.nif")];
        var error = new UnexpectedError(
            "reading mesh triangles", "InvalidDataException: broke", "Objects using the mesh are treated as having no mesh triangles.", new ErrorSubject(SubjectKind.Mesh, "m.nif", Position: 0));

        var normal = LogSections.BuildMeshProblemsSection(problems, [error], Normal);
        var detailed = LogSections.BuildMeshProblemsSection(problems, [error], Detailed);

        Assert.Equal("meshProblems", normal.Id);
        Assert.Equal(UnexpectedErrorLines.Of([error], Normal).ToArray(), normal.Lines.ToArray());
        Assert.Equal(["  [mesh] not found: m.nif", ..UnexpectedErrorLines.Of([error], Detailed)], detailed.Lines.ToArray());
        Assert.Empty(LogSections.BuildMeshProblemsSection([], [], Detailed).Lines);
    }

    /// <summary>Round 1: target 0 too close, protected target 3 held. Round 2: 1 touches 0 (its linked partner 2 goes along), protected 4 held.</summary>
    private static (CollectedObjects World, RemovalDecisions Decisions) RunRounds()
    {
        var world = CreateWorld();
        var decisions = TestSeededDecisions.StartDisablingAll(CreateProtection(world.Targets), TargetCount)
            .Apply(RoundKind.TooClose, [Propose(0, new RemovalReason.TooClose(new OtherId(0), BySize)), Propose(Protected, new RemovalReason.TooClose(new OtherId(0), BySize))])
            .Apply(RoundKind.AlsoRemove, [Propose(1, new RemovalReason.Touching(new TargetId(0))), Propose(ProtectedTouching, new RemovalReason.Touching(new TargetId(0)))]);
        return (world, decisions);
    }

    private static CollectedObjects CreateWorld()
    {
        var targets = TestTargets.CreateMany(TargetCount);
        return new CollectedObjects(
            [.. targets],
            [CreateOtherModObject()],
            [],
            [],
            [],
            new Dictionary<RecordKey, SpaceFact> { [TestTargets.Space] = new(TestTargets.Space, "Tamriel", SpaceKind.Worldspace, "Skyrim") },
            new ReadCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            [],
            TargetWorkOrder.Of(targets),
            null);
    }

    private static ObjectsToKeep CreateProtection(IReadOnlyList<TargetObject> targets) =>
        ObjectsToKeep.Build(
            targets,
            [TestTargets.Link(1, 2)],
            TestTargets.References(
                TargetCount, new Dictionary<int, KeepReason> { [Protected] = QuestReason, [ProtectedTouching] = QuestReason }),
            TestBaseFacts.Unresolved);

    private static ProposedRemoval Propose(int index, RemovalReason reason) => new(new TargetId(index), reason);

    private static OtherObject CreateOtherModObject() =>
        new(
            new OtherId(0),
            new FormKey(ModKey.FromNameAndExtension("Other.esp"), 0x10).ToRecordKey(),
            TestTargets.Space,
            new PluginName("Other.esp"),
            EditorId: null,
            Base: null,
            Vector3.Zero,
            default(Vector3),
            Scale: 1f,
            IsPrimitive: false,
            HasMapMarker: false,
            Change: new ObjectChange(new PluginName("Other.esp"), AcceptedFrom: null),
            Roles: ReferenceRoles.None,
            ChangeWithoutCleanedMod: null);
}
