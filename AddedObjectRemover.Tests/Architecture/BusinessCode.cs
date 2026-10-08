using System.Reflection;
using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The built types of the business modules: the steps, the decision list, the base-object shapes and the settings
/// validation. While everything is one project they are found by namespace; the project split replaces that with projects.
/// </summary>
internal static class BusinessCode
{
    public const string ContractsSuffix = ".Contracts";

    private const string RootNamespace = "AddedObjectRemover";
    private const string IdsProject = "AddedObjectRemover.Ids";

    private static readonly string[] BusinessNamespaces =
    [
        "AddedObjectRemover.Steps",
        "AddedObjectRemover.Caches.BaseObjectShapeAndKind",
        "AddedObjectRemover.Run.RunSettingsValidation",
    ];

    private static readonly string[] DomainRoots = ["AddedObjectRemover.Steps", "AddedObjectRemover.Caches", "AddedObjectRemover.Run"];

    /// <summary>Modules whose types still sit in the root namespace, found by the folder their files are in.</summary>
    private static readonly string[] RootNamespaceModuleFolders = ["Run/RunSettingsValidation"];

    private static readonly Lazy<HashSet<string>> IdTypeNames = new(ReadIdTypeNames);

    private static readonly Lazy<Dictionary<string, string>> RootNamespaceModules = new(ReadRootNamespaceModules);

    private static readonly string[] BusinessSourceFolders =
    [
        "Steps",
        "Caches/BaseObjectShapeAndKind",
        "Caches/BaseObjectShapeAndKind.Contracts",
        .. RootNamespaceModuleFolders,
    ];

    /// <summary>The source files of the same modules as <see cref="AllTypes"/>.</summary>
    public static IReadOnlyList<SourceFile> SourceFiles() =>
        SourceFile.All().Where(file => file.IsIn(BusinessSourceFolders)).ToList();

    /// <summary>Every type of the business modules, including the compiler's closure and state machine types.</summary>
    public static IReadOnlyList<Type> AllTypes() =>
        typeof(Settings).Assembly.GetTypes()
            .Where(IsBusinessType)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>The types the author wrote, nested ones included.</summary>
    public static IReadOnlyList<Type> WrittenTypes() =>
        AllTypes().Where(type => !IsCompilerGenerated(type)).ToList();

    public static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || (type.DeclaringType is { } outer && IsCompilerGenerated(outer));

    /// <summary>The folder of the module that owns the type, relative to the production project, such as "Steps/WriteThePatch".</summary>
    public static string ModuleFolder(Type type)
    {
        var ns = type.Namespace!;
        if (ns == RootNamespace) return RootNamespaceModules.Value[OutermostName(type)];
        var module = ns.EndsWith(ContractsSuffix, StringComparison.Ordinal) ? ns[..^ContractsSuffix.Length] : ns;
        return module[(RootNamespace.Length + 1)..].Replace('.', '/');
    }

    /// <summary>A type of Ids, or of any Contracts namespace of a step, a cache or the run.</summary>
    public static bool IsDomainType(Type type)
    {
        var name = NameWithoutArity(type);
        var ns = type.Namespace ?? "";
        return ns == RootNamespace
            ? IdTypeNames.Value.Contains(name)
            : ns.EndsWith(ContractsSuffix, StringComparison.Ordinal) && DomainRoots.Any(root => ns.StartsWith(root + ".", StringComparison.Ordinal));
    }

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

    public static string ProductionFolder() => Path.Combine(SolutionLayout.Root(), "AddedObjectRemover");

    private static bool IsBusinessType(Type type) =>
        type.Namespace is { } ns
            && (BusinessNamespaces.Any(business => ns == business || ns.StartsWith(business + ".", StringComparison.Ordinal))
                || (ns == RootNamespace && RootNamespaceModules.Value.ContainsKey(OutermostName(type))));

    private static string OutermostName(Type type) =>
        type.DeclaringType is { } outer ? OutermostName(outer) : NameWithoutArity(type);

    private static HashSet<string> ReadIdTypeNames() =>
        SourceFile.All().Where(file => file.Project == IdsProject).SelectMany(file => SourceFile.DeclaredTypes(file.Code)).ToHashSet();

    private static Dictionary<string, string> ReadRootNamespaceModules() =>
        RootNamespaceModuleFolders
            .SelectMany(folder => SourceFile.All().Where(file => file.IsIn([folder])).SelectMany(file => SourceFile.DeclaredTypes(file.Code)).Select(name => (name, folder)))
            .ToDictionary(entry => entry.name, entry => entry.folder);
}
