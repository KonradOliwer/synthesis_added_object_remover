using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Teleport doors and targets other placed objects link to are never removed.</summary>
internal sealed class KeepReferencedRule(IReadOnlyDictionary<FormKey, string> targetReferences)
{
    public bool TryGetKeepReason(TargetObject target, [NotNullWhen(true)] out string? reason)
    {
        if (target.IsTeleportDoor)
        {
            reason = "teleport door";
            return true;
        }
        return targetReferences.TryGetValue(target.Record.FormKey, out reason);
    }
}
