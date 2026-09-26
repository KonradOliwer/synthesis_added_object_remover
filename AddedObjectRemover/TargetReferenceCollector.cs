using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover;

/// <summary>
/// Records target-plugin objects a placed record depends on through any of its form links
/// (including script properties), labelled as Enable Parent, Linked Reference, Activate Parent or
/// door teleport destination where the link is one of those. Links to non-placed records are
/// recorded too but never looked up.
/// </summary>
internal static class TargetReferenceCollector
{
    public static void Collect(IPlacedGetter record, ModKey target, Dictionary<FormKey, string> references)
    {
        foreach (var link in record.EnumerateFormLinks())
        {
            var formKey = link.FormKey;
            if (link.IsNull || formKey.ModKey != target || formKey == record.FormKey || references.ContainsKey(formKey)) continue;
            references[formKey] = $"{DescribeRelation(record, formKey)} of {record.FormKey}";
        }
    }

    private static string DescribeRelation(IPlacedGetter record, FormKey linked)
    {
        if (record.EnableParent?.Reference.FormKey == linked) return "Enable Parent";
        if (GetLinkedReferences(record).Any(reference => reference.Reference.FormKey == linked)) return "Linked Reference";
        if (record is IPlacedObjectGetter { TeleportDestination: { } destination } && destination.Door.FormKey == linked)
        {
            return "teleport destination of door";
        }
        if (GetActivateParents(record).Any(parent => parent.Reference.FormKey == linked)) return "Activate Parent";
        return "referenced by a link";
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
