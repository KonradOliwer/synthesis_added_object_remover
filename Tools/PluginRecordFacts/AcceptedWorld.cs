using System.Collections.Immutable;

namespace AddedObjectRemover;

/// <summary>
/// For one kind of record version, how far each plugin's version of a record is believed: the plugins whose values that
/// version was made to stand among. It feeds <see cref="AcceptedVersions"/>.
/// </summary>
/// <param name="Patch">The plugin the output is written to. It is transparent: it holds what earlier runs wrote into it, which is never accepted.</param>
/// <param name="Plugins">The trust of every plugin except the patch.</param>
public sealed record AcceptedWorld(PluginName Patch, ImmutableDictionary<PluginName, VersionTrust> Plugins)
{
    public VersionTrust TrustOf(PluginName plugin) => plugin == Patch ? VersionTrust.Transparent : Plugins[plugin];
}
