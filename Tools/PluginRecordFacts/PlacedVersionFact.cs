using System.Collections.Immutable;
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

/// <summary>
/// What the target plugin last changed in a record of another plugin it overrides, part by part (<see cref="PartHistory"/>), over the
/// versions from the one just below the target plugin's up to the winner, the patch included.
/// </summary>
/// <param name="Undo">The parts whose earlier value is written back (<see cref="PartOutcome.Undo"/>).</param>
/// <param name="LeftToLaterPlugins">The parts the target plugin changed and a later plugin or the patch changed again, each with the last of them.</param>
/// <param name="KeptForRecordFormat">
/// The parts that would be undone, but whose earlier value holds bytes Mutagen does not read, stored in another record format than
/// the winner's, so they cannot be written back and stay.
/// </param>
public readonly record struct PlacedPartChanges(
    PlacedParts Undo, ImmutableSortedDictionary<PlacedParts, PluginName> LeftToLaterPlugins, PlacedParts KeptForRecordFormat);

/// <summary>What one version of a placed record says about the reference itself, apart from where it stands and its base object.</summary>
/// <param name="ReferenceRadius">The reference's own radius (XRDS) as the version stores it; null when it has none.</param>
/// <param name="PrimitiveBounds">The reference's primitive box, full sizes as the version exposes them; null when it is not a primitive.</param>
/// <param name="CollisionLayer">The layer number of the reference's own collision layer (XTRI); null when it stores none.</param>
/// <param name="IsCullingVolume">The reference carries room, portal or multibound data (XRMR/XLRM, XPOD, XMBP): it only culls, it has no collision.</param>
public readonly record struct ReferenceFacts(
    bool IsTeleportDoor, bool IsPrimitive, bool HasMapMarker, float? ReferenceRadius, Vector3? PrimitiveBounds, uint? CollisionLayer, bool IsCullingVolume);
