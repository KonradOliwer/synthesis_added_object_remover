using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// For one kind of judged object version, how far each plugin's version of a record is believed: the plugins whose values that
/// version was made to stand among. It feeds <see cref="AcceptedVersions"/>.
/// </summary>
/// <param name="Patch">This group's output plugin. It is transparent: it holds what earlier patchers of the group wrote, which is never accepted.</param>
/// <param name="Plugins">The trust of every plugin except the patch.</param>
public sealed record AcceptedWorld(PluginName Patch, ImmutableDictionary<PluginName, VersionTrust> Plugins)
{
    public VersionTrust TrustOf(PluginName plugin) => plugin == Patch ? VersionTrust.Transparent : Plugins[plugin];
}
