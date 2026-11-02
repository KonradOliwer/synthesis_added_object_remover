using AddedObjectRemover.Run.RunSettingsValidation.Contracts;

namespace AddedObjectRemover;

/// <summary>The cleaned mod looked up in the load order: found, or the reason the run stops.</summary>
internal abstract record CleanedModInLoadOrder
{
    /// <param name="AsTyped">The plugin as the setting names it.</param>
    /// <param name="Listing">The plugin as Synthesis loaded it.</param>
    public sealed record Found(PluginName AsTyped, PluginListing Listing) : CleanedModInLoadOrder;

    public sealed record Stopped(StopReason Reason) : CleanedModInLoadOrder;
}
