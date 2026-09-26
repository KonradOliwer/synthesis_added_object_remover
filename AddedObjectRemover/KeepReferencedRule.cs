using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>"Keep referenced objects": teleport doors and targets other placed objects link to are never removed.</summary>
internal sealed class KeepReferencedRule(bool enabled, IReadOnlyDictionary<FormKey, string> targetReferences)
{
    public bool TryGetKeepReason(TargetObject target, [NotNullWhen(true)] out string? reason)
    {
        reason = null;
        if (!enabled) return false;
        if (target.IsTeleportDoor)
        {
            reason = "teleport door";
            return true;
        }
        return targetReferences.TryGetValue(target.Record.FormKey, out reason);
    }
}
