using System.Reflection;
using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The built types and source files of the business modules: the steps, the shared rules, the decision list, the base-object
/// shapes and the settings validation, each with its contracts. A module is found by its project in the project table.
/// </summary>
internal static class BusinessCode
{
    private const string ContractsSuffix = ".Contracts";
    private const string RootNamespace = "AddedObjectRemover";
    private const string IdsProject = "AddedObjectRemover.Ids";
    private const string ReportProject = "BuildLogAndReports";
    private const string RecordPrintMembers = "PrintMembers";

    private static readonly ProjectRules Rules = ProjectRules.Load();

    private static readonly string[] DomainFolderRoots = ["Steps/", "Rules/", "Caches/", "Run/"];

    /// <summary>Every project under these folders is a business module.</summary>
    private static readonly string[] BusinessFolderRoots = ["Steps/", "Rules/"];

    /// <summary>Module folders outside <see cref="BusinessFolderRoots"/>.</summary>
    private static readonly string[] BusinessModuleFoldersOutsideSteps = ["Caches/BaseObjectShapeAndKind", "Caches/RunCaches", "Run/RunSettingsValidation", "Run/RunSettingsReading"];

    private static readonly Lazy<HashSet<string>> BusinessProjects = new(ReadBusinessProjects);

    /// <summary>The source files of the same modules as <see cref="AllTypes"/>.</summary>
    public static IReadOnlyList<SourceFile> SourceFiles() =>
        SourceFile.All().Where(file => BusinessProjects.Value.Contains(file.Project)).ToList();

    /// <summary>Every type of the business modules, including the compiler's closure and state machine types.</summary>
    public static IReadOnlyList<Type> AllTypes() =>
        ProductionSources.Assemblies()
            .Where(assembly => BusinessProjects.Value.Contains(assembly.GetName().Name!))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Namespace?.StartsWith(RootNamespace, StringComparison.Ordinal) == true)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>The types the author wrote, nested ones included.</summary>
    public static IReadOnlyList<Type> WrittenTypes() =>
        AllTypes().Where(type => !IsCompilerGenerated(type)).ToList();

    public static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || (type.DeclaringType is { } outer && IsCompilerGenerated(outer));

    /// <summary>The folder of the module that owns the type, such as "Steps/WriteThePatch"; a contracts project belongs to its logic module's folder.</summary>
    public static string ModuleFolder(Type type) => ModuleFolderOf(Rules.Find(AssemblyName(type)).Folder);

    /// <summary>A type of Ids, or of any contracts project of a step, a rule, a cache or the run, or one of Report's own internal types.</summary>
    public static bool IsDomainType(Type type)
    {
        var assemblyName = AssemblyName(type);
        if (assemblyName == IdsProject || IsReportInternalType(type)) return true;
        var row = Rules.Projects.SingleOrDefault(rule => rule.Name == assemblyName);
        return row is not null
            && assemblyName.EndsWith(ContractsSuffix, StringComparison.Ordinal)
            && DomainFolderRoots.Any(root => row.Folder.StartsWith(root, StringComparison.Ordinal));
    }

    /// <summary>A record or record struct declared in a project of the given module folder (see <see cref="ModuleFolder"/>).</summary>
    public static bool IsRecordOfModule(Type type, string moduleFolder) =>
        IsRecord(type)
            && Rules.Projects.Any(rule => rule.Name == AssemblyName(type))
            && ModuleFolder(type) == moduleFolder;

    /// <summary>The type itself, its generic arguments, or its element type, anywhere in the signature type.</summary>
    public static bool Mentions(Type type, Func<Type, bool> test)
    {
        if (type.HasElementType) return Mentions(type.GetElementType()!, test);
        if (type.IsGenericParameter) return false;
        return test(type.IsGenericType ? type.GetGenericTypeDefinition() : type)
            || type.GetGenericArguments().Any(argument => Mentions(argument, test));
    }

    public static string NameWithoutArity(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name[..tick];
    }

    /// <summary>The name of a written type, with its outer types in front: "Outer.Inner".</summary>
    public static string PathName(Type type) =>
        type.DeclaringType is { } outer ? $"{PathName(outer)}.{NameWithoutArity(type)}" : NameWithoutArity(type);

    /// <summary>The type the author wrote, which for a compiler-generated closure or state machine is the type that contains it.</summary>
    public static Type WrittenOwner(Type type) =>
        IsCompilerGenerated(type) ? WrittenOwner(type.DeclaringType!) : type;

    /// <summary>Report's records (such as its worded errors) exist only for its wording, so a method that takes or returns one is Report's business code.</summary>
    private static bool IsReportInternalType(Type type) =>
        AssemblyName(type) == ReportProject && !type.IsVisible && !IsCompilerGenerated(type);

    private static string AssemblyName(Type type) => type.Assembly.GetName().Name!;

    /// <summary>The compiler writes PrintMembers into every record and record struct.</summary>
    private static bool IsRecord(Type type) =>
        type.GetMethod(RecordPrintMembers, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is { } printMembers
            && printMembers.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false);

    private static string ModuleFolderOf(string projectFolder) =>
        projectFolder.EndsWith(ContractsSuffix, StringComparison.Ordinal) ? projectFolder[..^ContractsSuffix.Length] : projectFolder;

    private static HashSet<string> ReadBusinessProjects() =>
        Rules.Projects
            .Where(rule => IsBusinessFolder(rule.Folder))
            .Select(rule => rule.Name)
            .ToHashSet();

    private static bool IsBusinessFolder(string projectFolder) =>
        BusinessFolderRoots.Any(root => projectFolder.StartsWith(root, StringComparison.Ordinal))
            || BusinessModuleFoldersOutsideSteps.Contains(ModuleFolderOf(projectFolder));
}
