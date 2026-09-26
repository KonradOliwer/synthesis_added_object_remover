using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Records target-plugin objects a placed record depends on: as Enable Parent, Linked Reference,
/// Activate Parent, destination of its door teleport, or through any other form link of the
/// record. The named relations are checked first so they win over the generic one in the log.
/// Links to non-placed records are recorded too but never looked up.
/// </summary>
internal static class TargetReferenceCollector
{
    public static void Collect(IPlacedGetter record, ModKey target, Dictionary<FormKey, string> references)
    {
        void Add(IFormLinkGetter link, string relation)
        {
            if (link.IsNull || link.FormKey.ModKey != target || link.FormKey == record.FormKey) return;
            references.TryAdd(link.FormKey, $"{relation} of {record.FormKey}");
        }

        if (record.EnableParent is { } enableParent) Add(enableParent.Reference, "Enable Parent");

        foreach (var linked in GetLinkedReferences(record)) Add(linked.Reference, "Linked Reference");

        if (record is IPlacedObjectGetter { TeleportDestination: { } destination })
        {
            Add(destination.Door, "teleport destination of door");
        }

        foreach (var parent in GetActivateParents(record)) Add(parent.Reference, "Activate Parent");

        foreach (var link in record.EnumerateFormLinks()) Add(link, "referenced by a link");
    }

    private static IEnumerable<ILinkedReferencesGetter> GetLinkedReferences(IPlacedGetter record) => record switch
    {
        IPlacedObjectGetter placedObject => placedObject.LinkedReferences,
        IPlacedNpcGetter placedNpc => placedNpc.LinkedReferences,
        IAPlacedTrapGetter placedTrap => placedTrap.LinkedReferences,
        _ => [],
    };

    private static IEnumerable<IActivateParentGetter> GetActivateParents(IPlacedGetter record) =>
        (record switch
        {
            IPlacedObjectGetter placedObject => placedObject.ActivateParents,
            IPlacedNpcGetter placedNpc => placedNpc.ActivateParents,
            IAPlacedTrapGetter placedTrap => placedTrap.ActivateParents,
            _ => null,
        })?.Parents ?? [];
}
