using System.Numerics;
using AddedObjectRemover.Tests.Fixtures;

namespace AddedObjectRemover.Tests.Rules;

using static ChangedTerrainScenes;

/// <summary>The changed-terrain step's terrain type: textures of one material, the share of changed vertices and the plugin named for a type change.</summary>
public class ChangedTerrainTypeObjectsTests
{
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

        Assert.Equal([(new TargetId(0), (PluginName?)Other, TerrainChangeKind.Height)], result.Hits.Select(hit => (hit.Target, hit.ChangedBy, hit.Kind)));
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

    /// <summary>
    /// The other mod adds the land at the ground; OwnChanges.esp paves all of it, so every vertex has an accepted terrain type and none
    /// an accepted height. The later mod sinks the land deep and paints it back: the shown height, which stands in for the accepted
    /// one, decides whether the object reaches the ground.
    /// </summary>
    [Theory]
    [InlineData(Ground, false)]
    [InlineData(Deep, true)]
    public void AtAVertexWithAnAcceptedTypeButNoAcceptedHeightTheShownHeightDecidesTheReach(float objectHeight, bool removed)
    {
        var plugin = PaintedCell(
            FlatPainted(Other, Painted(Grass)),
            FlatPainted(OwnChanges, Painted(Road)),
            new TerrainVersion(Later, Heights(Deep), Painted(Grass)));

        var result = Find(plugin, Settings(), threads: 1, Placed(0, Rock, Spot with { Z = objectHeight }));

        ProposedRemoval[] expected = removed ? [Removal(0, Later, TerrainChangeKind.TerrainType)] : [];
        Assert.Equal(expected, result.Proposals.AsEnumerable());
    }

    /// <summary>
    /// The other mod adds the land; OwnChanges.esp raises the whole cell, so every vertex has an accepted height, and paves two of the
    /// sixteen vertices of the area, the only ones with an accepted terrain type. The later mod paints them back: the share is two of
    /// two, not two of sixteen.
    /// </summary>
    [Fact]
    public void VerticesWithoutAnAcceptedTerrainTypeAreLeftOutOfTheTypeShare()
    {
        var plugin = PaintedCell(
            FlatPainted(Other, Painted(Grass)),
            new TerrainVersion(OwnChanges, Heights(Raised), RoadAtWideSpot(FewerChanges)),
            new TerrainVersion(Later, Heights(Raised), Painted(Grass)));

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

    /// <summary>The trusted mod paves the ground and an earlier patcher paints the target's grass back: the type is accepted from the target.</summary>
    [Fact]
    public void AGroundTypeThePatchPutsBackToALowerTrustedVersionsIsAccepted() =>
        Assert.Empty(
            Find(PaintedCell(FlatPainted(Target, Painted(Grass)), FlatPainted(TrustedMod, Painted(Road)), FlatPainted(Patch, Painted(Grass))), Settings())
                .Proposals);

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
        var changed = 8 * TerrainGrid.VerticesPerSide + changedColumn;
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
        var mostlyGrass = FlatPainted(Other, new TerrainLayers([.. Enumerable.Range(0, TerrainQuadrants.Count).Select(_ => new QuadrantLayers(
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
}
