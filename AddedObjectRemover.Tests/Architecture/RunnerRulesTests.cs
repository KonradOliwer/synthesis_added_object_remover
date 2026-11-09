using System.Reflection;
using System.Runtime.CompilerServices;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The runner only wires: it hands each step its own settings record and decides nothing about the domain. Three rules:
/// the runner and the run caches read no member of a settings record (found in their compiled code) and hold no error boundary, and the runner has no condition (comparison, negation or loop included) of its own. A new settings
/// read, condition or error boundary fails here. It calls every listed rule provider.
/// It also carries nothing from one run to the next but the restored parts, so chained Standard runs equal one Batch run: the
/// state that crosses runs is what the patch writing writes into the patch, and the list of restored parts, which a chain hands on
/// in the restored parts file. The runner holds no field, its loop over the requests hands each run only its request, the
/// environment and the restored parts known so far, every member of the environment is listed with why no run changes it,
/// nothing the environment shares holds what a run opens, and each run's link cache is built when that run opens its plugin
/// records, so it is dropped with them.
/// </summary>
public class RunnerRulesTests
{
    internal const string RunnerFolder = "Run/RunAllSteps";

    internal const string RunCachesFolder = "Caches/RunCaches";

    /// <summary>"Owner.Method: Type.Member" of a settings member the runner or a run cache reads, and why it may read it.</summary>
    private static readonly Dictionary<string, string> AllowedSettingsReads = new()
    {
        ["StepRunner.CreateFitGroups: ObjectsJudgedWith<TooCloseSettings>.Settings"] =
            "The fit step takes the too-close rule built with the settings of its group; the step builds the rule.",
        ["StepRunner.CreateFitGroups: ObjectsJudgedWith<ChangedTerrainSettings>.Settings"] =
            "The fit step takes the changed-terrain rule built with the settings of its group; the step builds the rule.",
        ["StepRunner.CreateOldPlaceTooCloseRule: ChangedObjectCollision.TooClose"] =
            "The old-place check takes the too-close rule built with the too-close settings of the changed objects; the step builds the rule.",
        ["StepRunner.CreateOldPlaceChangedTerrainRule: ChangedObjectCollision.ChangedTerrain"] =
            "The old-place check takes the changed-terrain rule built with the changed-terrain settings of the changed objects; the step builds the rule.",
        ["StepRunner.CreateCleanedModsObjectRules: CollisionSettings.TooClose"] =
            "The restore judges a planned copy with the too-close settings of the cleaned mod's own objects; the step builds the rule.",
        ["StepRunner.CreateCleanedModsObjectRules: CollisionSettings.ChangedTerrain"] =
            "The restore takes the changed-terrain rule built with the changed-terrain settings of the cleaned mod's own objects; the step builds the rule.",
        ["StepRunner.CreateRestoreInput: WhereRemovedRestoreSettings.ClearedArea"] =
            "The restore takes the cleared-area rule built with its own cleared-area settings; the step for objects in cleared areas builds the rule.",
    };

    /// <summary>"file: Type.Member", how many lines of that member hold a condition, and why it is decided there.</summary>
    private static readonly Dictionary<string, (int Count, string Reason)> AllowedConditions = new()
    {
        ["StepRunner.cs: StepRunner.PatchLoadOrder"] = (1, "The settings validation may have ended the run before any step; the runner passes its stop on."),
        ["StepRunner.cs: StepRunner.MarkPatchWhenARunWillWrite"] =
            (1, "Run policy: the patch gets its marker only when some run of the batch will write; the marker's text and merging belong to the plugin writer."),
        ["StepRunner.cs: StepRunner.CollectDecideAndWrite"] =
            (1, "The collect-placed-objects step returns the stop for a run without target objects; the runner ends the run on any stop a step returns."),
        ["StepRunner.cs: StepRunner.PatchOneRun"] = (1, "The batch runs its requests one after the other; each run gets only its request and the environment."),
        ["StepRunner.cs: StepRunner.Print"] = (1, "Prints the sections a step's builder returned, in the order returned; it decides nothing."),
    };

    /// <summary>"file: use" of an error boundary in the runner or the run caches, and why it is there.</summary>
    private static readonly Dictionary<string, string> AllowedErrorBoundaries = new()
    {
        ["ResidentMeshTrees.cs: try"] = "Releases the build slot of a large mesh when its build throws; the exception passes on.",
    };

    private const string StepRunnerFile = "Run/RunAllSteps/StepRunner.cs";

    private const string LinkCacheFactoryFile = "Tools/PluginRecordReadingAndWriting/PluginRecordsFactory.cs";

    private const BindingFlags DeclaredFields =
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>
    /// "RunEnvironment member": why no run changes what it holds for the runs after it. Every run of a batch gets the same
    /// environment; a new member fails here until it is listed with its reason.
    /// </summary>
    private static readonly Dictionary<string, string> SharedBetweenRuns = new()
    {
        ["Plugins"] =
            "The plugin list facts, read once before the first run; no step changes them, and the patch's own header, which runs change, is not read into them.",
        ["PluginRecords"] = "Opens a new reader for each run, with a link cache of its own.",
        ["PatchWriting"] = "The one writer: what it writes into the patch is what later runs read, as they read any plugin.",
        ["MeshFiles"] = "Opens a new mesh reader for each run.",
        ["Bases"] = "Opens a new base reader for each run, over that run's link cache.",
        ["Log"] = "Only prints; no run reads it back.",
        ["ReportOutput"] = "Writes each run's report files into that run's folder; no run reads them back.",
        ["TimeSinceStart"] = "Reads the clock; the times go into log lines only.",
        ["RestoredPartsFile"] = "Read once before the first run and written once after the last; no run reads or writes it.",
    };

    /// <summary>What a run opens for itself and drops when it ends; nothing the environment shares may hold one.</summary>
    private static readonly Type[] OpenedPerRun = [typeof(IPluginRecords), typeof(IRecordIndex), typeof(IBaseFacts), typeof(IMeshFiles), typeof(ILinkCache)];

    /// <summary>
    /// The names the loop over the requests may use besides its own request: the environment, the results it collects, and the
    /// restored parts each run hands to the next.
    /// </summary>
    private static readonly string[] RequestLoopNames = ["Add", "PatchOneRun", "environment", "results", "ended", "Result", "Restored", "restored"];

    private const string RequestsName = "requests";

    private const string PartKeyName = "PartKey";

    private const string PartFailureName = "PartFailure";

    private const string GuardName = "Guard";

    private const string FailuresName = "Failures";

    private const string TryUse = "try";

    private const string CatchUse = "catch";

    private const string ImmutableLinkCacheBuild = "ToImmutableLinkCache";

    private const string MutableLinkCacheBuild = "ToMutableLinkCache";

    private const string LinkCacheOpener = "Open";

    private static readonly string[] ConditionCalls = ["Where", "Any", "All", "First", "FirstOrDefault", "Count", "TakeWhile"];

    private static readonly HashSet<SyntaxKind> ConditionBinaryKinds =
    [
        SyntaxKind.CoalesceExpression, SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression, SyntaxKind.EqualsExpression,
        SyntaxKind.NotEqualsExpression, SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanExpression,
        SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.IsExpression,
    ];

    [Fact]
    public void TheRunnerReadsNoMemberOfAnySettingsRecord()
    {
        var records = SettingsRecordTypes();
        var found = MemberUses.In(RunnerAssemblies().SelectMany(assembly => assembly.GetTypes()))
            .Select(use => SettingsReadOf(use, records))
            .OfType<string>()
            .ToHashSet();

        AssertOnlyAllowed(found, AllowedSettingsReads.Keys, "Member read on the settings in the runner; the step reads its own settings", nameof(AllowedSettingsReads));
    }

    [Fact]
    public void TheMemberUseReaderSeesPropertyAndFieldReadsInLambdasLocalFunctionsIteratorsAndGenericTypesButNotCapturesOwnMembersOrCompilerWrittenCode()
    {
        var samples = typeof(MemberUseSamples);
        var owner = $"{BusinessCode.PathName(samples)}.";

        var uses = MemberUses.In([samples, .. samples.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)])
            .Select(use => $"{use.Method[owner.Length..]}: {use.Member.Name}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "Direct: Value", "FieldRead: Total", "Generic: Content", "InClosure: Value", "InIterator: Value", "InLocalFunction: Value", "InNestedLambda: Value",
                "InStaticLambda: Value", "NullConditional: Next", "NullConditional: Value", "Reader.Read: Content", "StaticFieldRead: Shared",
            ],
            uses);
    }

    /// <summary>Reads through each form the compiler lowers differently, beside records whose generated members read their own fields.</summary>
    private static class MemberUseSamples
    {
        public sealed record Holder(Holder? Next, int Value)
        {
            public int Twice => Value * 2;
        }

        public sealed record Box<T>(T Content);

        public sealed class Counter
        {
            public static readonly int Shared = 1;

            public int Total = Shared;
        }

        public sealed class Reader<T>
        {
            public T Read(Box<T> box) => box.Content;
        }

        public static int Direct(Holder holder) => holder.Value;

        public static int FieldRead(Counter counter) => counter.Total;

        public static int StaticFieldRead() => Counter.Shared;

        public static Func<int> InClosure(Holder holder) => () => holder.Value;

        public static Func<Func<int>> InNestedLambda(Holder holder) => () => () => holder.Value;

        public static IEnumerable<int> InIterator(Holder holder)
        {
            yield return holder.Value;
        }

        public static IEnumerable<int> InStaticLambda(IEnumerable<Holder> holders) => holders.Select(holder => holder.Value);

        public static int InLocalFunction(Holder holder)
        {
            return Read();

            int Read() => holder.Value;
        }

        public static int? NullConditional(Holder holder) => holder.Next?.Value;

        public static int Generic(Box<int> box) => box.Content;

        public static Holder With(Holder holder) => holder with { Value = 1 };
    }

    [Fact]
    public void TheRunnerHasNoConditionsOfItsOwnBesidesTheAllowedStops()
    {
        var found = StepRunnerFiles().SelectMany(ConditionPlaces).GroupBy(place => place).ToDictionary(group => group.Key, group => group.Count());

        AllowListAssert.ExactCounts(
            found,
            AllowedConditions.ToDictionary(entry => entry.Key, entry => entry.Value.Count),
            (place, count) => $"{place} x{count}",
            "Condition in the runner; the domain decision belongs to a step",
            $"Allowed in {nameof(AllowedConditions)} but no longer found (remove)");
    }

    /// <summary>"file: Type.Member" of each line of the file that holds a condition, once per line.</summary>
    private static IEnumerable<string> ConditionPlaces(SourceFile file) =>
        FindConditionLines(file.Root).Select(line => $"{FileName(file)}: {file.Outline.PlaceOfLine(line)}");

    /// <summary>The runner makes one call per step: each step's one entry is called once, and the step runs its own phases.</summary>
    [Fact]
    public void TheRunnerCallsEachStepsEntryOnce()
    {
        var wrong = LogicPublicTypesTests.StepEntries()
            .Select(entry => (Call: $"{entry.DeclaringType!.Name}.{entry.Name}", Count: MembersAccessedOn(entry.DeclaringType!.Name).Count(member => member == entry.Name)))
            .Where(entry => entry.Count != 1)
            .Select(entry => $"{entry.Call}: {entry.Count} calls")
            .ToList();

        Assert.True(wrong.Count == 0, $"Step entries the runner does not call exactly once:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>
    /// Besides each step's entry, the runner calls on a step's entry type only the listed rule providers, whichever parameters they
    /// take, so a phase of a step cannot pass as a second call.
    /// </summary>
    [Fact]
    public void TheRunnerCallsOnAStepsEntryTypeOnlyItsEntryAndListedRuleProviders()
    {
        var wrong = LogicPublicTypesTests.StepEntries()
            .SelectMany(entry => FindMembersUsed(entry.DeclaringType!).Where(member => member != entry.Name).Select(member => $"{entry.DeclaringType!.Name}.{member}"))
            .Where(call => !LogicPublicTypesTests.RuleProviders.ContainsKey(call))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(wrong.Count == 0, $"Methods of a step's entry type the runner calls that are neither its entry nor a listed rule provider:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>The members of <paramref name="entryType"/> the runner uses; a nested type it declares is no member use.</summary>
    private static IEnumerable<string> FindMembersUsed(Type entryType) =>
        MembersAccessedOn(entryType.Name).Where(name => entryType.GetNestedType(name) is null);

    /// <summary>The names accessed on the type name <paramref name="typeName"/> in the runner files, called or not: <c>TypeName.Member</c>.</summary>
    private static IEnumerable<string> MembersAccessedOn(string typeName) =>
        RunnerFiles().SelectMany(file => file.MemberAccessesOn(typeName)).Select(access => access.Name.Identifier.Text);

    private static bool IsAccessed(string typeAndMember)
    {
        var parts = typeAndMember.Split('.');
        return RunnerFiles().Any(file => file.Reads(parts[0], parts[1]));
    }

    /// <summary>A rule provider the runner no longer calls or reads (a listed field) is a listed member nothing asks for.</summary>
    [Fact]
    public void TheRunnerUsesEveryListedRuleProvider()
    {
        var notUsed = LogicPublicTypesTests.RuleProviders.Keys
            .Where(provider => !IsAccessed(provider))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(notUsed.Count == 0, $"Listed in {nameof(LogicPublicTypesTests.RuleProviders)} but the runner never uses them:{Environment.NewLine}{string.Join(Environment.NewLine, notUsed)}");
    }

    [Fact]
    public void TheRunnerHoldsNoErrorBoundary()
    {
        var found = RunnerFiles()
            .SelectMany(file => FindErrorBoundaryUses(file).Select(use => $"{FileName(file)}: {use}"))
            .ToHashSet();

        AssertOnlyAllowed(found, AllowedErrorBoundaries.Keys, "Error boundary in the runner; each part catches its own failures", nameof(AllowedErrorBoundaries));
    }

    [Fact]
    public void TheErrorBoundaryReaderSeesPartKeysPartFailuresGuardFailuresTryAndCatch()
    {
        const string code = """
            var a = report.Guard(new PartKey("Run", "Files"), () => 1, () => 2);
            var b = Failures.Attempt(() => 1, _ => 2);
            try { Run(); } catch (Exception) { }
            errors.Add(new PartFailure(part, failure, null));
            """;

        Assert.Equal(["Failures.", "Guard(", "PartFailure", "PartKey", "catch", "try"], FindErrorBoundaryUses(SourceFile.OfSnippet(code)).Order(StringComparer.Ordinal));
        Assert.Equal(["Failures."], FindErrorBoundaryUses(SourceFile.OfSnippet("var d = Errors.Failures.Attempt(() => 1, _ => 2);")));
        Assert.Empty(FindErrorBoundaryUses(SourceFile.OfSnippet("var c = ReportOutput.Write(settings, tables);")));
    }

    [Fact]
    public void TheSettingsRecordsComeFromTheRunSettingsRecord()
    {
        var settings = SettingsOfRunSettings();

        Assert.Equal(
            ["AlsoRemoveSettings", "ChangedObjectCollision", "ChangedObjectSettings", "CollisionSettings", "Execution", "InvisibleLeftBehindSettings", "MarkerMoveSettings", "ModTrustSettings", "NeverRemovedInvisibleKinds", "NeverRemovedVisibleKinds", "ObjectsInClearedAreasSettings", "OtherModsObjectsSettings", "ReportFileSettings", "SelectionSettings", "VisibleLeftBehindSettings", "WhatToCollect", "WhereRemovedRestoreSettings", "WholeGroupsSettings"],
            settings.RecordTypes.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["AlsoRemove", "ChangedObjectCollision", "ChangedObjects", "Collision", "Execution", "InvisibleLeftBehind", "MarkerMoves", "ModTrust", "NeverRemovedInvisibleKinds", "NeverRemovedVisibleKinds", "ObjectsInClearedAreas", "OtherModsObjects", "Read", "Reports", "RestoreWhereRemoved", "Selection", "VisibleLeftBehind", "WholeGroups"],
            settings.RecordProperties.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["DetailedLog", "ObjectsInClearedAreasOff", "OnlyWarnWhenRequiringModLoadsAfterPatch", "Target", "TargetAsTyped"],
            settings.PlainProperties.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheConditionReaderSeesTheConditionFormsButNotNullableTypes()
    {
        const string code = """
            if (a) return;
            var b = c switch { 1 => 2, _ => 3 };
            var d = e is null;
            var f = g
                ? h
                : i;
            var j = k ? l : m;
            int? n = o ?? p;
            q ??= r;
            int? s = t;
            try { } catch (X x) when (x.Y) { }
            var u = v.Where(w => w);
            var y = z.Any();
            var aa = bb && cc;
            var dd = ee || ff;
            var gg = hh.Select(ii => ii);
            var jj = kk.All(ll => ll);
            var mm = nn.First();
            var oo = pp.FirstOrDefault();
            var qq = rr.Count();
            var ss = tt.TakeWhile(uu => uu);
            var vv = ww?.Length;
            var xx = yy.Count;
            long? zz = 1;
            var eq = ab == cd;
            var ne = ab != cd;
            var le = ab <= cd;
            var ge = ab >= cd;
            var lt = ab < cd;
            var gt = ab > cd;
            var negated = !ab;
            var forgiving = ab!.Length;
            var made = new List<int> { 1 };
            Func<int> make = () => 1;
            foreach (var item in items) use(item);
            for (var step = 0; step < 3; step++) use(step);
            while (go) use(go);
            do use(go); while (go);
            var typed = ab is string;
            switch (ab) { case 1: break; }
            foreach (var (left, right) in pairs) use(left);
            switch (ab) { case int number
                when number > 0: break; }
            var chain = items.Select(item => item)
                .Where(item => item)
                .Any();
            """;

        var codeLines = code.Split('\n');
        var lines = FindConditionLines(SourceFile.Parse(code)).Select(line => codeLines[line - 1].Trim());

        Assert.Equal(
            new[]
            {
                "? h", "try { } catch (X x) when (x.Y) { }",
                "var typed = ab is string;", "switch (ab) { case 1: break; }", "foreach (var (left, right) in pairs) use(left);", "switch (ab) { case int number",
                "when number > 0: break; }", ".Where(item => item)", ".Any();", "if (a) return;", "int? n = o ?? p;", "q ??= r;", "var aa = bb && cc;", "var b = c switch { 1 => 2, _ => 3 };",
                "var d = e is null;", "var dd = ee || ff;", "var j = k ? l : m;", "var jj = kk.All(ll => ll);", "var mm = nn.First();",
                "var oo = pp.FirstOrDefault();", "var qq = rr.Count();", "var ss = tt.TakeWhile(uu => uu);", "var u = v.Where(w => w);",
                "var vv = ww?.Length;", "var y = z.Any();",
                "var eq = ab == cd;", "var ne = ab != cd;", "var le = ab <= cd;", "var ge = ab >= cd;", "var lt = ab < cd;", "var gt = ab > cd;", "var negated = !ab;",
                "foreach (var item in items) use(item);", "for (var step = 0; step < 3; step++) use(step);", "while (go) use(go);", "do use(go); while (go);",
            }.Order(StringComparer.Ordinal),
            lines.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheRunnerHoldsNoFieldThatOutlivesARun()
    {
        var found = FindFieldsOutlivingARun(typeof(StepRunner));

        Assert.True(
            found.Count == 0,
            $"The runner keeps something from one run to the next; a run decides from the world alone:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void TheFieldReaderSeesTheRunnersFieldsAndTheStaticFieldsOfItsNestedTypes()
    {
        Assert.Equal(
            ["RunnerWithMemory.<RunsSoFar>k__BackingField", "Scope.<SeenRuns>k__BackingField"],
            FindFieldsOutlivingARun(typeof(RunnerWithMemory)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheRequestLoopHandsEachRunOnlyItsRequestTheEnvironmentAndTheRestoredParts()
    {
        var loop = Assert.Single(FindRequestLoops(SourceFile.All().Single(file => file.Path == StepRunnerFile).Root));

        var notAllowed = loop.Names.Except([loop.Request, .. RequestLoopNames]).ToList();

        Assert.True(
            notAllowed.Count == 0,
            $"The loop over the requests hands a run more than its request, the environment and the restored parts:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
    }

    [Fact]
    public void TheRequestLoopReaderSeesTheLoopVariableAndTheNamesOfTheLoopBody()
    {
        const string code = """
            foreach (var request in requests) results.Add(PatchOneRun(request, environment, earlier));
            foreach (var section in sections) log.Print(section);
            foreach (var next in requests)
            {
                var result = PatchOneRun(next, environment);
                earlier = result;
            }
            """;

        var loops = FindRequestLoops(SourceFile.Parse(code));

        Assert.Equal(
            ["request: Add, PatchOneRun, earlier, environment, request, results", "next: PatchOneRun, earlier, environment, next, result"],
            loops.Select(loop => $"{loop.Request}: {string.Join(", ", loop.Names)}"));
    }

    [Fact]
    public void EveryMemberTheEnvironmentSharesBetweenRunsIsListedWithWhyNoRunChangesIt()
    {
        var members = typeof(RunEnvironment).GetProperties().Select(property => property.Name).ToHashSet();

        AssertOnlyAllowed(
            members, SharedBetweenRuns.Keys, "Member of RunEnvironment, which every run of a batch shares, without why no run changes it", nameof(SharedBetweenRuns));
    }

    [Fact]
    public void NothingTheEnvironmentSharesHoldsWhatARunOpens()
    {
        var sharedTypes = typeof(RunEnvironment).GetProperties().Select(property => property.PropertyType).ToList();

        var found = ProductionSources.Assemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && sharedTypes.Any(shared => shared.IsAssignableFrom(type)))
            .SelectMany(type => type.GetFields(DeclaredFields)
                .Where(field => OpenedPerRun.Any(opened => opened.IsAssignableFrom(field.FieldType)))
                .Select(field => $"{type.Name}.{field.Name}: {field.FieldType.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            found.Count == 0,
            $"Shared by every run, but holds what one run opened, which then outlives that run:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void EachRunsLinkCacheIsBuiltOnlyWhenThatRunOpensItsPluginRecords()
    {
        var building = SourceFile.All()
            .Where(file => file.MentionsIdentifier(ImmutableLinkCacheBuild) || file.MentionsIdentifier(MutableLinkCacheBuild))
            .Select(file => file.Path)
            .ToList();
        var factory = SourceFile.All().Single(file => file.Path == LinkCacheFactoryFile);

        var builds = factory.CallsOf(ImmutableLinkCacheBuild, MutableLinkCacheBuild)
            .Select(call => (Line: SyntaxLines.FirstLineOf(call), Method: call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()))
            .ToList();
        var outsideMethods = builds.Where(build => build.Method is null).Select(build => $"{LinkCacheFactoryFile}:{build.Line}").ToList();

        Assert.Equal([LinkCacheFactoryFile], building);
        Assert.True(outsideMethods.Count == 0, $"A link cache is built outside a method:{Environment.NewLine}{string.Join(Environment.NewLine, outsideMethods)}");
        var method = Assert.Single(builds.Select(build => build.Method!).Distinct());
        Assert.Equal(LinkCacheOpener, method.Identifier.Text);
        Assert.Empty(method.ParameterList.Parameters);
    }

    /// <summary>
    /// The fields of the runner, and the static fields of the types nested in it, an auto-property's backing field included.
    /// An instance field of a nested type belongs to one run's object; compiler-generated nested types hold lambdas only.
    /// </summary>
    private static IReadOnlyList<string> FindFieldsOutlivingARun(Type runner) =>
    [
        .. runner.GetFields(DeclaredFields).Select(field => $"{runner.Name}.{field.Name}"),
        .. runner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .SelectMany(type => type.GetFields(DeclaredFields).Where(field => field.IsStatic).Select(field => $"{type.Name}.{field.Name}")),
    ];

    /// <param name="Request">The loop variable.</param>
    /// <param name="Names">Every name the loop body uses, in ordinal order.</param>
    private sealed record RequestLoop(string Request, IReadOnlyList<string> Names);

    private static IReadOnlyList<RequestLoop> FindRequestLoops(CompilationUnitSyntax root) =>
    [
        .. root.DescendantNodes().OfType<ForEachStatementSyntax>()
            .Where(loop => SyntaxNames.IsIdentifier(loop.Expression, RequestsName))
            .Select(loop => new RequestLoop(loop.Identifier.Text, [.. NamesUsedIn(loop.Statement).Distinct().Order(StringComparer.Ordinal)])),
    ];

    /// <summary>The identifiers of the node except <c>var</c>, which the parser reads as a name.</summary>
    private static IEnumerable<string> NamesUsedIn(SyntaxNode node) =>
        SyntaxNames.IdentifierTokensIn(node).Where(token => SyntaxFacts.GetContextualKeywordKind(token.Text) != SyntaxKind.VarKeyword).Select(token => token.Text);

    /// <summary>A runner that counts its runs, with a nested scope that keeps a static list: the field reader finds both.</summary>
    private static class RunnerWithMemory
    {
        public static int RunsSoFar { get; set; }

        public sealed record Scope(int Run)
        {
            public static List<int> SeenRuns { get; } = [];
        }
    }

    private static IReadOnlyList<string> FindErrorBoundaryUses(SourceFile file) =>
    [
        .. file.Identifiers.Where(name => name is PartKeyName or PartFailureName)
            .Concat(file.Root.DescendantNodes().Select(ErrorBoundaryUseOf).OfType<string>())
            .Distinct(),
    ];

    private static string? ErrorBoundaryUseOf(SyntaxNode node) => node switch
    {
        InvocationExpressionSyntax call when SyntaxNames.CalledName(call) == GuardName => $"{GuardName}(",
        MemberAccessExpressionSyntax access when SyntaxNames.EndsWithName(access.Expression, FailuresName) => $"{FailuresName}.",
        TryStatementSyntax => TryUse,
        CatchClauseSyntax => CatchUse,
        _ => null,
    };

    /// <summary>The runner's files, the run caches included: they take every rule from the steps, so the runner's rules on settings, error boundaries and wording hold for them too.</summary>
    internal static IEnumerable<SourceFile> RunnerFiles() => SourceFile.All().Where(file => file.IsIn([RunnerFolder, RunCachesFolder]));

    /// <summary>The runner's own files, without the run caches.</summary>
    private static IEnumerable<SourceFile> StepRunnerFiles() => SourceFile.All().Where(file => file.IsIn([RunnerFolder]));

    private static string FileName(SourceFile file) => System.IO.Path.GetFileName(file.Path);

    /// <param name="RecordTypes">The types of the settings records.</param>
    /// <param name="RecordProperties">The RunSettings properties that hold a settings record.</param>
    /// <param name="PlainProperties">The other RunSettings properties.</param>
    private sealed record RunSettingsMembers(List<string> RecordTypes, List<string> RecordProperties, List<string> PlainProperties);

    private static RunSettingsMembers SettingsOfRunSettings()
    {
        var properties = typeof(RunSettings).GetProperties()
            .Select(property => (Property: property.Name, Type: Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType))
            .ToList();
        var recordProperties = properties.Where(item => IsRecord(item.Type)).ToList();
        return new(
            [.. recordProperties.Select(item => item.Type.Name).Distinct()],
            [.. recordProperties.Select(item => item.Property)],
            [.. properties.Where(item => !IsRecord(item.Type)).Select(item => item.Property)]);
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    /// <summary>The built assemblies of the runner and the run caches.</summary>
    private static IEnumerable<Assembly> RunnerAssemblies() =>
        ProjectRules.Load().Projects.Where(row => row.Folder is RunnerFolder or RunCachesFolder).Select(row => Assembly.Load(row.Name));

    /// <summary>The record types a RunSettings property holds, and those their own properties hold, down the whole tree.</summary>
    private static IReadOnlySet<Type> SettingsRecordTypes()
    {
        var records = new HashSet<Type>();
        var pending = new Stack<Type>(RecordTypesOfProperties(typeof(RunSettings)));
        while (pending.TryPop(out var record))
        {
            if (!records.Add(record))
            {
                continue;
            }

            foreach (var inner in RecordTypesOfProperties(record))
            {
                pending.Push(inner);
            }
        }

        return records;
    }

    /// <summary>
    /// "Owner.Method: Type.Member" when the member is a setting: a member of a settings record, a plain (not record) member of RunSettings, or
    /// a member that holds a settings record on any other type, such as the settings of a collision group. Reading a settings
    /// record off RunSettings, to hand it on, is no read of a setting.
    /// </summary>
    private static string? SettingsReadOf(MemberUse use, IReadOnlySet<Type> records) =>
        MemberTypeOf(use.Member) is { } type && IsSettingRead(use.Member.DeclaringType!, records.Contains(type), records)
            ? ReadKeyOf(use)
            : null;

    /// <summary>The type of a property or field; null for any other member.</summary>
    private static Type? MemberTypeOf(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => null,
    };

    private static bool IsSettingRead(Type container, bool holdsRecord, IReadOnlySet<Type> records)
    {
        if (records.Contains(container)) return true;
        if (container == typeof(RunSettings)) return !holdsRecord;
        return holdsRecord;
    }

    private static string ReadKeyOf(MemberUse use) => $"{use.Method}: {DisplayNameOf(use.Member.DeclaringType!)}.{use.Member.Name}";

    /// <summary>The type's path name, with its type arguments named the same way: "ObjectsJudgedWith&lt;TooCloseSettings&gt;".</summary>
    private static string DisplayNameOf(Type type) =>
        type.IsGenericType
            ? $"{BusinessCode.PathName(type)}<{string.Join(", ", type.GetGenericArguments().Select(DisplayNameOf))}>"
            : BusinessCode.PathName(type);

    private static IEnumerable<Type> RecordTypesOfProperties(Type type) =>
        type.GetProperties()
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
            .Where(propertyType => IsRecord(propertyType) && propertyType != typeof(RunSettings));

    /// <summary>
    /// The lines (numbered from 1) that hold a condition: an if, switch, conditional operator or access, comparison, logical
    /// or coalescing operator, <c>is</c>, negation, <c>when</c> filter, loop, or a call of Where, Any, All, First, FirstOrDefault,
    /// Count or TakeWhile. A condition is on the line of its keyword, operator or called name, so each call of a chain counts on its own line.
    /// </summary>
    private static IEnumerable<int> FindConditionLines(SyntaxNode root) =>
        root.DescendantNodes().Select(ConditionTokenOf).OfType<SyntaxToken>().Select(SyntaxLines.LineOf).Distinct();

    private static SyntaxToken? ConditionTokenOf(SyntaxNode node) => node switch
    {
        IfStatementSyntax statement => statement.IfKeyword,
        SwitchStatementSyntax statement => statement.SwitchKeyword,
        SwitchExpressionSyntax expression => expression.SwitchKeyword,
        ConditionalExpressionSyntax expression => expression.QuestionToken,
        ConditionalAccessExpressionSyntax expression => expression.OperatorToken,
        IsPatternExpressionSyntax expression => expression.IsKeyword,
        WhenClauseSyntax clause => clause.WhenKeyword,
        CatchFilterClauseSyntax clause => clause.WhenKeyword,
        ForStatementSyntax statement => statement.ForKeyword,
        CommonForEachStatementSyntax statement => statement.ForEachKeyword,
        WhileStatementSyntax statement => statement.WhileKeyword,
        DoStatementSyntax statement => statement.DoKeyword,
        BinaryExpressionSyntax binary when ConditionBinaryKinds.Contains(binary.Kind()) => binary.OperatorToken,
        AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression) => assignment.OperatorToken,
        PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression) => unary.OperatorToken,
        InvocationExpressionSyntax call when ConditionCalls.Contains(SyntaxNames.CalledName(call)) => SyntaxNames.CalledNameToken(call),
        _ => null,
    };

    private static void AssertOnlyAllowed(IReadOnlySet<string> found, IEnumerable<string> allowed, string problem, string listName)
    {
        var allowedSet = allowed.ToHashSet();
        var notAllowed = found.Except(allowedSet).Order(StringComparer.Ordinal).ToList();
        var stale = allowedSet.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"{problem}:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {listName} but no longer found (remove)");
    }
}
