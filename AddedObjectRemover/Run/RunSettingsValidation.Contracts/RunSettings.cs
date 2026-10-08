using System.Collections.Immutable;
using AddedObjectRemover.Steps.BuildLogAndReports.Contracts;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Steps.IdentifyTheMods.Contracts;
using AddedObjectRemover.Steps.MoveKeptMarkers.Contracts;
using AddedObjectRemover.Steps.RemoveInvisibleObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsRestingOnRemovedOnes.Contracts;
using AddedObjectRemover.Steps.RemoveTooCloseObjects.Contracts;

namespace AddedObjectRemover.Run.RunSettingsValidation.Contracts;

/// <summary>The validated settings of one run.</summary>
/// <param name="Target">The target plugin as the load order spells it; used for comparisons.</param>
/// <param name="TargetAsTyped">The target plugin as the settings spell it; printed in the log and reports.</param>
/// <param name="ExcludedNames">The excluded plugins as parsed from the settings, loaded or not, for the settings echo.</param>
/// <param name="LeftBehind">Null when the left-behind invisible objects step is off.</param>
/// <param name="MarkerMoves">Null unless kept markers are moved, which needs the left-behind step.</param>
public sealed record RunSettings(
    PluginName Target,
    PluginName TargetAsTyped,
    ImmutableArray<string> ExcludedNames,
    ModIdentificationSettings ModIdentification,
    WhatToCollect Read,
    TooCloseOptions TooClose,
    AlsoRemoveSettings AlsoRemove,
    LeftBehindOptions? LeftBehind,
    MarkerMoveSettings? MarkerMoves,
    ReportOptions Reports,
    bool DetailedLog,
    Execution Execution);
