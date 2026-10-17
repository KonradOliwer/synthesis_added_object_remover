using System.Collections.Immutable;
using System.Numerics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

/// <summary>
/// The changed-terrain step: the reference terrain per cell, which plugins count as changing it and which one is named, the check
/// area, the height rule, which objects are checked, the per-object error boundary and the independence from the thread count.
/// </summary>
public class ChangedTerrainObjectsTests
{
    private const float Ground = 0f;
    private const float Raised = 64f;

    /// <summary>The height tolerance of <see cref="Settings"/>, and the smallest height step terrain heights come in.</summary>
    private const float Tolerance = 16f;
    private const float HeightStep = 8f;
    private const int VertexCount = TerrainHeights.VerticesPerSide * TerrainHeights.VerticesPerSide;

    /// <summary>A terrain vertex inside the check area of an object at <see cref="Spot"/>, and one far outside it.</summary>
    private const int Near = 8 * TerrainHeights.VerticesPerSide + 8;
    private const int Far = 20 * TerrainHeights.VerticesPerSide + 20;

    private static readonly Vector3 Spot = new(1000, 1000, 0);
    private static readonly RecordKey Worldspace = TestTargets.Space;
    private static readonly RecordKey Interior = TestTargets.SpaceKey(0x101);
    private static readonly RecordKey ChildWorldspace = TestTargets.SpaceKey(0x102);
    private static readonly ExteriorCell Cell = new(Worldspace, 0, 0);

    private static readonly PluginName BaseGame = new("Skyrim.esm");
    private static readonly PluginName Master = new("Master.esm");
    private static readonly PluginName Early = new("Early.esp");
    private static readonly PluginName Target = TestTargets.TargetPlugin;
    private static readonly PluginName Dependant = new("Dependant.esp");
    private static readonly PluginName Other = new("Other.esp");
    private static readonly PluginName CompatibilityPatch = new("CompatibilityPatch.esp");
    private static readonly PluginName Ignored = new("Ignored.esp");
    private static readonly PluginName Later = new("Later.esp");
    private static readonly PluginName Patch = new("Synthesis.esp");

    private static readonly BaseKey Rock = new(new RecordKey(Target, 0x900), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey Marker = new(new RecordKey(Target, 0x901), BaseLinkKind.PlaceableObject);
    private static readonly BaseKey Broken = new(new RecordKey(Target, 0x902), BaseLinkKind.PlaceableObject);

    /// <summary>The target's masters are the base game and a master that is not ignored; two plugins require the target.</summary>
    private static readonly IdentifiedMods Mods = new(
        Target, new HashSet<PluginName> { BaseGame, Target, Ignored, Patch }, [BaseGame, Master], [], DetectedPatches.Off, [Dependant, CompatibilityPatch]);

    private static readonly PluginName[] Plugins = [BaseGame, Master, Early, Target, Dependant, Other, CompatibilityPatch, Ignored, Later, Patch];

    private static readonly LoadOrderPlugins LoadOrder = new(
        new KnownPluginNames(Plugins), Patch, [.. Plugins.Select(plugin => new PluginListing(plugin, true, []))]);

    private static ChangedTerrainSettings Settings(
        TerrainCheckedObjects objects = TerrainCheckedObjects.VisibleObjectsOnTheGround,
        float tolerance = Tolerance,
        bool bySize = false,
        float multiplier = 0.5f,
        bool byDistance = true,
        bool heights = true,
        bool types = true) =>
        new(On: true, objects, heights, tolerance, types, bySize, multiplier, byDistance, AreaDistanceInUnits: 64f);

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

    private static ProposedRemoval Removal(int index, PluginName changedBy, TerrainChangeKind kind = TerrainChangeKind.Height) =>
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
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Dependant, Near)).Proposals);

    [Fact]
    public void APluginRequiringTheTargetsChangeCopiedByACountingModCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Dependant, Near), RaisedAt(Later, Near)).Proposals);

    [Fact]
    public void AnIgnoredModsChangeCopiedByACountingModCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Ignored, Near), RaisedAt(Later, Near)).Proposals);

    [Fact]
    public void ACountingModsChangeCarriedByACompatibilityPatchNamesTheCountingMod() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(CompatibilityPatch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void ACountingModCopyingAnotherCountingModsChangeLeavesItNamedOnTheFirst() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Later, Near)).Proposals.AsEnumerable());

    [Fact]
    public void ASmallIgnoredChangeOnTopOfACountingChangeNamesTheCountingMod() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Ignored, Near, Raised + HeightStep)).Proposals.AsEnumerable());

    [Fact]
    public void AnIgnoredModsOwnLargerChangeOnTopOfACountingChangeCountsForNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Ignored, Near, 2 * Raised)).Proposals);

    /// <summary>Each step is within the tolerance of the next, and only the ignored mod's own height is beyond it.</summary>
    [Fact]
    public void AnIgnoredModsStepPushingSmallChangesBeyondTheToleranceCountsForNothing() =>
        Assert.Empty(
            FindInCell(Flat(Target), RaisedAt(Other, Near, HeightStep), RaisedAt(CompatibilityPatch, Near, Tolerance), RaisedAt(Ignored, Near, Tolerance + HeightStep))
                .Proposals);

    /// <summary>Each step is within the tolerance of the next, and the later counting mod's own height is beyond it.</summary>
    [Fact]
    public void CountingModsSmallStepsAddingUpBeyondTheToleranceNameTheModWhoseOwnChangeExceedsIt() =>
        Assert.Equal(
            [Removal(0, Later)],
            FindInCell(Flat(Target), RaisedAt(Other, Near, HeightStep), RaisedAt(Later, Near, Tolerance + HeightStep)).Proposals.AsEnumerable());

    [Fact]
    public void APatchTakingAnEarlierChangeBackInNamesTheModThatMadeIt() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Later), RaisedAt(Patch, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AModRestoringTheTargetsTerrainOverAPluginRequiringTheTargetLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Dependant, Near), Flat(Other)).Proposals);

    [Fact]
    public void ALaterModRestoringTheReferenceLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Later)).Proposals);

    [Fact]
    public void AnIgnoredModRestoringTheReferenceLeavesNothingChanged() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Other, Near), Flat(Ignored)).Proposals);

    [Fact]
    public void AModLoadingBeforeTheReferenceIsOverriddenByIt() =>
        Assert.Empty(FindInCell(RaisedAt(Early, Near), Flat(Target)).Proposals);

    [Fact]
    public void AnIgnoredModCarryingAnotherModsChangeNamesTheOtherMod() =>
        Assert.Equal([Removal(0, Other)], FindInCell(Flat(Target), RaisedAt(Other, Near), RaisedAt(Ignored, Near)).Proposals.AsEnumerable());

    [Fact]
    public void AChangeOnlyByModsThatNeverCountRemovesNothing() =>
        Assert.Empty(FindInCell(Flat(Target), RaisedAt(Ignored, Near), RaisedAt(Patch, Near)).Proposals);

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
    public void TheBaseGameAsATargetMasterIsTheReferenceWhenTheTargetHasNoTerrainThere() =>
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
    public void ACellWithoutReferenceTerrainIsNotChecked()
    {
        var result = FindInCell(RaisedAt(Other, Near));

        Assert.Empty(result.Proposals);
        Assert.Equal((0, 1), (result.CellsCompared, result.ObjectsWithoutTerrain));
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
            cellX => new[] { Flat(Target), RaisedAt(Other, Near), cellX % 2 == 0 ? RaisedAt(Later, Near, 2 * Raised) : RaisedAt(Ignored, Near) });
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
            [Removal(0, Other, TerrainChangeKind.HeightAndTerrainType)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), changed), Settings()).Proposals.AsEnumerable());
    }

    [Theory]
    [InlineData(true, TerrainChangeKind.HeightAndTerrainType)]
    [InlineData(false, TerrainChangeKind.TerrainType)]
    public void ADeletedLandRecordShowsTheDefaultTerrain(bool heights, TerrainChangeKind kind) =>
        Assert.Equal(
            [Removal(0, Other, kind)],
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), new TerrainVersion(Other, Heights: null, Layers: null)), Settings(heights: heights)).Proposals.AsEnumerable());

    [Fact]
    public void TheKindIsHowTheShownTerrainDiffersEvenWhenTheNamedModChangedLess()
    {
        var heightOnly = new TerrainVersion(Other, Heights(Ground, (Near, Raised)), Painted(Grass));
        var shownByIgnoredMod = new TerrainVersion(Ignored, Heights(Ground, (Near, Raised)), Painted(Road));

        var result = Find(PaintedCell(FlatPainted(Target, Painted(Grass)), heightOnly, shownByIgnoredMod), Settings());

        Assert.Equal([new ObjectOnChangedTerrain(new TargetId(0), Other, TerrainChangeKind.HeightAndTerrainType)], result.Hits.AsEnumerable());
        Assert.Equal([Removal(0, Other, TerrainChangeKind.HeightAndTerrainType)], result.Proposals.AsEnumerable());
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

    /// <summary>Terrain versions by cell, and material chains by texture; only the test worldspace has land data, which its child worldspace uses.</summary>
    private sealed class TerrainPlugin(
        IReadOnlyDictionary<ExteriorCell, TerrainVersion[]> terrain, IReadOnlyDictionary<RecordKey, RecordKey[]>? materials = null) : IPluginRecords
    {
        private int _materialReads;

        public int MaterialReads => _materialReads;

        public TerrainVersion[] ReadTerrainVersions(ExteriorCell cell) => terrain.GetValueOrDefault(cell) ?? [];

        public RecordKey? FindLandWorldspace(RecordKey space) => space == Worldspace || space == ChildWorldspace ? Worldspace : null;

        public RecordKey[] ReadTextureMaterialChain(RecordKey texture)
        {
            Interlocked.Increment(ref _materialReads);
            return materials?.GetValueOrDefault(texture) ?? [];
        }

        public PlacedRecordFacts ReadPlacedRecords(PlacedReadScope scope) => throw new NotSupportedException();

        public ImmutableArray<LinkFact> ReadLinks(PluginName target, IReadOnlySet<RecordKey> targets) => throw new NotSupportedException();

        public bool HasEnableParent(RecordKey record) => throw new NotSupportedException();

        public void Write(IReadOnlyList<WriteOrder> orders) => throw new NotSupportedException();

        public CellGridPoint? ReadHomeCellGrid(RecordKey record) => throw new NotSupportedException();

        public float[]? ReadTerrain(ExteriorCell cell) => throw new NotSupportedException();

        public MeshTriangle[]? ReadNavmeshes(NavmeshBucket bucket) => throw new NotSupportedException();

        public NpcTraits? NpcTraitsOf(BaseKey npc) => throw new NotSupportedException();

        public TraitSupplier? TraitSupplierOf(BaseKey spawn, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public IReadOnlyList<RecordKey> LeveledEntriesOf(RecordKey list, NpcTemplateFlag templateFlag) => throw new NotSupportedException();

        public bool TryParsePluginName(string text, out PluginName name) => throw new NotSupportedException();
    }
}
