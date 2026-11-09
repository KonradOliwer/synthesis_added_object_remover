using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace AddedObjectRemover.Tests.Architecture;

/// <param name="Method">"Owner.Method": the written type and method the use is in, also when the compiler moved it into a lambda, local function or state machine.</param>
/// <param name="Member">The property read through its getter, or the field loaded; on the closed type for a member of a generic type.</param>
internal sealed record MemberUse(string Method, MemberInfo Member);

/// <summary>
/// The properties and fields each method of the given types reads, from its compiled code. A local function carries
/// <c>[CompilerGenerated]</c> but is written code, so it is read; every other compiler-generated method (accessors and
/// record members) is not. A member of the method's own written type is no use, and neither is a field the compiler wrote
/// (a backing field, a primary-constructor capture or a closure's capture). A constant is copied into the code, so its read is not seen.
/// A read inside an expression tree, or in a <c>[Conditional]</c> call the build left out, is not compiled to a call or field load, so it is not seen.
/// </summary>
internal static class MemberUses
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private const string LocalFunctionMarker = ">g__";
    private const string MoveNextMethod = "MoveNext";
    private const char CompilerNameStart = '<';
    private const char CompilerNameEnd = '>';
    private const byte TwoByteOpCodePrefix = 0xFE;
    private const int TwoByteOpCodeHigh = 0xFE00;

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    private static readonly HashSet<OpCode> MemberOpCodes = [OpCodes.Call, OpCodes.Callvirt, OpCodes.Ldfld, OpCodes.Ldflda, OpCodes.Ldsfld, OpCodes.Ldsflda];

    public static IEnumerable<MemberUse> In(IEnumerable<Type> types) =>
        types.Where(HasWrittenOwner)
            .SelectMany(type => type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
            .Where(IsWritten)
            .SelectMany(UsesIn);

    /// <summary>A type the compiler adds outside every written type, such as the read-only wrapper of a collection expression, has none.</summary>
    private static bool HasWrittenOwner(Type type) =>
        !BusinessCode.IsCompilerGenerated(type) || (type.DeclaringType is { } outer && HasWrittenOwner(outer));

    /// <summary>In a closure or state machine only the lambdas, local functions and <c>MoveNext</c> hold written code; the compiler writes the rest, such as an iterator's thread check.</summary>
    private static bool IsWritten(MethodBase method) =>
        BusinessCode.IsCompilerGenerated(method.DeclaringType!)
            ? method.Name.StartsWith(CompilerNameStart) || method.Name == MoveNextMethod
            : !method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || method.Name.Contains(LocalFunctionMarker, StringComparison.Ordinal);

    private static IEnumerable<MemberUse> UsesIn(MethodBase method)
    {
        var owner = BusinessCode.WrittenOwner(method.DeclaringType!);
        var name = $"{BusinessCode.PathName(owner)}.{WrittenMethodName(method)}";
        return MemberTokensIn(method.GetMethodBody()?.GetILAsByteArray() ?? [])
            .Select(token => method.Module.ResolveMember(token, GenericArgumentsOf(method.DeclaringType!), method.IsGenericMethod ? method.GetGenericArguments() : null))
            .Select(ReadMemberOf)
            .OfType<MemberInfo>()
            .Where(member => GenericDefinitionOf(member.DeclaringType!) != GenericDefinitionOf(owner))
            .Select(member => new MemberUse(name, member));
    }

    /// <summary>The operand tokens of the calls and field loads, in code order.</summary>
    private static IEnumerable<int> MemberTokensIn(byte[] code)
    {
        var offset = 0;
        while (offset < code.Length)
        {
            var opCode = ReadOpCode(code, ref offset);
            if (MemberOpCodes.Contains(opCode)) yield return BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(offset));
            offset += OperandSize(opCode, code, offset);
        }
    }

    private static OpCode ReadOpCode(byte[] code, ref int offset)
    {
        var first = code[offset++];
        var value = first == TwoByteOpCodePrefix ? unchecked((short)(TwoByteOpCodeHigh | code[offset++])) : first;
        return OpCodesByValue[value];
    }

    private static int OperandSize(OpCode opCode, byte[] code, int offset) => opCode.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => sizeof(byte),
        OperandType.InlineVar => sizeof(short),
        OperandType.InlineI8 or OperandType.InlineR => sizeof(long),
        OperandType.InlineSwitch => sizeof(int) + (sizeof(int) * BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(offset))),
        _ => sizeof(int),
    };

    /// <summary>The property whose getter is called, or the loaded field unless the compiler wrote it; null for any other call.</summary>
    private static MemberInfo? ReadMemberOf(MemberInfo? resolved) => resolved switch
    {
        MethodInfo method => PropertyOfGetter(method),
        FieldInfo field when !field.Name.StartsWith(CompilerNameStart) && !BusinessCode.IsCompilerGenerated(field.DeclaringType!) => field,
        _ => null,
    };

    private static PropertyInfo? PropertyOfGetter(MethodInfo method) =>
        method.DeclaringType?.GetProperties(Declared)
            .SingleOrDefault(property => property.GetMethod is { } getter && getter.MetadataToken == method.MetadataToken && getter.Module == method.Module);

    /// <summary>The written method, which for a lambda or local function is in its name and for a state machine in its type's name.</summary>
    private static string WrittenMethodName(MethodBase method)
    {
        if (method.Name.StartsWith(CompilerNameStart)) return NameInsideCompilerName(method.Name);
        return BusinessCode.IsCompilerGenerated(method.DeclaringType!) ? NameInsideCompilerName(method.DeclaringType!.Name) : method.Name;
    }

    /// <summary>"M" of <c>&lt;M&gt;b__0</c>, <c>&lt;M&gt;g__Local|0_0</c> or <c>&lt;M&gt;d__0</c>.</summary>
    private static string NameInsideCompilerName(string name)
    {
        var inner = name.TrimStart(CompilerNameStart);
        return inner[..inner.IndexOf(CompilerNameEnd)];
    }

    private static Type[]? GenericArgumentsOf(Type type) => type.IsGenericType ? type.GetGenericArguments() : null;

    private static Type GenericDefinitionOf(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition() : type;
}
