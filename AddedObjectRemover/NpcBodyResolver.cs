using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Works out an NPC's body from its records, first match wins and nothing is guessed:
/// 1. the meshes of its body armour (worn armour, else the race's skin) whose armour addons are
///    for its race, or for the race's Armor Race in the slots no addon of the race itself fills,
///    for its sex (the male model when the female one is missing), sized by their combined
///    bounds; for a playable race at least <see cref="HumanoidHeight"/> tall, because the head is
///    not part of the body armour;
/// 2. its Object Bounds;
/// 3. for a playable (humanoid) race only, <see cref="HumanoidBox"/>;
/// 4. otherwise the NPC is sized as a point at its placement position (<see cref="NpcBody.Point"/>).
/// Each is scaled by the race height for its sex × the NPC height. Race, sex, height and worn
/// armour come from the template chain while the Traits template flag is set.
/// </summary>
internal sealed class NpcBodyResolver(ILinkCache linkCache, BaseObjectShapeProvider shapes, SkinnedBodyMeasurer bodyMeasurer)
{
    /// <summary>
    /// Humanoid approximation: 64 × 64 units across and 128 tall, standing on the placement point,
    /// about a vanilla human at height 1.
    /// </summary>
    private const float HumanoidWidth = 64f;
    private const float HumanoidHeight = 128f;
    private const float HalfHumanoidWidth = HumanoidWidth / 2f;

    private static readonly Box HumanoidBox = new(
        new Vector3(-HalfHumanoidWidth, -HalfHumanoidWidth, 0f),
        new Vector3(HalfHumanoidWidth, HalfHumanoidWidth, HumanoidHeight));

    private readonly record struct Addon(IArmorAddonGetter Record, BipedObjectFlag Slots);

    public static bool IsFemale(INpcGetter npc) => npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);

    /// <summary>
    /// The record that supplies a placed base's traits, found along the Traits template chain: an
    /// NPC supplying its own traits, a leveled list, or null when the chain breaks or loops.
    /// </summary>
    public IMajorRecordGetter? FindTraitSupplier(FormKey spawn)
    {
        var visited = new HashSet<FormKey>();
        while (visited.Add(spawn))
        {
            if (!linkCache.TryResolve<INpcGetter>(spawn, out var npc))
            {
                return linkCache.TryResolve<ILeveledNpcGetter>(spawn, out var list) ? list : null;
            }
            if (!InheritsTraits(npc)) return npc;
            spawn = npc.Template.FormKey;
        }
        return null;
    }

    /// <summary>
    /// The NPCs supplying their own traits that a leveled list can spawn, through nested lists and
    /// Traits templates. Each record is visited once, so loops end.
    /// </summary>
    public List<INpcGetter> CollectTraitSources(ILeveledNpcGetter list)
    {
        var sources = new List<INpcGetter>();
        CollectTraitSources(list.FormKey, [], sources);
        return sources;
    }

    /// <summary>The body of an NPC that supplies its own traits.</summary>
    public NpcBody ResolveOwnBody(INpcGetter npc, bool female)
    {
        if (!linkCache.TryResolve<IRaceGetter>(npc.Race.FormKey, out var race))
        {
            return NpcBody.Point($"race {npc.Race.FormKey} not found");
        }

        var heightScale = GetHeightScale(race, npc, female);
        var playable = race.Flags.HasFlag(Race.Flag.Playable);
        if (MeasureBodyMeshes(npc, race, female) is { } measured)
        {
            return NpcBody.FromBox(NpcSizeSource.BodyMesh, playable ? WithHumanoidHeight(measured) : measured, heightScale);
        }
        var bounds = BaseObjectShapeProvider.ToBox(npc.ObjectBounds);
        if (HasVolume(bounds)) return NpcBody.FromBox(NpcSizeSource.ObjectBounds, bounds, heightScale);
        if (playable) return NpcBody.FromBox(NpcSizeSource.HumanoidApproximation, HumanoidBox, heightScale);
        return NpcBody.Point($"no body mesh for race {RecordNames.Describe(race)}, no Object Bounds, race not playable");
    }

    private void CollectTraitSources(FormKey spawn, HashSet<FormKey> visited, List<INpcGetter> sources)
    {
        if (!visited.Add(spawn)) return;
        if (linkCache.TryResolve<INpcGetter>(spawn, out var npc))
        {
            if (InheritsTraits(npc)) CollectTraitSources(npc.Template.FormKey, visited, sources);
            else sources.Add(npc);
            return;
        }
        if (!linkCache.TryResolve<ILeveledNpcGetter>(spawn, out var list)) return;
        foreach (var entry in list.Entries ?? [])
        {
            if (entry.Data is { Reference.IsNull: false } data) CollectTraitSources(data.Reference.FormKey, visited, sources);
        }
    }

    /// <summary>The Traits flag has no effect without a template.</summary>
    private static bool InheritsTraits(INpcGetter npc) =>
        npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.Traits) && !npc.Template.IsNull;

    private static float GetHeightScale(IRaceGetter race, INpcGetter npc, bool female) =>
        Geometry.NormalizeScale((female ? race.Height.Female : race.Height.Male) * npc.Height);

    private Box? MeasureBodyMeshes(INpcGetter npc, IRaceGetter race, bool female)
    {
        var meshPaths = ReadBodyMeshPaths(npc, race, female);
        return meshPaths.Count > 0 ? bodyMeasurer.Measure(meshPaths) : null;
    }

    /// <summary>The readable world models, for the sex, of the body armour's addons worn by the race; each mesh once.</summary>
    private List<string> ReadBodyMeshPaths(INpcGetter npc, IRaceGetter race, bool female)
    {
        var armorKey = npc.WornArmor.IsNull ? race.Skin.FormKey : npc.WornArmor.FormKey;
        if (!linkCache.TryResolve<IArmorGetter>(armorKey, out var armor)) return [];

        return SelectAddonsWornBy(ResolveAddons(armor), race)
            .Select(addon => GetWorldModelPath(addon, female))
            .OfType<string>()
            .Where(meshPath => shapes.GetMeshBox(meshPath) != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<Addon> ResolveAddons(IArmorGetter armor)
    {
        var addons = new List<Addon>();
        foreach (var addonLink in armor.Armature)
        {
            if (!linkCache.TryResolve<IArmorAddonGetter>(addonLink.FormKey, out var addon)) continue;
            addons.Add(new Addon(addon, addon.BodyTemplate?.FirstPersonFlags ?? default));
        }
        return addons;
    }

    /// <summary>The addons made for the race, plus those made for its Armor Race that fill none of their slots; in armature order.</summary>
    private static IEnumerable<IArmorAddonGetter> SelectAddonsWornBy(IReadOnlyList<Addon> addons, IRaceGetter race)
    {
        var ownSlots = addons
            .Where(addon => IsMadeForRace(addon.Record, race.FormKey))
            .Aggregate(default(BipedObjectFlag), (slots, addon) => slots | addon.Slots);
        var armorRace = race.ArmorRace.FormKeyNullable;
        return addons
            .Where(addon => IsMadeForRace(addon.Record, race.FormKey)
                || armorRace is { } fallback && IsMadeForRace(addon.Record, fallback) && (addon.Slots & ownSlots) == 0)
            .Select(addon => addon.Record);
    }

    private static bool IsMadeForRace(IArmorAddonGetter addon, FormKey race) =>
        addon.Race.FormKey == race || addon.AdditionalRaces.Any(additional => additional.FormKey == race);

    private static string? GetWorldModelPath(IArmorAddonGetter addon, bool female)
    {
        var model = (female ? addon.WorldModel?.Female : null) ?? addon.WorldModel?.Male;
        return model is { } found && !string.IsNullOrWhiteSpace(found.File.GivenPath)
            ? MeshFileSource.NormalizeMeshPath(found.File.GivenPath)
            : null;
    }

    private static Box WithHumanoidHeight(Box body) =>
        body with { Max = body.Max with { Z = MathF.Max(body.Max.Z, body.Min.Z + HumanoidHeight) } };

    private static bool HasVolume(Box box) => box.Size is { X: > 0, Y: > 0, Z: > 0 };
}
