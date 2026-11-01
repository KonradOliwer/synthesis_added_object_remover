using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>Where the terrain around an object is checked.</summary>
internal sealed class CheckArea(IBaseObjectShapes shapes, ChangedTerrainSettings settings)
{
    /// <summary>
    /// The areas that are on, size area first. Each is the axis-aligned world box around the object's box, grown on every side
    /// by its size on each axis times the multiplier (as the too-close box zone grows it) or by the distance in game units.
    /// An object without size gives a box around its position.
    /// </summary>
    public ImmutableArray<Box> Around(TargetObject target) =>
        Boxes.GrownWorldAabbs(
            shapes.Of(target.Base).Box,
            target.Transform,
            settings.AreaBySize ? settings.AreaMultiplier : null,
            settings.AreaByDistance ? settings.AreaDistanceInUnits : null);

    /// <summary>The terrain vertices under the box seen from above, cell by cell.</summary>
    public IEnumerable<CellVertices> VerticesUnder(Box area) => TerrainGrid.VerticesUnder(area);
}
