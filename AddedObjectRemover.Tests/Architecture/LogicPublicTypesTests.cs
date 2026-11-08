using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The public surface of each logic project (a step, the removal decision list, the base-object shapes and the log) is its one
/// entry type. Everything else in the project is internal; the data it passes lives in the contracts projects.
/// A step works out its result from its input, so the run never creates its entry type. A logic project whose entry type the run
/// creates, through a public constructor or a public method that returns it, and hands on (the decision list, the shapes cache,
/// the log) is not a step.
/// A step's entry type also declares the record its entry takes, <c>Input</c>, since that record holds the run caches, which no
/// step contract may name, and the records it is made of. A step has one entry, which takes that record; its other public members
/// are the listed rule providers, methods that provide the rules other modules ask of it, each taking one record the entry type
/// declares too, one cache or nothing. The public members of an entry type that is not a step are how the run creates it, the
/// members of the interfaces it implements, what it builds of its own contracts' types, and the listed rule providers.
/// </summary>
public class LogicPublicTypesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The name of the record a step's entry takes, declared in its entry type.</summary>
    private const string InputRecordName = "Input";

    private const string ContractsSuffix = ".Contracts";

    private const BindingFlags DeclaredPublic = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

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
        ["RemoveInvisibleObjectsLeftBehind"] = "InvisibleLeftBehindObjects",
        ["MoveKeptMarkers"] = "MarkerMovePlanner",
        ["RemoveObjectsInClearedAreas"] = "ObjectsInClearedAreas",
        ["RemoveWholeGroups"] = "WholeGroups",
        ["RestoreWhereRemoved"] = "WhereRemovedRestorer",
        ["WriteThePatch"] = "PatchWriter",
        ["BuildLogAndReports"] = "LogAndReports",
        ["ObjectsThatCanCauseRemovals"] = "RemovalCauses",
        ["TooCloseToOtherModsObjects"] = "TooCloseRules",
        ["TerrainChangedByOtherMods"] = "ChangedTerrainRules",
        ["ObjectsRestingOnRemovedOnes"] = "RestingObjectsRules",
        ["ObjectsLeftBehind"] = "LeftBehindRules",
    };

    /// <summary>
    /// "EntryType.Member" of each public member of an entry type that is neither a step's entry nor, for an entry type that is not
    /// a step, how the run creates it, an interface member or what it builds of its own contracts' types; with the rule it provides
    /// and who asks it. The runner binds each into the input of the step that asks, or hands it to the cache that applies it.
    /// </summary>
    internal static readonly Dictionary<string, string> RuleProviders = new()
    {
        ["BaseObjectShapes.SolidShapes"] = "Which shapes of a mesh count for a base object's shape, which the mesh reader takes when the run opens it.",
        ["RemovalCauseSelector.MeasurementRules"] = "How the object caches measure other mods' objects; the caches take it when they are built.",
        ["ChangedTerrainRules.CreatePlacementRule"] = "The changed-terrain check of one placement, which the fit step, the judge step's old-place check and the restore's copy check ask.",
        ["ChangedTerrainRules.CreateOldPlacePlacementRule"] = "The changed-terrain check of one placement in the world without the cleaned mod, which the judge step's old-place check asks.",
        ["TooCloseRules.CreateNpcCacheRules"] = "How the NPC body cache and the placed-NPC index work out an NPC's body; the caches take them when they are built.",
        ["TooCloseRules.CreatePlacementRule"] = "The too-close check of one placement, which the fit step asks.",
        ["RestingObjectsRules.SurfaceSamplesPerMesh"] = "How many points the support rule samples on a mesh's surface; the surface sample cache takes it when it is built.",
        ["WhereRemovedRestorer.MeasurementFailurePart"] = "The part the measurement failures in the worlds of what stays are recorded under; the world caches the restore step and its rules share take it.",
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

    /// <summary>A logic project cannot leave the steps' rules by making its entry type an object while it keeps a step's input.</summary>
    [Fact]
    public void NoEntryTypeThatIsNotAStepDeclaresAnInputRecord()
    {
        var wrong = LogicProjectNames().Except(StepProjectNames()).Where(project => InputRecordOf(project) is not null).ToList();

        Assert.Empty(wrong);
    }

    /// <summary>A step's entry type holds its entry and its rule providers; a value it provides is a provider method too.</summary>
    [Fact]
    public void NoStepsEntryTypeHasAPublicPropertyFieldOrConstant()
    {
        var wrong = StepProjectNames()
            .SelectMany(project => EntryTypeOf(project).GetMembers(DeclaredPublic)
                .Where(member => member is PropertyInfo or FieldInfo or EventInfo)
                .Select(member => $"{EntryTypes[project]}.{member.Name}"))
            .ToList();

        Assert.True(wrong.Count == 0, $"Public properties, fields or constants of a step's entry type:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    [Fact]
    public void EveryOtherPublicMemberOfAnEntryTypeIsAListedRuleProvider()
    {
        var providers = LogicProjectNames().SelectMany(RuleProviderNamesOf).ToHashSet();

        var notListed = providers.Except(RuleProviders.Keys).Order(StringComparer.Ordinal).ToList();
        var stale = RuleProviders.Keys.Except(providers).Order(StringComparer.Ordinal).ToList();

        Assert.True(notListed.Count == 0, $"Public members of an entry type that are no entry, creation, interface member or own output, not listed in {nameof(RuleProviders)}:{Environment.NewLine}{string.Join(Environment.NewLine, notListed)}");
        AllowListAssert.NoneStale(stale, $"Listed in {nameof(RuleProviders)} but no longer found (remove)");
    }

    /// <summary>"EntryType.Member" of each public member of the project's entry type that the structure of the type does not account for.</summary>
    private static IEnumerable<string> RuleProviderNamesOf(string project)
    {
        var entry = EntryTypeOf(project);
        var accountedFor = IsStep(project)
            ? (Func<MemberInfo, bool>)(member => EntryMethodsOf(project).Contains(member))
            : member => IsCreation(entry, member) || IsInterfaceMember(entry, member) || BuildsOwnContractsType(project, member);
        return PublicMembersOf(entry).Where(member => !accountedFor(member)).Select(member => $"{EntryTypes[project]}.{member.Name}").Distinct();
    }

    /// <summary>The constructors, methods, properties and fields the type declares itself; accessors are covered by their properties.</summary>
    private static IEnumerable<MemberInfo> PublicMembersOf(Type entry) =>
        entry.GetMembers(DeclaredPublic)
            .Where(member => member is ConstructorInfo or PropertyInfo or FieldInfo or EventInfo || member is MethodInfo { IsSpecialName: false })
            .Where(member => !member.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)));

    private static bool IsCreation(Type entry, MemberInfo member) =>
        member is ConstructorInfo || member is MethodInfo { IsStatic: true } method && method.ReturnType == entry;

    /// <summary>A member with the name and parameter types of an interface method the type implements, also a typed overload of one it implements explicitly.</summary>
    private static bool IsInterfaceMember(Type entry, MemberInfo member)
    {
        var method = member switch
        {
            MethodInfo own => own,
            PropertyInfo property => property.GetMethod,
            _ => null,
        };
        return method is not null && entry.GetInterfaces().SelectMany(contract => contract.GetMethods())
            .Any(contractMethod => contractMethod.Name == method.Name && ParameterTypesOf(contractMethod).SequenceEqual(ParameterTypesOf(method)));
    }

    /// <summary>A method or value whose type is declared in the project's own contracts, also as the element of a collection.</summary>
    private static bool BuildsOwnContractsType(string project, MemberInfo member)
    {
        var type = member switch
        {
            MethodInfo method => method.ReturnType,
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => null,
        };
        return type is not null && new[] { type }.Concat(type.GetGenericArguments()).Any(part => part.Assembly.GetName().Name == project + ContractsSuffix);
    }

    private static IEnumerable<Type> ParameterTypesOf(MethodInfo method) => method.GetParameters().Select(parameter => parameter.ParameterType);

    /// <summary>
    /// The method of each step that takes its <c>Input</c> record, the step's one entry, by step project; the runner calls it once
    /// (see <see cref="RunnerRulesTests"/>).
    /// </summary>
    internal static IReadOnlyList<MethodInfo> StepEntries() => [.. StepProjectNames().Select(project => EntryMethodsOf(project).Single())];

    private static IEnumerable<string> StepProjectNames() => LogicProjectNames().Where(IsStep);

    /// <summary>
    /// A step's entry type is no object the run creates: it has no public constructor and no public method that returns it. A rule
    /// module is no step: its entry type creates the rules its contracts declare.
    /// </summary>
    private static bool IsStep(string project)
    {
        var entry = EntryTypeOf(project);
        return !Rules.IsRuleLogic(Rules.Find(project))
            && entry.GetConstructors().Length == 0
            && !entry.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly).Any(method => method.ReturnType == entry);
    }

    private static IEnumerable<string> AllowedPublicTypeNames(string project)
    {
        if (!IsStep(project)) return [EntryTypes[project]];

        var records = RecordsOfEntriesAndProviders(project).ToList();
        return [EntryTypes[project], .. records.Concat(RecordsHeldBy(project, records)).Select(BusinessCode.PathName).Distinct()];
    }

    /// <summary>The step's <c>Input</c> record and the records the entry type declares that one of its rule providers takes as its one parameter.</summary>
    private static IEnumerable<Type> RecordsOfEntriesAndProviders(string project) =>
        EntryTypeOf(project).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.GetParameters() is [var only] ? only.ParameterType : null)
            .OfType<Type>()
            .Where(type => IsDeclaredRecordOf(project, type));

    /// <summary>The records the entry type declares that one of <paramref name="holders"/> holds as a member: the parts of an input.</summary>
    private static IEnumerable<Type> RecordsHeldBy(string project, IEnumerable<Type> holders) =>
        holders.SelectMany(holder => holder.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.PropertyType)
            .Where(type => IsDeclaredRecordOf(project, type));

    private static bool IsDeclaredRecordOf(string project, Type type) => type.DeclaringType == EntryTypeOf(project) && IsRecord(type);

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

    private static List<string> LogicProjectNames() =>
        Rules.Projects.Where(Rules.IsLogic).Select(project => project.Name).Order(StringComparer.Ordinal).ToList();

    /// <summary>The types a caller outside the project can name, nested ones included.</summary>
    private static List<string> PublicTypeNames(string project) =>
        Assembly.Load(project).GetTypes()
            .Where(type => type.IsVisible && !BusinessCode.IsCompilerGenerated(type))
            .Select(BusinessCode.PathName)
            .Order(StringComparer.Ordinal)
            .ToList();
}
