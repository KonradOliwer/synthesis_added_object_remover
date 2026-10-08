using System.Diagnostics.CodeAnalysis;

namespace AddedObjectRemover.Steps.FindTargetObjectsToKeep.Contracts;

/// <summary>The target objects that are never removed, with the reason each stays.</summary>
public interface IObjectsToKeep
{
    /// <summary>The linked groups of the target objects.</summary>
    ILinkedGroups Groups { get; }

    bool IsProtected(int targetIndex);

    /// <summary>Why the target stays: its own reason, or that of the first member of its group that has one.</summary>
    bool TryGetKeepReason(int targetIndex, [NotNullWhen(true)] out KeepReason? reason);

    /// <summary>Why the target itself must stay, ignoring its group; null when nothing depends on it.</summary>
    KeepReason? GetOwnReason(int targetIndex);
}
