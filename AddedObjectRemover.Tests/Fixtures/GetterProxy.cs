using System.Reflection;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// An implementation of a game library getter interface made at run time: each property named in the given values answers with the
/// value's result, which may throw as a record whose bytes cannot be parsed does; any other member is a test error.
/// </summary>
public class GetterProxy : DispatchProxy
{
    private const string GetterPrefix = "get_";

    private IReadOnlyDictionary<string, Func<object?>> _values = new Dictionary<string, Func<object?>>();

    public static TGetter Create<TGetter>(IReadOnlyDictionary<string, Func<object?>> values)
        where TGetter : class
    {
        var proxy = Create<TGetter, GetterProxy>();
        ((GetterProxy)(object)proxy)._values = values;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod?.Name ?? string.Empty;
        return name.StartsWith(GetterPrefix, StringComparison.Ordinal) && _values.TryGetValue(name[GetterPrefix.Length..], out var value)
            ? value()
            : throw new NotSupportedException($"The test getter has no value for {name}.");
    }
}
