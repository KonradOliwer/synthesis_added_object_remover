using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>An NPC template flag: which of its traits an NPC takes over from its template.</summary>
public enum NpcTemplateFlag
{
    Traits,
}

public enum TraitSupplierKind
{
    Npc,
    LeveledList,
}

/// <summary>The record that supplies a placed base's traits: an NPC supplying its own, or a leveled list.</summary>
public readonly record struct TraitSupplier(RecordKey Record, TraitSupplierKind Kind);

/// <param name="Slots">The biped slot bits the addon fills.</param>
/// <param name="Race">Null when the addon names no race.</param>
/// <param name="MaleModelPath">The male world model's path as given; null when the addon has no male model.</param>
/// <param name="FemaleModelPath">The female world model's path as given; null when the addon has no female model.</param>
public sealed record ArmorAddonTraits(
    uint Slots,
    RecordKey? Race,
    ImmutableArray<RecordKey> AdditionalRaces,
    string? MaleModelPath,
    string? FemaleModelPath);

/// <param name="EditorId">Null when the race has none.</param>
/// <param name="ArmorRace">Null when the race has none.</param>
public sealed record RaceTraits(string? EditorId, bool Playable, float MaleHeight, float FemaleHeight, RecordKey? ArmorRace);

/// <summary>An NPC's raw record facts; the rules that turn them into a body are not here.</summary>
/// <param name="Race">Null when the race record is not found.</param>
/// <param name="BodyAddons">
/// The armour addons, in armature order, of the NPC's body armour: its worn armour, else its race's skin. Empty
/// without a found race or armour.
/// </param>
public sealed record NpcTraits(
    bool Female,
    float Height,
    Box ObjectBounds,
    RecordKey RaceKey,
    RaceTraits? Race,
    ImmutableArray<ArmorAddonTraits> BodyAddons);
