using System.Collections.Immutable;
using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain.Contracts;

namespace AddedObjectRemover.Steps.RemoveObjectsOnChangedTerrain;

/// <summary>Where the terrain around an object is checked.</summary>
internal sealed class CheckArea(IBaseObjectShapes shapes, ChangedTerrainSettings settings)
{
    /// <summary>The areas the settings grow around the object, size area first.</summary>
    public ImmutableArray<Box> Around(TargetObject target) => settings.AreasAround(shapes.Of(target.Base).Box, target.Transform);

    /// <summary>The terrain vertices under the box seen from above, cell by cell.</summary>
    public IEnumerable<CellVertices> VerticesUnder(Box area) => TerrainGrid.VerticesUnder(area);
}
