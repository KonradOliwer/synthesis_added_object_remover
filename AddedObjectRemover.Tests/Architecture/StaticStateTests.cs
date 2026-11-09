using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Run state belongs to the run, so nothing static may hold changing data. A static array counts: its elements stay writable
/// by any caller, so fixed content is an <c>ImmutableArray</c> or a frozen set. The declared type of every static field is read
/// from the built assemblies; a read-only declared type can still hide a growable collection, so the static initialisers are
/// read too, by the type names they create or cast to. A target-typed <c>new()</c> or collection expression given as an element value
/// is judged by the written element type of its collection: the declared or cast type of a collection expression, or the type of an explicit creation.
/// Limit: a target-typed <c>new()</c> or collection expression given as an argument, a member value, a tuple part or an element of an untyped
/// collection is not found, as its type is written nowhere in the initialiser and needs the compiler; nor is any of them inside the
/// receiver of a call, as in <c>new Dictionary&lt;int, List&lt;int&gt;&gt; { [1] = [2] }.ToFrozenDictionary()</c>.
/// Limit: a static property with a getter body is not checked, as it holds no state.
/// </summary>
public class StaticStateTests
{
    private static readonly string[] ChangingTypeNames =
    [
        "Dictionary", "IDictionary", "HashSet", "ISet", "List", "IList", "Queue", "Stack", "ConditionalWeakTable", "MemoryCache",
        "ThreadLocal", "Lazy", "StringBuilder",
    ];

    private static readonly string[] ChangingTypeNamePrefixes = ["Concurrent", "Sorted", "ComputedOnce", "IndexMemo", "EvictingLeasedStore"];

    /// <summary>Every class and struct of these namespaces counts as a growable collection, a comparer or key-value pair too.</summary>
    private static readonly string[] GrowableCollectionNamespaces = ["System.Collections.Generic", "System.Collections.Concurrent"];

    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void NoStaticFieldCanHoldChangingData()
    {
        var found = FindChangingStaticFields(ProductionSources.Assemblies().SelectMany(assembly => assembly.GetTypes()));

        Assert.True(found.Count == 0, $"A static field that can hold changing data, which then outlives the run:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void NoStaticInitialiserKeepsAChangingCollection()
    {
        var found = SourceFile.All()
            .SelectMany(file => FindChangingInitialisers(file.Root).Select(name => $"{file.Description}: {name}"))
            .ToList();

        Assert.True(found.Count == 0, $"A static initialiser that keeps a growable collection behind a read-only type:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact]
    public void TheFieldCheckFindsWritableFieldsArraysAndChangingTypesButNotConstantsOrReadOnlyViews()
    {
        Assert.Equal(
            [
                "StaticStateTests.ChangingStatics.<Name>k__BackingField", "StaticStateTests.ChangingStatics.Cache", "StaticStateTests.ChangingStatics.Checks",
                "StaticStateTests.ChangingStatics.Count", "StaticStateTests.ChangingStatics.Fields", "StaticStateTests.ChangingStatics.Items",
                "StaticStateTests.ChangingStatics.Linked", "StaticStateTests.ChangingStatics.Log", "StaticStateTests.ChangingStatics.Pairs",
                "StaticStateTests.ChangingStatics.Seen", "StaticStateTests.ChangingStatics.Text", "StaticStateTests.ChangingStatics.Value",
            ],
            FindChangingStaticFields([typeof(ChangingStatics), typeof(ConstantStatics)]));
    }

    [Fact]
    public void TheInitialiserCheckFindsChangingCreationsHiddenByAReadOnlyType()
    {
        const string code = """
            class Sample
            {
                private static readonly IReadOnlyDictionary<int, int> View = new Dictionary<int, int>();
                private static readonly IReadOnlyList<int> Items = new List<int>
                {
                    1,
                };
                private static readonly IReadOnlyList<int> Cast = (List<int>)[1];
                public static Holder Empty { get; } = new(new HashSet<int>(), Failure: null);
                private static readonly IReadOnlyDictionary<int, int> Frozen = new Dictionary<int, int>
                {
                    [1] = 2,
                }.ToFrozenDictionary();
                public static Holder None { get; } = new(ImmutableDictionary<int, int>.Empty, Failure: null);
                private static readonly ImmutableArray<IReadOnlyList<int>> Singles = [.. Fields.Select(field => (IReadOnlyList<int>)[field.Length])];
                private static readonly object Lock = new();
                private readonly List<int> _seen = new();
            }
            """;

        Assert.Equal(["View", "Items", "Cast", "Empty"], FindChangingInitialisers(SourceFile.Parse(code)));
    }

    [Fact]
    public void TheInitialiserCheckFindsTargetTypedElementsOfAWrittenChangingTypeButNotArgumentsOrMemberValues()
    {
        const string code = """
            class Sample
            {
                public static Holder Argument { get; } = new(new(), Failure: null);
                private static readonly Holder Passed = Holder.Of([1]);
                private static readonly Holder Member = new() { Items = [1] };
                private static readonly Holder MemberNew = new() { Seen = new() };
                private static readonly ImmutableArray<List<int>> Lists = [[1]];
                private static readonly ImmutableArray<ImmutableArray<int>> Rows = [[1]];
                private static readonly Table<int, List<int>> Indexed = new Table<int, List<int>> { [1] = [2] };
                private static readonly Table<int, ImmutableArray<int>> IndexedRows = new Table<int, ImmutableArray<int>> { [1] = [2] };
                private static readonly Bag<HashSet<int>> Sets = new Bag<HashSet<int>> { new() };
                private static readonly Bag<Marker> Markers = new Bag<Marker> { new() };
                private static readonly IReadOnlyDictionary<int, List<int>> Frozen = new Dictionary<int, List<int>> { [1] = [2] }.ToFrozenDictionary();            }
            """;

        Assert.Equal(["Lists", "Indexed", "Sets"], FindChangingInitialisers(SourceFile.Parse(code)));
    }

    /// <summary>Static fields that can hold changing data; an auto-property's backing field included.</summary>
    private static class ChangingStatics
    {
        public static int Count = 1;

        public static string Name { get; set; } = "";

        public static readonly Dictionary<string, int> Cache = [];

        public static readonly HashSet<int> Seen = [];

        public static readonly Lazy<int> Value = new(() => Count);

        public static readonly string[] Fields = ["a"];

        public static readonly (int Id, string Text)[] Pairs = [];

        public static readonly Func<int, bool>[] Checks = [];

        public static readonly IList<int> Items = [];

        public static readonly LinkedList<int> Linked = new();

        public static readonly StringBuilder Text = new();

        public static readonly ConcurrentLog Log = new();

        /// <summary>Outside the collection namespaces, so only its name prefix makes it changing.</summary>
        public sealed class ConcurrentLog;
    }

    /// <summary>Static fields no run can change: constants, a lock, immutable and frozen collections, comparers and plain objects.</summary>
    private static class ConstantStatics
    {
        public const int Limit = 1;

        public static readonly object Lock = new();

        public static readonly ImmutableHashSet<int> Kinds = [Limit];

        public static readonly IReadOnlyList<int> View = [Limit];

        public static readonly ImmutableArray<string> Names = ["a"];

        public static readonly ImmutableArray<(int Id, string Text)> Pairs = [];

        public static readonly SearchValues<char> Quoted = SearchValues.Create(",");

        public static readonly IReadOnlyDictionary<int, int> Frozen = new Dictionary<int, int> { [Limit] = Limit }.ToFrozenDictionary();

        public static readonly IComparer<int> Order = Comparer<int>.Create((left, right) => left.CompareTo(right));

        public static Marker None { get; } = new();

        public static int Next => Limit;

        public sealed class Marker;
    }

    /// <summary>"Outer.Type.field" of each static field that is writable or whose declared type can hold changing data; compiler-generated types hold lambdas only.</summary>
    private static IReadOnlyList<string> FindChangingStaticFields(IEnumerable<Type> types) =>
    [
        .. types
            .Where(type => !BusinessCode.IsCompilerGenerated(type))
            .SelectMany(type => type.GetFields(StaticFields)
                .Where(field => !field.IsLiteral && (!field.IsInitOnly || CanHoldChangingData(field.FieldType)))
                .Select(field => $"{BusinessCode.PathName(type)}.{field.Name}"))
            .Order(StringComparer.Ordinal),
    ];

    private static bool CanHoldChangingData(Type type) =>
        type.IsArray
        || HasChangingName(BusinessCode.NameWithoutArity(type))
        || (!type.IsInterface && GrowableCollectionNamespaces.Contains(type.Namespace));

    private static bool HasChangingName(string name) =>
        ChangingTypeNames.Contains(name) || ChangingTypeNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    /// <param name="Name">The field variable or property.</param>
    private sealed record StaticInitialiser(string Name, ExpressionSyntax Value);

    private static IEnumerable<string> FindChangingInitialisers(SyntaxNode root) =>
        StaticInitialisersIn(root).Where(initialiser => KeepsChangingCreation(initialiser.Value)).Select(initialiser => initialiser.Name);

    private static IEnumerable<StaticInitialiser> StaticInitialisersIn(SyntaxNode root) =>
        root.DescendantNodes().SelectMany(node => node switch
        {
            FieldDeclarationSyntax field when IsStatic(field.Modifiers) =>
                field.Declaration.Variables.Where(variable => variable.Initializer is not null).Select(variable => new StaticInitialiser(variable.Identifier.Text, variable.Initializer!.Value)),
            PropertyDeclarationSyntax { Initializer: { } initializer } property when IsStatic(property.Modifiers) => [new StaticInitialiser(property.Identifier.Text, initializer.Value)],
            _ => [],
        });

    private static bool IsStatic(SyntaxTokenList modifiers) => modifiers.Any(SyntaxKind.StaticKeyword);

    /// <summary>A read-only type does not stop a caller from casting a growable collection back and changing it.</summary>
    private static bool KeepsChangingCreation(ExpressionSyntax value) =>
        value.DescendantNodesAndSelf().Any(node => node switch
        {
            ObjectCreationExpressionSyntax creation => HasChangingName(SyntaxNames.RightmostName(creation.Type)) && !IsInvokedReceiver(creation),
            CastExpressionSyntax { Expression: CollectionExpressionSyntax } cast => HasChangingName(SyntaxNames.RightmostName(cast.Type)),
            ImplicitObjectCreationExpressionSyntax or CollectionExpressionSyntax => KeepsTargetTypedCreation((ExpressionSyntax)node),
            _ => false,
        });

    /// <summary>A <c>new()</c> or collection expression takes its type from its target, read where the initialiser writes it.</summary>
    private static bool KeepsTargetTypedCreation(ExpressionSyntax creation) =>
        WrittenTargetType(creation) is { } target
        && HasChangingName(SyntaxNames.RightmostName(target))
        && !creation.AncestorsAndSelf().Any(IsInvokedReceiver);

    /// <summary>The element type of the collection the value is an element of, when that collection's type is written; null for any other value.</summary>
    private static TypeSyntax? WrittenTargetType(ExpressionSyntax value) => value.Parent switch
    {
        ExpressionElementSyntax { Parent: CollectionExpressionSyntax collection } => WrittenCollectionType(collection) is { } type ? ElementTypeOf(type) : null,
        InitializerExpressionSyntax { RawKind: (int)SyntaxKind.CollectionInitializerExpression, Parent: ObjectCreationExpressionSyntax creation } => ElementTypeOf(creation.Type),
        AssignmentExpressionSyntax { Left: ImplicitElementAccessSyntax, Parent.Parent: ObjectCreationExpressionSyntax creation } => ElementTypeOf(creation.Type),
        _ => null,
    };

    /// <summary>The type of a collection expression as written: by its cast, by the declaration it initialises, or as the element type of its outer collection.</summary>
    private static TypeSyntax? WrittenCollectionType(CollectionExpressionSyntax collection) => collection.Parent switch
    {
        CastExpressionSyntax cast => cast.Type,
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } => declaration.Type,
        EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Type,
        _ => WrittenTargetType(collection),
    };

    /// <summary>The element type of an array, or the last type argument of a generic type: the value type of a dictionary.</summary>
    private static TypeSyntax? ElementTypeOf(TypeSyntax type) => type switch
    {
        ArrayTypeSyntax array => array.ElementType,
        NullableTypeSyntax nullable => ElementTypeOf(nullable.ElementType),
        QualifiedNameSyntax qualified => ElementTypeOf(qualified.Right),
        GenericNameSyntax generic => generic.TypeArgumentList.Arguments.Last(),
        _ => null,
    };

    /// <summary>In <c>new Dictionary&lt;..&gt;{..}.ToFrozenDictionary()</c> only the result of the call is kept.</summary>
    private static bool IsInvokedReceiver(SyntaxNode creation) =>
        creation.Parent is MemberAccessExpressionSyntax access
        && access.Expression == creation
        && access.Parent is InvocationExpressionSyntax call
        && call.Expression == access;
}
