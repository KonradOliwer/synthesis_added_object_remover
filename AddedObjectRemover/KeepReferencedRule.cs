using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover;

/// <summary>Why a target object is never removed.</summary>
/// <param name="Category">Groups reasons in the summary, e.g. "Enable Parent of a placed object" or "linked from a Quest".</param>
/// <param name="Detail">The full reason for the detailed log, naming the linking record.</param>
internal sealed record KeepReason(string Category, string Detail);

/// <summary>
/// Teleport doors and targets that another record links to (placed objects, quests, packages,
/// locations, scripts, ...) are never removed, because the game may crash or break on them.
/// </summary>
internal sealed class KeepReferencedRule(IReadOnlyDictionary<FormKey, KeepReason> targetReferences)
{
    private static readonly KeepReason TeleportDoor = new("teleport door", "teleport door");

    public bool TryGetKeepReason(TargetObject target, [NotNullWhen(true)] out KeepReason? reason)
    {
        if (target.IsTeleportDoor)
        {
            reason = TeleportDoor;
            return true;
        }
        return targetReferences.TryGetValue(target.Record.FormKey, out reason);
    }
}
