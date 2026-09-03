using LibNCM;
using Xunit;

namespace Ncm.Tests;

public class NcmProcessorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ncm-proc-tests-" + Guid.NewGuid().ToString("N"));

    public NcmProcessorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ProcessAsync_WritesFileAndEmbedsMetadata()
    {
        var inputPath = Path.Combine(_tempDir, "input.ncm");
        await File.WriteAllBytesAsync(inputPath, SyntheticNcmBuilder.BuildFile());

        var result = await NcmProcessor.ProcessAsync(
            inputPath, _tempDir, "out",
            new NcmProcessOptions { FetchCoverArt = false });

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.MetadataWarning);
        Assert.Equal("out.flac", result.OutputFileName);
        Assert.Equal(Path.Combine(_tempDir, "out.flac"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath!));

        // Tags were embedded in place, so the file is no longer the raw payload.
        var onDisk = await File.ReadAllBytesAsync(result.OutputPath!);
        Assert.NotEqual(SyntheticNcmBuilder.AudioPlaintext, onDisk);

        using var tagFile = TagLib.File.Create(result.OutputPath!);
        Assert.Equal(SyntheticNcmBuilder.SongName, tagFile.Tag.Title);
        Assert.Equal(SyntheticNcmBuilder.AlbumName, tagFile.Tag.Album);
    }

    [Fact]
    public async Task ProcessToBytesAsync_ReturnsTaggedBytes()
    {
        await using var ms = new MemoryStream(SyntheticNcmBuilder.BuildFile());

        var result = await NcmProcessor.ProcessToBytesAsync(ms, "song.ncm", new NcmProcessOptions { FetchCoverArt = false });

        Assert.True(result.Success);
        Assert.Equal("song.flac", result.OutputFileName);
        Assert.Null(result.OutputPath);
        Assert.NotNull(result.Data);
        Assert.NotEqual(SyntheticNcmBuilder.AudioPlaintext, result.Data); // tags were embedded
        Assert.Equal(SyntheticNcmBuilder.SongName, ReadTitle(result.Data!, "roundtrip.flac"));
    }

    [Fact]
    public async Task ProcessAsync_InvalidFile_FailsGracefully()
    {
        var badPath = Path.Combine(_tempDir, "bad.ncm");
        await File.WriteAllBytesAsync(badPath, new byte[64]);

        var result = await NcmProcessor.ProcessAsync(badPath, _tempDir, "out");

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [Fact]
    public async Task ProcessAsync_MetadataFailure_IsNonFatalWarning()
    {
        var inputPath = Path.Combine(_tempDir, "input.ncm");
        await File.WriteAllBytesAsync(inputPath, SyntheticNcmBuilder.BuildFile(withCover: false));
        var brokenProvider = new FakeCoverArtProvider(() => throw new HttpRequestException("offline"));

        var result = await NcmProcessor.ProcessAsync(
            inputPath, _tempDir, "out",
            new NcmProcessOptions { FetchCoverArt = true },
            brokenProvider);

        // The audio is on disk even though metadata embedding failed.
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.False(string.IsNullOrEmpty(result.MetadataWarning));
        Assert.True(File.Exists(result.OutputPath!));
    }

    private static string? ReadTitle(byte[] data, string name)
    {
        using var file = TagLib.File.Create(new TestFileAbstraction(name, new MemoryStream(data)));
        return file.Tag.Title;
    }

    private sealed class TestFileAbstraction(string name, MemoryStream stream) : TagLib.File.IFileAbstraction
    {
        public string Name { get; } = name;
        public Stream ReadStream => stream;
        public Stream WriteStream => stream;
        public void CloseStream(Stream stream) { }
    }
}