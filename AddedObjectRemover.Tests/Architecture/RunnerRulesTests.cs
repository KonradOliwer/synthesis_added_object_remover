using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AddedObjectRemover.Run.RunSettingsValidation.Contracts;
using AddedObjectRemover.Steps.RemovalDecisionList.Contracts;
using Mutagen.Bethesda.Plugins.Cache;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The runner only wires: it hands each step its own settings record and decides nothing about the domain. Three rules,
/// checked on the source text (comments and literals removed) of the runner and the run caches: it reads no member of a settings
/// record, it has no condition (comparison, negation or loop included) of its own, and it holds no error boundary. A new settings
/// read, condition or error boundary fails here. It calls every listed rule provider.
/// It also carries nothing from one run to the next, so chained Standard runs equal one Batch run: the only state that
/// crosses runs is what the patch writing writes into the patch. The runner holds no field, its loop over the requests hands
/// each run only its request and the environment, every member of the environment is listed with why no run changes it,
/// nothing the environment shares holds what a run opens, no production type keeps static state a run can change, and each run's link cache is built when that run opens its plugin
/// records, so it is dropped with them.
/// </summary>
public class RunnerRulesTests
{
    private const string RunnerFolder = "Run/RunAllSteps";

    /// <summary>"file: Record.Member" of a member read on a settings record or on the settings of a group, and why the runner may read it.</summary>
    private static readonly Dictionary<string, string> AllowedSettingsReads = new()
    {
        ["StepRunner.Rules.cs: CollisionGroup.TooClose.Settings"] =
            "The fit step takes the too-close rule built with the settings of its group; the step builds the rule.",
        ["StepRunner.Rules.cs: CollisionGroup.ChangedTerrain.Settings"] =
            "The fit step takes the changed-terrain rule built with the settings of its group; the step builds the rule.",
        ["StepRunner.Rules.cs: ChangedObjectCollision.TooClose"] =
            "The old-place check takes the too-close rule built with the too-close settings of the changed objects; the step builds the rule.",
        ["StepRunner.Rules.cs: ChangedObjectCollision.ChangedTerrain"] =
            "The old-place check takes the changed-terrain rule built with the changed-terrain settings of the changed objects; the step builds the rule.",
    };

    /// <summary>"file: condition line (whitespace collapsed)" in the runner or the run caches, and why it is decided there.</summary>
    private static readonly Dictionary<string, string> AllowedConditions = new()
    {
        ["StepRunner.cs: return request switch"] =
            "The settings validation may have ended the run before any step; the runner passes its stop on.",
        ["StepRunner.cs: if (requests.Any(request => request is RunRequest.Ready)) environment.PatchWriting.WritePatchMarker();"] =
            "Run policy: the patch gets its marker only when some run of the batch will write; the marker's text and merging belong to the plugin writer.",
        ["StepRunner.cs: if (collected.Stop is { } stop)"] =
            "The collect-placed-objects step returns the stop for a run without target objects; the runner ends the run on any stop a step returns.",
        ["StepRunner.cs: foreach (var request in requests) results.Add(PatchOneRun(request, environment));"] =
            "The batch runs its requests one after the other; each run gets only its request and the environment.",
        ["StepRunner.cs: foreach (var section in sections) log.Print(section);"] =
            "Prints the sections a step's builder returned, in the order returned; it decides nothing.",
        ["OtherObjectIndex.cs: contains: slot => IsVisible(slot) && _containment.Contains(_objects[slot].Base, _objects[slot].Transform, worldPoint, scratch),"] = CacheQuery,
        ["ObjectContainment.cs: if (shapes.Of(baseKey).MeshPath is not { } meshPath) return false;"] = ContainmentTest,
        ["ObjectContainment.cs: if (!shapes.Of(baseKey).Box.Contains(local)) return false;"] = ContainmentTest,
        ["ObjectContainment.cs: return lease.Value is { } tree && MeshContact.PointInside(tree, local, transform.Rotation, scratch.Triangles);"] = ContainmentTest,
        ["PlacedSpaces.cs: foreach (var space in _bySpace.Spaces)"] = CacheIndexBuild,
        ["PlacedSpaces.cs: for (var slot = 0; slot < space.Items.Count; slot++) _slotOf[space.Items[slot].Id.Index] = slot;"] = CacheIndexBuild,
        ["VisibleObjectsOfAnyPlugin.cs: box.WorldAabb(distance), slot => index.IsVisible(slot) && box.Intersects(index.OrientedBoxOf(slot), distance), scratch.Spatial, scratch.Matches);"] = CacheQuery,
        ["VisibleObjectsOfAnyPlugin.cs: objectsOfAnyPlugin.IndexOf(space).FindContainingVisible(point, include: _ => true, scratch) >= 0;"] = CacheQuery,
        ["SurfaceSampleStore.cs: return lease.Value is { } tree ? new SampledSurface(SurfaceSampler.Sample(tree, samplesPerMesh).ToImmutableArray(), tree.Bounds) : null;"] =
            "A mesh without a tree has no sampled surface.",
        ["NpcBodyCache.cs: plugin.TraitSupplierOf(new BaseKey(placedBase, BaseLinkKind.Npc), templateFlag) switch"] =
            "Follows a base to the NPC or the leveled list that supplies its body; which flag hands over the traits is passed in.",
        ["NpcBodyCache.cs: return sources.Count == 0 ? null : distinctBodies(sources.Select(GetOwnBody).ToList());"] =
            "A leveled list with no NPC has no body set; how bodies are reduced to distinct ones is passed in.",
        ["NpcBodyCache.cs: ?? throw new InvalidOperationException(\"\");"] =
            "An NPC the plugin reader named but cannot read is a bug of ours, so it throws.",
        ["PlacedNpcIndex.cs: .Where(slot => bodies[slot].FirstPoint != null)"] = CacheIndexBuild,
        ["PlacedNpcIndex.cs: var placedNpcs = spaceObjects.Where(rules.IsPlacedNpc).ToArray();"] = CacheIndexBuild,
        ["PlacedNpcIndex.cs: var spawning = ParallelResults.IndicesWhere(placedBodies, body => body != null);"] = CacheIndexBuild,
        ["PlacedNpcIndex.cs: npc.Base is { } baseKey ? bodies.GetBodies(baseKey.Record) : null;"] = CacheIndexBuild,
        ["ObjectCaches.cs: AreTheCollectedObjects(targets) ? VisibleTargets : _indexes.Get(targets, () => new VisibleTargetObjects(targets, _shapes, _containment, _execution, IsVisible));"] =
            "The collected objects have their index from the start; any other list of objects gets an index built once.",
        ["ObjectCaches.cs: ReferenceEquals(first, second) || (first is not null && second is not null && first.SequenceEqual(second));"] =
            "Two lists of target objects are the same list when they hold the same objects in the same order.",
        ["LoadOrderCaches.cs: public int CountEffectOnlyMeshes() => MeshFiles.Bounds.Computed().Count(mesh => mesh.Status == MeshReadStatus.EffectOnly);"] = CacheStatistics,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: index.Bounds.Overlapping(searchBox, slot => index.IsVisible(slot) && IsUnchanged(index[slot]) && unchangedMatches(index, slot, state), scratch.Spatial, scratch.Matches);"] = CacheQuery,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: foreach (var slot in scratch.Matches) into.Add(index[slot].Id);"] = CacheQuery,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: foreach (var moved in changed.Values)"] = CacheQuery,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: if (moved is { } standing && standing.SpaceKey == space && movedMatches(BoxOf(standing), state)) into.Add(standing.Id);"] = CacheQuery,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: objectsOfAnyPlugin.IndexOf(space).FindContainingVisible(point, IsUnchanged, scratch) >= 0"] = CacheQuery,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: || MovedIn(space).Any(moved => containment.Contains(moved.Base, moved.Transform, point, scratch));"] = CacheQuery,
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: public OtherObject Get(OtherId id) => changed.TryGetValue(id, out var moved) && moved is { } standing ? standing : objectsOfAnyPlugin.Get(id);"] =
            "An object that was moved is answered where it stands now.",
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: private bool IsUnchanged(OtherObject other) => !changed.ContainsKey(other.Id);"] =
            "An object is unchanged when it was neither moved nor taken out.",
        ["VisibleObjectsOfAnyPluginAsTheyStand.cs: private IEnumerable<OtherObject> MovedIn(RecordKey space) => changed.Values.OfType<OtherObject>().Where(moved => moved.SpaceKey == space);"] =
            "The moved objects of one space.",
        ["VisibleTargetObjects.cs: var visible = targets.Where((_, index) => isVisible[index]).ToList();"] = CacheIndexBuild,
        ["VisibleTargetObjects.cs: space, point, radius, (point, radius), static (box, state) => !Vectors.IsFartherThan(box.ClosestPoint(state.point), state.point, state.radius), into);"] = CacheQuery,
        ["VisibleTargetObjects.cs: if (EntriesOf(space) is not { } entries) return false;"] = CacheQuery,
        ["VisibleTargetObjects.cs: .Where(include)"] = CacheQuery,
        ["VisibleTargetObjects.cs: .Any(target => _containment.Contains(target.Base, target.Transform, point, scratch));"] = CacheQuery,
        ["VisibleTargetObjects.cs: EntriesOf(space) is { } entries"] = CacheQuery,
        ["VisibleTargetObjects.cs: ? FindCandidates(entries, point, radius).Where(entry => include(entries.Targets[entry])).Select(entry => entries.Boxes[entry])"] = CacheQuery,
        ["VisibleTargetObjects.cs: if (EntriesOf(space) is not { } entries) return;"] = CacheQuery,
        ["VisibleTargetObjects.cs: foreach (var entry in FindCandidates(entries, point, radius))"] = CacheQuery,
        ["VisibleTargetObjects.cs: if (isNear(entries.Boxes[entry], state)) into.Add(new VisibleTargetNear(entries.Targets[entry], entries.Boxes[entry]));"] = CacheQuery,
        ["VisibleTargetObjects.cs: _visibleBySpace.TryGetValue(space, out var visible) ? _entries.Get(space, () => CreateEntries(visible)) : null;"] =
            "A space without visible targets has no entries.",
        ["VisibleTargetObjects.cs: Vector2.DistanceSquared(new Vector2(center.X, center.Y), point) <= radius * radius;"] = CacheQuery,
        ["TriangleStore.cs: meshes.Count(mesh => !mesh.TooLarge),"] = CacheStatistics,
        ["TriangleStore.cs: meshes.Count(mesh => mesh.TooLarge),"] = CacheStatistics,
        ["TriangleStore.cs: if (triangles is not { TriangleCount: > 0 }) return null;"] = "A mesh without triangles has no tree.",
        ["TriangleStore.cs: _builtMeshes.Get(meshPath, () => new BuiltMesh(tree?.TriangleCount ?? 0, TooLarge: tree == null));"] =
            "Remembers for the statistics what the build gave: the triangle count, or that the mesh was too large.",
        ["TriangleStore.cs: var large = triangles.TriangleCount > LargeMeshTriangles;"] = LargeMeshBuildSlot,
        ["TriangleStore.cs: if (large) _largeBuilds.Wait();"] = LargeMeshBuildSlot,
        ["TriangleStore.cs: if (large) _largeBuilds.Release();"] = LargeMeshBuildSlot,
    };

    /// <summary>"file: use" of an error boundary in the runner or the run caches, and why it is there.</summary>
    private static readonly Dictionary<string, string> AllowedErrorBoundaries = new()
    {
        ["TriangleStore.cs: try"] = "Releases the build slot of a large mesh when its build throws; the exception passes on.",
    };

    private const string CacheQuery = "A cache query picks the objects that answer the question asked of it; the rules it applies are given to the cache.";
    private const string ContainmentTest = "Whether a point lies inside an object's mesh: geometry over the meshes the cache was given.";
    private const string CacheIndexBuild = "A cache builds its index over the objects it was given; it decides nothing about the domain.";
    private const string CacheStatistics = "Counts what the cache holds, for the statistics.";
    private const string LargeMeshBuildSlot = "A large mesh waits for one of the few build slots, because reading and indexing it needs much memory; a limit on memory, not a domain rule.";

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
    };

    /// <summary>What a run opens for itself and drops when it ends; nothing the environment shares may hold one.</summary>
    private static readonly Type[] OpenedPerRun = [typeof(IPluginRecords), typeof(IRecordIndex), typeof(IBaseFacts), typeof(IMeshFiles), typeof(ILinkCache)];

    /// <summary>The names the loop over the requests may use besides its own request: the environment and the results it collects.</summary>
    private static readonly string[] RequestLoopNames = ["Add", "PatchOneRun", "environment", "results"];

    private static readonly Regex UsingDirective = new(@"^\s*using\s+[\w.]+\s*;", RegexOptions.Multiline);

    /// <summary>The words and operators that make a condition or a loop, besides the conditional <c>?</c> and the comparisons <c>&lt;</c> and <c>&gt;</c> between spaces.</summary>
    private const string ConditionWords =
        @"\b(?:if|switch|when|is|for|foreach|while|do)\b|\b(?:Where|Any|All|First|FirstOrDefault|Count|TakeWhile)\s*\(|&&|\|\||\?\?|(?<!\?)\?\.|==|!=|<=|>=|(?<![\w)\]])!(?!=)";

    private static readonly Regex LinkCacheBuild =new(@"\bTo(?:Immutable|Mutable)LinkCache\b");

    /// <summary>The build call inside the body of <c>Open()</c>, before any other member starts.</summary>
    private static readonly Regex LinkCacheBuildInOpen =
        new(@"\bOpen\(\)\s*(?:=>|\{)(?:(?!\b(?:public|internal|protected|private)\b).)*?\bToImmutableLinkCache\b", RegexOptions.Singleline);

    [Fact]
    public void TheRunnerReadsNoMemberOfAnySettingsRecord()
    {
        var settings = SettingsOfRunSettings();

        var found = RunnerFiles()
            .SelectMany(file => FindSettingsReads(file.Code, settings).Concat(FindGroupSettingsReads(file.Code)).Select(read => $"{FileName(file)}: {read}"))
            .ToHashSet();

        AssertOnlyAllowed(found, AllowedSettingsReads.Keys, "Member read on the settings in the runner; the step reads its own settings", nameof(AllowedSettingsReads));
    }

    [Fact]
    public void TheRunnerHasNoConditionsOfItsOwnBesidesTheAllowedStops()
    {
        var found = RunnerFiles()
            .SelectMany(file => FindConditionLines(file.Code).Select(line => $"{FileName(file)}: {line}"))
            .ToHashSet();

        AssertOnlyAllowed(found, AllowedConditions.Keys, "Condition in the runner; the domain decision belongs to a step", nameof(AllowedConditions));
    }

    /// <summary>The runner makes one call per step: each step's one entry is called once, and the step runs its own phases.</summary>
    [Fact]
    public void TheRunnerCallsEachStepsEntryOnce()
    {
        var code = string.Concat(RunnerFiles().Select(file => file.Code));

        var wrong = LogicPublicTypesTests.StepEntries()
            .Select(entry => (Call: $"{entry.DeclaringType!.Name}.{entry.Name}", Count: Regex.Matches(code, $@"\b{entry.DeclaringType!.Name}\.{entry.Name}\(").Count))
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
        var code = string.Concat(RunnerFiles().Select(file => file.Code));

        var wrong = LogicPublicTypesTests.StepEntries()
            .SelectMany(entry => FindMethodCalls(code, entry.DeclaringType!).Where(method => method != entry.Name).Select(method => $"{entry.DeclaringType!.Name}.{method}"))
            .Where(call => !LogicPublicTypesTests.RuleProviders.ContainsKey(call))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(wrong.Count == 0, $"Methods of a step's entry type the runner calls that are neither its entry nor a listed rule provider:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    /// <summary>The names of the methods of <paramref name="entryType"/> called in <paramref name="code"/>; constructing a record it declares is no method call.</summary>
    private static IEnumerable<string> FindMethodCalls(string code, Type entryType) =>
        Regex.Matches(code, $@"\b{entryType.Name}\.(?<method>\w+)\(")
            .Select(match => match.Groups["method"].Value)
            .Where(name => entryType.GetNestedType(name) is null);

    /// <summary>A rule provider the runner no longer calls or reads (a listed field) is a listed member nothing asks for.</summary>
    [Fact]
    public void TheRunnerUsesEveryListedRuleProvider()
    {
        var code = string.Concat(RunnerFiles().Select(file => file.Code));

        var notUsed = LogicPublicTypesTests.RuleProviders.Keys
            .Where(provider => !Regex.IsMatch(code, $@"\b{Regex.Escape(provider)}\b"))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(notUsed.Count == 0, $"Listed in {nameof(LogicPublicTypesTests.RuleProviders)} but the runner never uses them:{Environment.NewLine}{string.Join(Environment.NewLine, notUsed)}");
    }

    [Fact]
    public void TheRunnerHoldsNoErrorBoundary()
    {
        var found = RunnerFiles()
            .SelectMany(file => FindErrorBoundaryUses(file.Code).Select(use => $"{FileName(file)}: {use}"))
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

        Assert.Equal(["Failures.", "Guard(", "PartFailure", "PartKey", "catch", "try"], FindErrorBoundaryUses(code).Order(StringComparer.Ordinal));
        Assert.Empty(FindErrorBoundaryUses("var c = ReportOutput.Write(settings, tables);"));
    }

    [Fact]
    public void TheSettingsRecordsComeFromTheRunSettingsRecord()
    {
        var settings = SettingsOfRunSettings();

        Assert.Equal(
            ["AlsoRemoveSettings", "ChangedObjectCollision", "ChangedObjectSettings", "CollisionSettings", "Execution", "LeftBehindSettings", "MarkerMoveSettings", "ModTrustSettings", "NeverRemovedInvisibleKinds", "NeverRemovedVisibleKinds", "OtherModsObjectsSettings", "ReportFileSettings", "SelectionSettings", "VisibleLeftBehindSettings", "WhatToCollect", "WhereRemovedRestoreSettings"],
            settings.RecordTypes.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["AlsoRemove", "ChangedObjectCollision", "ChangedObjects", "Collision", "Execution", "LeftBehind", "MarkerMoves", "ModTrust", "NeverRemovedInvisibleKinds", "NeverRemovedVisibleKinds", "OtherModsObjects", "Read", "Reports", "RestoreWhereRemoved", "Selection", "VisibleLeftBehind"],
            settings.RecordProperties.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["DetailedLog", "OnlyWarnWhenRequiringModLoadsAfterPatch", "Target", "TargetAsTyped"],
            settings.PlainProperties.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheSettingsReaderSeesDirectAccessTypedVariablesVarLocalsAndPlainProperties()
    {
        var settings = SettingsOfRunSettings();
        const string code = """
            var a = settings.Collision.TooClose;
            var b = settings.Selection?.Npcs;
            var local = settings.LeftBehind;
            use(local.Radius);
            void Use(MarkerMoveSettings moves) => use(moves.Distance);
            use(settings.Execution);
            use(settings.DetailedLog);
            use(ready.Settings.TargetAsTyped);
            use(world.Targets);
            """;

        var reads = FindSettingsReads(code, settings);

        Assert.Equal(
            ["Collision.TooClose", "LeftBehind.Radius", "MarkerMoveSettings moves.Distance", "RunSettings.DetailedLog", "RunSettings.TargetAsTyped", "Selection.Npcs"],
            reads.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheGroupSettingsReaderSeesTheSettingsOfGroupPartsAndOfTypedGroupsButNotTheirObjects()
    {
        const string code = """
            use(group.TooClose.Settings);
            use(group.Fit);
            use(group.Fit.Objects);
            use(groups.Select(group => group.Settings.Fit));
            use(scope.Settings.Execution);
            void Use(ObjectsJudgedWith<FitSettings> judged, CollisionGroup all) => use(judged.Settings, all.Settings, judged.Objects);
            """;

        var reads = FindGroupSettingsReads(code);

        Assert.Equal(
            ["CollisionGroup all.Settings", "CollisionGroup.Settings.Fit", "CollisionGroup.TooClose.Settings", "ObjectsJudgedWith judged.Settings"],
            reads.Distinct().Order(StringComparer.Ordinal));
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
            catch (X x) when (x.Y) { }
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
            var not = !ab;
            var forgiving = ab!.Length;
            var made = new List<int> { 1 };
            Func<int> make = () => 1;
            foreach (var item in items) use(item);
            for (var step = 0; step < 3; step++) use(step);
            while (go) use(go);
            do use(go); while (go);
            """;

        var lines = FindConditionLines(code);

        Assert.Equal(
            new[]
            {
                "? h", "catch (X x) when (x.Y) { }", "if (a) return;", "int? n = o ?? p;", "q ??= r;", "var aa = bb && cc;", "var b = c switch { 1 => 2, _ => 3 };",
                "var d = e is null;", "var dd = ee || ff;", "var j = k ? l : m;", "var jj = kk.All(ll => ll);", "var mm = nn.First();",
                "var oo = pp.FirstOrDefault();", "var qq = rr.Count();", "var ss = tt.TakeWhile(uu => uu);", "var u = v.Where(w => w);",
                "var vv = ww?.Length;", "var y = z.Any();",
                "var eq = ab == cd;", "var ne = ab != cd;", "var le = ab <= cd;", "var ge = ab >= cd;", "var lt = ab < cd;", "var gt = ab > cd;", "var not = !ab;",
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
    public void TheRequestLoopHandsEachRunOnlyItsRequestAndTheEnvironment()
    {
        var loop = Assert.Single(FindRequestLoops(SourceFile.All().Single(file => file.Path == StepRunnerFile).Code));

        var notAllowed = loop.Names.Except([loop.Request, .. RequestLoopNames]).ToList();

        Assert.True(
            notAllowed.Count == 0,
            $"The loop over the requests hands a run more than its request and the environment:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
    }

    [Fact]
    public void TheRequestLoopReaderSeesTheLoopVariableAndTheNamesOfTheLoopBody()
    {
        const string code = """
            foreach (var request in requests) results.Add(PatchOneRun(request, environment, earlier));
            foreach (var section in sections) log.Print(section);
            foreach (var next in requests)
            {
                earlier = PatchOneRun(next, environment);
            }
            """;

        var loops = FindRequestLoops(code);

        Assert.Equal(
            ["request: Add, PatchOneRun, earlier, environment, request, results", "next: PatchOneRun, earlier, environment, next"],
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

    /// <summary>
    /// A static field lives as long as the process, so a run could leave in it what a later run reads. Every static field of the
    /// production code is read-only and does not hold a growable collection; a constant array, filled where it is declared, is not checked.
    /// </summary>
    [Fact]
    public void NoProductionTypeHoldsStaticStateThatARunCanChange()
    {
        var found = FindStaticStateARunCanChange(ProductionSources.Assemblies().SelectMany(assembly => assembly.GetTypes()));

        Assert.True(
            found.Count == 0,
            $"A static field that a run can change, which then outlives that run:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void TheStaticStateReaderSeesWritableFieldsAndGrowableCollectionsButNotConstantsOrReadOnlyViews()
    {
        Assert.Equal(
            ["RunnerWithMemory.<RunsSoFar>k__BackingField", "Scope.<SeenRuns>k__BackingField"],
            FindStaticStateARunCanChange([typeof(RunnerWithMemory), typeof(RunnerWithMemory.Scope), typeof(StaticConstants)]));
    }

    /// <summary>"Type.field" of each static field that is writable or holds a growable collection; compiler-generated types hold lambdas only.</summary>
    private static IReadOnlyList<string> FindStaticStateARunCanChange(IEnumerable<Type> types) =>
    [
        .. types
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !field.IsLiteral && (!field.IsInitOnly || IsGrowableCollection(field.FieldType)))
                .Select(field => $"{type.Name}.{field.Name}"))
            .Order(StringComparer.Ordinal),
    ];

    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || (type.DeclaringType is { } outer && IsCompilerGenerated(outer));

    private static bool IsGrowableCollection(Type type) =>
        !type.IsInterface && type.Namespace is "System.Collections.Generic" or "System.Collections.Concurrent";

    /// <summary>Static fields no run can change: a constant, a read-only view of a collection and an immutable array.</summary>
    private static class StaticConstants
    {
        public const int Limit = 1;

        public static readonly IReadOnlyList<int> View = [1];

        public static readonly System.Collections.Immutable.ImmutableArray<int> Values = [1];
    }

    [Fact]
    public void EachRunsLinkCacheIsBuiltOnlyWhenThatRunOpensItsPluginRecords()
    {
        var building = SourceFile.All().Where(file => LinkCacheBuild.IsMatch(file.Code)).Select(file => file.Path).ToList();

        Assert.Equal([LinkCacheFactoryFile], building);
        Assert.Matches(LinkCacheBuildInOpen, SourceFile.All().Single(file => file.Path == LinkCacheFactoryFile).Code);
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

    /// <summary>The loops over <c>requests</c> whose body is one statement or one block without nested braces.</summary>
    private static IReadOnlyList<RequestLoop> FindRequestLoops(string code) =>
    [
        .. Regex.Matches(code, @"\bforeach\s*\(\s*var\s+(?<request>\w+)\s+in\s+requests\s*\)\s*(?<body>\{[^{}]*\}|[^;{]*;)")
            .Select(match => new RequestLoop(
                match.Groups["request"].Value,
                [.. Regex.Matches(match.Groups["body"].Value, @"\b[A-Za-z_]\w*\b").Select(name => name.Value).Distinct().Order(StringComparer.Ordinal)])),
    ];

    /// <summary>A runner that counts its runs, with a nested scope that keeps a static list: the field reader finds both.</summary>
    private static class RunnerWithMemory
    {
        public static int RunsSoFar { get; set; }

        public sealed record Scope(int Run)
        {
            public static List<int> SeenRuns { get; } = [];
        }
    }

    private static IReadOnlyList<string> FindErrorBoundaryUses(string code) =>
        [.. Regex.Matches(code, @"\bPartKey\b|\bPartFailure\b|\bGuard\(|\bFailures\.|\btry\b|\bcatch\b").Select(match => match.Value).Distinct()];

    /// <summary>The runner's files, the run caches in its RunCaches folder included: they take every rule from the steps, so every rule of the runner holds for them too.</summary>
    private static IEnumerable<SourceFile> RunnerFiles() => SourceFile.All().Where(file => file.IsIn([RunnerFolder]));

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

    /// <summary>
    /// "Record.Member" for a member read on a settings record: through a RunSettings property (<c>settings.TooClose.Npcs</c>),
    /// through a name declared with a record type, or through a <c>var</c> local assigned from such a property.
    /// A read of one of RunSettings' other properties (<c>settings.DetailedLog</c>) is "RunSettings.Property".
    /// Using directives are left out: a step's namespace may carry the name of its settings property.
    /// </summary>
    private static IReadOnlyList<string> FindSettingsReads(string code, RunSettingsMembers settings)
    {
        code = UsingDirective.Replace(code, string.Empty);
        var propertyNames = string.Join("|", settings.RecordProperties.Select(Regex.Escape));
        var typeNames = string.Join("|", settings.RecordTypes.Select(Regex.Escape));
        var plainNames = string.Join("|", settings.PlainProperties.Select(Regex.Escape));

        var reads = Regex.Matches(code, $@"\.(?<record>{propertyNames})\s*\??\.\s*(?<member>\w+)")
            .Select(match => $"{match.Groups["record"].Value}.{match.Groups["member"].Value}")
            .Concat(Regex.Matches(code, $@"\.(?<plain>{plainNames})\b").Select(match => $"RunSettings.{match.Groups["plain"].Value}"))
            .ToList();

        var typedNames = Regex.Matches(code, $@"\b(?<type>{typeNames})\??\s+(?<name>\w+)\b")
            .Select(match => (Name: match.Groups["name"].Value, Label: $"{match.Groups["type"].Value} {match.Groups["name"].Value}"));
        var localNames = Regex.Matches(code, $@"\bvar\s+(?<name>\w+)\s*=\s*[\w.?]*\.(?<record>{propertyNames})\s*;")
            .Select(match => (Name: match.Groups["name"].Value, Label: match.Groups["record"].Value));

        foreach (var (name, label) in typedNames.Concat(localNames))
        {
            reads.AddRange(Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(name)}\s*\??\.\s*(?<member>\w+)")
                .Select(match => $"{label}.{match.Groups["member"].Value}"));
        }

        return reads;
    }

    /// <summary>
    /// "CollisionGroup.Part.Settings" for a read of the settings of a collision group's part (<c>group.TooClose.Settings</c>),
    /// "CollisionGroup.Settings.Part" for a read of a part of a group's own settings from any name (<c>group.Settings.Fit</c>;
    /// RunSettings has no property named like a part, so <c>scope.Settings.Execution</c> is no read of a group), and
    /// "Type name.Settings" for a read on a name declared as a collision group or as objects judged with settings.
    /// </summary>
    private static IReadOnlyList<string> FindGroupSettingsReads(string code)
    {
        var parts = string.Join("|", typeof(CollisionGroup).GetProperties()
            .Where(property => property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(ObjectsJudgedWith<>))
            .Select(property => property.Name));
        var settingsParts = string.Join("|", typeof(CollisionSettings).GetProperties().Select(property => property.Name));

        var throughParts = Regex.Matches(code, $@"\.(?<part>{parts})\s*\??\.\s*Settings\b")
            .Select(match => $"CollisionGroup.{match.Groups["part"].Value}.Settings");
        var throughOwnSettings = Regex.Matches(code, $@"\.Settings\s*\??\.\s*(?<part>{settingsParts})\b")
            .Select(match => $"CollisionGroup.Settings.{match.Groups["part"].Value}");
        var throughNames = Regex.Matches(code, @"\b(?<type>CollisionGroup|ObjectsJudgedWith)(?:<[^<>]*>)?\??\s+(?<name>\w+)\b")
            .SelectMany(typed => Regex.Matches(code, $@"(?<![\w.]){Regex.Escape(typed.Groups["name"].Value)}\s*\??\.\s*Settings\b")
                .Select(_ => $"{typed.Groups["type"].Value} {typed.Groups["name"].Value}.Settings"));

        return [.. throughParts, .. throughOwnSettings, .. throughNames];
    }

    /// <summary>
    /// The lines (whitespace collapsed) that hold an <c>if</c>, <c>switch</c>, <c>is</c>, <c>when</c>, a conditional operator,
    /// <c>Where(</c>, <c>Any(</c>, <c>All(</c>, <c>First(</c>, <c>FirstOrDefault(</c>, <c>Count(</c> (the call, not the property),
    /// <c>TakeWhile(</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>??</c> or <c>?.</c>, a loop (<c>for</c>, <c>foreach</c>, <c>while</c>, <c>do</c>), a
    /// comparison (<c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>) or a negation (<c>!</c>, not the
    /// null-forgiving <c>!</c> after a name).
    /// </summary>
    private static IReadOnlyList<string> FindConditionLines(string code)
    {
        var starts = Regex.Matches(code, ConditionWords).Select(match => match.Index)
            .Concat(Regex.Matches(code, @"\s[?<>]\s").Select(match => match.Index + 1));
        return [.. starts.Select(index => LineAt(code, index)).Distinct()];
    }

    private static string LineAt(string code, int index)
    {
        var start = code.LastIndexOf('\n', index) + 1;
        var end = code.IndexOf('\n', index);
        var line = end < 0 ? code[start..] : code[start..end];
        return Regex.Replace(line.Trim(), @"\s+", " ");
    }

    private static void AssertOnlyAllowed(IReadOnlySet<string> found, IEnumerable<string> allowed, string problem, string listName)
    {
        var allowedSet = allowed.ToHashSet();
        var notAllowed = found.Except(allowedSet).Order(StringComparer.Ordinal).ToList();
        var stale = allowedSet.Except(found).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"{problem}:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {listName} but no longer found (remove)");
    }
}
