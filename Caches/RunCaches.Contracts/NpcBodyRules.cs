using AddedObjectRemover.Caches.BaseObjectShapeAndKind.Contracts;

namespace AddedObjectRemover.Caches.RunCaches.Contracts;

/// <summary>How the bodies of the NPCs are worked out.</summary>
/// <param name="TemplateFlag">The template flag that makes an NPC take over its template's traits, and so its body.</param>
/// <param name="CreateOwnBodyResolver">Creates the rule that works out the body of an NPC supplying its own traits.</param>
/// <param name="DistinctBodies">Reduces possible bodies to distinct ones.</param>
public sealed record NpcBodyRules(
    NpcTemplateFlag TemplateFlag,
    Func<IMeshFiles, IBodyMeshBounds, Func<NpcTraits, NpcBody>> CreateOwnBodyResolver,
    Func<IReadOnlyList<NpcBody>, NpcBodySet> DistinctBodies);
