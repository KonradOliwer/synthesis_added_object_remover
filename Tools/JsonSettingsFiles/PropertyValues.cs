using System.Collections;

namespace AddedObjectRemover;

/// <summary>Questions about the values of properties and their types, as a settings file is read into a class.</summary>
public static class PropertyValues
{
    /// <summary>The item type of a list property (its elements are what a saved value replaces), or the type itself.</summary>
    public static Type ElementTypeOrSelf(Type type)
    {
        if (type == typeof(string)) return type;
        var enumerable = type.GetInterfaces()
            .Prepend(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerable?.GetGenericArguments()[0] ?? type;
    }

    /// <summary>
    /// Values are compared, not text: 32 and 32.0 are the same number, and lists are the same when their items are in the same order.
    /// A null list is an empty one.
    /// </summary>
    /// <param name="type">The property's type, which tells a list from a single value.</param>
    public static bool DifferByValue(object? first, object? second, Type type) =>
        IsList(type)
            ? !ItemsOf(first).SequenceEqual(ItemsOf(second))
            : !object.Equals(first, second);

    private static bool IsList(Type type) => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);

    private static IEnumerable<object> ItemsOf(object? list) => list is IEnumerable items ? items.Cast<object>() : [];
}
