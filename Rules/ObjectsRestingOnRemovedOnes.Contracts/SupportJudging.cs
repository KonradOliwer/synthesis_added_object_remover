using System.Collections.Immutable;
using AddedObjectRemover.Caches.RunCaches.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

namespace AddedObjectRemover.Rules.ObjectsRestingOnRemovedOnes.Contracts;

/// <summary>What every part of the other-mods support rule reads, and every rule it provides.</summary>
/// <param name="Targets">Where they stand before any decision.</param>
/// <param name="Caches">
/// Index the target objects and give the shapes, triangles and terrain; their objects of any plugin are asked only when there is
/// something to judge: the first ask builds their index. The search for the holders of objects that stay asks the caches of its
/// worlds instead.
/// </param>
/// <param name="Trust">Decides whose objects are judged; the cleaned mod's records a later plugin overrides are not judged.</param>
/// <param name="Contacts">The support rule the support of an object is measured with.</param>
public sealed record SupportJudging(
    ImmutableArray<TargetObject> Targets,
    IRunCaches Caches,
    ModTrustTable Trust,
    LoadOrderPlugins Plugins,
    OtherModsObjectsSettings Settings,
    ISupportContacts Contacts,
    Execution Execution);
