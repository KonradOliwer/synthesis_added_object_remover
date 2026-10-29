using System.Numerics;

namespace AddedObjectRemover;

/// <summary>One plugin's version of a placed record: where it stands, what it shows and whether the game has it. It is not judged here.</summary>
/// <param name="Plugin">The plugin that holds this version.</param>
/// <param name="Scale">As the version stores it; null when it has none.</param>
/// <param name="Base">The version's base object; null when its base link is empty.</param>
/// <param name="Cell">The cell this plugin puts the record in.</param>
/// <param name="SpaceKey">The worldspace or interior that cell belongs to.</param>
/// <param name="IsPersistent">The version's own Persistent flag is set.</param>
/// <param name="Reference">What the version says about the reference itself.</param>
public readonly record struct PlacedVersionFact(
    PluginName Plugin, PlacementFacts Placement, float? Scale, BaseKey? Base, CellFact Cell, RecordKey SpaceKey, bool IsPersistent, ReferenceFacts Reference);

/// <summary>What one version of a placed record says about the reference itself, apart from where it stands and its base object.</summary>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the version stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the version exposes them; null when it is not a primitive.</param>
public readonly record struct ReferenceFacts(bool IsTeleportDoor, bool IsPrimitive, bool HasMapMarker, float? ReferenceRadius, Vector3? PrimitiveBounds);
