using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// General-purpose code belongs in the tools. The business modules hold no generic type definitions and no
/// extension methods (checked exactly), and their static methods and local functions mention a domain type (a review
/// aid, not a proof: instance methods, generic blocks inside domain methods and pass-through use are not seen).
/// </summary>
public partial class GenericCodeTests
{
    private const string AllowListFile = "GenericCodeAllowList.txt";
    private const string ReasonSeparator = " | ";

    private static readonly Dictionary<string, string> GenericTypes = new()
    {
        ["IRemovalRounds"] = "Steps supply their own round details; the decision list can't name them.",
        ["RoundProposals"] = "The proposals an IRemovalRounds<TDetails> returns, carrying the same step-specific details.",
    };

    private static readonly BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    [Fact]
    public void BusinessCodeDefinesNoGenericTypesExceptTheListed()
    {
        var found = BusinessCode.WrittenTypes()
            .Where(type => type.IsGenericTypeDefinition)
            .Select(BusinessCode.PathName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(GenericTypes.Keys.Order(StringComparer.Ordinal), found);
    }

    [Fact]
    public void BusinessCodeDefinesNoExtensionMethods()
    {
        var found = BusinessCode.WrittenTypes()
            .SelectMany(type => type.GetMethods(AllDeclared).Where(method => method.IsDefined(typeof(ExtensionAttribute), inherit: false)).Select(method => $"{BusinessCode.PathName(type)}.{method.Name}"))
            .Order(StringComparer.Ordinal);

        Assert.Empty(found);
    }

    [Fact]
    public void EveryStaticMethodAndLocalFunctionMentionsADomainTypeOrIsListed()
    {
        var flagged = Flagged(BusinessCode.AllTypes()).ToHashSet();
        var listed = ReadAllowLists();

        var unlisted = flagged.Except(listed.Keys).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = listed.Keys.Except(flagged).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(unlisted.Count == 0, $"Mention no domain type (fix, or list in {AllowListFile} with a reason):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
        Assert.True(stale.Count == 0, $"Listed but no longer flagged (remove from {AllowListFile}):{Environment.NewLine}{string.Join(Environment.NewLine, stale)}");
    }

    [Fact]
    public void TheCheckFindsStaticMethodsAndLocalFunctionsWithoutDomainTypes()
    {
        var keys = Flagged([typeof(SampleStatics)]).Select(member => member.Key).Order(StringComparer.Ordinal);

        Assert.Equal(["SampleStatics.Add", "SampleStatics.Lambda", "SampleStatics.WithLocal.Twice"], keys);
    }

    [Fact]
    public void TheDomainTypesAreIdsAndContracts()
    {
        Assert.True(BusinessCode.IsDomainType(typeof(TargetId)));
        Assert.True(BusinessCode.IsDomainType(typeof(CollectedObjects)));
        Assert.False(BusinessCode.IsDomainType(typeof(Tolerant)));
        Assert.True(BusinessCode.IsDomainType(typeof(ZoneShape)));
        Assert.False(BusinessCode.IsDomainType(typeof(PluginNameParser)));
    }

    [Fact]
    public void TheBusinessModulesAreFound()
    {
        var modules = BusinessCode.WrittenTypes().Select(BusinessCode.ModuleFolder).ToHashSet();

        Assert.Contains("Steps/WriteThePatch", modules);
        Assert.Contains("Run/RunSettingsValidation", modules);
        Assert.Contains("Caches/BaseObjectShapeAndKind", modules);
    }

    private static string Describe((string Module, string Key) member) => $"{member.Module}: {member.Key}";

    private static IEnumerable<(string Module, string Key)> Flagged(IEnumerable<Type> types) =>
        types.SelectMany(type => CheckedMethods(type)
            .Where(checkedMethod => !checkedMethod.Method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(checkedMethod.Method.ReturnType)
                .Where(signatureType => !BusinessCode.Mentions(signatureType, BusinessCode.IsCompilerGenerated))
                .Any(signatureType => BusinessCode.Mentions(signatureType, BusinessCode.IsDomainType)))
            .Select(checkedMethod => (BusinessCode.ModuleFolder(type), checkedMethod.Key)));

    /// <summary>
    /// The static methods the author wrote and every local function (even those the compiler moved into closure types).
    /// Skipped: lambdas, property and operator members, and the record members the compiler writes, which are all instance members.
    /// </summary>
    private static IEnumerable<(string Key, MethodInfo Method)> CheckedMethods(Type type)
    {
        var owner = BusinessCode.PathName(BusinessCode.WrittenOwner(type));
        foreach (var method in type.GetMethods(AllDeclared))
        {
            var local = LocalFunctionPattern().Match(method.Name);
            if (local.Success) yield return ($"{owner}.{local.Groups["outer"].Value}.{local.Groups["name"].Value}", method);
            else if (method.IsStatic && !method.IsSpecialName && !BusinessCode.IsCompilerGenerated(type) && !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                yield return ($"{owner}.{method.Name}", method);
            }
        }
    }

    private static Dictionary<(string Module, string Key), string> ReadAllowLists()
    {
        var production = BusinessCode.ProductionFolder();
        var entries = new Dictionary<(string Module, string Key), string>();
        foreach (var file in Directory.EnumerateFiles(production, AllowListFile, SearchOption.AllDirectories).Where(file => !SolutionLayout.IsInGeneratedFolder(production, file)))
        {
            var module = Path.GetRelativePath(production, Path.GetDirectoryName(file)!).Replace('\\', '/');
            foreach (var line in File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')))
            {
                var separator = line.IndexOf(ReasonSeparator, StringComparison.Ordinal);
                if (separator <= 0 || line[(separator + ReasonSeparator.Length)..].Trim().Length == 0)
                {
                    throw new InvalidDataException($"{file}: '{line}' needs the form 'Type.Member{ReasonSeparator}reason'.");
                }
                entries[(module, line[..separator].Trim())] = line[(separator + ReasonSeparator.Length)..].Trim();
            }
        }
        return entries;
    }

    [GeneratedRegex(@"^<(?<outer>[^>]+)>g__(?<name>[^|]+)\|")]
    private static partial Regex LocalFunctionPattern();
}

internal sealed class SampleStatics
{
    public static int Add(int first, int second) => first + second;

    public static TargetId Next(TargetId id) => new(id.Index + 1);

    public static int WithLocal(TargetId id)
    {
        return Twice(id.Index);

        static int Twice(int value) => value * 2;
    }

    public static Func<int, int> Lambda() => value => value + 1;

    public static int Property { get; } = 1;

    public int Instance(int value) => value;
}
