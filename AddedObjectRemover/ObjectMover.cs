using System.Numerics;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover;

/// <summary>Writes overrides that change only a placed record's position (rotation kept). Not thread-safe.</summary>
internal sealed class ObjectMover(PlacedOverrideWriter overrides)
{
    public void MoveTo(IPlacedGetter record, TargetLocation location, Vector3 position)
    {
        var placed = overrides.GetOrAddOverride(record, location);
        placed.Placement!.Position = new P3Float(position.X, position.Y, position.Z);
    }
}
