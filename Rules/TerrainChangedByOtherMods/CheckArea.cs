using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Rules.TerrainChangedByOtherMods.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;

namespace AddedObjectRemover.Rules.TerrainChangedByOtherMods;

/// <summary>Which objects the settings check, and where the terrain around an object is checked.</summary>
internal sealed class CheckArea(IBaseObjectShapes shapes, ChangedTerrainSettings settings)
{
    /// <summary>Whether the settings check the object: every object, or with only visible objects checked, a visible one.</summary>
    public bool Covers(TargetObject target) =>
        settings.Objects == TerrainCheckedObjects.AllObjectsIncludingInvisibleOnes || shapes.VisibilityOf(target).IsVisible;

    /// <summary>
    /// The areas that are on, size area first. Each is the axis-aligned world box around the object's box, grown on every side by its
    /// size on each axis times the multiplier (as the too-close box zone grows it) or by the distance in game units. An object without
    /// size gives a box around its position.
    /// </summary>
    public ImmutableArray<Box> Around(TargetObject target) =>
        Boxes.GrownWorldAabbs(
            shapes.Of(target.Base).Box,
            target.Transform,
            settings.AreaBySize ? settings.AreaMultiplier : null,
            settings.AreaByDistance ? settings.AreaDistanceInUnits : null);

    /// <summary>The terrain vertices under the box seen from above, cell by cell.</summary>
    public IEnumerable<CellVertices> VerticesUnder(Box area) => TerrainGrid.VerticesUnder(area);

    /// <summary>
    /// Whether an area around an object is checked: every area, or with only visible objects on the ground checked, one whose height
    /// range overlaps the ground at its checked vertices in either of two versions of the ground; a version without heights does not.
    /// </summary>
    /// <param name="heightsOne">The heights at the area's checked vertices in one version of the ground.</param>
    /// <param name="heightsOther">The heights at the same vertices in the other version.</param>
    /// <remarks>An area without a checked vertex overlaps no ground.</remarks>
    public bool ChecksArea(Box area, IEnumerable<float> heightsOne, IEnumerable<float> heightsOther) =>
        settings.Objects != TerrainCheckedObjects.VisibleObjectsOnTheGround || area.HeightRangeOverlaps(heightsOne) || area.HeightRangeOverlaps(heightsOther);
}
