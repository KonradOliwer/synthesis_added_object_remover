using System.Buffers.Binary;
using Mutagen.Bethesda.Skyrim;

namespace AddedObjectRemover.Tests.Fixtures;

/// <summary>
/// Writes a plugin with collision layer records (COLL), breaks the first one's count of the layers it collides with (INTV) so it
/// names far more layers than its list holds, which the game library cannot parse, and opens the plugin as the library reads load orders.
/// </summary>
public static class BrokenCollisionLayerData
{
    private static readonly byte[] RecordTag = "COLL"u8.ToArray();
    private static readonly byte[] CountTag = "INTV"u8.ToArray();

    /// <summary>The tag and the length before a subrecord's data.</summary>
    private const int SubrecordHeaderSize = 6;

    private const uint CountAddedBeyondTheList = 100;

    public static ISkyrimModDisposableGetter WriteAndOpen(SkyrimMod mod, string folder)
    {
        var path = Path.Combine(folder, mod.ModKey.FileName);
        mod.WriteToBinary(path);
        var bytes = File.ReadAllBytes(path);
        Break(bytes);
        File.WriteAllBytes(path, bytes);
        return SkyrimMod.CreateFromBinaryOverlay(path, mod.SkyrimRelease);
    }

    private static void Break(byte[] bytes)
    {
        var records = bytes.AsSpan().IndexOf(RecordTag);
        var countAt = records + bytes.AsSpan(records).IndexOf(CountTag) + SubrecordHeaderSize;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(countAt));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(countAt), count + CountAddedBeyondTheList);
    }
}
