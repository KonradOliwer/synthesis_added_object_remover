namespace AddedObjectRemover;

/// <summary>Every decision of a run, from the setup through the relocations.</summary>
/// <param name="Components">Null unless the touch rounds ran and the detailed log or the report files need them.</param>
/// <param name="Final">The ledger with every round.</param>
internal record Decided(
    World World,
    TargetLooks Looks,
    RivalCensus Census,
    Replacements Replacements,
    Protection Protection,
    Hosts Hosts,
    ClashResult Clashes,
    FollowUpResult FollowUp,
    TouchComponentSet? Components,
    LeftoverResult Leftovers,
    RelocationResult Relocation,
    Ledger Final);
