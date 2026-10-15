using System.Diagnostics;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>Where the terrain around an object is checked.</summary>
internal sealed class CheckArea(IBaseObjectShapes shapes, ChangedTerrainSettings settings)
{
    /// <summary>
    /// The axis-aligned world box around the object's box, grown on every side by the distance in game units, or by its size on
    /// each axis times the multiplier (as the too-close box zone grows it). An object without size gives a box around its position.
    /// </summary>
    public Box Around(TargetObject target)
    {
        var local = shapes.Of(target.Base).Box;
        var (position, rotation, scale) = (target.Transform.Position, target.Transform.Rotation, target.Transform.Scale);
        return settings.DistanceMode switch
        {
            TerrainDistanceMode.GameUnits => Boxes.WorldAabb(local.Scaled(scale), position, rotation).Grown(settings.DistanceInUnits),
            TerrainDistanceMode.ObjectSize => Boxes.WorldAabb(Boxes.ExpandedLocalBox(local, scale, settings.DistanceSizeMultiplier), position, rotation),
            _ => throw new UnreachableException($"Unknown terrain distance mode {settings.DistanceMode}."),
        };
    }

    /// <summary>The terrain vertices under the box seen from above, cell by cell.</summary>
    public IEnumerable<CellVertices> VerticesUnder(Box area) =>
        TerrainGrid.VerticesUnder((new(area.Min.X, area.Min.Y), new(area.Max.X, area.Max.Y)));
}
