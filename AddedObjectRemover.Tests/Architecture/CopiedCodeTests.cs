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
    private const string SectionForwarders = "One-line members of the log and reports object, each handing one step's result to that step's section builder; no logic to share.";
    private const string ErrorBoundary ="Per-object error boundary of two steps: each catches the failure of its own kind of object under its own part key and recovers in its own way; they share only the shape.";

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
    private const string LogAndReports = Report + "LogAndReports.cs:LogAndReports.";

    /// <summary>Each copy by the places it occurs in (see <see cref="CopyFamily.Key"/>), with the reason it is not shared.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        [CopyFamily.KeyOf([Shapes + "CountBounds", TooClose + "TooCloseObjects.cs:TooCloseObjects.CountSizes"])] = CountLines + " " + CounterRecord,
        [CopyFamily.KeyOf([Settings + "ClampMarginInUnits", Settings + "CreateChangedTerrainSettings", Settings + "CreateTooCloseSettings", Settings + "WarnOfUnlistableNeverTrusted"])] = SettingChecks,
        [CopyFamily.KeyOf([Settings + "CreateChangedTerrainSettings", Settings + "CreateTooCloseSettings"])] = SettingChecks,
        [CopyFamily.KeyOf([Settings + "CreateAlsoRemoveSettings", Settings + "CreateChangedTerrainSettings"])] = SettingChecks,
        [CopyFamily.KeyOf([
            Settings + "AnchoringThresholdName", Settings + "Build", Settings + "CleanedModName", Settings + "FollowUpModeName",
            Settings + "HeightChangeToleranceName", Settings + "MaxOtherMastersName", Settings + "MinPercent", Settings + "NeverTrustedModsName",
            Settings + "NpcsName", Settings + "OccupiedDirectionsName", Settings + "OwnChangesModsName", Settings + "SearchRadiusName",
            Settings + "SurroundingObjectsGroup", Settings + "TerrainCheckedObjectsName",
            Settings + "TerrainDistanceMarginName", Settings + "TerrainSizeMarginName", Settings + "TerrainTypeChangePercentName",
            Settings + "TooCloseDistanceMarginName", Settings + "TooCloseSizeMarginName", Settings + "TouchDistanceName", Settings + "TrustedModsName",
            Settings + "WaterCoverChangePercentName", Settings + "WaterHeightChangeToleranceName", KeepText + "Category", KeepText + "CheckFailed", KeepText + "LinkedGroupCategory",
            KeepText + "LocationRoleCategory", KeepText + "OwnLocationRefType", KeepText + "PlacedCategoryPrefix", KeepText + "ScriptCategory",
            KeepText + "TeleportDoor"])] = TextConstants,
        [CopyFamily.KeyOf([Describe + "HintType", Describe + "PointReason", Describe + "RemovalReason", Describe + "TimedSuffix"])] = EnumTextArms,
        [CopyFamily.KeyOf([
            Describe + "HintType", Describe + "TimedSuffix",
            LeftBehindText + "Describe", LeftBehindText + "DescribeReason", MeshText + "FailureCategory", MeshText + "ReadFailure"])] = EnumTextArms,
        [CopyFamily.KeyOf([
            Describe + "HintType", Describe + "LeftBehindReason", KeepText + "Relation", LeftBehindText + "Describe",
            MeshText + "FailureCategory", MeshText + "Line", PartText + "LinkedStep", PartText + "Word"])] = EnumTextArms,
        [CopyFamily.KeyOf([KeepText + "Relation", PartText + "LinkedStep"])] = EnumTextArms,
        [CopyFamily.KeyOf([MeshText + "FailureCategory", MeshText + "Line", MeshText + "ReadFailure"])] = EnumTextArms,
        [CopyFamily.KeyOf([PartText + "Wordings"])] = PartKeyLines,
        [CopyFamily.KeyOf([LogAndReports + "BuildArchiveSections", LogAndReports + "BuildIndexSections", LogAndReports + "BuildStepSections"])] = SectionForwarders,
        [CopyFamily.KeyOf([
            Report + "Tables.LeftBehind.cs", Report + "Tables.LeftBehind.cs:Tables", Report + "Tables.LeftBehind.cs:Tables.LeftBehindHeader",
            Report + "Tables.LeftBehind.cs:Tables.MoveColumnCount", Report + "Tables.Support.cs", Report + "Tables.Support.cs:Tables",
            Report + "Tables.Support.cs:Tables.MaxListedSupporters", Report + "Tables.Support.cs:Tables.SupportHeader",
            Report + "Tables.Touch.cs:Tables.ComponentHeader", Report + "Tables.Touch.cs:Tables.EdgeHeader", Report + "Tables.Touch.cs:Tables.MaxTopBases"])] = TableDeclarations + " " + CsvColumnNames,
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
