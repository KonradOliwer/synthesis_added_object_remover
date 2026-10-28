namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// No business code is copied: no 40 consecutive tokens (names and numbers normalised, comments ignored) appear
/// twice. Limit: shorter copies that the known-idiom list does not name are not found. Repetition that is
/// structure rather than logic is listed with its reason, by the places it occurs in (file and type.member), which
/// the failure message prints with the lines of each copy.
/// </summary>
public class CopiedCodeTests
{
    private const int WindowLength = 40;

    private const string CountLines = "One count per line of a statistics record; each line names its own kind, so there is no logic to share.";
    private const string EnumTextArms = "Enum-to-text switch arms ending in the same unreachable-case throw; each switch lists the values of its own enum.";
    private const string CsvColumnNames = "CSV header column names; each table lists its own columns.";
    private const string CounterRecord = "Counter records: each lists its own counters and its sum, which only repeat in shape.";
    private const string PartKeyLines = "One line per part key: Report's own keys and the wording table; each line names its own part, so there is no logic to share.";
    private const string ViewForwarders ="One-line members of a read-only view, each forwarding one question of the index to its caller; no logic to share.";
    private const string SettingChecks = "Setting-by-setting check lines; each reads another setting with its own default, limit and wording.";
    private const string TableDeclarations = "Declaration of a report class with a constant or header list; the tables share only the shape of the declaration.";
    private const string TextConstants = "Lists of text constants; each names its own setting or wording, so there is no logic to share.";
    private const string ErrorBoundary ="Per-object error boundary of two steps: each catches the failure of its own kind of object under its own part key and recovers in its own way; they share only the shape.";

    private const string NoSharedStepTypes =
        "The generic parts live in CompassSectorAreas and the decision list; a tool may not reference step types, and a logic project may not reference another logic project.";

    private const string ResultRecords = "Result records of two steps (outcome, evaluations, proposals, failure), each over its own check type. " + NoSharedStepTypes;

    private const string FindSkeleton = "Two steps' Find: switched-off check and the Failures.Attempt wrapper, around each step's own rule and result types. " + NoSharedStepTypes;

    private const string JoinSkeleton =
        "Two steps' Find and the mapping of a round's decisions onto the step's own outcomes, over each step's own rule, result and outcome types. " + NoSharedStepTypes;

    private const string ProposalLists = "Two steps' removal proposals: one proposal per removed evaluation, with each step's own removal reason. " + NoSharedStepTypes;

    private const string RuleSkeleton =
        "Two rules' entry and parallel evaluation of candidates: each reads its own settings, candidate test and check type. " + NoSharedStepTypes;

    private const string OutcomeLines =
        "The per-object outcome line of two steps (the invisible step's also names the radius) and their call of the shared summary line; each reads its own outcome and wording types.";

    private const string SettingsLines = "The settings lines of two steps: each prints its own group name, switch and kind names with the shared surroundings wording.";

    private const string StepSections = "Overloads of the log's entry type: each hands one step's result to its own section builder under the same skeleton; they share only the shape.";

    private const string PartKeyHolders = "Part key holders of two steps with an owner and two keys each; each key names its own part, so there is no logic to share.";

    private const string PlacementRules = "The placement rule of two steps: each places the object and runs its own step's check, which returns its own result type; they share only the shape.";

    private const string EntryForwarders =
        "Public methods of a step's entry type that take the run's caches one by one and hand them to the internal code as one context; each forwards to its own method, so there is no logic to share.";

    private const string ParallelCalls ="One call of the order-independent parallel map over a list with per-worker buffers; each asks its own question, so there is no logic to share.";

    private const string Shapes = "Caches/BaseObjectShapeAndKind/BaseObjectShapes.cs:BaseObjectShapes.";
    private const string TooClose = "Steps/RemoveTooCloseObjects/";
    private const string TooCloseContracts = "Steps/RemoveTooCloseObjects.Contracts/";
    private const string Settings = "Run/RunSettingsValidation/SettingsValidator.cs:SettingsValidator.";
    private const string Report = "Steps/BuildLogAndReports/";
    private const string Describe = Report + "Describe.cs:Describe.";
    private const string LeftBehindText = Report + "LeftBehindOutcomeText.cs:LeftBehindOutcomeText.";
    private const string MeshText = Report + "MeshProblemText.cs:MeshProblemText.";
    private const string KeepText = Report + "KeepReasonText.cs:KeepReasonText.";
    private const string PartText = Report + "RunPartText.cs:RunPartText.";
    private const string VisibilityText = Report + "ObjectVisibilityText.cs:ObjectVisibilityText.";
    private const string OtherModsText = Report + "OtherModsObjectText.cs:OtherModsObjectText.";
    private const string Invisible = "Steps/RemoveInvisibleObjectsLeftBehind/";
    private const string InvisibleContracts = "Steps/RemoveInvisibleObjectsLeftBehind.Contracts/";
    private const string VisibleStep = "Steps/RemoveVisibleObjectsLeftBehind/";
    private const string VisibleContracts = "Steps/RemoveVisibleObjectsLeftBehind.Contracts/";
    private const string LeftBehindSections = Report + "LogSections.LeftBehindAndMarkerMoves.cs:LogSections.";
    private const string VisibleSections = Report + "LogSections.VisibleLeftBehind.cs:LogSections.";
    private const string StartSections = Report + "LogSections.Start.cs:LogSections.";
    private const string Resting = "Steps/RemoveObjectsRestingOnRemovedOnes/";
    private const string RestingContracts = "Steps/RemoveObjectsRestingOnRemovedOnes.Contracts/";

    /// <summary>Each copy by the places it occurs in (see <see cref="CopyFamily.Key"/>), with the reason it is not shared.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        [CopyFamily.KeyOf([Settings + "CreateLeftBehindSettings", Settings + "CreateVisibleLeftBehindSettings", Settings + "CreateWhatToCollect", Settings + "ValidateSurroundings"])] = SettingChecks,
        [CopyFamily.KeyOf([Report + "LogAndReports.cs:LogAndReports.BuildStepSections"])] = StepSections,
        [CopyFamily.KeyOf([
            LeftBehindSections + "LeftBehindOutcomeLine", LeftBehindSections + "LeftBehindSummaryLine",
            VisibleSections + "VisibleLeftBehindOutcomeLine", VisibleSections + "VisibleLeftBehindSummaryLine"])] = OutcomeLines,
        [CopyFamily.KeyOf([StartSections + "DescribeLeftBehind", StartSections + "DescribeVisibleLeftBehind"])] = SettingsLines,
        [CopyFamily.KeyOf([
            InvisibleContracts + "LeftBehindResult.cs", InvisibleContracts + "LeftBehindResult.cs:LeftBehindResult", InvisibleContracts + "LeftBehindResult.cs:LeftBehindResult.SwitchedOff",
            VisibleContracts + "VisibleLeftBehindResult.cs", VisibleContracts + "VisibleLeftBehindResult.cs:VisibleLeftBehindResult",
            VisibleContracts + "VisibleLeftBehindResult.cs:VisibleLeftBehindResult.SwitchedOff"])] = ResultRecords,
        [CopyFamily.KeyOf([Invisible + "LeftBehindObjects.cs:LeftBehindObjects.Find", VisibleStep + "VisibleLeftBehindObjects.cs:VisibleLeftBehindObjects.Find"])] = FindSkeleton,
        [CopyFamily.KeyOf([
            Invisible + "LeftBehindObjects.cs:LeftBehindObjects.Find", Invisible + "LeftBehindObjects.cs:LeftBehindObjects.JoinVerdicts",
            VisibleStep + "VisibleLeftBehindObjects.cs:VisibleLeftBehindObjects.Find", VisibleStep + "VisibleLeftBehindObjects.cs:VisibleLeftBehindObjects.JoinVerdicts"])] =
            JoinSkeleton,
        [CopyFamily.KeyOf([
            Invisible + "LeftBehindRound.cs", Invisible + "LeftBehindRound.cs:LeftBehindRound", Invisible + "LeftBehindRound.cs:LeftBehindRound.ProposalsOf",
            VisibleStep + "VisibleLeftBehindRound.cs", VisibleStep + "VisibleLeftBehindRound.cs:VisibleLeftBehindRound",
            VisibleStep + "VisibleLeftBehindRound.cs:VisibleLeftBehindRound.ProposalsOf"])] = ProposalLists,
        [CopyFamily.KeyOf([
            Invisible + "LeftBehindRule.cs:LeftBehindRule", Invisible + "LeftBehindRule.cs:LeftBehindRule.EvaluateAll", Invisible + "LeftBehindRule.cs:LeftBehindRule.SelectRemovals",
            VisibleStep + "VisibleLeftBehindRule.cs:VisibleLeftBehindRule", VisibleStep + "VisibleLeftBehindRule.cs:VisibleLeftBehindRule.EvaluateAll",
            VisibleStep + "VisibleLeftBehindRule.cs:VisibleLeftBehindRule.SelectRemovals"])] = RuleSkeleton,
        [CopyFamily.KeyOf([
            Invisible + "LeftBehindRule.cs:LeftBehindRule.Evaluate", Invisible + "LeftBehindRule.cs:LeftBehindRule.EvaluateAll",
            VisibleStep + "VisibleLeftBehindRule.cs:VisibleLeftBehindRule.Evaluate", VisibleStep + "VisibleLeftBehindRule.cs:VisibleLeftBehindRule.EvaluateAll"])] = RuleSkeleton,
        [CopyFamily.KeyOf([Shapes + "CountBounds", TooClose + "TooCloseObjects.cs:TooCloseObjects.CountSizes"])] = CountLines + " " + CounterRecord,
        [CopyFamily.KeyOf([Settings + "ClampMarginInUnits", Settings + "CreateChangedTerrainSettings", Settings + "CreateTooCloseSettings", Settings + "WarnOfUnlistableNeverTrusted"])] = SettingChecks,
        [CopyFamily.KeyOf([Settings + "CreateChangedTerrainSettings", Settings + "CreateTooCloseSettings"])] = SettingChecks,
        [CopyFamily.KeyOf([Settings + "CreateAlsoRemoveSettings", Settings + "CreateChangedTerrainSettings"])] = SettingChecks,
        [CopyFamily.KeyOf([
            Settings + "AnchoringThresholdName", Settings + "Build", Settings + "CleanedModName", Settings + "ClearedDirectionsName", Settings + "DirectionThresholdName", Settings + "FollowUpModeName",
            Settings + "HeightChangeToleranceName", Settings + "MaxOtherMastersName", Settings + "MinOccupiedDirectionsName", Settings + "MinShrinkLimitPercent", Settings + "MovedDistanceName",
            Settings + "NeverTrustedModsName", Settings + "NpcsName", Settings + "OtherModsObjectsName", Settings + "OwnChangesModsName",
            Settings + "SearchRadiusName", Settings + "ShrinkLimitName", Settings + "ShrinkOrTurnGroup", Settings + "InvisibleSurroundingObjectsGroup", Settings + "VisibleSurroundingObjectsGroup",
            Settings + "TerrainCheckedObjectsName", Settings + "TerrainDistanceMarginName", Settings + "TerrainSizeMarginName", Settings + "TerrainTypeChangePercentName",
            Settings + "TooCloseDistanceMarginName", Settings + "TooCloseSizeMarginName", Settings + "TouchDistanceName", Settings + "TrustedModsName",
            Settings + "TurnLimitName", Settings + "WaterCoverChangePercentName", Settings + "WaterHeightChangeToleranceName",
            KeepText + "Category", KeepText + "CheckFailed", KeepText + "LinkedGroupCategory", KeepText + "LocationRoleCategory", KeepText + "OtherModsObjectRestsOnIt",
            KeepText + "OwnLocationRefType", KeepText + "PlacedCategoryPrefix", KeepText + "ScriptCategory", KeepText + "TeleportDoor"])] = TextConstants,
        [CopyFamily.KeyOf([Describe + "HintType", Describe + "PointReason", Describe + "RemovalReason", Describe + "TimedSuffix"])] = EnumTextArms,
        [CopyFamily.KeyOf([
            Describe + "HintType", Describe + "LeftBehindReason", Describe + "TimedSuffix", KeepText + "Link", KeepText + "Relation",
            Report + "LeftBehindOutcomeText.cs", Report + "LeftBehindOutcomeText.cs:LeftBehindOutcomeText",
            LeftBehindText + "Describe", LeftBehindText + "DescribeReason", MeshText + "FailureCategory", MeshText + "Line", MeshText + "ReadFailure",
            Report + "VisibleLeftBehindOutcomeText.cs", Report + "VisibleLeftBehindOutcomeText.cs:VisibleLeftBehindOutcomeText",
            Report + "VisibleLeftBehindOutcomeText.cs:VisibleLeftBehindOutcomeText.Describe", Report + "VisibleLeftBehindOutcomeText.cs:VisibleLeftBehindOutcomeText.DescribeReason",
            Report + "VisibleLeftBehindOutcomeText.cs:VisibleLeftBehindOutcomeText.KindName", Report + "VisibleLeftBehindOutcomeText.cs:VisibleLeftBehindOutcomeText.KindNameOrEmpty",
            VisibilityText + "Describe", VisibilityText + "KindName", OtherModsText + "NothingBelow", OtherModsText + "Outcome", OtherModsText + "RoleName",
            OtherModsText + "Roles", OtherModsText + "Summary", PartText + "LinkedStep", PartText + "Word"])] = EnumTextArms,
        [CopyFamily.KeyOf([MeshText + "FailureCategory", MeshText + "Line", MeshText + "ReadFailure"])] = EnumTextArms,
        [CopyFamily.KeyOf([Describe + "OriginPlace", Report + "Tables.Fits.cs:Tables.OutcomeName"])] = EnumTextArms,
        [CopyFamily.KeyOf([
            Report + "LogSections.Fit.cs:LogSections.FittedObjectLine", Report + "LogSections.Fit.cs:LogSections.OutcomeText",
            Report + "Tables.Fits.cs:Tables.FitRow", Report + "Tables.Fits.cs:Tables.OutcomeName"])] = EnumTextArms,
        [CopyFamily.KeyOf([PartText + "Wordings"])] = PartKeyLines,
        [CopyFamily.KeyOf([
            Report + "Tables.Fits.cs:Tables.BuildFitsTable", Report + "Tables.Fits.cs:Tables.FitsHeader",
            Report + "Tables.Support.cs:Tables.BuildSupportTable", Report + "Tables.Support.cs:Tables.MaxListedSupporters", Report + "Tables.Support.cs:Tables.SupportHeader",
            Report + "Tables.Touch.cs:Tables.ComponentHeader", Report + "Tables.Touch.cs:Tables.EdgeHeader", Report + "Tables.Touch.cs:Tables.MaxTopBases"])] = TableDeclarations + " " + CsvColumnNames,
        [CopyFamily.KeyOf([
            Report + "Tables.Fits.cs", Report + "Tables.Fits.cs:Tables", Report + "Tables.Fits.cs:Tables.FitsHeader",
            Report + "Tables.OtherModsObjects.cs", Report + "Tables.OtherModsObjects.cs:Tables", Report + "Tables.OtherModsObjects.cs:Tables.LeftWithoutSupportHeader",
            Report + "Tables.Removals.cs", Report + "Tables.Removals.cs:Tables", Report + "Tables.Removals.cs:Tables.RemovalsHeader"])] = TableDeclarations + " " + CsvColumnNames,
        [CopyFamily.KeyOf([
            Report + "Tables.Fits.cs:Tables.BuildFitsTable", Report + "Tables.Fits.cs:Tables.FitsHeader",
            Report + "Tables.OtherModsObjects.cs:Tables.BuildLeftWithoutSupportTable", Report + "Tables.OtherModsObjects.cs:Tables.LeftWithoutSupportHeader"])] =
            TableDeclarations + " " + CsvColumnNames,
        [CopyFamily.KeyOf(["Steps/MoveKeptMarkers/KeptMarkerMover.cs:KeptMarkerMover.Move", Resting + "TargetsHoldingUpOtherModsObjects.cs:TargetsHoldingUpOtherModsObjects.Find"])] =
            ParallelCalls,
        [CopyFamily.KeyOf([Resting + "SupportContactFinder.cs:SupportContactFinder.FindContactsOfAll", Resting + "TargetsHoldingUpOtherModsObjects.cs:TargetsHoldingUpOtherModsObjects.Find"])] =
            ParallelCalls,
        [CopyFamily.KeyOf([Resting + "LeftWithoutSupport.cs:LeftWithoutSupport.FindMissingWeightsBefore", Resting + "RestingRule.cs:RestingRule.FindResting"])] = ParallelCalls,
        [CopyFamily.KeyOf([Resting + "RestingObjects.cs:RestingObjects.FindHeldUpTargets", Resting + "RestingObjects.cs:RestingObjects.SettleOtherModsObjects"])] =
            EntryForwarders,
        [CopyFamily.KeyOf([
            "Steps/RemoveObjectsOnChangedTerrain/ChangedTerrainPlacementRule.cs:ChangedTerrainPlacementRule.StartTries",
            "Steps/RemoveObjectsOnChangedTerrain/ChangedTerrainPlacementRule.cs:ChangedTerrainPlacementRule.Tries",
            "Steps/RemoveObjectsOnChangedTerrain/ChangedTerrainPlacementRule.cs:ChangedTerrainPlacementRule.Tries.Check",
            "Steps/RemoveObjectsOnChangedTerrain/ChangedTerrainPlacementRule.cs:ChangedTerrainPlacementRule.Tries.Dispose",
            TooClose + "TooClosePlacementRule.cs:TooClosePlacementRule.StartTries",
            TooClose + "TooClosePlacementRule.cs:TooClosePlacementRule.Tries",
            TooClose + "TooClosePlacementRule.cs:TooClosePlacementRule.Tries.Check",
            TooClose + "TooClosePlacementRule.cs:TooClosePlacementRule.Tries.Dispose"])] = PlacementRules,
        [CopyFamily.KeyOf([
            "Steps/FitClashingObjects.Contracts/FitParts.cs", "Steps/FitClashingObjects.Contracts/FitParts.cs:FitParts",
            "Steps/FitClashingObjects.Contracts/FitParts.cs:FitParts.FitCheck", "Steps/FitClashingObjects.Contracts/FitParts.cs:FitParts.Owner",
            "Steps/FitClashingObjects.Contracts/FitParts.cs:FitParts.RestingCheck",
            RestingContracts + "OtherModsObjectsParts.cs", RestingContracts + "OtherModsObjectsParts.cs:OtherModsObjectsParts",
            RestingContracts + "OtherModsObjectsParts.cs:OtherModsObjectsParts.HeldUpTargets", RestingContracts + "OtherModsObjectsParts.cs:OtherModsObjectsParts.LeftWithoutSupport",
            RestingContracts + "OtherModsObjectsParts.cs:OtherModsObjectsParts.Owner",
            "Steps/RemoveObjectsRestingOnRemovedOnes.Contracts/RestingObjectsParts.cs", "Steps/RemoveObjectsRestingOnRemovedOnes.Contracts/RestingObjectsParts.cs:RestingObjectsParts",
            "Steps/RemoveObjectsRestingOnRemovedOnes.Contracts/RestingObjectsParts.cs:RestingObjectsParts.AlsoRemove",
            "Steps/RemoveObjectsRestingOnRemovedOnes.Contracts/RestingObjectsParts.cs:RestingObjectsParts.Owner",
            "Steps/RemoveObjectsRestingOnRemovedOnes.Contracts/RestingObjectsParts.cs:RestingObjectsParts.TouchChains"])] = PartKeyHolders,
        [CopyFamily.KeyOf([
            Report + "Tables.Removals.cs:Tables.BuildRemovalsTable", Report + "Tables.Removals.cs:Tables.RemovalsHeader",
            Report + "Tables.Support.cs:Tables.BuildSupportTable", Report + "Tables.Support.cs:Tables.SupportHeader"])] = CsvColumnNames,
        [CopyFamily.KeyOf([
            "Steps/FindTargetObjectsToKeep/ObjectsToKeep.cs:ObjectsToKeep.FindOwnReason",
            "Steps/SelectObjectsThatCanCauseRemovals/RemovalCauseSelector.cs:RemovalCauseSelector.MeasureCountingFailuresAsInvisible"])] = ErrorBoundary,
        [CopyFamily.KeyOf([
            TooCloseContracts + "NpcWork.cs:NpcWork", TooCloseContracts + "NpcWork.cs:NpcWork.Zero", TooCloseContracts + "NpcWork.cs:NpcWork.operator",
            TooCloseContracts + "ShapeZoneStats.cs:ShapeZoneWork", TooCloseContracts + "ShapeZoneStats.cs:ShapeZoneWork.Zero", TooCloseContracts + "ShapeZoneStats.cs:ShapeZoneWork.operator",
            TooCloseContracts + "TooCloseWork.cs:TooCloseWork", TooCloseContracts + "TooCloseWork.cs:TooCloseWork.Zero", TooCloseContracts + "TooCloseWork.cs:TooCloseWork.operator"])] = CounterRecord,
        [CopyFamily.KeyOf([
            TooClose + "NpcsThatCanSpawn.cs:NpcsThatCanSpawn.BodiesOf", TooClose + "NpcsThatCanSpawn.cs:NpcsThatCanSpawn.MeasureBodiesIn",
            TooClose + "NpcsThatCanSpawn.cs:NpcsThatCanSpawn.NpcOf", TooClose + "NpcsThatCanSpawn.cs:NpcsThatCanSpawn.Overlapping",
            TooClose + "NpcsThatCanSpawn.cs:NpcsThatCanSpawn.TransformOf", TooClose + "NpcsThatCanSpawn.cs:NpcsThatCanSpawn.WorldBoxOf"])] = ViewForwarders,
    };

    [Fact]
    public void NoBusinessCodeIsCopiedExceptTheListed()
    {
        var files = BusinessCode.SourceFiles().Select(file => (file.Path, File.ReadAllText(file.FullPath))).ToList();

        var families = CodeCopies.Find(files, WindowLength);

        var unlisted = families.Where(family => !Allowed.ContainsKey(family.Key)).Select(family => family.ToString()).ToList();
        var stale = Allowed.Keys.Except(families.Select(family => family.Key)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Copied code (reuse a tool or share one method, or list its places with a reason):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        AllowListAssert.NoneStale(stale, "Listed but no longer copied");
    }

    [Fact]
    public void TheScanFindsACopyWithRenamedNamesAndOtherComments()
    {
        const string first = """
            // one
            int Total(List<int> values)
            {
                var sum = 0;
                foreach (var value in values) { if (value > 3) sum += value * 2; }
                return sum + 7;
            }
            """;
        const string second = """
            void Other() { }
            /* two */
            int Count(List<int> numbers)
            {
                var result = 0;
                foreach (var number in numbers) { if (number > 9) result += number * 5; } // note
                return result + 1;
            }
            """;

        var family = Assert.Single(CodeCopies.Find([("a", first), ("b", second)], windowLength: 20));

        var runs = family.Runs;
        Assert.Equal(["a", "b"], runs.Select(run => run.File));
        Assert.Equal((2, 6), (runs[0].FirstLine, runs[0].LastLine));
        Assert.Equal((3, 7), (runs[1].FirstLine, runs[1].LastLine));
        Assert.Equal("a:Total | b:Count", family.Key);
    }

    private const string TwoMemberCopyFirst = """
        class A
        {
            void X()
            {
                Run(a);
                if (c > 2) Stop(c);
            }
            void Y()
            {
                foreach (var d in c) Use(d);
                Finish(a);
            }
        }
        """;

    private const string TwoMemberCopySecond = """
        class B
        {
            void P()
            {
                Other(a, b);
                if (c > 2) Stop(c);
            }
            void Q()
            {
                foreach (var d in c) Use(d);
                Done(a, b, d);
            }
        }
        """;

    [Fact]
    public void ACopyThatSpansTwoMembersIsPlacedInBoth()
    {
        var family = Assert.Single(CodeCopies.Find([("a", TwoMemberCopyFirst), ("b", TwoMemberCopySecond)], windowLength: 12));

        Assert.Equal("a:A.X | a:A.Y | b:B.P | b:B.Q", family.Key);
    }

    [Fact]
    public void TheKeyOfACopyStaysTheSameWhenOtherCodeNextToItChanges()
    {
        const string changed = """
            class A
            {
                int Unrelated() => 5;
                void X()
                {
                    Run(a);
                    Extra(c);
                    if (c > 2) Stop(c);
                }
                void Y()
                {
                    foreach (var d in c) Use(d);
                    Finish(a);
                    Finish(b);
                }
            }
            """;

        var family = Assert.Single(CodeCopies.Find([("a", changed), ("b", TwoMemberCopySecond)], windowLength: 12));

        Assert.Equal("a:A.X | a:A.Y | b:B.P | b:B.Q", family.Key);
    }

    [Fact]
    public void TheOutlineNamesMembersTypesAndCodeOutsideThem()
    {
        const string source = """
            namespace N;
            class Outer
            {
                int field = 1;
                public Outer() { }
                int Prop { get; set; } = 3;
                int Expr(int x) => x + 1;
                void Method<T>(T a) where T : class
                {
                    var f = () => { return 1; };
                }
                class Inner { void Deep() { } }
            }
            record Point(int X, int Y);
            """;

        var outline = CodeOutline.Of(source);

        Assert.Empty(outline.PlacesOfLine(1));
        Assert.Equal(["Outer"], outline.PlacesOfLine(2));
        Assert.Equal(["Outer.field"], outline.PlacesOfLine(4));
        Assert.Equal(["Outer.Outer"], outline.PlacesOfLine(5));
        Assert.Equal(["Outer.Prop"], outline.PlacesOfLine(6));
        Assert.Equal(["Outer.Expr"], outline.PlacesOfLine(7));
        Assert.Equal(["Outer.Method"], outline.PlacesOfLine(8));
        Assert.Equal(["Outer.Method"], outline.PlacesOfLine(10));
        Assert.Equal(["Outer.Inner.Deep"], outline.PlacesOfLine(12));
        Assert.Equal(["Point"], outline.PlacesOfLine(14));
    }

    [Fact]
    public void TheScanFindsACopyInsideOneFileButNotOverlappingRepetition()
    {
        const string copiedTwice = "int A(int x) { return x + 1 * 2 - 3; } int B(int y) { return y + 1 * 2 - 3; }";
        const string repeatingPattern = "a(); a(); a(); a();";

        Assert.Equal(2, Assert.Single(CodeCopies.Find([("a", copiedTwice)], windowLength: 10)).Runs.Count);
        Assert.Empty(CodeCopies.Find([("a", repeatingPattern)], windowLength: 10));
    }

    [Fact]
    public void TheScanIgnoresDifferentCodeAndCodeShorterThanTheWindow()
    {
        var runs = CodeCopies.Find([("a", "int A(int x) { return x + 1; }"), ("b", "int B(int x) { return x - 1; }")], windowLength: 20);

        Assert.Empty(runs);
    }

    [Fact]
    public void TheTokenizerKeepsEachLiteralAsOneTokenAndNormalisesNames()
    {
        const string source = """"
            var a = "x { y " + @"p ""q"" r" + $"v {Get("}")} w" + """raw "quote" """ + 'c' + '\'' + 12.5f; // tail
            /* block
               comment */
            """";

        var tokens = CodeTokens.Tokenize(source).Select(token => token.Text);

        Assert.Equal(
            ["var", "name", "=", "text", "+", "text", "+", "text", "+", "text", "+", "text", "+", "text", "+", "number", ";"],
            tokens);
    }

    [Fact]
    public void TheTokenizerKeepsTheLineOfEachTokenAcrossCommentsAndMultiLineStrings()
    {
        const string source = "a /* x\ny */ b\n@\"p\nq\" c";

        var lines = CodeTokens.Tokenize(source).Select(token => token.Line);

        Assert.Equal([1, 2, 3, 4], lines);
    }

    [Fact]
    public void TheScanSkipsLinesOfBracesUsingsAndAttributes()
    {
        const string source = """
            using System;
            using var file = Open();
            [Fact]
            {
            }
            return 1;
            """;

        var lines = CodeTokens.Significant(source).Select(token => token.Line).Distinct();

        Assert.Equal([2, 6], lines);
    }
}
