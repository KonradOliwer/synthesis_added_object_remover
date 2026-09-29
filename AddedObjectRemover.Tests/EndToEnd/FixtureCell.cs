using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>One plugin's copy of a cell, with helpers that place records into its child lists.</summary>
internal sealed class FixtureCell(FixtureMod owner, Cell cell)
{
    public PlacedObject Place(string editorId, FormKey baseKey, Vector3 position, bool persistent = false)
    {
        var placed = new PlacedObject(owner.NextKey(), FixtureMod.Release)
        {
            EditorID = editorId,
            Placement = CreatePlacement(position),
        };
        placed.Base.SetTo(baseKey);
        Add(placed, persistent);
        return placed;
    }

    public PlacedNpc PlaceNpc(string editorId, FormKey npcKey, Vector3 position)
    {
        var placed = new PlacedNpc(owner.NextKey(), FixtureMod.Release)
        {
            EditorID = editorId,
            Placement = CreatePlacement(position),
        };
        placed.Base.SetTo(npcKey);
        Add(placed, persistent: false);
        return placed;
    }

    /// <summary>Adds this plugin's version of a record another plugin created.</summary>
    public PlacedObject PlaceCopyOf(PlacedObject original)
    {
        var copy = original.DeepCopy();
        Add(copy, persistent: false);
        return copy;
    }

    private void Add(IPlaced placed, bool persistent) => (persistent ? cell.Persistent : cell.Temporary).Add(placed);

    private static Placement CreatePlacement(Vector3 position) => new() { Position = new P3Float(position.X, position.Y, position.Z) };
}
