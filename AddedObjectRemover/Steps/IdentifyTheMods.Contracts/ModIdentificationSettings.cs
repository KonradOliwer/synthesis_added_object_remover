using System.Collections.Immutable;

namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <param name="Excluded">The excluded plugins that the table knows of.</param>
/// <param name="PatchMasterLimit">The most other masters a compatibility patch may have.</param>
public sealed record ModIdentificationSettings(PluginName Target, ImmutableArray<PluginName> Excluded, bool IgnoreTargetMasters, bool IgnorePatched, int PatchMasterLimit);
