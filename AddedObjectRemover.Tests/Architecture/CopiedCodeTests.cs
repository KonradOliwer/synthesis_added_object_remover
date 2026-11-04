namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// No production code (every file of every production project: steps, caches, tools, the runner and the entry) is copied: no 40
/// consecutive tokens of one member (names and numbers normalised, comments and lists ignored, names renamed one to one) appear
/// twice; <see cref="CodeCopies"/> says what counts as a copy. Limit: shorter copies that the known-idiom list does not name are
/// not found. Limit: code that repeats one pattern per axis, item or switch arm, written one per line or side by side on one line, is
/// not compared, so a copied per-axis test or transform is not found. Repetition that is structure rather than logic is listed with its reason, by the members it occurs in, which the
/// failure message prints with the lines of each copy.
/// </summary>
public class CopiedCodeTests
{
    private const int WindowLength = 40;

    private const string CsvColumnNames = "CSV header column names; each table lists its own columns.";
    private const string SettingChecks = "Setting-by-setting check lines; each reads another setting with its own default, limit and wording.";
    private const string PartKeyLines = "One entry per part key in the wording table; each entry names its own part and words its own consequence, so there is no logic to share.";

    private const string ParallelCalls = "One call of the order-independent parallel map over a list with per-worker buffers; each asks its own question, so there is no logic to share.";

    private const string CollisionBuilder = "Run/RunSettingsValidation/CollisionSettingsBuilder.cs:CollisionSettingsBuilder.";
    private const string Report = "Steps/BuildLogAndReports/";
    private const string Resting = "Steps/RemoveObjectsRestingOnRemovedOnes/";
    private const string OtherMods = "Steps/OtherModsObjectsLeftWithoutSupport/";

    private const string RoundAddingEntries =
        "Entries of the deciding steps: each runs its step and returns its own result with the run's decisions after its own kind of "
        + "addition (a round, rounds or moves). A shared helper would only wrap the constructor of Decided around that one addition, "
        + "so they share only this two-line shape.";

    private const string Runner = "Run/RunAllSteps/StepRunner.Decide.cs:StepRunner.";
    private const string StepCalls =
        "Timed calls of two different steps: each builds its own step's input from its own earlier results and settings. They repeat the "
        + "Log.Timed call around them and the report lambda that prints the step's sections; that lambda cannot be one method, because "
        + "BuildStepSections is overloaded per result type and each lambda picks its own overload.";
    private const string ParallelMapFile = "Tools/OrderIndependentParallelWork/ParallelMap.cs:ParallelMap.";
    private const string ParallelMapOverloads =
        "Overloads of the parallel map: each takes its own item order (index order, a permutation, a work order or a subset) and its own mix "
        + "of per-worker buffers and summed work, and checks or maps the order its own way before the one shared core; they share only the "
        + "parameter list, which C# cannot share between methods.";
    private const string Records = "Tools/PluginRecordReadingAndWriting/";

    private const string RestoreCountFacts =
        "The facts of a count line about what the restore step judged: each counts its own kind of change, tests its own rule and words its own count, so there is no logic to share.";

    private const string RestoreOutcomesType = Report + "RestoreOutcomes.cs:RestoreOutcomes.";

    private const string KeptReasons =
        "Why each kind of change the restore step judged stays: each reads its own judged record's never-rule, guard and area; they share only the opening "
        + "\"restored: none, never-rule: its words\" and the label parameter.";

    /// <summary>Each copy by the places it occurs in (see <see cref="CopyFamily.Key"/>), with the reason it is not shared.</summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        [CopyFamily.KeyOf([
            "Steps/FitClashingObjects/ClashingObjectFitter.cs:ClashingObjectFitter.Fit", "Steps/MoveKeptMarkers/MarkerMovePlanner.cs:MarkerMovePlanner.Plan",
            "Steps/RemoveObjectsInClearedAreas/ObjectsInClearedAreas.cs:ObjectsInClearedAreas.Find",
            "Steps/RemoveVisibleObjectsLeftBehind/VisibleLeftBehindObjects.cs:VisibleLeftBehindObjects.Find"])] = RoundAddingEntries,
        [CopyFamily.KeyOf([
            OtherMods + "OtherModsObjects.cs:OtherModsObjects.Settle", "Steps/RemoveInvisibleObjectsLeftBehind/InvisibleLeftBehindObjects.cs:InvisibleLeftBehindObjects.Find"])] = RoundAddingEntries,
        [CopyFamily.KeyOf([CollisionBuilder + "CreateChangedTerrainSettings", CollisionBuilder + "CreateTooCloseSettings"])] = SettingChecks,
        [CopyFamily.KeyOf([CollisionBuilder + "CreateChangedTerrainSettings"])] = SettingChecks,
        [CopyFamily.KeyOf([Report + "RestoreCountLines.cs:RestoreCountLines.LeftToLaterPlugins"])] = RestoreCountFacts,
        [CopyFamily.KeyOf([Report + "RestoreCountLines.cs:RestoreCountLines.Restored"])] = RestoreCountFacts,
        [CopyFamily.KeyOf([RestoreOutcomesType + "KeptBecause"])] = KeptReasons,
        [CopyFamily.KeyOf([
            Report + "Tables.Fits.cs:Tables.FitsHeader", Report + "Tables.Support.cs:Tables.SupportHeader", Report + "Tables.Touch.cs:Tables.ComponentHeader",
            Report + "Tables.Touch.cs:Tables.EdgeHeader"])] = CsvColumnNames,
        [CopyFamily.KeyOf([Report + "Tables.Support.cs:Tables.SupportHeader", Report + "Tables.Touch.cs:Tables.ComponentHeader"])] = CsvColumnNames,
        [CopyFamily.KeyOf([Report + "RunPartText.cs:RunPartText.Wordings"])] = PartKeyLines,
        [CopyFamily.KeyOf(["Steps/MoveKeptMarkers/KeptMarkerMover.cs:KeptMarkerMover.Move", OtherMods + "TargetsHoldingUpOtherModsObjects.cs:TargetsHoldingUpOtherModsObjects.Find"])] =
            ParallelCalls,
        [CopyFamily.KeyOf([Resting + "SupportContactFinder.cs:SupportContactFinder.FindContactsOfAll", OtherMods + "TargetsHoldingUpOtherModsObjects.cs:TargetsHoldingUpOtherModsObjects.Find"])] =
            ParallelCalls,
        [CopyFamily.KeyOf([Runner + "RunChangedTerrain", Runner + "RunTooClose"])] = StepCalls,
        [CopyFamily.KeyOf([Runner + "RunAlsoRemove", Runner + "RunObjectsInClearedAreas", Runner + "RunVisibleLeftBehind"])] = StepCalls,
        [CopyFamily.KeyOf([Runner + "RunInvisibleLeftBehind", Runner + "RunObjectsInClearedAreas", Runner + "RunVisibleLeftBehind"])] = StepCalls,
        [CopyFamily.KeyOf([ParallelMapFile + "Run", ParallelMapFile + "RunOver"])] = ParallelMapOverloads,
        [CopyFamily.KeyOf([ParallelMapFile + "Run"])] = ParallelMapOverloads,
        [CopyFamily.KeyOf([Records + "CellRecordParts.cs:CellRecordParts.FieldParts"])] =
            "The table of the cell's whole-field parts: each entry pairs its own part with its own fields, so neighbouring entries of one shape repeat "
            + "and there is no logic to share.",
        [CopyFamily.KeyOf([Records + "CellRecordParts.cs:CellRecordParts.CopyFields", Records + "LandRecordParts.cs:LandRecordParts.CopyFields"])] =
            "A masked copy into a cell and into a landscape: Mutagen generates DeepCopyIn per record type, on internal interfaces that share no "
            + "base, so each type needs its own one-line call.",
        [CopyFamily.KeyOf(["Tools/IndexGraphQueries/AdjacencyPairs.cs:AdjacencyPairs.Collect", "Tools/IndexGraphQueries/AdjacencyPairs.cs:AdjacencyPairs.From"])] =
            "From is Collect kept to each pair's smaller end; the two share only their parameter list, which C# cannot share between methods.",
    };

    [Fact]
    public void NoProductionCodeIsCopiedExceptTheListed()
    {
        var files = SourceFile.All().Select(file => (file.Path, File.ReadAllText(file.FullPath))).ToList();

        var families = CodeCopies.Find(files, WindowLength);

        var unlisted = families.Where(family => !Allowed.ContainsKey(family.Key)).Select(family => family.ToString()).ToList();
        var stale = Allowed.Keys.Except(families.Select(family => family.Key)).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Copied code (reuse a tool or share one method, or list its places with a reason):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        AllowListAssert.NoneStale(stale, "Listed but no longer copied");
    }

    [Fact]
    public void TheScanFindsAPlantedCopyButNotItsLookAlikes()
    {
        const string copied = """
            class Prices
            {
                decimal Total(IReadOnlyList<Item> items, decimal rate)
                {
                    var sum = 0m;
                    foreach (var item in items) { if (item.Count > 0) sum += item.Price * item.Count; }
                    return Math.Round(sum * rate, 2);
                }
            }
            """;
        const string copy = """
            class Orders
            {
                decimal Charge(IReadOnlyList<Line> lines, decimal tax)
                {
                    var amount = 0m;
                    foreach (var line in lines) { if (line.Count > 0) amount += line.Price * line.Count; }
                    return Math.Round(amount * tax, 2);
                }
            }
            """;
        const string propertiesAndArms = """
            class Window
            {
                public int Width { get; set; } = 640;
                public int Height { get; set; } = 480;
                public bool Resizable { get; set; } = true;
                public string Title { get; set; } = "Main";
                string Describe(Side side) => side switch
                {
                    Side.Left => "left",
                    Side.Right => "right",
                    Side.Top => "top",
                    _ => throw new UnreachableException(),
                };
            }
            """;
        const string otherPropertiesAndArms = """
            class Sound
            {
                public int Volume { get; set; } = 80;
                public int Balance { get; set; } = 0;
                public bool Muted { get; set; } = false;
                public string Device { get; set; } = "Default";
                string Describe(Level level) => level switch
                {
                    Level.Low => "low",
                    Level.Middle => "middle",
                    Level.High => "high",
                    _ => throw new UnreachableException(),
                };
            }
            """;
        const string area = """
            class Rectangle
            {
                int Margin(Box box) { return box.Width * box.Height - box.Width; }
            }
            """;
        const string crossedArea = """
            class Square
            {
                int Margin(Box box) { return box.Height * box.Width - box.Width; }
            }
            """;

        var families = CodeCopies.Find(
            [("a", copied), ("b", copy), ("c", propertiesAndArms), ("d", otherPropertiesAndArms), ("e", area), ("f", crossedArea)], windowLength: 20);

        Assert.Equal("a:Prices.Total | b:Orders.Charge", Assert.Single(families).Key);
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

    [Fact]
    public void AStretchAcrossTwoMembersIsNoCopy()
    {
        const string first = """
            class A
            {
                void X()
                {
                    Prepare(first);
                    if (count > 2) Stop(count);
                }
                void Y()
                {
                    foreach (var item in items) Use(item);
                    Finish();
                }
            }
            """;
        const string second = """
            class B
            {
                void P()
                {
                    return;
                    if (count > 2) Stop(count);
                }
                void Q()
                {
                    foreach (var item in items) Use(item);
                    return;
                }
            }
            """;

        Assert.Empty(CodeCopies.Find([("a", first), ("b", second)], windowLength: 20));
    }

    private const string CopyInOneMember = """
        class B
        {
            void P()
            {
                Other(a, b);
                foreach (var item in items) { if (item > 2) Stop(item); }
                Done(a, b);
            }
        }
        """;

    [Fact]
    public void TheKeyOfACopyStaysTheSameWhenOtherCodeNextToItChanges()
    {
        const string original = """
            class A
            {
                void P()
                {
                    Begin(x);
                    foreach (var entry in entries) { if (entry > 5) Stop(entry); }
                    End(x);
                }
            }
            """;
        const string changed = """
            class A
            {
                int Unrelated() => 5;
                void P()
                {
                    Begin(x, y, z);
                    foreach (var entry in entries) { if (entry > 5) Stop(entry); }
                    End(x, y, z);
                }
            }
            """;

        var before = Assert.Single(CodeCopies.Find([("a", original), ("b", CopyInOneMember)], windowLength: 12));
        var after = Assert.Single(CodeCopies.Find([("a", changed), ("b", CopyInOneMember)], windowLength: 12));

        Assert.Equal("a:A.P | b:B.P", before.Key);
        Assert.Equal(before.Key, after.Key);
    }

    [Fact]
    public void TheOutlineNamesTheMemberOfEachLineAndNoneOutsideMembers()
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

        Assert.Null(outline.OnlyMemberOfLine(1));
        Assert.Null(outline.OnlyMemberOfLine(2));
        Assert.Equal("Outer.field", outline.OnlyMemberOfLine(4)?.Place);
        Assert.Equal("Outer.Outer", outline.OnlyMemberOfLine(5)?.Place);
        Assert.Equal("Outer.Prop", outline.OnlyMemberOfLine(6)?.Place);
        Assert.Equal("Outer.Expr", outline.OnlyMemberOfLine(7)?.Place);
        Assert.Equal("Outer.Method", outline.OnlyMemberOfLine(8)?.Place);
        Assert.Equal("Outer.Method", outline.OnlyMemberOfLine(10)?.Place);
        Assert.Equal("Outer.Inner.Deep", outline.OnlyMemberOfLine(12)?.Place);
        Assert.Null(outline.OnlyMemberOfLine(14));
    }

    [Fact]
    public void TheOutlineNamesEachMemberByItsOwnNameWhateverTypeOrTupleItsHeaderHolds()
    {
        const string source = """
            class Shapes
            {
                ImmutableArray<(int A, int B)> Table = [];
                (int A, int B) Pair() => (1, 2);
                Dictionary<string, (int, int)> Map { get; } = new();
                List<(int, int)> Pairs<T>(T value) => [];
                int this[int index] => index;
                int IShape.Area => 1;
                static Shapes operator +(Shapes a, Shapes b) => a;
                static Shapes operator -(Shapes a, Shapes b) => a;
                static bool operator <(Shapes a, Shapes b) => true;
                static implicit operator int(Shapes a) => 1;
            }
            """;

        const int firstMemberLine = 3;
        string[] members =
            ["Shapes.Table", "Shapes.Pair", "Shapes.Map", "Shapes.Pairs", "Shapes.this", "Shapes.Area", "Shapes.operator +", "Shapes.operator -", "Shapes.operator <", "Shapes.operator int"];

        var outline = CodeOutline.Of(source);

        Assert.Equal(members, Enumerable.Range(firstMemberLine, members.Length).Select(line => outline.OnlyMemberOfLine(line)?.Place));
    }

    [Fact]
    public void TheOutlineNamesNoMemberForALineOfTwoMembers()
    {
        var outline = CodeOutline.Of("class C\n{\n    int A; int B;\n}");

        Assert.Null(outline.OnlyMemberOfLine(3));
    }

    [Fact]
    public void TheScanFindsACopyInsideOneFileButNotOverlappingRepetition()
    {
        const string copiedTwice = """
            class C
            {
                int A(int x) { return x + 1 * 2 - 3; }
                int B(int y) { return y + 1 * 2 - 3; }
            }
            """;
        const string repeatingPattern = """
            class C
            {
                void A() { a(); a(); a(); a(); }
            }
            """;

        Assert.Equal(2, Assert.Single(CodeCopies.Find([("a", copiedTwice)], windowLength: 10)).Runs.Count);
        Assert.Empty(CodeCopies.Find([("a", repeatingPattern)], windowLength: 10));
    }

    private const int TermWindowLength = 24;

    /// <summary>
    /// A member of one line that is written as an arrow in one file and as a block in the other, so only the stretch from "new" on is the same.
    /// Each window of that stretch is a copy unless the repetition rule skips it.
    /// </summary>
    private static IReadOnlyList<CopyFamily> FindTermsCopiedInArrowAndBlockMembers(string terms)
    {
        var arrow = $"class Product\n{{\n    Vector3 Scale(Vector3 a, Vector3 b) => new Vector3({terms});\n}}";
        var block = $"class Order\n{{\n    Vector3 Scale(Vector3 a, Vector3 b) {{ return new Vector3({terms}); }}\n}}";
        return CodeCopies.Find([("a", arrow), ("b", block)], TermWindowLength);
    }

    [Fact]
    public void TheScanSkipsACopyWhoseSharedStretchIsOnePatternRepeatedPerAxisOnOneLine()
    {
        Assert.Empty(FindTermsCopiedInArrowAndBlockMembers("a.X * b.X, a.Y * b.Y, a.Z * b.Z"));
    }

    [Fact]
    public void TheScanFindsACopyWhoseSharedStretchRepeatsNoPattern()
    {
        var family = Assert.Single(FindTermsCopiedInArrowAndBlockMembers("a.X * b.X, a.Y + 1, Math.Abs(a.Z)"));

        Assert.Equal("a:Product.Scale | b:Order.Scale", family.Key);
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
