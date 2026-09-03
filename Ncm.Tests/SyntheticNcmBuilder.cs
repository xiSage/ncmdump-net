using System.Security.Cryptography;
using System.Text;
using LibNCM;

namespace Ncm.Tests;

/// <summary>
///   Builds deterministic synthetic NCM files in memory so the parser/decryption
///   pipeline can be tested without shipping real (copyrighted) samples.
///   Encryption fixtures are produced with the platform AES implementation, which
///   cross-validates the hand-written <see cref="AesEcbPure"/> decryption.
/// </summary>
internal static class SyntheticNcmBuilder
{
    public static readonly byte[] KeyBytes = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];

    public const string SongName = "Test Song";
    public const string AlbumName = "Test Album";
    public static readonly string[] Artists = ["Artist One"];
    public const string CoverUrl = "http://example.com/cover.jpg";

    /// <summary>Sample JSON matching the real NetEase metadata schema.</summary>
    private static readonly string MetadataJson =
        $$"""{"musicName":"{{SongName}}","album":"{{AlbumName}}","artist":[["Artist One"]],"bitrate":320000,"duration":265000,"format":"flac","albumPic":"{{CoverUrl}}"}""";

    /// <summary>Small JPEG-ish payload used as embedded cover art.</summary>
    public static byte[] CoverImage =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    /// <summary>
    ///   Audio plaintext: a structurally valid minimal FLAC (magic + STREAMINFO)
    ///   so TagLib can open and tag it in metadata-round-trip tests.
    /// </summary>
    public static byte[] AudioPlaintext { get; } = BuildMinimalFlac();

    /// <summary>
    ///   Builds a complete, valid NCM file as bytes. The audio payload is
    ///   "encrypted" with the same key-box XOR used in reverse by the decrypting
    ///   stream (the operation is its own inverse).
    /// </summary>
    public static byte[] BuildFile(
        bool withMetadata = true,
        bool withCover = true,
        byte[]? audioPlaintext = null,
        byte[]? keyBytes = null,
        bool brokenCover = false)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        w.Write("CTENFDAM"u8);                       // magic (8 bytes)
        w.Write(new byte[2]);                        // gap after magic
        WriteKeySection(w, keyBytes ?? KeyBytes);
        WriteMetadataSection(w, withMetadata);
        WriteCoverSection(w, withCover, brokenCover);
        AudioOut(w, audioPlaintext ?? AudioPlaintext, keyBytes ?? KeyBytes);
        return ms.ToArray();
    }

    private static byte[] BuildMinimalFlac()
    {
        var ms = new MemoryStream();
        ms.Write("fLaC"u8);
        // STREAMINFO metadata block: last-block-flag=1 (0x80), type=0, length=34.
        // Marking it as the LAST metadata block matters: whatever follows is audio
        // frame data that TagLib must not try to parse as another block header.
        ms.Write([0x80, 0x00, 0x00, 0x22]);

        var streamInfo = new byte[34];
        streamInfo[0] = 0x00; streamInfo[1] = 0x10;  // min blocksize = 16
        streamInfo[2] = 0xFF; streamInfo[3] = 0xFF;  // max blocksize = 65535
        // packed field: sample rate (20 bits) | channels (3) | bps (5) | total samples (36)
        ulong packed = ((ulong)44100 << 44) | ((ulong)2 << 41) | ((ulong)16 << 36);
        for (var i = 0; i < 8; i++)
            streamInfo[4 + i] = (byte)(packed >> (56 - 8 * i));
        // bytes 12..33 = MD5 digest (zeros are fine for TagLib)
        ms.Write(streamInfo);
        // A few bytes that look like an audio frame start, then trailing zeros.
        ms.Write([0xFF, 0xF8, 0x00, 0x00]);
        ms.Write(new byte[128]);
        return ms.ToArray();
    }

    private static void WriteKeySection(BinaryWriter w, byte[] keyBytes)
    {
        var plainKey = "neteasecloudmusic"u8.ToArray().Concat(keyBytes).ToArray();
        var cipher = AesEcbEncrypt(NcmHeaderParser.CoreKey, plainKey);
        var xorMasked = cipher.Select(b => (byte)(b ^ 0x64)).ToArray();
        w.Write(xorMasked.Length);
        w.Write(xorMasked);
    }

    private static void WriteMetadataSection(BinaryWriter w, bool withMetadata)
    {
        if (!withMetadata)
        {
            w.Write(0);
            return;
        }

        var plainMeta = Encoding.UTF8.GetBytes("music:" + MetadataJson);
        var cipherMeta = AesEcbEncrypt(NcmHeaderParser.ModifyKey, plainMeta);
        var swapModifyData = Convert.ToBase64String(cipherMeta);
        var description = Encoding.UTF8.GetBytes("163 key(Don't modify):" + swapModifyData);
        var xorMasked = description.Select(b => (byte)(b ^ 0x63)).ToArray();
        w.Write(xorMasked.Length);
        w.Write(xorMasked);
    }

    private static void WriteCoverSection(BinaryWriter w, bool withCover, bool brokenCover = false)
    {
        w.Write(new byte[5]);                        // CRC (4) + gap (1)
        if (!withCover)
        {
            w.Write(0);                              // coverFrameLen
            w.Write(0);                              // coverFrameDataLen
            return;
        }

        var cover = CoverImage;
        // brokenCover: frame length smaller than data length ⇒ parser must reject.
        var coverFrameLen = brokenCover ? cover.Length - 1 : cover.Length;
        w.Write(coverFrameLen);
        w.Write(cover.Length);
        w.Write(cover);
        if (brokenCover)
            w.Write(new byte[1]);
    }

    private static void AudioOut(BinaryWriter w, byte[] plaintext, byte[] keyBytes)
    {
        var payload = (byte[])plaintext.Clone();
        var keyBox = NcmAudioStream.BuildKeyBox(keyBytes);
        for (var i = 0; i < payload.Length; i++)
        {
            var j = (int)((i + 1) & 0xff);
            payload[i] ^= keyBox[(keyBox[j] + keyBox[(keyBox[j] + j) & 0xff]) & 0xff];
        }
        w.Write(payload);
    }

    /// <summary>Platform AES-128-ECB with PKCS#7 — used only to build fixtures.</summary>
    public static byte[] AesEcbEncrypt(byte[] key, byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }
}

/// <summary>Cover provider stub for tests: records calls, returns or throws on demand.</summary>
internal sealed class FakeCoverArtProvider(Func<byte[]> fetch) : ICoverArtProvider
{
    public int Calls { get; private set; }

    public Task<byte[]?> FetchAsync(string albumPicUrl, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<byte[]?>(fetch());
    }
}