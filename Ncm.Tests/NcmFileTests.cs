using LibNCM;
using Xunit;

namespace Ncm.Tests;

public class NcmFileTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "ncm-tests-" + Guid.NewGuid().ToString("N"));

    public NcmFileTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private static MemoryStream OpenStreamOf(byte[] fileBytes)
    {
        var ms = new MemoryStream(fileBytes);
        return ms;
    }

    [Fact]
    public async Task OpenFromMemory_DumpToBytes_ReturnsPlaintext()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        using var ncm = NcmFile.Open(new MemoryStream(bytes));

        var decrypted = await ncm.DumpToBytesAsync();

        Assert.Equal(SyntheticNcmBuilder.AudioPlaintext, decrypted);
    }

    [Fact]
    public async Task OpenFromPath_DumpToFile_WritesFormattedFile()
    {
        var ncmPath = Path.Combine(_tempDir, "input.ncm");
        await File.WriteAllBytesAsync(ncmPath, SyntheticNcmBuilder.BuildFile());

        using var ncm = NcmFile.Open(ncmPath);
        await ncm.DumpToFileAsync(_tempDir, "output");

        var outputPath = Path.Combine(_tempDir, "output.flac");
        Assert.True(File.Exists(outputPath));
        Assert.Equal(SyntheticNcmBuilder.AudioPlaintext, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task DumpToFile_IsIdempotent_NoHandleLeak()
    {
        var ncmPath = Path.Combine(_tempDir, "input.ncm");
        await File.WriteAllBytesAsync(ncmPath, SyntheticNcmBuilder.BuildFile());

        using var ncm = NcmFile.Open(ncmPath);
        await ncm.DumpToFileAsync(_tempDir, "a");
        await ncm.DumpToFileAsync(_tempDir, "b");
        await ncm.DumpToFileAsync(_tempDir, "c");

        Assert.True(File.Exists(Path.Combine(_tempDir, "a.flac")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "b.flac")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "c.flac")));
        Assert.Equal(SyntheticNcmBuilder.AudioPlaintext, await File.ReadAllBytesAsync(Path.Combine(_tempDir, "c.flac")));
    }

    [Fact]
    public async Task FixMetadata_InMemory_RoundTripsThroughTagLib()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        await using var ncm = NcmFile.Open(OpenStreamOf(bytes));

        _ = await ncm.DumpToBytesAsync();
        await ncm.FixMetadataAsync(fetchCoverArt: false);
        var tagged = await ncm.DumpToBytesAsync();

        Assert.NotEqual(SyntheticNcmBuilder.AudioPlaintext, tagged); // tags were written
        var title = ReadTitle(new MemoryStream(tagged), "roundtrip.flac");
        Assert.Equal(SyntheticNcmBuilder.SongName, title);
    }

    [Fact]
    public async Task FixMetadata_OnDisk_RoundTripsThroughTagLib()
    {
        var ncmPath = Path.Combine(_tempDir, "input.ncm");
        await File.WriteAllBytesAsync(ncmPath, SyntheticNcmBuilder.BuildFile());

        await using var ncm = NcmFile.Open(ncmPath);
        await ncm.DumpToFileAsync(_tempDir, "song");
        await ncm.FixMetadataAsync(fetchCoverArt: false);

        var outputPath = Path.Combine(_tempDir, "song.flac");
        using var tagFile = TagLib.File.Create(outputPath);
        Assert.Equal(SyntheticNcmBuilder.SongName, tagFile.Tag.Title);
        Assert.Equal(SyntheticNcmBuilder.AlbumName, tagFile.Tag.Album);
        Assert.Equal(SyntheticNcmBuilder.Artists, tagFile.Tag.Performers);
        Assert.NotNull(tagFile.Tag.Pictures);
        Assert.NotEmpty(tagFile.Tag.Pictures);
    }

    [Fact]
    public async Task FixMetadata_WithNoMetadataOrCover_IsNoOp()
    {
        var bytes = SyntheticNcmBuilder.BuildFile(withMetadata: false, withCover: false);
        await using var ncm = NcmFile.Open(OpenStreamOf(bytes));

        var before = await ncm.DumpToBytesAsync();
        await ncm.FixMetadataAsync(fetchCoverArt: false);
        var after = await ncm.DumpToBytesAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task FixMetadata_FetchCoverArt_UsesInjectedProvider()
    {
        var fetchedCover = new byte[] { 0xFF, 0xD8, 0xAA, 0xBB };
        var provider = new FakeCoverArtProvider(() => fetchedCover);
        var bytes = SyntheticNcmBuilder.BuildFile(withCover: false); // no embedded image ⇒ fetch path
        await using var ncm = NcmFile.Open(OpenStreamOf(bytes), provider);

        await ncm.FixMetadataAsync(fetchCoverArt: true);

        Assert.Equal(1, provider.Calls);
        Assert.Equal(fetchedCover, ncm.ImageData);
        var tagged = await ncm.DumpToBytesAsync();
        using var tagFile = TagLib.File.Create(new TestFileAbstraction("out.flac", new MemoryStream(tagged)));
        Assert.NotEmpty(tagFile.Tag.Pictures);
        Assert.Equal(fetchedCover.Length, tagFile.Tag.Pictures[0].Data.Data.Length);
    }

    [Fact]
    public async Task FixMetadata_ProviderFailure_Throws()
    {
        var provider = new FakeCoverArtProvider(() => throw new HttpRequestException("network down"));
        var bytes = SyntheticNcmBuilder.BuildFile(withCover: false);
        await using var ncm = NcmFile.Open(OpenStreamOf(bytes), provider);

        await Assert.ThrowsAsync<NcmMetadataException>(() => ncm.FixMetadataAsync(fetchCoverArt: true));
    }

    [Fact]
    public async Task FixMetadata_EmbeddedCover_PreventsRemoteFetch()
    {
        var provider = new FakeCoverArtProvider(() => throw new InvalidOperationException("must not be called"));
        var bytes = SyntheticNcmBuilder.BuildFile(withCover: true); // embedded image ⇒ no fetch needed
        await using var ncm = NcmFile.Open(OpenStreamOf(bytes), provider);

        await ncm.FixMetadataAsync(fetchCoverArt: true);

        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void OpenRejectsNonNcmStream()
    {
        using var ms = new MemoryStream(new byte[32]);
        Assert.Throws<NcmFileFormatException>(() => NcmFile.Open(ms));
    }

    [Fact]
    public void OpenRejectsMissingFile()
    {
        Assert.Throws<NcmFileFormatException>(() => NcmFile.Open(Path.Combine(_tempDir, "does-not-exist.ncm")));
    }

    [Fact]
    public void OutputFileNameFor_AppliesFormatExtension()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        using var ncm = NcmFile.Open(OpenStreamOf(bytes));

        Assert.Equal("song.flac", ncm.OutputFileNameFor("song.ncm"));
    }

    private static string? ReadTitle(MemoryStream stream, string name)
    {
        using var file = TagLib.File.Create(new TestFileAbstraction(name, stream));
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