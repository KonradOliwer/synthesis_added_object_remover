using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The changed-terrain step: the accepted height and terrain type per vertex, the accepted water per cell, which plugin is named for a
/// change, the check area, the height rule, which objects are checked, the per-object error boundary and the independence from the
/// thread count.
/// </summary>
public class ChangedTerrainObjectsTests
{
    private const float Ground = 0f;
    private const float Raised = 64f;

    /// <summary>The height tolerance of <see cref="Settings"/>, and the smallest height step terrain heights come in.</summary>
    private const float Tolerance = 16f;
    private const float HeightStep = 8f;
    private const int VertexCount = TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide;

    /// <summary>The share of an area's vertices whose terrain type must change in <see cref="Settings"/>: 4 of the 16 at <see cref="WideSpot"/>.</summary>
    private const int TypePercent = 25;
    private const int ChangesAtTypeShare = 4;
    private const int AnyChange = 0;
    private const int WholeArea = 100;
    private const int MostChanges = 3;
    private const int FewerChanges = 2;
    private const float FullOpacity = 1f;

    /// <summary>A terrain vertex inside the check area of an object at <see cref="Spot"/>, and one far outside it.</summary>
    private const int Near = 8 * TerrainHeights.VerticesPerSide + 8;
    private const int Far = 20 * TerrainHeights.VerticesPerSide + 20;

    /// <summary>The vertex next to <see cref="Near"/>, also inside the check area (column 9 of row 8).</summary>
    private const int Beside = Near + 1;

    private static readonly Vector3 Spot = new(1000, 1000, 0);

    /// <summary>The 64-unit area of an object here covers 4 by 4 vertices from column and row 7 of the cell, all inside its bottom left quadrant.</summary>
    private static readonly Vector3 WideSpot = new(1088, 1088, 0);
    private const int WideSpotFirstVertex = 7;
    private const int WideSpotVerticesPerSide = 4;

    private static readonly (int Column, int Row)[] WideSpotVertices =
    [
        .. Enumerable.Range(WideSpotFirstVertex, WideSpotVerticesPerSide)
            .SelectMany(row => Enumerable.Range(WideSpotFirstVertex, WideSpotVerticesPerSide).Select(column => (column, row))),
    ];
    private static readonly RecordKey Worldspace = TestTargets.Space;
    private static readonly RecordKey Interior = TestTargets.SpaceKey(0x101);
    private static readonly RecordKey ChildWorldspace = TestTargets.SpaceKey(0x102);
    private static readonly ExteriorCell Cell = new(Worldspace, 0, 0);

    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName Master = new("Master.esm");
    private static readonly PluginName Early = new("Early.esp");
    private static readonly PluginName Target = TestTargets.TargetName;
    private static readonly PluginName RequiringMod = new("RequiringMod.esp");
    private static readonly PluginName Other = new("Other.esp");
    private static readonly PluginName CompatibilityPatch = new("CompatibilityPatch.esp");
    private static readonly PluginName TrustedMod = new("TrustedMod.esp");
    private static readonly PluginName OwnChanges = new("OwnChanges.esp");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName Patch = new("Synthesis.esp");

    private static readonly BaseKey Rock = new(new RecordKey(Target, 0x900), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey Marker = new(new RecordKey(Target, 0x901), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey Broken = new(new RecordKey(Target, 0x902), BaseLinkKind.PlaceableObject);

    /// <summary>
    /// The target's masters are the base game and Master.esm. TrustedMod.esp is trusted, and so are the two plugins that require the
    /// target, as the rule for such mods trusts them; OwnChanges.esp is trusted for its own changes; Early.esp loads before the
    /// target and is untrusted.
    /// </summary>
    private static readonly ModTrustTable Mods = TestModTrust.Of(
        Target,
        Patch,
        trusted: [BaseGame, Master, TrustedMod, RequiringMod, CompatibilityPatch],
        untrusted: [Early, Other, Later],
        trustedForOwnChanges: [OwnChanges]);

    private static readonly ImmutableArray<PluginName> TargetMasters = [BaseGame, Master];

    private static readonly PluginName[] Plugins = [BaseGame, Master, Early, Target, RequiringMod, Other, CompatibilityPatch, TrustedMod, OwnChanges, Later, Patch];

    private static readonly LoadOrderPlugins LoadOrder = TestLoadOrderPlugins.Of(
        new KnownPluginNames(Plugins), Patch, [.. Plugins.Select(plugin => new PluginListing(plugin, true, plugin == Target ? TargetMasters : []))]);

    private static ChangedTerrainSettings Settings(
        TerrainCheckedObjects objects = TerrainCheckedObjects.VisibleObjectsOnTheGround,
        float tolerance = Tolerance,
        bool bySize = false,
        float multiplier = 0.5f,
        bool byDistance = true,
        bool heights = true,
        bool types = true,
        int typePercent = TypePercent,
        bool waterHeights = true,
        float waterTolerance = Tolerance,
        bool waterAddedOrRemoved = true,
        int waterPercent = TypePercent) =>
        new(
            On: true, objects, heights, tolerance, types, typePercent, waterHeights, waterTolerance, waterAddedOrRemoved, waterPercent,
            bySize, multiplier, byDistance, AreaDistanceInUnits: 64f);

    private static float[] Heights(float height, params (int Vertex, float Height)[] changes)
    {
        var heights = Enumerable.Repeat(height, VertexCount).ToArray();
        foreach (var (vertex, changed) in changes) heights[vertex] = changed;
        return heights;
    }

    private static TerrainVersion Flat(PluginName plugin) => new(plugin, Heights(Ground), Layers: null);

    private static TerrainVersion RaisedAt(PluginName plugin, int vertex, float height = Raised) => new(plugin, Heights(Ground, (vertex, height)), Layers: null);

    private static TargetObject Placed(int index, BaseKey baseKey, Vector3? position = null, RecordKey? space = null) =>
        TestTargets.Create(index, TestTargets.At(position ?? Spot), baseKey, space ?? Worldspace);

    private static ChangedTerrainResult Find(
        IReadOnlyDictionary<ExteriorCell, TerrainVersion[]> terrain, ChangedTerrainSettings settings, int threads = 1, params TargetObject[] targets) =>
        Find(new TerrainPlugin(terrain), settings, threads, targets);

    private static ChangedTerrainResult Find(TerrainPlugin plugin, ChangedTerrainSettings settings, int threads = 1, params TargetObject[] targets) =>
        ChangedTerrainObjects.Find(
            TestScenes.CreateWorld(targets.Length == 0 ? [Placed(0, Rock)] : targets, []),
            Mods,
            LoadOrder,
            plugin,
            new BoxShapes(),
            new Execution(threads),
            settings);

    private static ChangedTerrainResult FindInCell(params TerrainVersion[] versions) =>
        Find(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = versions }, Settings());

    /// <param name="changedBy">Null for a change only the patch holds.</param>
    private static ProposedRemoval Removal(int index, PluginName? changedBy, TerrainChangeKind kind = TerrainChangeKind.Height) =>
        new(new TargetId(index), new RemovalReason.TerrainChanged(changedBy, kind));

    [Fact]
    public void ASwitchedOffStepReadsNoTerrainAndProposesNothing()
    {
        var result = ChangedTerrainObjects.Find(
            TestScenes.CreateWorld([Placed(0, Rock)], []), Mods, LoadOrder, new PluginNameParsingPlugin(), new BoxShapes(), new Execution(1),
            Settings() with { On = false });

        Assert.Equal(StepOutcome.SwitchedOff, result.Outcome);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public void AnotherModRaisingTheTerrainUnderTheObjectRemovesIt()
    {
        var result = FindInCell(Flat(Target), RaisedAt(Other, Near));

        Assert.Equal(StepOutcome.Performed, result.Outcome);
        Assert.Equal([Removal(0, Other)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.CellsCompared);
    }

    [Fact]
    public void AChangeOutsideTheCheckAreaCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Far)).Proposals);

    [Theory]
    [InlineData(Tolerance, Tolerance, false)]
    [InlineData(Tolerance, Tolerance + HeightStep, true)]
    [InlineData(0f, HeightStep, true)]
    public void AHeightChangeCountsOnlyBeyondTheTolerance(float tolerance, float change, bool removed)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near, change)] };

        Assert.Equal(removed, Find(terrain, Settings(tolerance: tolerance)).Proposals.Any());
    }

    [Fact]
    public void APluginRequiringTheTargetsOwnChangeCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(RequiringMod, Near)).Proposals);

    [Fact]
    public void APluginRequiringTheTargetsChangeCarriedByAnUntrustedModCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(RequiringMod, Near), RaisedAt(Later, Near)).Proposals);

    [Fact]
    public void ATrustedModsChangeCarriedByAnUntrustedModCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(TrustedMod, Near), RaisedAt(Later, Near)).Proposals);

    [Fact]
    public void AnUntrustedModsHeightCarriedByATrustedCompatibilityPatchIsAccepted() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(CompatibilityPatch, Near)).Proposals);

    /// <summary>The carried height is the accepted one, so the later mod is named, not the mod whose height was carried.</summary>
    [Fact]
    public void AChangeOnTopOfACarriedHeightIsComparedWithTheCarriedHeight() =>
        Assert.Equal(
            [Removal(0, Later)],
            FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(CompatibilityPatch, Near), RaisedAt(Later, Near, 2 * Raised)).Proposals.AsEnumerable());

    [Fact]
    public void AnUntrustedModCarryingAnotherUntrustedModsChangeLeavesTheFirstNamed() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Later, Near)).Proposals.AsEnumerable());

    [Fact]
    public void ATrustedModsSmallChangeOnTopOfAnUntrustedChangeMakesItAccepted() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(TrustedMod, Near, Raised + HeightStep)).Proposals);

    /// <summary>
    /// OwnChanges.esp re-saves the other mod's land: one height step above the other mod's is within the tolerance, so it owns no
    /// height there and the other mod is named; twice the raise is its own height, which is accepted.
    /// </summary>
    [Theory]
    [InlineData(Raised + HeightStep, true)]
    [InlineData(2 * Raised, false)]
    public void AModTrustedForItsOwnChangesOwnsAHeightOnlyBeyondTheTolerance(float resaved, bool removed)
    {
        var result = FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(OwnChanges, Near, resaved));

        ProposedRemoval[] expected = removed ? [Removal(0, Other)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// Each step is within the tolerance of the version below, so OwnChanges.esp owns no height; together they are beyond it, and
    /// OwnChanges.esp holds the first height that differs from the accepted one beyond the tolerance.
    /// </summary>
    [Fact]
    public void SmallStepsAddingUpThroughAModTrustedForItsOwnChangesNameThatMod() =>
        Assert.Equal(
            [Removal(0, OwnChanges)],
            FindInCell(Flat(Target), RaisedAt(Other, Near, Tolerance), RaisedAt(OwnChanges, Near, Tolerance + HeightStep)).Proposals.AsEnumerable());

    [Fact]
    public void ATrustedModsOwnLargerChangeOnTopOfAnUntrustedChangeCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(TrustedMod, Near, 2 * Raised)).Proposals);

    /// <summary>Each step is within the tolerance of the next, and only the trusted mod's own height is beyond it.</summary>
    [Fact]
    public void ATrustedModsStepPushingSmallChangesBeyondTheToleranceCountsForNothing() =>
        Assert.Empty(
            FindInCell(Flat(Target), RaisedAt(Other, Near, HeightStep), RaisedAt(CompatibilityPatch, Near, Tolerance), RaisedAt(TrustedMod, Near, Tolerance + HeightStep))
                .Proposals);

    /// <summary>Each step is within the tolerance of the next, and the later untrusted mod's own height is beyond it.</summary>
    [Fact]
    public void UntrustedModsSmallStepsAddingUpBeyondTheToleranceNameTheModWhoseOwnChangeExceedsIt() =>
        Assert.Equal(
            [Removal(0, Later)],
            FindInCell(Flat(Target), RaisedAt(Other, Near, HeightStep), RaisedAt(Later, Near, Tolerance + HeightStep)).Proposals.AsEnumerable());

    [Fact]
    public void APatchTakingAnEarlierChangeBackInNamesTheModThatMadeIt() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Later), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AModUndoingTheChangeOfAPluginRequiringTheTargetChangesTheAcceptedTerrain() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(RequiringMod, Near), Flat(Other)).Proposals.AsEnumerable());

    [Fact]
    public void ALaterModRestoringTheAcceptedTerrainLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Later)).Proposals);

    [Fact]
    public void ATrustedModRestoringTheAcceptedTerrainLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(TrustedMod)).Proposals);

    [Fact]
    public void AModLoadingBeforeTheCleanedModIsOverriddenByIt() =>
        Assert.Empty(FindInCell(RaisedAt(Early, Near), Flat(Target)).Proposals);

    [Fact]
    public void ThePatchCarryingAnUntrustedModsChangeNamesThatMod() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AChangeOnlyByTrustedModsAndThePatchRemovesNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(TrustedMod, Near), RaisedAt(Patch, Near)).Proposals);

    /// <summary>Only the patch holds the change: an earlier patcher of the group made it, and no plugin is named.</summary>
    [Fact]
    public void AChangeOnlyThePatchHoldsNamesAnEarlierPatcher() =>
        Assert.Equal([Removal(0, changedBy: null)], FindInCell(Flat(Target), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void TheLastLoadingChangingModIsNamedOverEveryCellTheAreaCrosses()
    {
        const int westCellEastEdge = 8 * TerrainHeights.VerticesPerSide + TerrainGrid.SquaresPerSide;
        const int eastCellWestEdgeNeighbour = 8 * TerrainHeights.VerticesPerSide + 1;
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]>
        {
            [Cell] = [Flat(Target), RaisedAt(Later, westCellEastEdge)],
            [new ExteriorCell(Worldspace, 1, 0)] = [Flat(Target), RaisedAt(Other, eastCellWestEdgeNeighbour)],
        };

        var result = Find(terrain, Settings(), threads: 1, Placed(0, Rock, new Vector3(ExteriorGrid.CellSize, Spot.Y, 0)));

        Assert.Equal([Removal(0, Later)], result.Proposals.AsEnumerable());
        Assert.Equal(2, result.CellsCompared);
    }

    [Fact]
    public void TheBaseGameAsAMasterOfTheCleanedModIsAcceptedWhenTheCleanedModHasNoTerrainThere() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(BaseGame), RaisedAt(Other, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AModMatchingTheLastTargetMastersTerrainChangesNothingWhenTheTargetHasNoTerrainThere() =>
        Assert.Empty(FindInCell(Flat(BaseGame), RaisedAt(Master, Near), RaisedAt(Other, Near)).Proposals);

    [Fact]
    public void AModRestoringAnEarlierMastersTerrainOverTheLastTargetMastersChangesItWhenTheTargetHasNoTerrainThere() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(BaseGame), RaisedAt(Master, Near), Flat(Other)).Proposals.AsEnumerable());

    [Fact]
    public void AModThatIsNoTargetMasterLoadingBeforeTheTargetChangesTheMastersTerrain() =>
        Assert.Equal([Removal(0, Early)], FindInCell(Flat(BaseGame), RaisedAt(Early, Near)).Proposals.AsEnumerable());

    [Fact]
    public void ADeletedLandRecordOfAnotherModIsAHeightChange() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), new TerrainVersion(Other, Heights: null, Layers: null)).Proposals.AsEnumerable());

    [Fact]
    public void ACellNoTrustedPluginHasALandRecordOfIsNotChecked()
    {
        var result = FindInCell(RaisedAt(Other, Near));

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 1), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// The other mod's land is the cell's first, as in a world it adds; OwnChanges.esp raises the vertex beside the object's, so only
    /// that vertex has an accepted height. The later mod changes the object's vertex or the one beside it.
    /// </summary>
    [Theory]
    [InlineData(2 * Raised, Raised, false)]
    [InlineData(Raised, Ground, true)]
    public void AVertexWithoutATrustedVersionIsNotChecked(float nearHeight, float besideHeight, bool removed)
    {
        var result = FindInCell(
            RaisedAt(Other, Near),
            OwnChangesRaisingBeside,
            new TerrainVersion(Later, Heights(Ground, (Near, nearHeight), (Beside, besideHeight)), Layers: null));

        ProposedRemoval[] expected = removed ? [Removal(0, Later)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
        Assert.Equal((1, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>
    /// OwnChanges.esp keeps the other mod's raise at the object's vertex and raises the vertex beside it. Over the target's flat land
    /// the object's vertex is accepted from the target and the one beside it from OwnChanges.esp, a terrain no single version holds.
    /// </summary>
    private static readonly TerrainVersion OwnChangesRaisingBeside = new(OwnChanges, Heights(Ground, (Near, Raised), (Beside, Raised)), Layers: null);

    [Fact]
    public void TheAcceptedHeightsTakeEachVertexFromItsOwnAcceptedVersion()
    {
        var comparison = new CellTerrainComparison(
            new TerrainPlugins(Mods, LoadOrder), new TerrainTypes(new TerrainPlugin(new Dictionary<ExteriorCell, TerrainVersion[]>())), Settings());

        var terrain = comparison.Compare([Flat(Target), RaisedAt(Other, Near), OwnChangesRaisingBeside]);

        Assert.Equal((Ground, Raised), (terrain!.AcceptedHeights![Near], terrain.AcceptedHeights[Beside]));
    }

    /// <summary>The later mod keeps the accepted terrain, lowers the vertex beside the object's, or keeps the other mod's raise.</summary>
    [Theory]
    [InlineData(Ground, Raised, null)]
    [InlineData(Ground, Ground, "Later.esp")]
    [InlineData(Raised, Raised, "Other.esp")]
    public void EachVertexIsComparedWithItsOwnAcceptedVersion(float nearHeight, float besideHeight, string? changedBy)
    {
        var result = FindInCell(
            Flat(Target),
            RaisedAt(Other, Near),
            OwnChangesRaisingBeside,
            new TerrainVersion(Later, Heights(Ground, (Near, nearHeight), (Beside, besideHeight)), Layers: null));

        ProposedRemoval[] expected = changedBy is { } plugin ? [Removal(0, new PluginName(plugin))] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    [Fact]
    public void ObjectsInSpacesWithoutTerrainAreCountedAndNotChecked()
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };

        var result = Find(terrain, Settings(), threads: 1, Placed(0, Rock, space: Interior), Placed(1, Rock));

        Assert.Equal([Removal(1, Other)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.ObjectsWithoutTerrain);
    }

    [Theory]
    [InlineData(TerrainCheckedObjects.VisibleObjectsOnTheGround, new[] { 0 })]
    [InlineData(TerrainCheckedObjects.VisibleObjectsAtAnyHeight, new[] { 0, 1 })]
    [InlineData(TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes, new[] { 0, 1, 2 })]
    public void TheObjectsCheckedFollowTheSetting(TerrainCheckedObjects objects, int[] removed)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };
        TargetObject[] targets = [Placed(0, Rock), Placed(1, Rock, Spot + new Vector3(0, 0, 1000)), Placed(2, Marker)];

        var result = Find(terrain, Settings(objects), threads: 1, targets);

        Assert.Equal(removed, result.Proposals.Select(proposal => proposal.Target.Index));
    }

    [Theory]
    [InlineData(0.5f, false)]
    [InlineData(10f, true)]
    public void TheDistanceCanBeAMultipleOfTheObjectsSize(float multiplier, bool removed)
    {
        var aside = (int)(Spot.Y / TerrainGrid.VertexSpacing) * TerrainHeights.VerticesPerSide + 10;
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, aside)] };

        var result = Find(terrain, Settings(bySize: true, multiplier: multiplier, byDistance: false));

        Assert.Equal(removed, result.Proposals.Any());
    }

    /// <summary>With the object 20 units wide, the 64-unit area reaches vertex column 9 and the 10-times-size area vertex column 10.</summary>
    [Theory]
    [InlineData(0.5f, 9, true)]
    [InlineData(10f, 10, true)]
    [InlineData(0.5f, 10, false)]
    [InlineData(10f, 11, false)]
    public void WithBothAreasOnAChangeInEitherAreaRemovesTheObject(float multiplier, int changedColumn, bool removed)
    {
        var changed = 8 * TerrainHeights.VerticesPerSide + changedColumn;
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, changed)] };

        var result = Find(terrain, Settings(bySize: true, multiplier: multiplier, byDistance: true));

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AnObjectWhoseCheckFailsIsReportedAndTheOthersAreStillChecked(int threads)
    {
        var terrain = new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] };
        TargetObject[] targets = [Placed(0, Rock), Placed(1, Broken), Placed(2, Rock)];

        var result = Find(terrain, Settings(), threads, targets);

        Assert.Equal([0, 2], result.Proposals.Select(proposal => proposal.Target.Index));
        Assert.Equal(
            new PartFailure(ChangedTerrainParts.TerrainCheck, "InvalidDataException: broken base", new FailureSubject(targets[1].Key.ToString(), null, 1)),
            Assert.Single(result.Failures));
    }

    [Fact]
    public void TheResultDoesNotDependOnTheThreadCount()
    {
        var terrain = Enumerable.Range(0, 4).ToDictionary(
            cellX => new ExteriorCell(Worldspace, cellX, 0),
            cellX => new[] { Flat(Target), RaisedAt(Other, Near), cellX % 2 == 0 ? RaisedAt(Later, Near, 2 * Raised) : RaisedAt(Patch, Near) });
        var targets = Enumerable.Range(0, 40)
            .Select(index => Placed(index, Rock, Spot + new Vector3(index % 4 * ExteriorGrid.CellSize, index / 4 * 4f, 0)))
            .ToArray();

        var single = Find(terrain, Settings(), threads: 1, targets);
        var parallel = Find(terrain, Settings(), threads: 4, targets);

        Assert.Equal(40, single.Proposals.Length);
        Assert.Equal(single.Proposals.AsEnumerable(), parallel.Proposals.AsEnumerable());
        Assert.Equal((4, 4), (single.CellsCompared, parallel.CellsCompared));
        Assert.Contains(Removal(1, Other), single.Proposals);
        Assert.Contains(Removal(0, Later), single.Proposals);
    }

    private static readonly RecordKey GrassMaterial = new(BaseGame, 0x10);
    private static readonly RecordKey ThickGrassMaterial = new(Other, 0x20);
    private static readonly RecordKey StoneMaterial = new(BaseGame, 0x30);

    private static readonly RecordKey Grass = new(BaseGame, 0xA00);
    private static readonly RecordKey OtherGrass = new(BaseGame, 0xA01);
    private static readonly RecordKey ThickGrass = new(Other, 0xA02);
    private static readonly RecordKey Road = new(BaseGame, 0xA03);
    private static readonly RecordKey Unresolved = new(BaseGame, 0xA04);
    private static readonly RecordKey OtherUnresolved = new(BaseGame, 0xA05);

    /// <summary>Two grass textures, a mod's grass whose material is a child of grass, and a road on stone.</summary>
    private static readonly Dictionary<RecordKey, RecordKey[]> Materials = new()
    {
        [Grass] = [GrassMaterial],
        [OtherGrass] = [GrassMaterial],
        [ThickGrass] = [ThickGrassMaterial, GrassMaterial],
        [Road] = [StoneMaterial],
    };

    /// <summary>Every quadrant painted with the base texture, and optionally one alpha layer of the same opacity everywhere.</summary>
    private static TerrainLayers Painted(RecordKey baseTexture, RecordKey? alpha = null, float opacity = 1f) =>
        new([.. Enumerable.Range(0, TerrainQuadrants.PerSide * TerrainQuadrants.PerSide).Select(_ => new QuadrantLayers(
            baseTexture,
            alpha is { } texture ? [new TerrainAlphaLayer(texture, LayerNumber: 0, Enumerable.Repeat(opacity, TerrainQuadrants.VertexCount).ToArray())] : []))]);

    private static TerrainVersion FlatPainted(PluginName plugin, TerrainLayers? layers) => new(plugin, Heights(Ground), layers);

    private static TerrainPlugin PaintedCell(params TerrainVersion[] versions) =>
        new(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = versions }, Materials);

    [Fact]
    public void AnotherModChangingTheTerrainTypeRemovesTheObject() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.TerrainType)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, Painted(Road))), Settings()).Proposals.AsEnumerable());

    [Fact]
    public void ATextureOfTheSameMaterialIsNoChange() =>
        Assert.Empty(Find(PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, Painted(OtherGrass))), Settings()).Proposals);

    [Fact]
    public void ATextureOfAChildMaterialIsTheParentsType() =>
        Assert.Empty(Find(PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, Painted(ThickGrass))), Settings()).Proposals);

    [Fact]
    public void TwoTexturesWithoutMaterialAreTheSameType() =>
        Assert.Empty(Find(PaintedCell(FlatPainted(Target, Painted(Unresolved)), FlatPainted(Other, Painted(OtherUnresolved))), Settings()).Proposals);

    [Fact]
    public void AHeightAndTypeChangeHasBothKinds()
    {
        var changed = new TerrainVersion(Other, Heights(Ground, (Near, Raised)), Painted(Road));

        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.Height | TerrainChangeKind.TerrainType)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), changed), Settings()).Proposals.AsEnumerable());
    }

    /// <summary>The other mod raises the object's vertex; the later mod carries the height and paves the ground.</summary>
    [Fact]
    public void AHeightAndATypeChangeOfDifferentModsNameTheLaterLoadingOne()
    {
        var raised = new TerrainVersion(Other, Heights(Ground, (Near, Raised)), Painted(Grass));
        var paved = new TerrainVersion(Later, Heights(Ground, (Near, Raised)), Painted(Road));

        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.Height | TerrainChangeKind.TerrainType)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), raised, paved), Settings()).Proposals.AsEnumerable());
    }

    [Theory]
    [InlineData(true, TerrainChangeKind.Height | TerrainChangeKind.TerrainType)]
    [InlineData(false, TerrainChangeKind.TerrainType)]
    public void ADeletedLandRecordShowsTheDefaultTerrain(bool heights, TerrainChangeKind kind) =>
        Assert.Equal(
            [Removal(0, Other, kind)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), new TerrainVersion(Other, Heights: null, Layers: null)), Settings(heights: heights)).Proposals.AsEnumerable());

    /// <summary>OwnChanges.esp carries the other mod's height and paves the ground: the road is its own change, the height is not.</summary>
    [Fact]
    public void ATypeChangeOfAModTrustedForItsOwnChangesIsLeftOutOfTheKind()
    {
        var heightOnly = new TerrainVersion(Other, Heights(Ground, (Near, Raised)), Painted(Grass));
        var shownByOwnChanges = new TerrainVersion(OwnChanges, Heights(Ground, (Near, Raised)), Painted(Road));

        var result = Find(PaintedCell(FlatPainted(Target, Painted(Grass)), heightOnly, shownByOwnChanges), Settings());

        Assert.Equal([new ObjectOnChangedTerrain(new TargetId(0), Other, TerrainChangeKind.Height)], result.Hits.AsEnumerable());
        Assert.Equal([Removal(0, Other, TerrainChangeKind.Height)], result.Proposals.AsEnumerable());
    }

    /// <summary>OwnChanges.esp carries the other mod's road and raises the ground: the height is its own change, the road is not.</summary>
    [Fact]
    public void AHeightChangeOfAModTrustedForItsOwnChangesIsLeftOutOfTheKind()
    {
        var typeOnly = FlatPainted(Other, Painted(Road));
        var shownByOwnChanges = new TerrainVersion(OwnChanges, Heights(Ground, (Near, Raised)), Painted(Road));

        var result = Find(PaintedCell(FlatPainted(Target, Painted(Grass)), typeOnly, shownByOwnChanges), Settings());

        Assert.Equal([Removal(0, Other, TerrainChangeKind.TerrainType)], result.Proposals.AsEnumerable());
    }

    [Theory]
    [InlineData(ChangesAtTypeShare - 1, false)]
    [InlineData(ChangesAtTypeShare, true)]
    [InlineData(ChangesAtTypeShare + 1, true)]
    public void ATypeChangeRemovesTheObjectOnlyFromTheShareOfChangedVerticesOn(int changed, bool removed)
    {
        var plugin = PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, RoadAtWideSpot(changed)));

        var result = Find(plugin, Settings(), threads: 1, Placed(0, Rock, WideSpot));

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.TerrainType)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void AShareOfZeroRemovesTheObjectOnAnyOneChangedVertex(int changed, bool removed)
    {
        var plugin = PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, RoadAtWideSpot(changed)));

        Assert.Equal(removed, Find(plugin, Settings(typePercent: AnyChange), threads: 1, Placed(0, Rock, WideSpot)).Proposals.Any());
    }

    /// <summary>The untrusted mod changed the type of fewer vertices than the share; the later mod adds the rest.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OnlyTheVerticesUntrustedModsChangedMakeUpTheShare(bool laterUntrusted, bool removed)
    {
        var plugin = PaintedCell(
            FlatPainted(Target, Painted(Grass)),
            FlatPainted(Other, RoadAtWideSpot(ChangesAtTypeShare - 1)),
            FlatPainted(laterUntrusted ? Later : TrustedMod, RoadAtWideSpot(ChangesAtTypeShare + 1)));

        Assert.Equal(removed, Find(plugin, Settings(), threads: 1, Placed(0, Rock, WideSpot)).Proposals.Any());
    }

    /// <summary>
    /// The other mod adds the land; OwnChanges.esp paves two of the sixteen vertices of the area, the only ones with an accepted
    /// terrain type, and the later mod paints them back: all checked vertices changed type, though only two of sixteen did.
    /// </summary>
    [Fact]
    public void VerticesWithoutAnAcceptedValueAreLeftOutOfTheShare()
    {
        var plugin = PaintedCell(
            FlatPainted(Other, Painted(Grass)), FlatPainted(OwnChanges, RoadAtWideSpot(FewerChanges)), FlatPainted(Later, Painted(Grass)));

        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.TerrainType)],
            Find(plugin, Settings(), threads: 1, Placed(0, Rock, WideSpot)).Proposals.AsEnumerable());
    }

    /// <summary>The later mod keeps the earlier mod's road and paints more next to it.</summary>
    [Theory]
    [InlineData(MostChanges, FewerChanges, false)]
    [InlineData(FewerChanges, FewerChanges, true)]
    [InlineData(FewerChanges, MostChanges, true)]
    public void ATypeChangeNamesTheModThatChangedTheMostVerticesAndTheLaterLoadingOnATie(int otherChanges, int laterChanges, bool laterNamed)
    {
        var plugin = PaintedCell(
            FlatPainted(Target, Painted(Grass)),
            FlatPainted(Other, RoadAtWideSpot(otherChanges)),
            FlatPainted(Later, RoadAtWideSpot(otherChanges + laterChanges)));

        var result = Find(plugin, Settings(), threads: 1, Placed(0, Rock, WideSpot));

        Assert.Equal([Removal(0, laterNamed ? Later : Other, TerrainChangeKind.TerrainType)], result.Proposals.AsEnumerable());
    }

    [Theory]
    [InlineData(AnyChange)]
    [InlineData(TypePercent)]
    [InlineData(WholeArea)]
    public void OneHeightChangeRemovesTheObjectWhateverTheTypePercent(int typePercent) =>
        Assert.Equal(
            [Removal(0, Other)],
            Find(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] }, Settings(typePercent: typePercent)).Proposals.AsEnumerable());

    [Fact]
    public void ATypeChangeBelowTheShareIsLeftOutOfTheKindOfAHeightChange()
    {
        var plugin = PaintedCell(FlatPainted(Target, Painted(Grass)), new TerrainVersion(Other, Heights(Ground, (Near, Raised)), RoadAtWideSpot(1)));

        Assert.Equal([Removal(0, Other, TerrainChangeKind.Height)], Find(plugin, Settings(), threads: 1, Placed(0, Rock, WideSpot)).Proposals.AsEnumerable());
    }

    /// <summary>The other mod raises the object's vertex; an earlier patcher repaints the ground in the patch, which loads last.</summary>
    [Fact]
    public void ARealModsHeightChangeIsNamedBeforeATypeChangeOnlyThePatchHolds()
    {
        var raised = new TerrainVersion(Other, Heights(Ground, (Near, Raised)), Painted(Grass));
        var repainted = new TerrainVersion(Patch, Heights(Ground, (Near, Raised)), Painted(Road));

        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.Height | TerrainChangeKind.TerrainType)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), raised, repainted), Settings()).Proposals.AsEnumerable());
    }

    /// <summary>The patch carries the other mod's road and an earlier patcher paints more vertices: the real mod is named.</summary>
    [Fact]
    public void ATypeChangeNamesARealModEvenWhenThePatchAloneChangedMoreVertices()
    {
        var plugin = PaintedCell(
            FlatPainted(Target, Painted(Grass)),
            FlatPainted(Other, RoadAtWideSpot(FewerChanges)),
            FlatPainted(Patch, RoadAtWideSpot(FewerChanges + MostChanges)));

        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.TerrainType)],
            Find(plugin, Settings(), threads: 1, Placed(0, Rock, WideSpot)).Proposals.AsEnumerable());
    }

    /// <summary>Grass everywhere, with road painted over the first <paramref name="count"/> vertices of the area of an object at <see cref="WideSpot"/>.</summary>
    private static TerrainLayers RoadAtWideSpot(int count)
    {
        var opacities = new float[TerrainQuadrants.VertexCount];
        foreach (var (column, row) in WideSpotVertices.Take(count)) opacities[row * TerrainQuadrants.VerticesPerSide + column] = FullOpacity;
        var quadrants = Painted(Grass).Quadrants;
        quadrants[TerrainQuadrants.IndexOf(0, 0)] = new QuadrantLayers(Grass, [new TerrainAlphaLayer(Road, LayerNumber: 0, opacities)]);
        return new TerrainLayers(quadrants);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void TwoObjectsInOneCellEachGetTheirOwnChangerAndKind(int threads)
    {
        var roadTopRight = RoadIn(TerrainQuadrants.IndexOf(1, 1));
        var plugin = PaintedCell(
            FlatPainted(Target, Painted(Grass)),
            new TerrainVersion(Other, Heights(Ground, (Near, Raised)), Painted(Grass)),
            FlatPainted(Later, roadTopRight),
            new TerrainVersion(Patch, Heights(Ground, (Near, Raised)), roadTopRight));

        var result = Find(plugin, Settings(), threads, Placed(0, Rock), Placed(1, Rock, new Vector3(3000, 3000, 0)));

        Assert.Equal([Removal(0, Other, TerrainChangeKind.Height), Removal(1, Later, TerrainChangeKind.TerrainType)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.CellsCompared);
    }

    /// <summary>A vertex on the edge between two quadrants shows both: grass and road half each, a tie that goes to grass.</summary>
    [Theory]
    [InlineData(1900f, false)]
    [InlineData(2100f, true)]
    public void AVertexOnAQuadrantEdgeAveragesBothQuadrants(float x, bool removed)
    {
        var plugin = PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, RoadIn(TerrainQuadrants.IndexOf(1, 0))));

        var result = Find(plugin, Settings(), threads: 1, Placed(0, Rock, new Vector3(x, Spot.Y, 0)));

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.TerrainType)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>Grass everywhere except one quadrant, whose base layer is road.</summary>
    private static TerrainLayers RoadIn(int quadrantIndex) =>
        new([.. Painted(Grass).Quadrants.Select((quadrant, index) => index == quadrantIndex ? new QuadrantLayers(Road, []) : quadrant)]);

    /// <summary>The object's area ends inside the west cell's last squares, so only the west cell is compared, even at their shared edge.</summary>
    [Theory]
    [InlineData(0, TerrainGrid.SquaresPerSide, true)]
    [InlineData(1, 0, false)]
    public void AVertexOnACellEdgeIsComparedInEachCellWhoseSquaresTheAreaOverlaps(int changedCellX, int changedColumn, bool removed)
    {
        var changed = 8 * TerrainHeights.VerticesPerSide + changedColumn;
        var terrain = Enumerable.Range(0, 2).ToDictionary(
            cellX => new ExteriorCell(Worldspace, cellX, 0),
            cellX => new[] { Flat(Target), cellX == changedCellX ? RaisedAt(Other, changed) : Flat(Other) });

        var result = Find(terrain, Settings(), threads: 1, Placed(0, Rock, new Vector3(4000, Spot.Y, 0)));

        ProposedRemoval[] expected = removed ? [Removal(0, Other)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
        Assert.Equal(1, result.CellsCompared);
    }

    [Fact]
    public void AnObjectInAWorldspaceUsingItsParentsLandIsCheckedOnTheParentsTerrain() =>
        Assert.Equal(
            [Removal(0, Other)],
            Find(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] }, Settings(), threads: 1, Placed(0, Rock, space: ChildWorldspace))
                .Proposals.AsEnumerable());

    [Theory]
    [InlineData(0.4f, false)]
    [InlineData(0.5f, false)]
    [InlineData(0.6f, true)]
    public void TheTypeWithTheLargestShareShowsAndEqualSharesGoToTheFirstMaterial(float roadOpacity, bool removed)
    {
        var painted = FlatPainted(Other, Painted(Grass, Road, roadOpacity));

        Assert.Equal(removed, Find(PaintedCell(FlatPainted(Target, Painted(Grass)), painted), Settings()).Proposals.Any());
    }

    [Fact]
    public void SharesOfTexturesOfOneTypeAddUp()
    {
        var mostlyGrass = FlatPainted(Other, new TerrainLayers([.. Enumerable.Range(0, TerrainQuadrants.PerSide * TerrainQuadrants.PerSide).Select(_ => new QuadrantLayers(
            Road,
            [
                new TerrainAlphaLayer(Grass, LayerNumber: 0, Enumerable.Repeat(0.3f, TerrainQuadrants.VertexCount).ToArray()),
                new TerrainAlphaLayer(OtherGrass, LayerNumber: 1, Enumerable.Repeat(0.3f, TerrainQuadrants.VertexCount).ToArray()),
            ]))]));

        Assert.Empty(Find(PaintedCell(FlatPainted(Target, Painted(Grass)), mostlyGrass), Settings()).Proposals);
    }

    [Fact]
    public void TypeChangesCountOnlyWhenSwitchedOnAndAreThenNotRead()
    {
        var plugin = PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, Painted(Road)));

        Assert.Empty(Find(plugin, Settings(types: false)).Proposals);
        Assert.Equal(0, plugin.MaterialReads);
    }

    [Fact]
    public void HeightChangesCountOnlyWhenSwitchedOn() =>
        Assert.Empty(Find(new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target), RaisedAt(Other, Near)] }, Settings(heights: false)).Proposals);

    [Fact]
    public void EachTexturesMaterialIsReadOncePerCall()
    {
        var plugin = PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(Other, Painted(Road)));
        var targets = Enumerable.Range(0, 8).Select(index => Placed(index, Rock, Spot + new Vector3(index * 4f, 0, 0))).ToArray();

        Assert.Equal(8, Find(plugin, Settings(), threads: 4, targets).Proposals.Length);
        Assert.Equal(2, plugin.MaterialReads);
    }

    private const float BelowTheGround = -100f;
    private const float AboveTheGround = 50f;
    private const float High = 64f;

    /// <summary>The share of an area's vertices that must get covered or uncovered in <see cref="Settings"/>: 4 of the 16 at <see cref="WideSpot"/>.</summary>
    private const int CoverChangesAtShare = 4;

    /// <summary>A plugin's copy of the cell with water at its own height; null for no water.</summary>
    private static CellWaterCopy Water(PluginName plugin, float? level) => new(plugin, HasWater: level != null, level);

    private static CellWaterCopy WaterAtTheDefault(PluginName plugin) => new(plugin, HasWater: true, OwnLevel: null);

    private static WorldspaceWaterCopy Default(PluginName plugin, float? level) => new(plugin, level);

    /// <summary>The worldspace copies of a test that gives none: only the base game's.</summary>
    private static readonly WorldspaceWaterCopy[] BaseGameWorldspace = [Default(BaseGame, BelowTheGround)];

    private static ChangedTerrainResult FindWithWater(
        TerrainVersion[] terrain,
        CellWaterCopy[] water,
        ChangedTerrainSettings? settings = null,
        Vector3? position = null,
        WorldspaceWaterCopy[]? worldspace = null,
        RecordKey? space = null) =>
        Find(
            new TerrainPlugin(
                new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = terrain },
                water: new Dictionary<ExteriorCell, WaterCopies> { [Cell with { WorldspaceKey = space ?? Worldspace }] = new(water, worldspace ?? BaseGameWorldspace) }),
            settings ?? Settings(),
            threads: 1,
            Placed(0, Rock, position ?? Spot, space));

    [Fact]
    public void AnotherModRaisingTheWaterOverTheGroundRemovesTheObject() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)]).Proposals.AsEnumerable());

    [Fact]
    public void AnotherModLoweringTheWaterBelowTheGroundRemovesTheObject() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, AboveTheGround), Water(Other, BelowTheGround)]).Proposals.AsEnumerable());

    [Theory]
    [InlineData(null, AboveTheGround, TerrainChangeKind.WaterAdded)]
    [InlineData(AboveTheGround, null, TerrainChangeKind.WaterRemoved)]
    public void AnotherModAddingOrRemovingWaterOverTheGroundRemovesTheObject(float? expected, float? shown, TerrainChangeKind kind) =>
        Assert.Equal([Removal(0, Other, kind)], FindWithWater([Flat(Target)], [Water(Target, expected), Water(Other, shown)]).Proposals.AsEnumerable());

    [Fact]
    public void UnchangedWaterRemovesNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(BaseGame, AboveTheGround), Water(Target, AboveTheGround), Water(Other, AboveTheGround)]).Proposals);

    [Fact]
    public void AWaterChangeThatLeavesTheGroundUnderWaterRemovesNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, AboveTheGround), Water(Other, 2 * AboveTheGround)]).Proposals);

    [Fact]
    public void WaterAddedBelowTheGroundRemovesNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, null), Water(Other, BelowTheGround)]).Proposals);

    /// <summary>The ground is at 0; the water moves from just below it to just above it by the given change.</summary>
    [Theory]
    [InlineData(Tolerance, Tolerance, false)]
    [InlineData(Tolerance, Tolerance + HeightStep, true)]
    [InlineData(0f, HeightStep, true)]
    public void AWaterHeightChangeCountsOnlyBeyondItsTolerance(float tolerance, float change, bool removed)
    {
        var result = FindWithWater([Flat(Target)], [Water(Target, -1f), Water(Other, change - 1f)], Settings(waterTolerance: tolerance));

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void WaterHeightChangesCountOnlyWhenSwitchedOn() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)], Settings(waterHeights: false)).Proposals);

    [Fact]
    public void WaterAddedOrRemovedCountsOnlyWhenSwitchedOn() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, null), Water(Other, AboveTheGround)], Settings(waterAddedOrRemoved: false)).Proposals);

    [Fact]
    public void WithNoWaterChangeCountedTheWaterIsNotRead()
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)] },
            water: new Dictionary<ExteriorCell, WaterCopies> { [Cell] = new([Water(Target, BelowTheGround), Water(Other, AboveTheGround)], BaseGameWorldspace) });

        Assert.Empty(Find(plugin, Settings(waterHeights: false, waterAddedOrRemoved: false)).Proposals);
        Assert.Equal(0, plugin.WaterReads);
    }

    /// <summary>The ground of the area at <see cref="WideSpot"/> is high except at the given number of vertices, which the raised water covers.</summary>
    [Theory]
    [InlineData(CoverChangesAtShare - 1, false)]
    [InlineData(CoverChangesAtShare, true)]
    public void AWaterChangeRemovesTheObjectOnlyFromTheShareOfCoveredOrUncoveredVerticesOn(int covered, bool removed)
    {
        var land = new TerrainVersion(Target, Heights(High, [.. WideSpotVertices.Take(covered).Select(vertex => (vertex.Row * TerrainHeights.VerticesPerSide + vertex.Column, Ground))]), Layers: null);

        var result = FindWithWater([land], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)], position: WideSpot);

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void LandLoweredUnderUnchangedWaterIsAHeightChangeOnly() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.Height)],
            FindWithWater([new TerrainVersion(Target, Heights(High), Layers: null), Flat(Other)], [Water(Target, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>
    /// The water rises, but stays below the expected ground; only the lowered ground puts the point under water, so the water
    /// change is not counted and the height check alone decides.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void APointCoveredOnlyThroughTheLandChangeIsLeftToTheHeightCheck(bool heights, bool removed)
    {
        var result = FindWithWater(
            [new TerrainVersion(Target, Heights(High), Layers: null), Flat(Other)],
            [Water(Target, AboveTheGround - Tolerance - HeightStep), Water(Other, AboveTheGround)],
            Settings(heights: heights));

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.Height)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    [Fact]
    public void AHeightAndWaterChangeHasBothKindsAndNamesTheLaterMod() =>
        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.Height | TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target), RaisedAt(Other, Near, -Raised)], [Water(Target, BelowTheGround), Water(Later, AboveTheGround)]).Proposals.AsEnumerable());

    [Fact]
    public void AWaterChangeOnlyByAPluginRequiringTheTargetCountsForNothing() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(RequiringMod, AboveTheGround)]).Proposals);

    [Fact]
    public void AnUntrustedModsWaterCarriedByATrustedModIsAccepted() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround), Water(TrustedMod, AboveTheGround)]).Proposals);

    [Fact]
    public void AnUntrustedModsDefaultCarriedByATrustedModIsAccepted() =>
        Assert.Empty(
            FindWithWater(
                [Flat(Target)],
                [WaterAtTheDefault(Target)],
                worldspace: [Default(BaseGame, BelowTheGround), Default(Other, AboveTheGround), Default(TrustedMod, AboveTheGround)]).Proposals);

    [Fact]
    public void ThePatchCarryingAnUntrustedModsWaterNamesThatMod() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround), Water(Patch, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>Only the patch holds the change: an earlier patcher of the group made it, and no plugin is named.</summary>
    [Fact]
    public void AWaterChangeOnlyThePatchHoldsNamesAnEarlierPatcher() =>
        Assert.Equal(
            [Removal(0, changedBy: null, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Patch, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>
    /// OwnChanges.esp re-saves the other mod's water: one height step above it is within the tolerance, so it owns no water height
    /// there and the other mod is named; twice the other mod's height is its own, which is accepted.
    /// </summary>
    [Theory]
    [InlineData(AboveTheGround + HeightStep, true)]
    [InlineData(2 * AboveTheGround, false)]
    public void AModTrustedForItsOwnChangesOwnsAWaterHeightOnlyBeyondTheTolerance(float resaved, bool removed)
    {
        var result = FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround), Water(OwnChanges, resaved)]);

        ProposedRemoval[] expected = removed ? [Removal(0, Other, TerrainChangeKind.WaterHeight)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// Only untrusted mods have the cell, as in a world one of them adds, so its water has no accepted value: it is not checked, and
    /// nothing about it is reported. The land is still compared, and the object is not one without terrain.
    /// </summary>
    [Fact]
    public void ACellNoTrustedPluginHasIsNotCheckedForWater()
    {
        var result = FindWithWater([Flat(Target)], [Water(Other, BelowTheGround), Water(Later, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Failures);
        Assert.Equal((1, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>The cell uses the default water height, and only an untrusted mod has the worldspace: the default has no accepted value.</summary>
    [Fact]
    public void ACellUsingADefaultNoTrustedPluginHasIsNotCheckedForWater()
    {
        var result = FindWithWater([Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [Default(Other, AboveTheGround)]);

        Assert.Empty(result.Proposals);
        Assert.Empty(result.Failures);
        Assert.Equal((1, 0), (result.CellsCompared, result.ObjectsWithoutTerrain));
    }

    /// <summary>The accepted cell has no water, so its worldspace's default is not needed, though no trusted plugin has the worldspace.</summary>
    [Fact]
    public void ACellWithoutWaterIsCheckedWhateverItsWorldspaceHas() =>
        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.WaterAdded)],
            FindWithWater([Flat(Target)], [Water(Target, null), Water(Later, AboveTheGround)], worldspace: [Default(Other, AboveTheGround)]).Proposals.AsEnumerable());

    [Fact]
    public void AnUntrustedModCarryingTheLastTrustedCellCopysWaterChangesNothing() =>
        Assert.Empty(FindWithWater([Flat(BaseGame)], [Water(BaseGame, BelowTheGround), Water(Master, AboveTheGround), Water(Early, AboveTheGround)]).Proposals);

    [Fact]
    public void AModLoadingBeforeTheTargetDoesNotChangeTheTargetsWater() =>
        Assert.Empty(FindWithWater([Flat(Target)], [Water(Early, AboveTheGround), Water(Target, BelowTheGround)]).Proposals);

    /// <summary>The target's copy of the worldspace and none of the cell, as for a neighbour cell reached by the margin.</summary>
    private static readonly WorldspaceWaterCopy[] TargetWorldspace = [Default(BaseGame, BelowTheGround), Default(Target, BelowTheGround)];

    [Fact]
    public void TheMastersCellCopyIsAcceptedWhenTheTargetHasOnlyAWorldspaceCopy() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Master, BelowTheGround), Water(Other, AboveTheGround)], worldspace: TargetWorldspace).Proposals.AsEnumerable());

    [Fact]
    public void AnUntrustedCellCopyIsNotAcceptedThroughTheTargetsWorldspaceCopy() =>
        Assert.Equal(
            [Removal(0, Early, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [Water(Master, BelowTheGround), Water(Early, AboveTheGround)], worldspace: TargetWorldspace).Proposals.AsEnumerable());

    [Fact]
    public void ALaterModCarryingTheMastersWaterChangesNothing() =>
        Assert.Empty(
            FindWithWater(
                [Flat(Target)], [Water(Master, BelowTheGround), Water(Early, AboveTheGround), Water(Later, BelowTheGround)], worldspace: TargetWorldspace).Proposals);

    [Fact]
    public void AChangedWorldspaceDefaultNamesThePluginOfTheWorldspaceCopy() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [.. TargetWorldspace, Default(Other, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>The water is read in the object's own worldspace, whose land comes from its parent; the copies then hold the parent's defaults.</summary>
    [Fact]
    public void AChangedDefaultOfAWorldspaceSuppliedToAChildNamesThePluginOfTheSupplyingWorldspace() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [.. TargetWorldspace, Default(Other, AboveTheGround)], space: ChildWorldspace)
                .Proposals.AsEnumerable());

    /// <summary>The worldspace stores no default water height, so the target's cell using the default has no water.</summary>
    [Fact]
    public void ACellUsingTheDefaultOfAWorldspaceWithoutOneHasNoWater() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterAdded)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target), Water(Other, AboveTheGround)], worldspace: [Default(BaseGame, level: null)])
                .Proposals.AsEnumerable());

    [Fact]
    public void AWorldspaceVersionAddingTheDefaultAddsWaterToCellsUsingIt() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterAdded)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [Default(BaseGame, level: null), Default(Other, AboveTheGround)])
                .Proposals.AsEnumerable());

    [Fact]
    public void AWorldspaceVersionDroppingTheDefaultRemovesTheWaterOfCellsUsingIt() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterRemoved)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target)], worldspace: [Default(BaseGame, AboveTheGround), Default(Other, level: null)])
                .Proposals.AsEnumerable());

    /// <summary>The other mod switches the cell from the worldspace's default to its own height, or back.</summary>
    [Theory]
    [InlineData(null, AboveTheGround)]
    [InlineData(AboveTheGround, null)]
    public void SwitchingBetweenTheOwnHeightAndTheDefaultIsAWaterHeightChange(float? targetOwnLevel, float? otherOwnLevel) =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater([Flat(Target)], [new CellWaterCopy(Target, HasWater: true, targetOwnLevel), new CellWaterCopy(Other, HasWater: true, otherOwnLevel)])
                .Proposals.AsEnumerable());

    [Fact]
    public void AMissingDefaultDoesNotMatterToACellWithItsOwnHeight() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [Water(Target, BelowTheGround), Water(Other, AboveTheGround)], worldspace: [Default(BaseGame, level: null)])
                .Proposals.AsEnumerable());

    /// <summary>A plugin with two cells at the grid has two copies in load order; the last is the one the game shows.</summary>
    [Theory]
    [InlineData(AboveTheGround, BelowTheGround, false)]
    [InlineData(BelowTheGround, AboveTheGround, true)]
    public void OfTwoCellCopiesInOnePluginTheLastIsShown(float first, float last, bool removed)
    {
        var result = FindWithWater([Flat(Target)], [Water(Target, BelowTheGround), Water(Other, first), Water(Other, last)]);

        Assert.Equal(removed, result.Proposals.Any());
    }

    [Fact]
    public void ACellWithoutTheHasWaterFlagHasNoWaterWhateverHeightItStores() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterAdded)],
            FindWithWater([Flat(Target)], [new CellWaterCopy(Target, HasWater: false, AboveTheGround), Water(Other, AboveTheGround)]).Proposals.AsEnumerable());

    /// <summary>
    /// The other mod gives the cell its own height; the later mod's changed default is not what the shown water is made of, so the
    /// later mod is not named though it loads later.
    /// </summary>
    [Fact]
    public void ADefaultTheShownCellDoesNotUseIsNotNamed() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [WaterAtTheDefault(Target), Water(Other, AboveTheGround)], worldspace: [.. TargetWorldspace, Default(Later, AboveTheGround)])
                .Proposals.AsEnumerable());

    /// <summary>The other mod switches the cell to the default, which the later mod changed: both made the shown water, the later is named.</summary>
    [Fact]
    public void WhenTheCellAndTheDefaultItUsesBothChangedTheLaterLoadingModIsNamed() =>
        Assert.Equal(
            [Removal(0, Later, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)], [Water(Target, BelowTheGround), WaterAtTheDefault(Other)], worldspace: [.. TargetWorldspace, Default(Later, AboveTheGround)])
                .Proposals.AsEnumerable());

    /// <summary>The trusted mod's cell uses the default, which only the untrusted mod's worldspace version changed.</summary>
    [Fact]
    public void ATrustedCellUsingTheDefaultDoesNotMakeAnUntrustedDefaultAccepted() =>
        Assert.Equal(
            [Removal(0, Other, TerrainChangeKind.WaterHeight)],
            FindWithWater(
                [Flat(Target)],
                [WaterAtTheDefault(Target), WaterAtTheDefault(Other), WaterAtTheDefault(TrustedMod)],
                worldspace: [.. TargetWorldspace, Default(Other, AboveTheGround)])
                .Proposals.AsEnumerable());

    /// <summary>Both worldspaces use the same land cell, which is compared once; each has its own cell water.</summary>
    [Fact]
    public void ObjectsInAChildAndItsParentAtOneGridShareTheLandAndKeepTheirOwnWater()
    {
        var plugin = new TerrainPlugin(
            new Dictionary<ExteriorCell, TerrainVersion[]> { [Cell] = [Flat(Target)] },
            water: new Dictionary<ExteriorCell, WaterCopies>
            {
                [Cell] = new([Water(Target, BelowTheGround), Water(Other, BelowTheGround)], BaseGameWorldspace),
                [Cell with { WorldspaceKey = ChildWorldspace }] = new([Water(Target, BelowTheGround), Water(Other, AboveTheGround)], BaseGameWorldspace),
            });

        var result = Find(plugin, Settings(), threads: 1, Placed(0, Rock), Placed(1, Rock, space: ChildWorldspace));

        Assert.Equal([Removal(1, Other, TerrainChangeKind.WaterHeight)], result.Proposals.AsEnumerable());
        Assert.Equal(1, result.CellsCompared);
    }

    /// <summary>A rock 20 units wide and high, a marker without size, and a base whose visibility cannot be read.</summary>
    private sealed class BoxShapes : IBaseObjectShapes
    {
        public BaseShape Of(BaseKey? baseKey) =>
            baseKey == Marker ? new BaseShape(Box.Zero, null, InvisibleObjectKind.XMarkers) : new BaseShape(new Box(new Vector3(-10, -10, 0), new Vector3(10, 10, 20)), null, null);

        public ObjectVisibility VisibilityOf(BaseKey? baseKey, bool isPrimitive, bool hasMapMarker) =>
            baseKey == Broken ? throw new InvalidDataException("broken base")
            : baseKey == Marker ? ObjectVisibility.Invisible(InvisibleObjectKind.XMarkers)
            : ObjectVisibility.Visible;

        public IReadOnlyList<MeasuredBase> Computed() => [];
    }

    /// <summary>
    /// Terrain and water versions by cell, and material chains by texture; only the test worldspace has land data, which its child
    /// worldspace uses.
    /// </summary>
    private sealed class TerrainPlugin(
        IReadOnlyDictionary<ExteriorCell, TerrainVersion[]> terrain,
        IReadOnlyDictionary<RecordKey, RecordKey[]>? materials = null,
        IReadOnlyDictionary<ExteriorCell, WaterCopies>? water = null) : IPluginRecords
    {
        private int _materialReads;
        private int _waterReads;

        public int MaterialReads => _materialReads;

        public int WaterReads => _waterReads;

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => terrain.GetValueOrDefault(cell) ?? [];

        public WaterCopies ReadWaterCopies(ExteriorCell cell)
        {
            Interlocked.Increment(ref _waterReads);
            return water?.GetValueOrDefault(cell) ?? new WaterCopies([], []);
        }

        public RecordKey? FindLandWorldspace(RecordKey space) => space == Worldspace || space == ChildWorldspace ? Worldspace : null;

        public RecordKey[] ReadTextureMaterialChain(RecordKey texture)
        {
            Interlocked.Increment(ref _materialReads);
            return materials?.GetValueOrDefault(texture) ?? [];
        }

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope, Func<PluginName, bool> stopsTheWalk) => throw new NotSupportedException();

        public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public IWritableRecords ReadWritableRecords() => throw new NotSupportedException();

        public IRecordIndex ReadRecordIndex() => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }
}
