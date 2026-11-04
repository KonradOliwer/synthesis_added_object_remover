using System.Collections.Immutable;
using System.Diagnostics;
using AddedObjectRemover.Steps.CollectPlacedObjects.Contracts;
using AddedObjectRemover.Rules.ObjectsLeftBehind.Contracts;
using AddedObjectRemover.Steps.RemoveObjectsInClearedAreas.Contracts;
using AddedObjectRemover.Steps.RestoreWhereRemoved.Contracts;

namespace AddedObjectRemover.Steps.BuildLogAndReports;

/// <summary>
/// The words for what the step that restores what the cleaned mod changed where it was removed decides, for the log and the report
/// files: why a change stays, what goes back, and the parts of a cell and a navmesh. A phrase fits both "kept: ..." and a count
/// ("3 objects (2 ...)"), so one reason reads the same everywhere.
/// </summary>
internal static class RestoreWhereRemovedText
{
    /// <summary>The rows and the columns of the quarters, as <see cref="TerrainQuadrants.IndexOf"/> numbers them: <see cref="TerrainQuadrants.PerSide"/> of each.</summary>
    private static readonly ImmutableArray<string> SouthToNorth = ["south", "north"];

    private static readonly ImmutableArray<string> WestToEast = ["west", "east"];

    /// <summary>What became of a change, as the report files say it: "restored", or "kept: " and the reason.</summary>
    /// <param name="keptBecause">Why the change stays; null when it goes back.</param>
    public static string Result(string? keptBecause) => keptBecause is null ? "restored" : $"kept: {keptBecause}";

    /// <summary>What became of a placed record, as the report files say it: as <see cref="Result(string?)"/>, or "restored with a copy at the cleaned mod's place".</summary>
    /// <param name="keptBecause">Why the record stays; null when it goes back.</param>
    /// <param name="withCopy">It goes back and the patch adds a copy of it where the cleaned mod placed it.</param>
    public static string Result(string? keptBecause, bool withCopy) => withCopy ? $"{Result(keptBecause)} {WithCopy}" : Result(keptBecause);

    /// <summary>What became of a change, as <see cref="Result(string?)"/>, with "; water level kept: ..." when a part of a change that goes back stays.</summary>
    /// <param name="keptBecause">Why the change stays; null when it goes back.</param>
    /// <param name="keptPart">The part that may stay although the rest goes back, as in "water level".</param>
    /// <param name="partKeptBecause">Why that part stays; null when it goes back too.</param>
    public static string Result(string? keptBecause, string keptPart, string? partKeptBecause) =>
        keptBecause is null && partKeptBecause is not null ? $"{Result(null)}; {keptPart} kept: {partKeptBecause}" : Result(keptBecause);

    public const string WaterLevelPart = "water level";
    public const string GroundTexturesPart = "ground textures";

    /// <summary>The words for a record that goes back with a copy, after what was done to it.</summary>
    public const string WithCopy = "with a copy at the cleaned mod's place";

    /// <summary>What a placed record that went back is, in a count.</summary>
    public static string Kind(PlacedRestoreKind kind) => kind switch
    {
        PlacedRestoreKind.OwnModel => "with their own model again",
        PlacedRestoreKind.ShownAgain => "shown again",
        PlacedRestoreKind.HiddenAgain => "hidden again",
        PlacedRestoreKind.MovedBack => "moved back",
        PlacedRestoreKind.TurnedOrRescaledBack => "turned or rescaled back",
        _ => throw new UnreachableException($"Unknown restored kind {kind}."),
    };

    /// <summary>What was done to a placed record that went back, at the start of its line.</summary>
    public static string KindVerb(PlacedRestoreKind kind) => kind switch
    {
        PlacedRestoreKind.OwnModel => "Own model again",
        PlacedRestoreKind.ShownAgain => "Shown again",
        PlacedRestoreKind.HiddenAgain => "Hidden again",
        PlacedRestoreKind.MovedBack => "Moved back",
        PlacedRestoreKind.TurnedOrRescaledBack => "Turned or rescaled back",
        _ => throw new UnreachableException($"Unknown restored kind {kind}."),
    };

    /// <summary>What was done to a navmesh of a group that went back, in its line.</summary>
    public static string UndoVerb(NavmeshUndo undo) => Undone(undo, "sunk under the world and cut off");

    public static string Never(NeverRestoredReason reason) => reason switch
    {
        NeverRestoredReason.WinnerLoadsAfterPatch => "a plugin that loads after the patch has the last word on it",
        NeverRestoredReason.LinkedFromCleanedMod => "a record of the cleaned mod links to it",
        NeverRestoredReason.InDeletedCell => "a plugin deletes the cell it would be written into",
        _ => throw new UnreachableException($"Unknown reason {reason} for a change that never goes back."),
    };

    /// <summary>The reason, naming the winning plugin when that plugin has the last word, as in "... the last word on it (Later.esp)".</summary>
    public static string Never(NeverRestoredReason reason, PluginName winningMod) =>
        reason == NeverRestoredReason.WinnerLoadsAfterPatch ? $"{Never(reason)} ({winningMod})" : Never(reason);

    /// <summary>Why a change stays for its area; a cleared area keeps nothing.</summary>
    public static string Area(AreaVerdict verdict) => verdict switch
    {
        AreaVerdict.NoObjectsAround => "the cleaned mod has no objects around it",
        AreaVerdict.StillStanding => "the cleaned mod's objects still stand around it",
        AreaVerdict.Cleared => throw new UnreachableException("A cleared area was given as the reason a change stays."),
        _ => throw new UnreachableException($"Unknown area verdict {verdict}."),
    };

    /// <summary>
    /// Why another mod's object the cleaned mod moved stays where the cleaned mod's content holds it, as in "the cleaned mod's
    /// standing content holds it and it may not be copied (persistent)" or "the cleaned mod's objects still stand around it and it may
    /// not be copied (not plain scenery)".
    /// </summary>
    public static string NotCopied(CloneJudged cloning) => cloning.Need switch
    {
        CloneNeed.AreaStillStands => $"the cleaned mod's objects still stand around it and it may not be copied ({CopyRefused(cloning)})",
        CloneNeed.NeighbourOfStandingObject => $"the cleaned mod's standing content holds it and it may not be copied ({CopyRefused(cloning)})",
        _ => throw new UnreachableException($"Unknown copy need {cloning.Need}."),
    };

    /// <summary>Why a copy is refused, with the failure of a check that stopped, as in "too close as the cleaned mod's object (the check stopped unexpectedly: ...)".</summary>
    public static string CopyRefused(CloneJudged cloning)
    {
        var refusal = cloning.RefusedBy ?? throw new UnreachableException("A planned copy was given as a refused one.");
        return cloning.Failure is { } failure ? $"{CloneRefused(refusal)} (the check stopped unexpectedly: {failure})" : CloneRefused(refusal);
    }

    /// <summary>The kind of a refusal whose check stopped unexpectedly, in a count.</summary>
    public const string WithStoppedCheck = "with a check that stopped unexpectedly";

    /// <summary>Why another mod's object the cleaned mod moved may not be copied.</summary>
    public static string CloneRefused(CloneRefusal refusal) => refusal switch
    {
        CloneRefusal.NotPlainScenery => "not plain scenery",
        CloneRefusal.HasDistantLod => "has distant LOD",
        CloneRefusal.Persistent => "persistent",
        CloneRefusal.HoldsMoreThanPlacement => "holds more than its placement",
        CloneRefusal.TooCloseAsCleanedModsObject => "too close as the cleaned mod's object",
        CloneRefusal.OnChangedTerrainAsCleanedModsObject => "on changed terrain as the cleaned mod's object",
        CloneRefusal.ActorsWalkOnIt => "actors walk on it",
        _ => throw new UnreachableException($"Unknown copy refusal {refusal}."),
    };

    /// <summary>Why a cell keeps its water level although the rest of it may go back; <paramref name="by"/> is the keeper <paramref name="reason"/> names.</summary>
    /// <param name="label">Names a record.</param>
    public static string WaterKept(WaterKeptReason reason, RecordKey by, Func<RecordKey, string> label) => reason switch
    {
        WaterKeptReason.AtWaterLine => $"{label(by)} lies across the water line between the level before the cleaned mod and the one that stays",
        WaterKeptReason.AroundObjectThatStays => $"the changed-terrain check around {label(by)}, which stays, reaches the cell and counts water changes",
        WaterKeptReason.AroundCopy => $"the changed-terrain check around the copy of {label(by)} reaches the cell and counts water changes",
        _ => throw new UnreachableException($"Unknown water kept reason {reason}."),
    };

    /// <summary>The words for <see cref="LandQuarterOutcome.TexturesKeptForObjectThatStays"/>.</summary>
    public const string TexturesKeptForObjectThatStays =
        "the changed-terrain check around an object that stays or a copy the patch adds reaches its ground and counts terrain type changes";

    /// <summary>The words for <see cref="LandQuarterOutcome.ColoursSharedWithGroundThatStays"/>.</summary>
    public const string ColoursSharedWithGroundThatStays = "its vertex colours lie on a line shared with ground that stays";

    /// <summary>The part of a LAND record that belongs to no terrain quarter.</summary>
    public const string RestOfLandRecord = "the rest of the record";

    /// <summary>The words for <see cref="NavmeshChangeJudged.NothingGoesBack"/>.</summary>
    public const string NothingGoesBack ="nothing of what the cleaned mod changed in it goes back";

    /// <summary>
    /// Why a placed record stays although its area is cleared: the guard, with the record it found and the plugin that keeps the ground
    /// changed with how far it lies from the earlier ground, as in "on ground that stays changed (Later.esp, differs from the
    /// ground without the cleaned mod by up to 12.5 units, tolerance 2.0 units)", or the failure that stopped the check.
    /// </summary>
    /// <param name="label">Names a record.</param>
    public static string Refusal(PlacedRefusal refusal, Func<RecordKey, string> label)
    {
        if (refusal.Failure is { } failure) return $"the check stopped unexpectedly ({failure})";
        var found = string.Join(Describe.ListSeparator, new[] { refusal.By is { } by ? label(by) : null, refusal.Plugin?.ToString(), refusal.Gap is { } gap ? GapText(gap) : null }.OfType<string>());
        return found.Length > 0 ? $"{Guard(refusal.Guard)} ({found})" : Guard(refusal.Guard);
    }

    private static string GapText(GroundGap gap)
    {
        var tolerance = $"tolerance {TextFormat.Fixed(gap.Tolerance, 1)} units";
        return gap.Largest is { } largest ? $"differs from the ground without the cleaned mod by up to {TextFormat.Fixed(largest, 1)} units{Describe.ListSeparator}{tolerance}" : tolerance;
    }

    /// <summary>The kind of refusal a count line groups placed records by.</summary>
    public static string RefusalKind(PlacedRefusal refusal) => refusal.Failure is null ? Guard(refusal.Guard) : WithStoppedCheck;

    public static string Guard(PlacedGuard guard) => guard switch
    {
        PlacedGuard.TooCloseToWhatStays => "too close to objects that stay",
        PlacedGuard.OnGroundThatStaysChanged => "on ground that stays changed",
        PlacedGuard.AtWaterLineThatStaysChanged => "at a water line that stays as the cleaned mod changed it",
        PlacedGuard.HoldsInvisibleObjectThatStays => "holding an invisible object of the cleaned mod that stays",
        PlacedGuard.StandsInChangedNavmesh => "in a navmesh that stays as the cleaned mod changed it",
        PlacedGuard.CarriesObjectThatStays => "carrying an object that stays",
        PlacedGuard.RestsOnWhatDoesNotStay => "resting on an object that does not stay",
        PlacedGuard.DoorLinkChangedLater => "with a navmesh door link a later plugin changed again",
        PlacedGuard.DoorLinkDoesNotMatch => "with a navmesh door link that would not match the navmeshes as they end",
        _ => throw new UnreachableException($"Unknown placed guard {guard}."),
    };

    public static string TerrainKept(TerrainKeptReason reason) => reason switch
    {
        TerrainKeptReason.UnderObjectThatStays => "under objects that stay",
        TerrainKeptReason.UnderObjectKeptAsChanged => "under objects kept as the cleaned mod changed them",
        TerrainKeptReason.UnderObjectPlacedForTheGroundAsItIs => "under objects of other plugins placed for the ground as it is",
        TerrainKeptReason.UnderNavmesh => "under navmeshes that lie on the ground as it is",
        TerrainKeptReason.TooSteepToStore => "too steep to store",
        TerrainKeptReason.OnEdgeOfLandThatStays => "on a cell edge whose neighbouring ground stays",
        TerrainKeptReason.EarlierEdgeHeightsDiffer => "on a cell edge whose two sides held different heights before the cleaned mod",
        TerrainKeptReason.EdgeStoredOnDifferentSteps => "on a cell edge whose two sides store heights on different steps",
        TerrainKeptReason.NextToQuarterThatStays => "on the line to a quarter of the cell whose area is not cleared",
        TerrainKeptReason.WithoutHeightMap => "in a land record whose version before the cleaned mod or winning version holds no height map",
        _ => throw new UnreachableException($"Unknown terrain kept reason {reason}."),
    };

    /// <summary>The parts of a cell, as a phrase; a region part is named without its region.</summary>
    public static string PartOfCell(CellPart part) => part switch
    {
        CellPart.Lighting => "lighting",
        CellPart.ImageSpace => "image space",
        CellPart.AcousticSpace => "acoustic space",
        CellPart.Music => "music",
        CellPart.WaterLevel => "water level",
        CellPart.WaterLook => "water look",
        CellPart.Region => "regions",
        CellPart.SkyAndWeatherFromRegion => "sky and weather from region",
        CellPart.Name => "name",
        CellPart.Ownership => "ownership",
        CellPart.EncounterZone => "encounter zone",
        CellPart.Location => "location",
        CellPart.CantTravelFromHere => "can't travel from here",
        CellPart.NoLodWater => "no LOD water",
        CellPart.PublicArea => "public area",
        CellPart.HandChanged => "hand changed",
        CellPart.ShowSky => "show sky",
        CellPart.UseSkyLighting => "use sky lighting",
        CellPart.OffLimits => "off limits",
        CellPart.CantWait => "can't wait",
        CellPart.RestOfCell => "other cell settings",
        _ => throw new UnreachableException($"Unknown cell part {part}."),
    };

    /// <summary>What a restored navmesh is, in a count: restored, brought back, or one the cleaned mod added, sunk.</summary>
    public static string Undo(NavmeshUndo undo) => Undone(undo, "of the cleaned mod's sunk under the world");

    /// <param name="sunk">How the line or the count words a navmesh of the cleaned mod that was sunk.</param>
    private static string Undone(NavmeshUndo undo, string sunk) => undo switch
    {
        NavmeshUndo.Geometry or NavmeshUndo.MapEntry => "restored",
        NavmeshUndo.BroughtBack => "brought back",
        NavmeshUndo.Sunk => sunk,
        _ => throw new UnreachableException($"Unknown navmesh undo {undo}."),
    };

    /// <summary>Why a group of navmeshes stays; the navmesh data fault comes with its navmesh, triangle and the other record.</summary>
    /// <param name="label">Names a record.</param>
    public static string Refusal(NavmeshRefusal refusal, Func<RecordKey, string> label) =>
        refusal.Fault is { } fault ? $"{Guard(refusal.Guard)} ({Fault(fault)})" : $"{Guard(refusal.Guard)} ({label(refusal.By)})";

    public static string Guard(NavmeshGuard guard) => guard switch
    {
        NavmeshGuard.ChangedLater => "changed again by a later plugin, itself or a door linked to it",
        NavmeshGuard.StoredInAnotherRecordFormat => "stored in another record format",
        NavmeshGuard.EdgeLinkUnmatched => "linked to navmeshes that stay",
        NavmeshGuard.DidNotExistBefore => "with an earlier cell, map entry or replacement missing",
        NavmeshGuard.PreferredPaths => "tied to preferred paths that would no longer match",
        NavmeshGuard.MapEntry => "with a map entry that merges with a navmesh that is sunk or deleted",
        NavmeshGuard.Door => "tied to a door that would not stand or would not link back",
        NavmeshGuard.EarlierDataInconsistent => "with inconsistent data from before the cleaned mod",
        NavmeshGuard.DataAsItStandsInconsistent => "inconsistent with navmesh data that stays",
        NavmeshGuard.Ground => "on ground that stays changed",
        NavmeshGuard.Object => "with an object that stays standing in it",
        NavmeshGuard.Water => "in a cell whose water level stays as the cleaned mod changed it",
        _ => throw new UnreachableException($"Unknown navmesh guard {guard}."),
    };

    /// <summary>E.g. "00AB12:Mod.esp has a link the other navmesh does not answer (triangle 12, 00AB13:Mod.esp)".</summary>
    public static string Fault(NavmeshFault fault)
    {
        var place = fault.Index is { } index ? $"{(fault.Kind == NavmeshFaultKind.VertexOutsideBounds ? "vertex" : "triangle")} {index}" : null;
        var other = fault.Other?.ToString();
        var where = string.Join(Describe.ListSeparator, new[] { place, other }.OfType<string>());
        return $"{fault.Navmesh} {FaultKind(fault.Kind)}{(where.Length == 0 ? string.Empty : $" ({where})")}";
    }

    private static string FaultKind(NavmeshFaultKind kind) => kind switch
    {
        NavmeshFaultKind.Missing => "does not exist",
        NavmeshFaultKind.Deleted => "is deleted",
        NavmeshFaultKind.CellDeleted => "lies in a deleted cell",
        NavmeshFaultKind.NoData => "has no navmesh data",
        NavmeshFaultKind.ParentIsAnotherCell => "names another cell than the one holding it as its parent",
        NavmeshFaultKind.NoMapEntry => "has no map entry",
        NavmeshFaultKind.MapEntryNamesAnotherParent => "has a map entry that names another parent than its data",
        NavmeshFaultKind.VertexOutOfRange => "has a triangle naming a vertex that does not exist",
        NavmeshFaultKind.InnerEdgeOutOfRange => "has a triangle whose inner edge names a triangle that does not exist",
        NavmeshFaultKind.EdgeLinkEntryOutOfRange => "has a triangle whose edge link names a link table entry that does not exist",
        NavmeshFaultKind.LinkedNavmeshMissing => "has a triangle linked to a navmesh that does not exist or is deleted",
        NavmeshFaultKind.LinkedTriangleOutOfRange => "has a link to a triangle that the other navmesh does not have",
        NavmeshFaultKind.NoLinkBack => "has a link the other navmesh does not answer",
        NavmeshFaultKind.SharedEdgeApart => "has a link whose shared edge does not meet the other navmesh's",
        NavmeshFaultKind.DoorTriangleOutOfRange => "has a door triangle that does not exist",
        NavmeshFaultKind.DoorMissing => "names a door that does not exist or is deleted",
        NavmeshFaultKind.DoorDoesNotStand => "names a door that does not stand",
        NavmeshFaultKind.DoorLinksElsewhere => "names a door that links to another navmesh or triangle",
        NavmeshFaultKind.DoorLinkWithoutDoorTriangle => "has a door linking to it without a door triangle",
        NavmeshFaultKind.VertexOutsideBounds => "has a vertex outside its bounds",
        NavmeshFaultKind.GridFromAnotherTriangleCount => "has a grid made for another number of triangles",
        NavmeshFaultKind.MergedNavmeshMissing => "has a map entry merging it with a navmesh that does not exist",
        NavmeshFaultKind.LinkedDoorMissing => "has a map entry naming a door that does not exist or is deleted",
        NavmeshFaultKind.PreferredPathNavmeshMissing => "lies in preferred paths that name a navmesh that does not exist",
        _ => throw new UnreachableException($"Unknown navmesh fault kind {kind}."),
    };

    public static string Heights(int count) => TextFormat.Count(count, "height", "heights");

    public static string VertexColours(int count) => TextFormat.Count(count, "vertex colour", "vertex colours");

    /// <summary>E.g. "210 heights, 3 vertex colours, ground textures"; empty when there is none.</summary>
    public static string Pieces(TerrainPieces pieces) =>
        string.Join(
            Describe.ListSeparator,
            new[]
            {
                pieces.Heights > 0 ? Heights(pieces.Heights) : null,
                pieces.Colours > 0 ? VertexColours(pieces.Colours) : null,
                pieces.Textures ? "ground textures" : null,
            }.OfType<string>());

    /// <summary>The quarter of a cell's terrain, by <see cref="TerrainQuadrants.IndexOf"/>.</summary>
    public static string Quarter(int quarter) =>
        $"{SouthToNorth[quarter / TerrainQuadrants.PerSide]}-{WestToEast[quarter % TerrainQuadrants.PerSide]}";
}
