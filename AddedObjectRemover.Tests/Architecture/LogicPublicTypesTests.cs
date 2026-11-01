using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The public surface of each logic project (a step, the removal decision list and the base-object shapes) is its one
/// entry type. Everything else in the project is internal; the data it passes lives in the contracts projects. A step's entry
/// type also declares the record its entry takes, <c>Input</c>, since that record holds the run caches, which no step contract
/// may name. A step has one entry, which takes that record; its other public methods are the listed rule providers, which
/// provide the rules other modules ask of it, and each takes one record the entry type declares too, or one cache.
/// </summary>
public class LogicPublicTypesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The name of the record a step's entry takes, declared in its entry type.</summary>
    private const string InputRecordName = "Input";

    /// <summary>The logic projects that are not pipeline steps: the shapes cache, the decision list the steps add to, and the log, which words each step's result.</summary>
    private static readonly string[] NotSteps = ["BaseObjectShapeAndKind", "RemovalDecisionList", "BuildLogAndReports"];

    /// <summary>The entry type of each logic project, by project name.</summary>
    private static readonly Dictionary<string, string> EntryTypes = new()
    {
        ["BaseObjectShapeAndKind"] = "BaseObjectShapes",
        ["IdentifyTheMods"] = "ModTrustClassifier",
        ["CollectPlacedObjects"] = "PlacedObjectCollector",
        ["SelectObjectsThatCanCauseRemovals"] = "RemovalCauseSelector",
        ["FindTargetObjectsToKeep"] = "ObjectsToKeepFinder",
        ["RemovalDecisionList"] = "RemovalDecisions",
        ["JudgeChangedObjects"] = "ChangedObjectJudging",
        ["FitClashingObjects"] = "ClashingObjectFitter",
        ["RemoveObjectsOnChangedTerrain"] = "ChangedTerrainObjects",
        ["RemoveTooCloseObjects"] = "TooCloseObjects",
        ["RemoveObjectsRestingOnRemovedOnes"] = "RestingObjects",
        ["OtherModsObjectsLeftWithoutSupport"] = "OtherModsObjects",
        ["RemoveVisibleObjectsLeftBehind"] = "VisibleLeftBehindObjects",
        ["RemoveInvisibleObjectsLeftBehind"] = "LeftBehindObjects",
        ["MoveKeptMarkers"] = "MarkerMovePlanner",
        ["RestoreWhereRemoved"] = "WhereRemovedRestorer",
        ["WriteThePatch"] = "PatchWriter",
        ["BuildLogAndReports"] = "LogAndReports",
    };

    /// <summary>The public members of each entry type, by project name; see <see cref="PublicMemberNames"/> for the spelling.</summary>
    private static readonly Dictionary<string, string[]> EntryMembers = new()
    {
        ["BaseObjectShapeAndKind"] =
        [
            "BaseObjectShapes(IBaseFacts, IMeshFiles, IReadOnlyDictionary<UInt32, CollisionLayerFact>)",
            "static ShapeInclusion SolidShapes",
            "static BoundsStats CountBounds(IReadOnlyList<MeasuredBase>, IReadOnlyList<MeshBounds>, Int32)",
            "IReadOnlyList<MeasuredBase> Computed()",
            "BaseShape Of(BaseKey?)",
            "ObjectVisibility VisibilityOf(BaseKey?, Boolean, Boolean)",
            "Boolean LayerBlocksActors(UInt32)",
        ],
        ["IdentifyTheMods"] = ["static ModTrustTable Classify(Input)"],
        ["CollectPlacedObjects"] = ["static PlacedObjectsCollected Collect(Input)"],
        ["SelectObjectsThatCanCauseRemovals"] =
        [
            "static ObjectMeasurementRules MeasurementRules(IUnexpectedErrors)",
            "static ObjectMeasurementRules MeasurementRules(IUnexpectedErrors, PartKey)",
            "static SelectedObjects Select(Input)",
            "static ICausesOfWorlds CreateCausesOfWorlds(IObjectCachesOfWorlds)",
        ],
        ["FindTargetObjectsToKeep"] = ["static ObjectsToKeepFound Find(Input)"],
        ["RemovalDecisionList"] =
        [
            "IEnumerable<ValueTuple<TargetId, Decision>> All()",
            "ImmutableArray<MarkerMove> Moves { get; }",
            "ImmutableArray<Round> Rounds { get; }",
            "ImmutableArray<Round> RoundsAfter(IRemovalDecisions)",
            "ImmutableArray<TargetId> KeptIn(Round)",
            "ImmutableArray<TargetId> FittedIn(Round)",
            "ImmutableArray<TargetId> FittedAtFirstIn(Round)",
            "ImmutableArray<TargetId> RemovedIn(Round)",
            "ImmutableArray<TargetId> RestoredIn(Round)",
            "ImmutableArray<TargetId> LeftIn(Round)",
            "ImmutableArray<TargetId> RemovedTargets()",
            "ImmutableArray<TargetObject> TargetsAsTheRoundsMeasureThem(ImmutableArray<TargetObject>)",
            "Boolean AnyMeasuredElsewhere()",
            "ImmutableArray<TargetObject> TargetsAsTheyStand(ImmutableArray<TargetObject>)",
            "Boolean AnyStandsElsewhere()",
            "Boolean IsDecided(TargetId)",
            "WhenRemoved WhenRemovedOf(TargetId)",
            "Boolean MayStillBeRemoved(TargetId)",
            "Boolean IsProtected(TargetId)",
            "Boolean IsRemoved(TargetId)",
            "Boolean HasLeftItsPlace(TargetId)",
            "BoxFate FateOf(TargetId)",
            "Decision Of(TargetId)",
            "IRemovalDecisionsCopy WorkingCopy()",
            "IReadOnlyDictionary<TargetId, Decision> DecideRoundOnCopy(RoundKind, IReadOnlyList<ProposedRemoval>)",
            "RemovalDecisions AddMoves(IReadOnlyList<MarkerMove>)",
            "RemovalDecisions ApplyFits(IReadOnlyList<ProposedFit>)",
            "RemovalDecisions Apply(RoundKind, IReadOnlyList<ProposedRemoval>)",
            "RemovalDecisions ApplyRounds(IReadOnlyList<ProposedRound>)",
            "RemovalDecisions ApplyRounds(RoundKind, IReadOnlyList<IReadOnlyList<ProposedRemoval>>)",
            "static RemovalDecisions Start(IObjectsToKeep, ImmutableArray<WhenRemoved>)",
        ],
        ["JudgeChangedObjects"] = ["static ChangedObjectsJudged Judge(Input)"],
        ["FitClashingObjects"] = ["static Decided<FitResult> Fit(Input)"],
        ["RemoveObjectsOnChangedTerrain"] =
        [
            "static IPlacementRule CreatePlacementRule(PlacementRuleInput)",
            "static Decided<ChangedTerrainResult> Find(Input)",
        ],
        ["RemoveTooCloseObjects"] =
        [
            "static NpcBodyRules NpcBodyRules { get; }",
            "static PlacedNpcRules PlacedNpcRules { get; }",
            "static IPlacementRule CreatePlacementRule(PlacementRuleInput)",
            "static IOldPlaceTooCloseRule CreateOldPlaceTooCloseRule(OldPlaceRuleInput)",
            "static ITooCloseToWhatStaysRule CreateTooCloseToWhatStaysRule(WhatStaysRuleInput)",
            "static Decided<TooCloseResult> Find(Input)",
        ],
        ["RemoveObjectsRestingOnRemovedOnes"] =
        [
            "static IAlsoRemoveFollowUp CreateFollowUp(FollowUpInput)",
            "static IRestingRule CreateRestingRule(RestingRuleInput)",
            "static IAlsoRemoveRoundsAfter CreateAlsoRemoveRoundsAfter(RoundsAfterInput)",
            "static ISupportContacts CreateSupportContacts(ILoadOrderCaches)",
            "static Int32 SurfaceSamplesPerMesh",
            "static Decided<RestingObjectsResult> Find(Input)",
        ],
        ["OtherModsObjectsLeftWithoutSupport"] =
        [
            "static IHeldUpTargetsSearch CreateHeldUpTargetsSearch(SupportJudging)",
            "static IRestingRule CreateRestingRule(RestingRuleInput)",
            "static ITargetObjectsDropTest CreateDropTest(DropTestInput)",
            "static IHoldersOfObjectsThatStay CreateHoldersOfObjectsThatStay(HoldersInput)",
            "static Decided<OtherModsObjectsResult> Settle(Input)",
        ],
        ["RemoveVisibleObjectsLeftBehind"] = ["static Decided<VisibleLeftBehindResult> Find(Input)"],
        ["RemoveInvisibleObjectsLeftBehind"] = ["static Decided<LeftBehindResult> Find(Input)"],
        ["MoveKeptMarkers"] = ["static Decided<MarkerMoves> Plan(Input)"],
        ["RestoreWhereRemoved"] = ["static RestoreWhereRemovedResult Plan(Input)"],
        ["WriteThePatch"] =
        [
            "static INavmeshCheckIfWritten CreateNavmeshCheck(IPluginRecords, IPatchWriting)",
            "static WriteSummary Write(Input)",
        ],
        ["BuildLogAndReports"] =
        [
            "LogAndReports(RunSettings, ReportFolderPrepared, ILoadOrderCaches, IUnexpectedErrors)",
            "static ImmutableArray<LogSection> BuildReportFolderSections(ReportFolderPrepared)",
            "static ImmutableArray<LogSection> BuildStartSections(RunRequest)",
            "static ImmutableArray<LogSection> BuildStopSections(StopReason, SettingsSource)",
            "ImmutableArray<LogSection> BuildArchiveSections(TimeSpan)",
            "ImmutableArray<LogSection> BuildIndexSections(CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(ModTrustTable, LoadOrderPlugins, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(CollectedObjects, TimeSpan, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(Replacements, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(ObjectsToKeepResult, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(HeldUpTargets, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(OtherModsObjectsResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(ChangedObjectJudgingResult, CollectedObjects, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(FitResult, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(ChangedTerrainResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(TooCloseResult, IRemovalDecisions, CollectedObjects, IVisibleTargetObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(RestingObjectsResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(VisibleLeftBehindResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(LeftBehindResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(MarkerMoves, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(RestoreWhereRemovedResult, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(WriteSummary, TimeSpan)",
            "EndOfRun BuildEndOfRun(StepResults, WriteSummary)",
            "ImmutableArray<LogSection> BuildFilesWrittenSections(EndOfRun, ReportFilesResult, TimeSpan)",
        ],
    };

    /// <summary>
    /// "EntryType.Method" of each public method of a step's entry type besides its entry, with the rule it provides and who asks
    /// it. The runner binds each into the input of the step that asks; it calls no other method of a step.
    /// </summary>
    internal static readonly Dictionary<string, string> RuleProviders = new()
    {
        ["RemovalCauseSelector.MeasurementRules"] = "How the object caches measure other mods' objects; the caches take it when they are built.",
        ["RemovalCauseSelector.CreateCausesOfWorlds"] = "The objects that can cause removals in worlds other than the run's, which the too-close step's rules for those worlds ask.",
        ["ChangedTerrainObjects.CreatePlacementRule"] = "The changed-terrain check of one placement, which the fit step and the judge step's old-place check ask.",
        ["TooCloseObjects.CreatePlacementRule"] = "The too-close check of one placement, which the fit step asks.",
        ["TooCloseObjects.CreateOldPlaceTooCloseRule"] = "The too-close check of an earlier version at its old place, which the judge step asks.",
        ["TooCloseObjects.CreateTooCloseToWhatStaysRule"] = "The too-close check of a placed record going back among what stays, which the restore step asks.",
        ["RestingObjects.CreateFollowUp"] = "The also-remove rounds that follow up each round of the visible step, which that step asks.",
        ["RestingObjects.CreateRestingRule"] = "The target objects other target objects rest on, which the fit step asks through the other-mods rule.",
        ["RestingObjects.CreateAlsoRemoveRoundsAfter"] = "The also-remove rounds the step for other mods' objects asks after its own rounds.",
        ["RestingObjects.CreateSupportContacts"] = "The support rule the step for other mods' objects measures with.",
        ["OtherModsObjects.CreateHeldUpTargetsSearch"] = "The target objects other mods' objects rest on, which the keep step asks.",
        ["OtherModsObjects.CreateRestingRule"] = "The objects others rest on, other mods' objects included, which the fit step asks.",
        ["OtherModsObjects.CreateDropTest"] = "Whether a target object holds up another, which the also-remove step's resting rule asks.",
        ["OtherModsObjects.CreateHoldersOfObjectsThatStay"] = "The objects that move away and hold up an object that stays, which the restore step asks.",
        ["PatchWriter.CreateNavmeshCheck"] = "The navmesh check of a trial write, which the restore step asks.",
    };

    [Fact]
    public void EveryLogicProjectHasAListedEntryType()
    {
        var logicProjects = LogicProjectNames();

        Assert.Equal(logicProjects, EntryTypes.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryLogicProjectPublishesOnlyItsEntryTypeAndAStepTheRecordsItsMethodsTake()
    {
        var wrong = LogicProjectNames()
            .SelectMany(project => PublicTypeNames(project).Except(AllowedPublicTypeNames(project)).Select(name => $"{project}: {name}"))
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryStepHasOnePublicEntryThatTakesItsInputRecord()
    {
        var wrong = StepProjectNames()
            .Where(project => EntryMethodsOf(project).Count != 1)
            .Select(project => $"{project}: {string.Join(", ", EntryMethodsOf(project).Select(method => method.Name))}")
            .ToList();

        Assert.True(wrong.Count == 0, $"Steps without exactly one public method that takes their {InputRecordName} record:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    [Fact]
    public void EveryStepsInputRecordIsARecord()
    {
        var wrong = StepProjectNames().Where(project => InputRecordOf(project) is not { } input || !IsRecord(input)).ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryOtherPublicMethodOfAStepsEntryTypeIsAListedRuleProvider()
    {
        var providers = StepProjectNames().SelectMany(RuleProviderNamesOf).ToHashSet();

        var notListed = providers.Except(RuleProviders.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = RuleProviders.Keys.Except(providers).Order(StringComparer.Ordinal).ToList();

        Assert.True(notListed.Count == 0, $"Public methods of a step's entry type besides its entry, not listed in {nameof(RuleProviders)}:{Environment.NewLine}{string.Join(Environment.NewLine, notListed)}");
        AllowListAssert.NoneStale(stale, $"Listed in {nameof(RuleProviders)} but no longer found (remove)");
    }

    /// <summary>"EntryType.Method" of each public method of the step's entry type besides its entry.</summary>
    private static IEnumerable<string> RuleProviderNamesOf(string project) =>
        EntryTypeOf(project).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && !EntryMethodsOf(project).Contains(method))
            .Select(method => $"{EntryTypes[project]}.{method.Name}");

    /// <summary>
    /// The method of each step that takes its <c>Input</c> record, the step's one entry, by step project; the runner calls it once
    /// (see <see cref="RunnerRulesTests"/>).
    /// </summary>
    internal static IReadOnlyList<MethodInfo> StepEntries() => [.. StepProjectNames().Select(project => EntryMethodsOf(project).Single())];

    private static IEnumerable<string> StepProjectNames() => LogicProjectNames().Except(NotSteps);

    private static IEnumerable<string> AllowedPublicTypeNames(string project) =>
        NotSteps.Contains(project) ? [EntryTypes[project]] : [EntryTypes[project], $"{EntryTypes[project]}.{InputRecordName}", .. ProviderRecordNamesOf(project)];

    /// <summary>The records the entry type declares that one of its rule providers takes as its one parameter.</summary>
    private static IEnumerable<string> ProviderRecordNamesOf(string project) =>
        EntryTypeOf(project).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.GetParameters() is [var only] ? only.ParameterType : null)
            .OfType<Type>()
            .Where(type => type.DeclaringType == EntryTypeOf(project) && IsRecord(type))
            .Select(BusinessCode.PathName)
            .Distinct();

    private static Type EntryTypeOf(string project) =>
        Assembly.Load(project).GetTypes().Single(type => type.IsVisible && BusinessCode.PathName(type) == EntryTypes[project]);

    private static Type? InputRecordOf(string project) => EntryTypeOf(project).GetNestedType(InputRecordName, BindingFlags.Public);

    private static List<MethodInfo> EntryMethodsOf(string project) =>
        InputRecordOf(project) is { } input
            ? [.. EntryTypeOf(project).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => method.GetParameters() is [var only] && only.ParameterType == input)]
            : [];

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    [Fact]
    public void EveryLogicProjectsEntryTypeIsPublic()
    {
        var missing = LogicProjectNames()
            .Where(project => !PublicTypeNames(project).Contains(EntryTypes[project]))
            .Select(project => $"{project}: {EntryTypes[project]}")
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryEntryTypeHasExactlyTheListedPublicMembers()
    {
        Assert.Equal(EntryTypes.Keys.Order(StringComparer.Ordinal), EntryMembers.Keys.Order(StringComparer.Ordinal));

        var problems = EntryTypes.Keys.Order(StringComparer.Ordinal)
            .SelectMany(project =>
            {
                var actual = PublicMemberNames(project);
                return actual.Except(EntryMembers[project]).Select(member => $"{project}: not listed: {member}")
                    .Concat(EntryMembers[project].Except(actual).Select(member => $"{project}: listed but missing: {member}"));
            })
            .ToList();

        Assert.True(problems.Count == 0, $"Public members of the entry types differ from the list in {nameof(LogicPublicTypesTests)}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    private static List<string> LogicProjectNames() =>
        Rules.Projects.Where(Rules.IsLogic).Select(project => project.Name).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The public constructors, methods, properties and fields the entry type declares itself, written as
    /// "[static ]ReturnType Name(ParameterTypes)" or "[static ]Type Name { accessors }". Names and types only.
    /// </summary>
    private static List<string> PublicMemberNames(string project)
    {
        var entry = EntryTypeOf(project);
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var constructors = entry.GetConstructors(Declared).Select(constructor => $"{BusinessCode.NameWithoutArity(entry)}({ParameterTypes(constructor)})");
        var methods = entry.GetMethods(Declared)
            .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .Select(method => $"{StaticPrefix(method.IsStatic)}{TypeText(method.ReturnType)} {MethodName(method)}({ParameterTypes(method)})");
        var properties = entry.GetProperties(Declared)
            .Select(property => $"{StaticPrefix(property.GetMethod?.IsStatic ?? property.SetMethod!.IsStatic)}{TypeText(property.PropertyType)} {property.Name} {{ {Accessors(property)} }}");
        var fields = entry.GetFields(Declared)
            .Where(field => !field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .Select(field => $"{StaticPrefix(field.IsStatic)}{TypeText(field.FieldType)} {field.Name}");

        return constructors.Concat(methods).Concat(properties).Concat(fields).Order(StringComparer.Ordinal).ToList();
    }

    private static string StaticPrefix(bool isStatic) => isStatic ? "static " : "";

    private static string MethodName(MethodInfo method) =>
        method.IsGenericMethodDefinition ? $"{method.Name}<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : method.Name;

    private static string ParameterTypes(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter => TypeText(parameter.ParameterType)));

    private static string Accessors(PropertyInfo property) =>
        string.Join(" ", new[] { property.GetMethod is { IsPublic: true } ? "get;" : null, property.SetMethod is { IsPublic: true } setter ? (IsInitOnly(setter) ? "init;" : "set;") : null }.OfType<string>());

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string TypeText(Type type)
    {
        if (type.IsByRef) return $"ref {TypeText(type.GetElementType()!)}";
        if (type.IsArray) return $"{TypeText(type.GetElementType()!)}[]";
        if (Nullable.GetUnderlyingType(type) is { } underlying) return $"{TypeText(underlying)}?";
        if (!type.IsGenericType) return type.Name;
        return $"{BusinessCode.NameWithoutArity(type)}<{string.Join(", ", type.GetGenericArguments().Select(TypeText))}>";
    }

    /// <summary>The types a caller outside the project can name, nested ones included.</summary>
    private static List<string> PublicTypeNames(string project) =>
        Assembly.Load(project).GetTypes()
            .Where(type => type.IsVisible && !BusinessCode.IsCompilerGenerated(type))
            .Select(BusinessCode.PathName)
            .Order(StringComparer.Ordinal)
            .ToList();
}
