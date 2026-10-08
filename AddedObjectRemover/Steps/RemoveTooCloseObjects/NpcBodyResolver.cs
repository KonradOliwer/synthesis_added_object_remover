using System.Numerics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Steps.RemoveTooCloseObjects;

/// <summary>
/// Works out an NPC's body from its traits, first match wins and nothing is guessed:
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
internal sealed class NpcBodyResolver(IMeshFiles meshFiles, IBodyMeshBounds bodyBounds)
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

    /// <summary>Bodies with the same box are tested once; the first of them is kept.</summary>
    public static NpcBodySet DistinctBodies(IReadOnlyList<NpcBody> possibleBodies)
    {
        var distinct = KeyedGroups.DistinctBy(possibleBodies, body => body.LocalBox, EqualityComparer<Box>.Default);
        return new NpcBodySet(distinct, Box.UnionAll(distinct.Select(body => body.LocalBox)));
    }

    /// <summary>The body of an NPC that supplies its own traits.</summary>
    public NpcBody ResolveOwnBody(NpcTraits npc)
    {
        if (npc.Race is not { } race)
        {
            return NpcBody.Point(new PointReason(PointReasonKind.RaceNotFound, npc.RaceKey, null));
        }

        var heightScale = GetHeightScale(race, npc);
        if (MeasureBodyMeshes(npc) is { } measured)
        {
            return NpcBody.FromBox(NpcSizeSource.BodyMesh, race.Playable ? WithHumanoidHeight(measured) : measured, heightScale);
        }
        if (HasVolume(npc.ObjectBounds)) return NpcBody.FromBox(NpcSizeSource.ObjectBounds, npc.ObjectBounds, heightScale);
        if (race.Playable) return NpcBody.FromBox(NpcSizeSource.HumanoidApproximation, HumanoidBox, heightScale);
        return NpcBody.Point(new PointReason(PointReasonKind.NoBodyMeshNoBoundsNotPlayable, npc.RaceKey, race.EditorId));
    }

    private static float GetHeightScale(RaceTraits race, NpcTraits npc) =>
        ReferenceScale.Normalize((npc.Female ? race.FemaleHeight : race.MaleHeight) * npc.Height);

    private Box? MeasureBodyMeshes(NpcTraits npc)
    {
        var meshPaths = ReadBodyMeshPaths(npc);
        return meshPaths.Count > 0 ? bodyBounds.Of(meshPaths) : null;
    }

    /// <summary>The readable world models, for the sex, of the body armour's addons worn by the race; each mesh once.</summary>
    private List<string> ReadBodyMeshPaths(NpcTraits npc) =>
        SelectAddonsWornBy(npc)
            .Select(addon => GetWorldModelPath(addon, npc.Female))
            .OfType<string>()
            .Where(meshPath => meshFiles.Bounds.Of(meshPath).Box != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The addons made for the race, plus those made for its Armor Race that fill none of their slots; in armature order.</summary>
    private static IEnumerable<ArmorAddonTraits> SelectAddonsWornBy(NpcTraits npc)
    {
        var ownSlots = npc.BodyAddons
            .Where(addon => IsMadeForRace(addon, npc.RaceKey))
            .Aggregate(0u, (slots, addon) => slots | addon.Slots);
        var armorRace = npc.Race?.ArmorRace;
        return npc.BodyAddons
            .Where(addon => IsMadeForRace(addon, npc.RaceKey)
                || armorRace is { } fallback && IsMadeForRace(addon, fallback) && (addon.Slots & ownSlots) == 0);
    }

    private static bool IsMadeForRace(ArmorAddonTraits addon, RecordKey race) =>
        addon.Race == race || addon.AdditionalRaces.Contains(race);

    private string? GetWorldModelPath(ArmorAddonTraits addon, bool female)
    {
        var modelPath = (female ? addon.FemaleModelPath : null) ?? addon.MaleModelPath;
        return !string.IsNullOrWhiteSpace(modelPath) ? meshFiles.NormalizeMeshPath(modelPath) : null;
    }

    private static Box WithHumanoidHeight(Box body) =>
        body with { Max = body.Max with { Z = MathF.Max(body.Max.Z, body.Min.Z + HumanoidHeight) } };

    private static bool HasVolume(Box box) => box.Size is { X: > 0, Y: > 0, Z: > 0 };
}
