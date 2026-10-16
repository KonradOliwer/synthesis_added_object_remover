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
    public ImmutableArray<Box> Around(TargetObject target)
    {
        var local = shapes.Of(target.Base).Box;
        var (position, rotation, scale) = (target.Transform.Position, target.Transform.Rotation, target.Transform.Scale);
        var areas = ImmutableArray.CreateBuilder<Box>();
        if (settings.AreaBySize) areas.Add(Boxes.WorldAabb(Boxes.ExpandedLocalBox(local, scale, settings.AreaMultiplier), position, rotation));
        if (settings.AreaByDistance) areas.Add(Boxes.WorldAabb(local.Scaled(scale), position, rotation).Grown(settings.AreaDistanceInUnits));
        return areas.ToImmutable();
    }

    /// <summary>The terrain vertices under the box seen from above, cell by cell.</summary>
    public IEnumerable<CellVertices> VerticesUnder(Box area) =>
        TerrainGrid.VerticesUnder((new(area.Min.X, area.Min.Y), new(area.Max.X, area.Max.Y)));
}
