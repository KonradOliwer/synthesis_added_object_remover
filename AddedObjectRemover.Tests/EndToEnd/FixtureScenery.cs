using System.Numerics;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.EndToEnd;

/// <summary>What every scene needs: the plugins, the base objects, and the crate-beside-a-pad building block.</summary>
internal sealed class FixtureScenery(FixtureMods mods, FixtureBases bases)
{
    private const string TargetPrefix = "T_";
    private const string RivalPrefix = "R_";
    private const string PadSuffix = "_Pad";

    public FixtureMods Mods { get; } = mods;

    public FixtureBases Bases { get; } = bases;

    public PlacedObject PlaceTargetCrate(string name, Vector3 position) =>
        Mods.Target.Interior.Place(TargetPrefix + name, Bases.TargetCrate, position);

    /// <summary>A rival pad beside the crate, in the crate's removal zone.</summary>
    public PlacedObject PlacePinningPad(string name, Vector3 cratePosition, Vector3 offset) =>
        Mods.Rival.Interior.Place(RivalPrefix + name + PadSuffix, Bases.RivalPad, cratePosition + offset);

    /// <summary>A target crate that a rival pad makes too close.</summary>
    public PlacedObject PlaceCratePinnedByPad(string name, Vector3 position)
    {
        PlacePinningPad(name, position, FixtureLayout.PinningPadOffset);
        return PlaceTargetCrate(name, position);
    }

    public static EnableParent EnableParentOf(IPlacedGetter parent)
    {
        var enableParent = new EnableParent();
        enableParent.Reference.SetTo(parent.FormKey);
        return enableParent;
    }
}
