namespace AddedObjectRemover;

/// <summary>Created in the entry from the final world's known plugin names; each run's reader resolves through that run's link cache.</summary>
public sealed class BaseFactsFactory(KnownPluginNames knownPlugins) : IBaseFactsFactory
{
    private const string ForeignIndexMessage = "The record index was not opened by this plugin tool.";

    public IBaseFacts Open(IRecordIndex records) =>
        new BaseFactsReader(
            (records as RunLinkCache ?? throw new ArgumentException(ForeignIndexMessage, nameof(records))).Cache,
            knownPlugins);
}
