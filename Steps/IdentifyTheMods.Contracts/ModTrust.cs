namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <summary>How far a plugin's values are accepted as the world the cleaned mod was made for. The patch has none of these values.</summary>
public enum ModTrust
{
    /// <summary>Everything the plugin contains is accepted, even values it only carries over.</summary>
    Trusted,

    /// <summary>
    /// A record the plugin adds is accepted in full; in other records only its own changes are. "Own changes" are the values that
    /// differ from the version loading just before it, not from its masters.
    /// </summary>
    TrustedForOwnChanges,

    /// <summary>Its objects and changes can cause removals.</summary>
    Untrusted,
}
