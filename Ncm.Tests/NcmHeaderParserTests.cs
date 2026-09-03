using LibNCM;
using Xunit;

namespace Ncm.Tests;

public class NcmHeaderParserTests
{
    [Fact]
    public void ParsesSyntheticFile()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        using var ms = new MemoryStream(bytes);

        var header = NcmHeaderParser.Parse(ms);

        Assert.Equal(NcmFormat.Flac, header.Format);
        Assert.NotNull(header.Metadata);
        Assert.Equal(SyntheticNcmBuilder.SongName, header.Metadata!.Name);
        Assert.Equal(SyntheticNcmBuilder.AlbumName, header.Metadata.Album);
        Assert.Equal(SyntheticNcmBuilder.Artists, header.Metadata.Artist);
        Assert.Equal(SyntheticNcmBuilder.CoverUrl, header.AlbumPicUrl);
        Assert.Equal(SyntheticNcmBuilder.CoverImage, header.ImageData);
    }

    [Fact]
    public void LeavesStreamAtAudioStart()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        using var ms = new MemoryStream(bytes);
        var header = NcmHeaderParser.Parse(ms);

        // Parse must leave the stream exactly at the start of the (still
        // encrypted) audio payload, nothing before it (header consumed) and
        // nothing after it (payload untouched).
        var expectedOffset = bytes.Length - SyntheticNcmBuilder.AudioPlaintext.Length;
        Assert.Equal(expectedOffset, ms.Position);
        Assert.NotNull(header);
    }

    [Fact]
    public void MissingMagicThrows()
    {
        var junk = new byte[64];
        Random.Shared.NextBytes(junk);
        using var ms = new MemoryStream(junk);
        Assert.Throws<NcmFileFormatException>(() => NcmHeaderParser.Parse(ms));
    }

    [Fact]
    public void EmptyStream_IsNotAnNcmFile()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        Assert.Throws<NcmFileFormatException>(() => NcmHeaderParser.Parse(ms));
    }

    [Fact]
    public void KeyTooLongThrows()
    {
        using var ms = new MemoryStream();
        ms.Write("CTENFDAM"u8);
        ms.Write(new byte[2]);
        ms.Write(BitConverter.GetBytes(4096)); // keyLen = 4096 (> 1024)
        ms.Write(new byte[32]);                // irrelevant body
        ms.Position = 0;

        Assert.Throws<NcmFileFormatException>(() => NcmHeaderParser.Parse(ms));
    }

    [Fact]
    public void InvalidKeyCiphertextThrows()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        bytes[14] ^= 0x01; // flip a byte inside the key ciphertext (after magic 8 + gap 2 + len 4)
        using var ms = new MemoryStream(bytes);

        Assert.Throws<NcmDecryptionException>(() => NcmHeaderParser.Parse(ms));
    }

    [Fact]
    public void NoMetadataYieldsNull()
    {
        var bytes = SyntheticNcmBuilder.BuildFile(withMetadata: false);
        using var ms = new MemoryStream(bytes);

        var header = NcmHeaderParser.Parse(ms);

        Assert.Null(header.Metadata);
        Assert.Null(header.AlbumPicUrl);
        Assert.NotNull(header.ImageData);
    }

    [Fact]
    public void NoCoverYieldsNoImage()
    {
        var bytes = SyntheticNcmBuilder.BuildFile(withCover: false);
        using var ms = new MemoryStream(bytes);

        var header = NcmHeaderParser.Parse(ms);

        Assert.Null(header.ImageData);
        Assert.NotNull(header.Metadata);
    }

    [Fact]
    public void InvalidCoverFrameLengthThrows()
    {
        var bytes = SyntheticNcmBuilder.BuildFile(brokenCover: true);
        using var ms = new MemoryStream(bytes);

        Assert.Throws<NcmFileFormatException>(() => NcmHeaderParser.Parse(ms));
    }

    [Fact]
    public void UnknownAudioFormatThrows()
    {
        var bogusAudio = new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45, 0x46 }; // "ABCDEF"
        var bytes = SyntheticNcmBuilder.BuildFile(audioPlaintext: bogusAudio);
        using var ms = new MemoryStream(bytes);

        Assert.Throws<NcmFileFormatException>(() => NcmHeaderParser.Parse(ms));
    }
}