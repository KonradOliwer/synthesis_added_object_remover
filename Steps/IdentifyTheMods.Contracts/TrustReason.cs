namespace AddedObjectRemover.Steps.IdentifyTheMods.Contracts;

/// <summary>The rule that decided a plugin's <see cref="ModTrust"/>.</summary>
public enum TrustReason
{
    /// <summary>Skyrim.esm, Update.esm or one of the three DLCs; Creation Club content is not part of it.</summary>
    BaseGame,

    CleanedMod,

    CleanedModMaster,

    /// <summary>Loads before the cleaned mod, with that rule on.</summary>
    LoadsBeforeCleanedMod,

    /// <summary>Listed under the trusted mods and their masters.</summary>
    Listed,

    MasterOfListed,

    /// <summary>Has the cleaned mod as a master and at most the allowed number of other masters, with that rule on.</summary>
    RequiringMod,

    MasterOfRequiringMod,

    ListedOwnChangesOnly,

    NeverTrusted,

    /// <summary>Untrusted: an Added Object Remover patch of another group, which the automatic rules skip.</summary>
    OtherGroupPatch,

    /// <summary>Untrusted: has the cleaned mod as a master but more other masters than allowed.</summary>
    RequiringModTooManyMasters,
}
