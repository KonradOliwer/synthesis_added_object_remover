using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Rules over the built types of all production projects: type names are unique, and the contracts projects (the rows of kind C
/// and Q in the project table) hold only public data shapes. A contracts type carries no method, operator, computed property, accessor
/// with a body, constructor or initialiser unless it is listed with a reason, and every listed one is a single expression of at most
/// <see cref="MaxMemberLines"/> lines unless it is listed as over that budget with a reason.
/// </summary>
public class ProductionTypeTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private const string GetterPrefix = "get_";
    private const string SetterPrefix = "set_";
    private const string ConstructorName = ".ctor";
    private const string ConstructorKeySuffix = "." + ConstructorName;
    private const string IndexerReflectionName = "Item";
    private const string IndexerSourceName = "this";
    private const string OperatorPrefix = "op_";
    private const int MaxMemberLines = 10;

    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The source name of each operator a contracts type writes, by the name the compiled method has.</summary>
    private static readonly Dictionary<string, string> OperatorSourceNames = new() { ["op_Addition"] = "operator +" };

    /// <summary>
    /// Listed members that are longer than <see cref="MaxMemberLines"/> lines, each "Type.Member" with why it is allowed: a rule that
    /// several steps need and that cannot be shorter.
    /// </summary>
    private static readonly Dictionary<string, string> OverBudgetMembers = new()
    {
        ["TargetObject.RestoredAs"] = "the one rule of how a restore changes an object, applied by the decision list, the judge and the restore step; it names every part a restore can put back, so its length follows the parts",
    };

    /// <summary>
    /// The static classes of the contracts that hold only named values: no written method, and every field a constant or a
    /// read-only field (an auto-property's included) set where it is declared. A value they hold needs no listing in
    /// <see cref="AllowedMembers"/>, but is a single expression.
    /// </summary>
    private static readonly Lazy<IReadOnlySet<string>> NamedConstantTypes = new(FindNamedConstantTypes);

    /// <summary>
    /// Contract types that are not a record, interface, enum, readonly struct or set of named constants, each with why it is allowed.
    /// Every method, computed property, constructor and initialiser of one is listed in <see cref="AllowedMembers"/> on its own.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedRuleAndLookupTypes = new()
    {
        ["ReferenceScale"] = "rule over a contract value: a reference's scale",
        ["TargetVisibility"] = "rule over contract types: how a target looks in the game",
        ["LeftBehindOutcomes"] = "one-line rules over a contract enum: whether an outcome is a removal or a restoration",
        ["VisibleObjectKinds"] = "rule over contract types: the listed kind of a visible object, which the visible left-behind step reports and the kinds-always-kept rule keeps",
        ["ClearedAreaKinds"] = "rule over contract types: how an object counts in the area method, which the area judging counts by and the cleared-area step judges by",
        ["SupporterShares"] = "rules over contract records: the total share of a support category, and whether a category left its place",
        ["TooCloseZoneReach"] = "how far from the centre of a box the on-reach zones of the too-close rule reach, restated from the too-close rule's zone growth and held equal to it by a test",
        ["ContainingObjects"] = "read-only lookup of the object containing each target",
        ["Replacements"] = "read-only lookup of the replaced other-mod objects",
        ["ObjectQueryScratch"] = "per-worker buffers for the object queries",
        ["ResavedPlacedParts"] = "rules over contract values: the rounding of plugins that re-save a placed record, which the collect step compares versions with and the restore step compares where an object ends",
        ["CloneMarker"] = "the one format of a clone's EditorID, which the restore step writes and the collect step reads",
        ["RestoredPartFacts"] = "rules over contract values: the parts a record's write gives back, which the write step lists and the restore judges its plan by",
        ["RestoredPartsByRecord"] = "read-only lookup of the restored parts by record, which the rules that decide acceptance read",
        ["ChangedTerrainScanNeeds"] = "rules over the changed-terrain settings: what the step needs the scan to read, which the settings validation asks before the run",
    };

    /// <summary>
    /// Members of the contracts that carry logic, each "Type.Member" with why it is allowed: a method, a computed property, an
    /// accessor with a body ("Type.Property"), a constructor ("Type..ctor"), and a field or property with an initialiser. Auto-properties,
    /// init-only properties without a body, constants, the values of <see cref="NamedConstantTypes"/> and every member that
    /// <see cref="WrittenMember.DecidesNothing"/> (a fixed value, a factory, a plain accessor, a field-by-field sum) are not listed.
    /// Every listed member is a single expression; a longer one belongs in the step that uses it.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedMembers = new()
    {
        ["FollowUpPass.AsProposedRounds"] = "single-expression opening of every follow-up round, so no step opens them differently",
        ["IRemovalDecisions.LeftInNewestRound"] = "single-expression read of the newest round through the contract's own members",
        ["IRemovalDecisions.MayARoundReplacingFitsStillRemove"] = "single-expression candidate rule of the rounds that replace fits, shared by the one-round steps",
        ["CollectedObjects.Other"] = "index accessor",
        ["CollectedObjects.TargetSpaces"] = "accessor over the collected objects",
        ["CollectedObjects.OtherModAndSupportOnlyObjects"] = "accessor joining two collected lists",
        ["CollectedObjects.WinningPresence"] = "the empty default of a lookup the collector fills",
        ["CollectedObjects.WinningPrimitives"] = "the empty default of a lookup the collector fills",
        ["NpcBodySet.Source"] = "single-expression value derived from its own bodies",
        ["NpcBodySet.FirstPoint"] = "single-expression value derived from its own bodies",
        ["RecordChangedByCleanedMod.HasPartsToUndo"] = "single-expression rule over its own parts: the restore's candidate filter, which the log's kept-by-later-plugins count is the complement of",
        ["RecordChangedByCleanedMod.HasPartsLeftToLaterPlugins"] = "single-expression rule over its own parts",
        ["RecordChangedByCleanedMod.RemovedBeforeTheCleanedMod"] = "single-expression rule over its own parts: keeps the reading of presence facts in the collector's contracts",
        ["RecordChangedByCleanedMod.PresentBeforeTheCleanedMod"] = "single-expression rule over its own parts: keeps the reading of presence facts in the collector's contracts",
        ["CellChangedByCleanedMod.HasPartsToUndo"] = "single-expression rule over its own parts: the restore's candidate filter, which the log's kept-by-later-plugins count is the complement of",
        ["CellChangedByCleanedMod.HasPartsLeftToLaterPlugins"] = "single-expression rule over its own parts",
        ["LandChangedByCleanedMod.HasPartsToUndo"] = "single-expression rule over its own parts: the restore's candidate filter, which the log's kept-by-later-plugins count is the complement of",
        ["LandLeftToLater.HasParts"] = "single-expression rule over its own parts",
        ["LeftBehindVerdict.IsRemoved"] ="single-expression rule over its own outcome",
        ["LeftBehindVerdict.IsRestored"] = "single-expression rule over its own outcome",
        ["FitSettings.ShrinksOrTurns"] = "single-expression rule over its own limits",
        ["NpcBody.FromBox"] = "factory",
        ["ObjectVisibility.IsVisible"] = "single-expression value derived from its own fields",
        ["ObjectVisibility.IsShown"] = "single-expression value derived from its own fields",
        ["BaseShape.ShownBox"] = "single-expression value derived from its own fields",
        ["ObjectVisibility.IsUsedByActorsOrMap"] = "single-expression value derived from its own fields",
        ["RestingObjectsResult.EvaluatedSupport"] = "single-expression value derived from its own fields",
        ["RestingObjectsResult.DetailsHoldTouch"] = "single-expression value derived from its own fields",
        ["RestingObjectsResult.DetailsHoldMeshOrigins"] = "single-expression value derived from its own fields",
        ["OtherObject.Transform"] = "single-expression value derived from its own fields",
        ["OtherObject.IsPlacedNpc"] = "single-expression value derived from its own fields",
        ["OtherModsObjectOutcome.NewPosition"] = "single-expression value derived from its own fields",
        ["OtherModsObjectOutcome.IsTakenAway"] = "single-expression value derived from its own fields",
        ["OtherModsObjectOutcome.IsRemoved"] = "single-expression value derived from its own fields",
        ["OtherModsObjectsResult.TakenAway"] = "the objects as the step leaves them, read by the later steps and derived only from its own outcomes",
        ["HeldUpTargets.SwitchedOff"] = "the fixed result of the switched-off step",
        ["TargetObject.PlacedAt"] = "single-expression copy of itself at a given placement, for the rules asked about a shrunk or turned object",
        ["TargetObject.RestoredAs"] = "copy of itself with the parts that go back taken from its earlier version, for the views of the objects as they stand and the old-place check",
        ["NavmeshChangedByCleanedMod.Winner"] = "single-expression choice between two of its own fields, the version the game shows",
        ["NavmeshChangedByCleanedMod.ChangesTriangles"] = "single-expression test of two of its own fields, shared by the navmesh plan and the restore step's switched-off case",
        ["EarlierVersion.GoesBackToEarlierList"] = "single-expression rule over its own fields, shared by the collector, which judges the cell a restore writes into, and the patch writing",
        ["EarlierVersion.GoesBackToEarlierCell"] = "single-expression rule over its own fields and the record's cell now, shared by the collector, which judges the cell a restore writes into, and the patch writing",
        ["RunSettings.DetailedLog"] = "single-expression value derived from its own fields: the one setting that has the report files, so it is stored once",
        ["ModTrustTable.IsPatch"] = "single-expression rule over its own field",
        ["ModTrustTable.IsAnyGroupsPatch"] = "single-expression fact over its own entries: the plugin is this group's patch or another group's, recognised by its marker",
        ["ModTrustTable.VersionTrustWithoutCleanedMod"] = "the one mapping of a plugin to its version trust in the world without the cleaned mod",
        ["ModTrustTable.LoadsAfterPatch"] = "single-expression fact over its own entries: the plugin loads after the patch",
        ["ModTrustTable.IsTrustedItself"] = "single-expression fact over its own entries: a rule names the plugin itself as trusted",
        ["ModTrustTable.IsTrustedInFull"] = "single-expression fact over its own entries: everything the plugin contains is accepted",
        ["ModTrustTable.IsBaseGame"] ="single-expression fact over its own entries: the plugin is one of the game's own",
        ["ModTrustTable.IsMasterOfCleanedMod"] = "single-expression fact over its own entries: the cleaned mod lists the plugin as a master",
        ["ModTrustTable.NeedsCleanedMod"] = "single-expression fact over its own entries: the plugin cannot load without the cleaned mod",
        ["ModTrustTable.IsNeverTrusted"] = "single-expression fact over its own entries: the user listed the plugin as never trusted",
        ["ModTrustTable.VersionTrustOf"] = "the one mapping of a plugin's trust to the version trust of the accepted-value rule",
        ["ModTrustTable.AcceptedWorld"] = "the world the cleaned mod's objects are judged in, built once from the table's own plugins by the one mapping of plugin trust to version trust",
        ["ModTrustTable.AcceptedWorldWithoutCleanedMod"] = "the world without the cleaned mod, built once from the table's own plugins",
        ["ModTrustTable.AcceptedWorldBeforeCleanedMod"] = "the world before the cleaned mod, built once from the table's own plugins",
        ["ChangedTerrainSettings.CountsWaterChanges"] = "single-expression value derived from its own fields: either water switch is on",
        ["ChangedTerrainSettings.CountsAChange"] = "single-expression value derived from its own fields: any of the three kinds of change is counted",
        ["ChangedTerrainSettings.HasAnArea"] = "single-expression value derived from its own fields: either terrain area is on",
        ["ChangedTerrainSettings.Runs"] = "single-expression rule over its own fields: on, and counting a change in at least one area, so the rule could find something",
        ["ChangedTerrainSettings.AsRun"] = "single-expression copy with the switch as the rule runs, so the step and the log compare groups as they run",
        ["WhereRemovedRestoreSettings.Runs"] = "single-expression rule over its own switches: the step runs when any kind goes back",
        ["ScanNeeds.UnionOf"] = "the one union of the steps' needs",
        ["ScanNeeds.Union"] = "the flag-by-flag union of two steps' needs",
        ["ScanNeeds.UnionOfAreaRecordVersions"] = "the area record reads come from the one step that asks for them",
        ["ChangedTerrainScanNeeds.Of"] = "what the changed-terrain step needs the scan to read, derived from the rule's settings",
        ["ChangedTerrainScanNeeds.OfBoth"] = "what the changed-terrain step needs the scan to read for its two groups of objects, and whether only the changed objects need it",
        ["ChangedObjectSettings.NeedsWhenReplacedObjectsAre"] = "what the step needs the scan to read, derived from its own settings",
        ["WhereRemovedRestoreSettings.Needs"] = "what the step needs the scan to read, derived from its own settings",
        ["WhereRemovedRestoreSettings.ReadAreaRecords"] = "the area records the step's guards look at, derived from its own switches",
        ["SupportEvaluation.RemovedShare"] = "single-expression sum over its own shares",
        ["SupportEvaluation.RestoredShare"] = "single-expression sum over its own shares",
        ["SupportEvaluation.LeftItsPlaceShare"] = "single-expression sum over its own shares",
        ["SupportEvaluation.MainLeftItsPlaceSupporter"] = "single-expression pick from its own shares",
        ["JudgedPasses.Outcome"] = "single-expression rule over its own passes: the step is switched off when every pass is",
        ["JudgedPasses.Proposals"] = "the one merge of the passes' proposals into target order",
        ["JudgedPasses.Failures"] = "the one merge of the passes' failures into target order",
        ["RunSettings.CollisionGroupsOf"] = "one group of the target objects of each origin with the collision settings of that origin, handed to the steps as their groups",
        ["RunSettings.CollisionOf"] = "the pick of the collision settings of an origin",
        ["ChangedObjectCollision.ForMoved"] = "single-expression view of the section's settings for moved objects: the limits only when their switch is on",
        ["ChangedObjectCollision.ForReplaced"] = "single-expression view of the section's settings for replaced objects: the limits only when their switch is on",
        ["SelectedObjects.ObjectsThatCanCauseRemovals"] = "single-expression pick of the view for the standard NPC handling",
        ["SectorAreas.OccupiedCount"] = "single-expression count over its own states",
        ["SectorAreas.ClearedCount"] = "single-expression count over its own states",
        ["SectorAreas.RemovedCount"] = "single-expression count over its own states",
        ["SectorAreas.RestoredCount"] = "single-expression count over its own states",
        ["Decision.Effect"] = "the one reading of what the decision's round did to the object, one arm per kind of decision",
        ["ObjectsJudgedWith.SettingsByObject"] = "single-expression factory over the tool's lookup: the settings each object of the groups is judged with",
        ["ObjectsJudgedWith.MergeEqualSettings"] = "single-expression factory over the tool's merge: the groups with equal settings as one group each",
        ["Decision.EffectIn"] = "single-expression rule over its own round",
        ["Decision.FollowedARestoredMember"] = "single-expression rule over its own reason and the decision of the group member it names",
        ["TerrainPieces.Any"] = "single-expression value derived from its own fields",
        ["ReferenceScale.Normalize"] = "the one rule of a placed reference's scale",
        ["CloneMarker.Read"] = "reads an EditorID as a clone marker, telling a broken marker from an ordinary EditorID",
        ["CloneMarker.MarkOf"] = "parses a clone marker exactly as CloneMarker.Of writes it",
        ["CloneMarkerReading.IsBroken"] = "single-expression value derived from its own fields",
        ["CloneMarker.HexOf"] = "the marker's spelling of a plugin name",
        ["CloneMarker.NameOf"] = "reads the marker's spelling of a plugin name back",
        ["CloneMarker.HashedNameOf"] = "the marker's short spelling of a plugin name, which the scan matches the load order against",
        ["CloneMarker.Of"] = "writes the marker, spelling the names out or hashing them when that is too long",
        ["CloneMarker.LowerCaseBytesOf"] = "the bytes both spellings of a plugin name are made from",
        ["RestoredPartFacts.ListPlaced"] = "the one mapping of a placed record's undone parts to restored parts, shared by the write step and the restore",
        ["RestoredPartFacts.ListCell"] = "the one mapping of a cell's undone parts to restored parts, shared by the write step and the restore",
        ["RestoredPartFacts.ListLand"] = "the one mapping of a LAND record's heights and texture quarters to restored parts, shared by the write step and the restore",
        ["RestoredPartsByRecord._byRecord"] = "the lookup built once from the parts it is given",
        ["RestoredPartsByRecord.SourcesOf"] = "the one reading of which plugins a part was given back from, shared by the rules that decide acceptance",
        ["ResavedPlacedParts.PositionsDiffer"] = "the one rule of a re-saved position, shared by the collect step and the restore step",
        ["ResavedPlacedParts.RotationsDiffer"] = "the one rule of a re-saved rotation, shared by the collect step and the restore step",
        ["ResavedPlacedParts.ScalesDiffer"] = "the one rule of a re-saved scale, shared by the collect step and the restore step",
        ["ResavedPlacedParts.ObjectPropertyDiffers"] = "the one comparison of two versions in an object property, shared by the rule that decides acceptance and the collect step's winner past the patches",
        ["ResavedPlacedParts.ObjectPropertiesDiffer"] = "the one test that a version only copies another, shared by the collect step's winner past the patches",
        ["TargetVisibility.VisibilityOf"] = "the one question of how a target looks, asked of the base object shapes",
        ["LeftBehindOutcomes.IsRemoval"] = "single-expression rule over the outcome enum",
        ["LeftBehindOutcomes.IsRestoration"] = "single-expression rule over the outcome enum",
        ["VisibleObjectKinds.Of"] = "the one mapping of a base record type to a listed visible kind",
        ["ClearedAreaKinds.Of"] = "the one reading of how a target counts in the area method, from how it looks",
        ["IPlacementRule.CheckWhereItStands"] = "single-expression default member: the protocol's first check, the check of the object where it stands",
        ["SupporterShares.TotalOf"] = "single-expression sum over a category of the shares",
        ["SupporterShares.HasLeftItsPlace"] = "single-expression rule over the support category enum",
        ["TooCloseZoneReach.CornerReach"] = "the corner factor of a box, a fact of geometry",
        ["TooCloseZoneReach.ReachRadius"] = "the reach of the zones that are on, from the centre of a box of the given half extents",
        ["Replacements..ctor"] = "private constructor of the factories: it builds the set of replaced objects",
        ["Replacements.IsReplaced"] = "single-expression lookup in the set of replaced objects",
        ["Supporter..ctor"] = "private constructor of the factories: it only stores the type and the index",
        ["RunOutcome..ctor"] = "constructor that stores what the run decided and found",
    };

    /// <summary>The name of the generic Lazy type; every delegate type (Func, Action, custom ones) is found by its base type instead.</summary>
    private static readonly Regex CallbackTypePattern = new(@"^Lazy`\d+$");

    /// <summary>The members of the contracts projects as their source files write them, by the path of their type.</summary>
    private static readonly Lazy<ILookup<string, WrittenMember>> WrittenMembers = new(ReadWrittenMembers);

    /// <summary>
    /// Contract members that hold a Lazy or a delegate (Func, Action or a custom one), also inside generic arguments, arrays or nullables, each "Type.Member" with why it is allowed. Any other one is a
    /// finding to report, not to list.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedCallbackMembers = new()
    {
        ["RunEnvironment.TimeSinceStart"] = "steps may not read the clock themselves; the entry hands them the clock",
        ["ObjectMeasurementRules.OtherModObjects"] = "a step's rule handed to a cache at creation",
        ["ObjectMeasurementRules.ObjectsOfAnyPlugin"] = "a step's rule handed to a cache at creation",
        ["NpcBodyRules.CreateOwnBodyResolver"] = "a step's rule handed to a cache at creation",
        ["NpcBodyRules.DistinctBodies"] = "a step's rule handed to a cache at creation",
        ["PlacedNpcRules.IsPlacedNpc"] = "a step's rule handed to a cache at creation",
        ["PlacedNpcRules.BodyTransform"] = "a step's rule handed to a cache at creation",
        ["PlacedNpcRules.CountSizes"] = "a step's rule handed to a cache at creation",
    };

    [Fact]
    public void ContractsHoldNoLazyFuncOrActionMemberExceptTheListedOnes()
    {
        var members = ContractsTypes()
            .SelectMany(type => type.GetProperties(AllDeclared).Cast<MemberInfo>().Concat(type.GetFields(AllDeclared))
                .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && IsCallbackType(MemberType(member)))
                .Select(member => $"{BusinessCode.PathName(type)}.{member.Name}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        var notListed = members.Except(AllowedCallbackMembers.Keys).ToList();
        var notFound = AllowedCallbackMembers.Keys.Except(members).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            notListed.Count == 0 && notFound.Count == 0,
            $"Contract members that hold a Lazy, Func or Action (not listed): {string.Join(", ", notListed)}. Listed but not found: {string.Join(", ", notFound)}");
    }

    private static Type MemberType(MemberInfo member) =>
        member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

    /// <summary>A Lazy or a delegate of any kind, also inside generic arguments, arrays and nullables.</summary>
    private static bool IsCallbackType(Type type)
    {
        if (type.HasElementType) return IsCallbackType(type.GetElementType()!);
        if (typeof(Delegate).IsAssignableFrom(type)) return true;
        if (!type.IsGenericType) return false;
        return CallbackTypePattern.IsMatch(type.GetGenericTypeDefinition().Name) || type.GetGenericArguments().Any(IsCallbackType);
    }

    private delegate int CustomCallback(int value);

    [Fact]
    public void TheCallbackScanSeesLazyAndDelegatesInsideGenericArgumentsArraysAndNullables()
    {
        Assert.True(IsCallbackType(typeof(Lazy<int>)));
        Assert.True(IsCallbackType(typeof(Func<int>)));
        Assert.True(IsCallbackType(typeof(Action)));
        Assert.True(IsCallbackType(typeof(CustomCallback)));
        Assert.True(IsCallbackType(typeof(System.Collections.Immutable.ImmutableArray<Func<int>>)));
        Assert.True(IsCallbackType(typeof(IReadOnlyList<Lazy<int>>)));
        Assert.True(IsCallbackType(typeof(Dictionary<string, CustomCallback>)));
        Assert.True(IsCallbackType(typeof(Func<int>[])));
        Assert.True(IsCallbackType(typeof((int, Action)?)));
        Assert.False(IsCallbackType(typeof(int)));
        Assert.False(IsCallbackType(typeof(IReadOnlyList<string>)));
        Assert.False(IsCallbackType(typeof(Dictionary<string, int[]>)));
    }

    [Fact]
    public void NoTwoProductionTypesShareAName()
    {
        var shared = WrittenTopLevelTypes(ProductionSources.Assemblies())
            .GroupBy(type => type.Name)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(type => type.FullName).Order(StringComparer.Ordinal))}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(shared);
    }

    [Fact]
    public void NoProductionTypeIsNamedLikeTheLastPartOfItsNamespace()
    {
        var clashes = WrittenTopLevelTypes(ProductionSources.Assemblies())
            .Where(type => type.Namespace is not null && type.Name == type.Namespace[(type.Namespace.LastIndexOf('.') + 1)..])
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(clashes);
    }

    [Fact]
    public void EveryContractsTypeIsPublic()
    {
        var hidden = ContractsTypes().Where(type => !type.IsVisible).Select(BusinessCode.PathName).Order(StringComparer.Ordinal).ToList();

        Assert.Empty(hidden);
    }

    [Fact]
    public void ContractsHoldOnlyRecordsInterfacesEnumsReadonlyStructsAndTheListedOtherTypes()
    {
        var others = ContractsTypes()
            .Where(type => !IsDataShape(type))
            .Select(BusinessCode.PathName)
            .Where(path => !NamedConstantTypes.Value.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(AllowedRuleAndLookupTypes.Keys.Order(StringComparer.Ordinal), others);
    }

    [Fact]
    public void ContractTypesCarryNoMethodsAccessorsConstructorsOrInitialisersExceptTheListedOnes()
    {
        var found = FindLogicMembers();

        var notListed = found.Where(member => !AllowedMembers.ContainsKey(member.Key)).Select(member => $"{member.Key} ({member.Value})").ToList();
        var notFound = AllowedMembers.Keys.Except(found.Keys).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            notListed.Count == 0 && notFound.Count == 0,
            $"Contract members with logic that are not listed in {nameof(AllowedMembers)} (list with a reason, or move into the step that uses them):{Environment.NewLine}"
                + $"{string.Join(Environment.NewLine, notListed)}{Environment.NewLine}Listed but not found (remove):{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}");
    }

    [Fact]
    public void EveryListedContractMemberAndEveryNamedConstantIsOneExpression()
    {
        var notFound = new List<string>();
        var longer = new List<string>();
        foreach (var key in AllowedMembers.Keys.Order(StringComparer.Ordinal))
        {
            var members = SourceMembersOfKey(key).ToList();
            if (members.Count == 0) notFound.Add(key);
            longer.AddRange(members.Where(member => !member.IsOneExpression).Select(member => $"{key} ({member.Where})"));
        }
        longer.AddRange(
            WrittenMembers.Value.SelectMany(group => group)
                .Where(member => NamedConstantTypes.Value.Contains(member.Type) && member.HasInitialiser && !member.IsOneExpression)
                .Select(member => $"{member.Place} ({member.Where})"));

        Assert.True(
            longer.Count == 0 && notFound.Count == 0,
            $"Members of the contracts longer than one expression (listed in {nameof(AllowedMembers)} or a named constant; move into the step that uses them):{Environment.NewLine}"
                + $"{string.Join(Environment.NewLine, longer)}{Environment.NewLine}Listed but not found in the source:{Environment.NewLine}{string.Join(Environment.NewLine, notFound)}");
    }

    [Fact]
    public void EveryListedContractMemberIsWithinTheLineBudgetExceptTheListedOnes()
    {
        var overBudget = AllowedMembers.Keys
            .Where(key => SourceMembersOfKey(key).Any(member => member.LineCount > MaxMemberLines))
            .Order(StringComparer.Ordinal)
            .ToList();

        var notListed = overBudget.Except(OverBudgetMembers.Keys).ToList();
        var notNeeded = OverBudgetMembers.Keys.Except(overBudget).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            notListed.Count == 0 && notNeeded.Count == 0,
            $"Listed members longer than {MaxMemberLines} lines (shorten them, move them into the step that uses them, or list them in {nameof(OverBudgetMembers)} with a reason): {string.Join(", ", notListed)}. "
                + $"Listed in {nameof(OverBudgetMembers)} but within the budget (remove): {string.Join(", ", notNeeded)}");
    }

    [Fact]
    public void TheMemberReaderCountsTheLinesOfAMemberAndNamesAnOperatorBySymbol()
    {
        const string source = """
            class Sample
            {
                public static Sample operator +(Sample a, Sample b) => a;
                public int Block(int x)
                {
                    var y = x;
                    return y;
                }
            }
            """;

        var members = WrittenMember.Of("Sample.cs", source).Select(member => $"{member.Name} lines={member.LineCount} oneExpression={member.IsOneExpression}");

        Assert.Equal(["operator + lines=1 oneExpression=True", "Block lines=5 oneExpression=False"], members);
    }

    [Fact]
    public void TheMemberReaderSeesConstructorsInitialisersAndExpressionBodies()
    {
        const string source = """
            class Sample
            {
                private const int Limit = 3;
                private static readonly int[] Cache = [1];
                public int Value { get; } = 2;
                public Sample(int a)
                {
                    Value = a;
                }
                public int Twice(int x) => x * 2;
                public int Block(int x)
                {
                    return x;
                }
                public int Statements(int x) => Run(y => { Use(y); return y; });
                public int Prop { get => 1; }
                public int Computed => a == b ? 1 : 2;
            }
            """;

        var members = WrittenMember.Of("Sample.cs", source)
            .Select(member => $"{member.Name} constructor={member.IsConstructor} initialiser={member.HasInitialiser} oneExpression={member.IsOneExpression}");

        Assert.Equal(
            [
                "Limit constructor=False initialiser=False oneExpression=False",
                "Cache constructor=False initialiser=True oneExpression=True",
                "Value constructor=False initialiser=True oneExpression=True",
                "Sample constructor=True initialiser=False oneExpression=False",
                "Twice constructor=False initialiser=False oneExpression=True",
                "Block constructor=False initialiser=False oneExpression=False",
                "Statements constructor=False initialiser=False oneExpression=False",
                "Prop constructor=False initialiser=False oneExpression=False",
                "Computed constructor=False initialiser=False oneExpression=True",
            ],
            members);
    }

    [Fact]
    public void TheMemberReaderTellsAFixedValueFactoryAccessorOrSumFromAMemberThatMayDecide()
    {
        const string source = """
            record Sample(int A, int B)
            {
                public static Sample Zero => new(0, 0);
                public static readonly Sample None = new Sample(0, 0);
                public static Sample Of(int a) => new(a, 1);
                public int Item(int index) => _values[index].A;
                public static Sample operator +(Sample x, Sample y) => new(x.A + y.A, x.B + y.B);
                public bool IsProtected => A == 1 || B == 2;
                public int Pick => A > B ? A : B;
                public bool IsKind => A is 1 or 2;
                public int Arm => A switch { _ => 0 };
                public int Called => Math.Max(A, B);
                public int Fallback => _maybe ?? 0;
                public int Negated => !_flag ? 1 : 0;
                public IEnumerable<int> Lambda => _values.Select(value => value.A);
                public int Thrown => throw new X();
                public float Reach => A * 0.5f;
                public int Difference => A - B;
                public Sample(int a) => A = a;
            }
            """;

        var deciding = WrittenMember.Of("Sample.cs", source).Where(member => !member.DecidesNothing).Select(member => member.Name);

        Assert.Equal(["IsProtected", "Pick", "IsKind", "Arm", "Called", "Fallback", "Negated", "Lambda", "Thrown", "Reach", "Difference", "Sample"], deciding);
    }

    [Fact]
    public void AStaticClassHoldingARuleAsAFunctionIsNoSetOfNamedConstants()
    {
        Assert.True(IsNamedConstantType(typeof(OnlyNamedValues)));
        Assert.False(IsNamedConstantType(typeof(NamedValuesAndARule)));
    }

    private static class OnlyNamedValues
    {
        public const int Limit = 3;
        public static readonly int[] Sizes = [1, 2];
    }

    private static class NamedValuesAndARule
    {
        public const int Limit = 3;
        public static readonly Func<int, bool> IsSmall = value => value < Limit;
    }

    /// <summary>"Type.Member" of each member that carries logic and may decide something, with where it is written.</summary>
    private static SortedDictionary<string, string> FindLogicMembers()
    {
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var type in ContractsTypes().Where(type => !type.IsEnum))
        {
            var path = BusinessCode.PathName(type);
            foreach (var key in WrittenMethodAndAccessorNames(type).Select(name => $"{path}.{name}").Where(MayDecide)) found.TryAdd(key, WhereIsMember(key));
            foreach (var member in WrittenMembers.Value[path].Where(NeedsListing)) found.TryAdd($"{path}.{(member.IsConstructor ? ConstructorName : member.Name)}", member.Where);
        }
        return found;
    }

    /// <summary>A member whose source is not found, or one of whose overloads may decide something.</summary>
    private static bool MayDecide(string key) =>
        SourceMembersOfKey(key).ToList() is var members && (members.Count == 0 || members.Any(member => !member.DecidesNothing));

    /// <summary>A constructor, or a field or property with an initialiser that may decide something; the values of a set of named constants need no listing.</summary>
    private static bool NeedsListing(WrittenMember member) =>
        member.IsConstructor || (member.HasInitialiser && !member.DecidesNothing && !NamedConstantTypes.Value.Contains(member.Type));

    private static IReadOnlySet<string> FindNamedConstantTypes() =>
        ContractsTypes().Where(IsNamedConstantType).Select(BusinessCode.PathName).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A static class (abstract and sealed) with no written method and only constant or read-only fields, none of them a
    /// delegate: a lambda compiles into a closure class, so a rule held as a function would otherwise look like a value.
    /// </summary>
    private static bool IsNamedConstantType(Type type) =>
        type is { IsClass: true, IsAbstract: true, IsSealed: true }
            && !type.GetMethods(AllDeclared).Any(method => IsWrittenMethodOrAccessor(type, method))
            && type.GetFields(AllDeclared).All(field => (field.IsLiteral || field.IsInitOnly) && !typeof(Delegate).IsAssignableFrom(field.FieldType));

    private static IEnumerable<string> WrittenMethodAndAccessorNames(Type type) =>
        type.GetMethods(AllDeclared).Where(method => IsWrittenMethodOrAccessor(type, method)).Select(MemberName);

    /// <summary>An interface member counts only when it has a body (a default implementation); the members the compiler writes for records do not.</summary>
    private static bool IsWrittenMethodOrAccessor(Type type, MethodInfo method) =>
        (!type.IsInterface || !method.IsAbstract)
            && (!method.IsSpecialName || IsAccessorOfProperty(method) || IsOperator(method))
            && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static bool IsAccessorOfProperty(MethodInfo method) =>
        method.Name.StartsWith(GetterPrefix, StringComparison.Ordinal) || method.Name.StartsWith(SetterPrefix, StringComparison.Ordinal);

    private static bool IsOperator(MethodInfo method) => method.Name.StartsWith(OperatorPrefix, StringComparison.Ordinal);

    private static string MemberName(MethodInfo method) =>
        IsAccessorOfProperty(method) ? method.Name[GetterPrefix.Length..] : method.Name;

    private static string WhereIsMember(string key) =>
        SourceMembersOfKey(key).Select(member => member.Where).FirstOrDefault() ?? "source not found";

    private static string SourceNameOf(string reflectedName) =>
        reflectedName == IndexerReflectionName ? IndexerSourceName : OperatorSourceNames.GetValueOrDefault(reflectedName, reflectedName);

    private static IEnumerable<WrittenMember> SourceMembersOfKey(string key)
    {
        if (key.EndsWith(ConstructorKeySuffix, StringComparison.Ordinal))
        {
            return WrittenMembers.Value[key[..^ConstructorKeySuffix.Length]].Where(member => member.IsConstructor);
        }
        var split = key.LastIndexOf('.');
        var name = SourceNameOf(key[(split + 1)..]);
        return WrittenMembers.Value[key[..split]].Where(member => member.Name == name);
    }

    private static ILookup<string, WrittenMember> ReadWrittenMembers()
    {
        var contractsProjects = Rules.Projects.Where(Rules.IsContracts).Select(rule => rule.Name).ToHashSet();
        return WrittenMember.Of(SourceFile.All().Where(file => contractsProjects.Contains(file.Project))).ToLookup(member => member.Type);
    }

    private static IEnumerable<Type> WrittenTopLevelTypes(IEnumerable<Assembly> assemblies) =>
        assemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.DeclaringType is null && IsWritten(type));

    /// <summary>The written types of the contracts projects, nested ones included.</summary>
    private static IEnumerable<Type> ContractsTypes() =>
        ContractsAssemblies().SelectMany(assembly => assembly.GetTypes()).Where(IsWritten);

    private static bool IsWritten(Type type) => !BusinessCode.IsCompilerGenerated(type) && !type.Name.StartsWith('<');

    private static IReadOnlyList<Assembly> ContractsAssemblies() =>
        ProductionSources.Assemblies().Where(IsContractsProject).ToList();

    /// <summary>A project whose row in the project table has a contracts kind (C or Q).</summary>
    private static bool IsContractsProject(Assembly assembly) =>
        Rules.Projects.Any(rule => rule.Name == assembly.GetName().Name && Rules.IsContracts(rule));

    private static bool IsDataShape(Type type) =>
        type.IsInterface
            || type.IsEnum
            || type.GetMethod("<Clone>$", AllDeclared) is not null
            || (type.IsValueType && type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false));
}
