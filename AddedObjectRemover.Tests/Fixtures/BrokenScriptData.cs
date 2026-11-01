using System.Buffers.Binary;
using System.Text;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Writes a plugin whose only script data (VMAD) is one <see cref="TestScripts.Naming"/> script, breaks that data in a way the game
/// library cannot parse, and opens the plugin as the library reads load orders.
/// </summary>
public static class BrokenScriptData
{
    public enum Fault
    {
        EndsInsideTheScriptName,
        EndsInsideTheLastPropertyValue,
        UnknownPropertyType,
    }

    private static readonly byte[] Tag = "VMAD"u8.ToArray();
    private const int TagLength = 4;

    /// <summary>The version, object format and script count before the first script.</summary>
    private const int ScriptsStart = 6;

    private const int StringLengthSize = 2;
    private const int BytesCutOffTheValue = 3;
    private const byte NoPropertyType = 99;

    public static ISkyrimModDisposableGetter WriteAndOpen(SkyrimMod mod, string folder, Fault fault)
    {
        var path = Path.Combine(folder, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        var bytes = File.ReadAllBytes(path);
        Break(bytes, fault);
        File.WriteAllBytes(path, bytes);
        return SkyrimMod.CreateFromBinaryOverlay(path, mod.SkyrimRelease);
    }

    private static void Break(byte[] bytes, Fault fault)
    {
        var lengthAt = bytes.AsSpan().IndexOf(Tag) + TagLength;
        var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(lengthAt));
        switch (fault)
        {
            case Fault.EndsInsideTheScriptName:
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(lengthAt), (ushort)(ScriptsStart + StringLengthSize + TestScripts.ScriptName.Length - 1));
                break;
            case Fault.EndsInsideTheLastPropertyValue:
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(lengthAt), (ushort)(length - BytesCutOffTheValue));
                break;
            case Fault.UnknownPropertyType:
                var name = Encoding.ASCII.GetBytes(TestScripts.PropertyName(0));
                bytes[bytes.AsSpan().IndexOf(name) + name.Length] = NoPropertyType;
                break;
        }
    }
}
