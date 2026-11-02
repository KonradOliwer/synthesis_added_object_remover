using System.Text;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;

namespace AddedObjectRemover.Tests.Meshes;

/// <summary>A mesh in an archive whose stored data is corrupt is reported as an archive extract failure, and the run goes on.</summary>
public sealed class CorruptArchiveTests : IDisposable
{
    private const string Mesh = @"meshes\crate.nif";
    private const string Folder = "meshes";
    private const string File = "crate.nif";
    private const uint Version = 105;
    private const uint IncludesFolderNames = 1;
    private const uint IncludesFileNames = 2;
    private const uint CompressedByDefault = 4;
    private const ushort HoldsMeshes = 1;
    private const int HeaderSize = 36;
    private const int FolderRecordSize = 24;
    private const int FileRecordSize = 16;

    /// <summary>A size an LZ4-compressed file states it has once decompressed, beyond the sizes a signed 32-bit number holds.</summary>
    private const uint SizeBeyondInt = uint.MaxValue - 1;

    /// <summary>A size an LZ4-compressed file states it has once decompressed, within a signed 32-bit number but beyond the largest array.</summary>
    private const uint SizeBeyondAnyArray = int.MaxValue;

    /// <summary>Where the first compressed block's data starts in an LZ4 frame: after the 7-byte frame header and the 4-byte block size.</summary>
    private const int FirstBlockData = 11;

    private static readonly ModKey Plugin = ModKey.FromNameAndExtension("Crates.esp");

    /// <summary>A repeating pattern, so its LZ4 frame holds a compressed block rather than a stored one.</summary>
    private static readonly byte[] MeshBytes = [.. Enumerable.Range(0, 4096).Select(value => (byte)(value % 7))];

    private static readonly byte[] NotLz4 = [.. Enumerable.Repeat((byte)0xEE, 64)];

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("aor-corrupt-archive-");

    /// <remarks>The library leaves the archive open when a compressed file's frame cannot be opened, until its handle is finalized.</remarks>
    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        _folder.Delete(recursive: true);
    }

    [Fact]
    public void AnUncompressedArchivedMeshIsRead()
    {
        WriteArchive(MeshBytes, compressed: false);

        var (bytes, problems) = LoadMesh();

        Assert.Equal(MeshBytes, bytes);
        Assert.Empty(problems);
    }

    [Fact]
    public void ACompressedArchivedMeshIsRead()
    {
        WriteArchive(Compressed((uint)MeshBytes.Length, Lz4Frame(MeshBytes)), compressed: true);

        var (bytes, problems) = LoadMesh();

        Assert.Equal(MeshBytes, bytes);
        Assert.Empty(problems);
    }

    [Fact]
    public void ACompressedMeshWhoseStatedSizeIsBeyondASigned32BitNumberIsAnExtractFailure()
    {
        WriteArchive(Compressed(SizeBeyondInt, Lz4Frame(MeshBytes)), compressed: true);

        AssertExtractFailed(nameof(EndOfStreamException));
    }

    [Fact]
    public void ACompressedMeshWhoseStatedSizeIsBeyondAnyArrayIsAnExtractFailure()
    {
        WriteArchive(Compressed(SizeBeyondAnyArray, Lz4Frame(MeshBytes)), compressed: true);

        AssertExtractFailed(nameof(EndOfStreamException));
    }

    [Fact]
    public void ACompressedMeshWhoseDataIsNotAnLz4FrameIsAnExtractFailure()
    {
        WriteArchive(Compressed((uint)MeshBytes.Length, NotLz4), compressed: true);

        AssertExtractFailed(nameof(Mutagen.Bethesda.Archives.Exceptions.ArchiveException));
    }

    [Fact]
    public void ACompressedMeshWhoseLz4BlockIsCorruptIsAnExtractFailure()
    {
        var frame = Lz4Frame(MeshBytes);
        frame.AsSpan(FirstBlockData).Fill(0xFF);
        WriteArchive(Compressed((uint)MeshBytes.Length, frame), compressed: true);

        AssertExtractFailed(nameof(InvalidOperationException));
    }

    private void AssertExtractFailed(string exceptionType)
    {
        var (bytes, problems) = LoadMesh();

        Assert.Null(bytes);
        var problem = Assert.Single(problems);
        Assert.Equal((AssetProblemKind.ArchiveExtractFailed, exceptionType), (problem.Kind, problem.ExceptionType));
    }

    private static byte[] Lz4Frame(byte[] data)
    {
        using var frame = new MemoryStream();
        using (var encoder = K4os.Compression.LZ4.Streams.LZ4Stream.Encode(frame, leaveOpen: true)) encoder.Write(data);
        return frame.ToArray();
    }

    /// <summary>A compressed file's stored data: the size it states it has once decompressed, then its LZ4 frame.</summary>
    private static byte[] Compressed(uint statedSize, byte[] frame) => [.. BitConverter.GetBytes(statedSize), .. frame];

    private (byte[]? Bytes, IReadOnlyList<AssetProblem> Problems) LoadMesh()
    {
        var problems = new AssetProblemLog();
        var bytes = new MeshFileSource(_folder.FullName, GameRelease.SkyrimSE, [Plugin], problems).Load(Mesh).Bytes;
        return (bytes, problems.Since(default));
    }

    /// <summary>Writes the plugin's archive (a Skyrim SE archive, version 105) holding the one mesh with the given stored data.</summary>
    private void WriteArchive(byte[] storedData, bool compressed)
    {
        var folderName = Encoding.ASCII.GetBytes(Folder);
        var fileName = Encoding.ASCII.GetBytes(File);
        var fileNamesLength = fileName.Length + 1;
        var folderBlockSize = 1 + folderName.Length + 1 + FileRecordSize;
        var dataOffset = HeaderSize + FolderRecordSize + folderBlockSize + fileNamesLength;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("BSA\0"u8);
            writer.Write(Version);
            writer.Write((uint)HeaderSize);
            writer.Write(IncludesFolderNames | IncludesFileNames | (compressed ? CompressedByDefault : 0));
            writer.Write(1u);
            writer.Write(1u);
            writer.Write((uint)(folderName.Length + 1));
            writer.Write((uint)fileNamesLength);
            writer.Write(HoldsMeshes);
            writer.Write((ushort)0);

            writer.Write(0UL);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write((ulong)(HeaderSize + FolderRecordSize + fileNamesLength));

            writer.Write((byte)(folderName.Length + 1));
            writer.Write(folderName);
            writer.Write((byte)0);
            writer.Write(0UL);
            writer.Write((uint)storedData.Length);
            writer.Write((uint)dataOffset);

            writer.Write(fileName);
            writer.Write((byte)0);
            writer.Write(storedData);
        }
        System.IO.File.WriteAllBytes(Path.Combine(_folder.FullName, Plugin.Name + ".bsa"), stream.ToArray());
    }
}
