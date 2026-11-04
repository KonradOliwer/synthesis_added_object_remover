using System.Collections.Immutable;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>What the removal steps judge each target with, as the restore step reads it, for scenes that do not run those steps.</summary>
internal static class TestTargetGroups
{
    /// <summary>No target is judged by any group, so no plugin's trust is asked.</summary>
    public static WhereRemovedRestorer.RemovalStepJudging None => new([], [], new AcceptedWorld(TestTargets.PatchName, ImmutableDictionary<PluginName, VersionTrust>.Empty));
}
