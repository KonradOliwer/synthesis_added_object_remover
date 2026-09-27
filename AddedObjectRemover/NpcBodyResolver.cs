using System.Numerics;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Works out an NPC's body from its records, first match wins and nothing is guessed:
/// 1. the meshes of its body armour (worn armour, else the race's skin) whose armour addons are
///    for its race, for its sex;
/// 2. its Object Bounds;
/// 3. for a playable (humanoid) race only, <see cref="HumanoidBox"/>;
/// 4. otherwise the NPC is sized as a point at its placement position (<see cref="NpcBody.Point"/>).
/// Each is scaled by the race height for its sex × the NPC height. Race, sex, height and worn
/// armour come from the template chain while the Traits template flag is set.
/// </summary>
internal sealed class NpcBodyResolver(ILinkCache linkCache, BaseObjectShapeProvider shapes)
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

    private readonly record struct BodyMesh(string Path, Box Box);

    public static bool IsFemale(INpcGetter npc) => npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);

    /// <summary>
    /// The NPCs that supply the traits of a placed base: one for a plain NPC, one per possible leaf
    /// for a leveled list (nested lists included). Each record is visited once, so loops end.
    /// </summary>
    public List<INpcGetter> CollectTraitSources(FormKey spawn)
    {
        var sources = new List<INpcGetter>();
        CollectTraitSources(spawn, [], sources);
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
        var meshes = ReadBodyMeshes(npc, race, female);
        if (meshes.Count > 0)
        {
            return NpcBody.FromMeshes(meshes.Select(mesh => mesh.Path).ToList(), UnionOfBoxes(meshes), heightScale);
        }
        var bounds = BaseObjectShapeProvider.ToBox(npc.ObjectBounds);
        if (HasVolume(bounds)) return NpcBody.FromBox(NpcSizeSource.ObjectBounds, bounds, heightScale);
        if (race.Flags.HasFlag(Race.Flag.Playable)) return NpcBody.FromBox(NpcSizeSource.HumanoidApproximation, HumanoidBox, heightScale);
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

    /// <summary>The readable world models, for the sex, of the body armour's addons made for the race; each mesh once.</summary>
    private List<BodyMesh> ReadBodyMeshes(INpcGetter npc, IRaceGetter race, bool female)
    {
        var armorKey = npc.WornArmor.IsNull ? race.Skin.FormKey : npc.WornArmor.FormKey;
        if (!linkCache.TryResolve<IArmorGetter>(armorKey, out var armor)) return [];

        var meshes = new List<BodyMesh>();
        foreach (var addonLink in armor.Armature)
        {
            if (!linkCache.TryResolve<IArmorAddonGetter>(addonLink.FormKey, out var addon) || !IsMadeForRace(addon, race.FormKey)) continue;
            if (GetWorldModelPath(addon, female) is not { } meshPath || shapes.GetMeshBox(meshPath) is not { } box) continue;
            meshes.Add(new BodyMesh(meshPath, box));
        }
        return meshes.DistinctBy(mesh => mesh.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsMadeForRace(IArmorAddonGetter addon, FormKey race) =>
        addon.Race.FormKey == race || addon.AdditionalRaces.Any(additional => additional.FormKey == race);

    private static string? GetWorldModelPath(IArmorAddonGetter addon, bool female)
    {
        var model = female ? addon.WorldModel?.Female : addon.WorldModel?.Male;
        return model is { } found && !string.IsNullOrWhiteSpace(found.File.GivenPath)
            ? MeshFileSource.NormalizeMeshPath(found.File.GivenPath)
            : null;
    }

    private static Box UnionOfBoxes(IReadOnlyList<BodyMesh> meshes) =>
        meshes.Skip(1).Aggregate(meshes[0].Box, (union, mesh) => union.Union(mesh.Box));

    private static bool HasVolume(Box box) => box.Size is { X: > 0, Y: > 0, Z: > 0 };
}
