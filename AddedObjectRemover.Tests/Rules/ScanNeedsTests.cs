namespace AddedObjectRemover.Tests.Rules;

/// <summary>What each step's settings state they need the scan to read, and the union the run reads.</summary>
public sealed class ScanNeedsTests
{
    private static readonly AreaRecordReads AreaReads = new(new AreaPartTolerances(8f, 4f), Cells: true, Lands: false);

    [Fact]
    public void TheUnionOfNoStepsReadsNothing() =>
        Assert.Equal(new ScanNeeds(), ScanNeeds.UnionOf());

    [Fact]
    public void AStepThatIsOffGivesNullAndAddsNothing() =>
        Assert.Equal(new ScanNeeds(Navmesh: true), ScanNeeds.UnionOf(null, new ScanNeeds(Navmesh: true), null));

    [Fact]
    public void TheUnionReadsWhatAnyStepNeeds()
    {
        var union = ScanNeeds.UnionOf(
            new ScanNeeds(Navmesh: true, TerrainVersions: true),
            new ScanNeeds(OtherModDetails: true, TerrainLayers: true, Water: true),
            new ScanNeeds(TerrainForChangedObjectsOnly: true, JudgeOldPlaces: true, DecideEveryObjectWithoutCleanedMod: true, AreaRecordVersions: AreaReads));

        Assert.Equal(
            new ScanNeeds(true, true, true, true, true, TerrainForChangedObjectsOnly: true, JudgeOldPlaces: true, DecideEveryObjectWithoutCleanedMod: true, AreaRecordVersions: AreaReads),
            union);
    }

    [Fact]
    public void TwoStepsAskingForTheAreaRecordReadsIsABug() =>
        Assert.Throws<InvalidOperationException>(() => ScanNeeds.UnionOf(new ScanNeeds(AreaRecordVersions: AreaReads), new ScanNeeds(AreaRecordVersions: AreaReads)));

    [Fact]
    public void TheWhatToCollectTakesTheUnionAndTheChangedObjectsValues()
    {
        var needs = new ScanNeeds(Navmesh: true, JudgeOldPlaces: true, AreaRecordVersions: AreaReads);

        Assert.Equal(
            new WhatToCollect(true, false, false, false, false, 7f, ProcessReplaced: true, JudgeOldPlaces: true, TerrainForChangedObjectsOnly: false, AreaRecordVersions: AreaReads),
            WhatToCollect.Of(needs, 7f, processReplaced: true));
    }

    [Fact]
    public void AMarkerMoveReadsTheNavmeshes() =>
        Assert.Equal(new ScanNeeds(Navmesh: true), new MarkerMoveSettings(10f).Needs);

    [Fact]
    public void TheReportsReadTheOtherModDetails() =>
        Assert.Equal(new ScanNeeds(OtherModDetails: true), new ReportFileSettings("/out").Needs);

    [Theory]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, false, true, true, false, true)]
    [InlineData(false, true, true, false, false, false)]
    public void ChangedTerrainReadsTheTerrainOnlyWhenOnAndTheLayersAndWaterOnlyWhenCounted(bool on, bool types, bool water, bool versions, bool layers, bool waterRead) =>
        Assert.Equal(new ScanNeeds(TerrainVersions: versions, TerrainLayers: layers, Water: waterRead), Terrain(on, types, water).Needs);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public void TheTerrainIsReadForTheChangedObjectsOnlyWhenOnlyTheirTerrainCheckIsOn(bool cleanedMod, bool changedObjects, bool changedObjectsOnly)
    {
        var needs = ChangedTerrainSettings.NeedsOfBoth(Terrain(cleanedMod, types: false, water: false), Terrain(changedObjects, types: false, water: false));

        Assert.Equal(new ScanNeeds(TerrainVersions: cleanedMod || changedObjects, TerrainForChangedObjectsOnly: changedObjectsOnly), needs);
    }

    [Theory]
    [InlineData(RestoreMode.RestoreIfNoCollision, RestoreMode.Remove, false, true)]
    [InlineData(RestoreMode.RestoreIfNoCollision, RestoreMode.RestoreIfNoCollision, false, true)]
    [InlineData(RestoreMode.Remove, RestoreMode.RestoreIfNoCollision, false, false)]
    [InlineData(RestoreMode.Remove, RestoreMode.RestoreIfNoCollision, true, true)]
    [InlineData(RestoreMode.Restore, RestoreMode.Restore, true, false)]
    [InlineData(RestoreMode.Remove, RestoreMode.Remove, true, false)]
    public void ChangedObjectsJudgeOldPlacesOnlyWhenAMovedObjectOrAProcessedReplacedObjectChecksThem(RestoreMode moved, RestoreMode replaced, bool processed, bool judged) =>
        Assert.Equal(new ScanNeeds(JudgeOldPlaces: judged), new ChangedObjectSettings(moved, replaced).NeedsWhenReplacedObjectsAre(processed));

    [Theory]
    [InlineData(true, false, false, false, true, true, true)]
    [InlineData(false, true, false, false, false, true, false)]
    [InlineData(false, false, true, false, true, false, false)]
    [InlineData(false, false, false, true, true, true, false)]
    public void TheRestoreReadsTheNavmeshesAndTheAreaRecordsOfTheKindsItsGuardsLookAt(
        bool otherModsObjects, bool terrain, bool cells, bool navmeshes, bool cellsRead, bool landsRead, bool judgesOldPlaces)
    {
        var needs = Restore(otherModsObjects, terrain, cells, navmeshes).Needs;

        Assert.Equal(
            new ScanNeeds(
                Navmesh: true,
                JudgeOldPlaces: judgesOldPlaces,
                DecideEveryObjectWithoutCleanedMod: judgesOldPlaces,
                AreaRecordVersions: new AreaRecordReads(new AreaPartTolerances(8f, 4f), cellsRead || otherModsObjects, landsRead || otherModsObjects)),
            needs);
    }

    private static ChangedTerrainSettings Terrain(bool on, bool types, bool water) =>
        new(
            on, TerrainCheckedObjects.VisibleObjectsOnTheGround, CountHeightChanges: false, 8f, CountTerrainTypeChanges: types, 0,
            CountWaterHeightChanges: water, 0f, CountWaterAddedOrRemoved: false, 0, AreaBySize: false, 0f, AreaByDistance: false, 0f);

    private static WhereRemovedRestoreSettings Restore(bool otherModsObjects, bool terrain, bool cells, bool navmeshes) =>
        new(
            otherModsObjects,
            terrain,
            cells,
            navmeshes,
            new ClearedArea(1000f, 0.1, ClearedAreaMeasure.ObjectSize),
            TerrainEdgeBlendDistance: 0f,
            HeightChangeTolerance: 8f,
            WaterHeightChangeTolerance: 4f,
            new TooCloseSettings(BySize: false, Multiplier: 0f, ByDistance: true, DistanceInUnits: 10f, ZoneShape.BoundingBox, NpcHandling.Never),
            Terrain(on: false, types: false, water: false));
}
