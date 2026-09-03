using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibNCM;

/// <summary>
///   Pure, state-less parser for the NCM container header. Given a stream positioned
///   at the start of an NCM file it validates the magic, decrypts the key and
///   metadata sections, extracts the embedded cover image, detects the output
///   audio format, and leaves the stream positioned at the start of the (still
///   encrypted) audio payload.
/// </summary>
/// <remarks>
///   This is the deep, directly-testable core of NCM parsing: no file I/O, no
///   network, no output state — only the format itself.
/// </remarks>
public static class NcmHeaderParser
{
    private const uint NcmMagic1 = 0x4E455443; // "CTEN" little-endian
    private const uint NcmMagic2 = 0x4D414446; // "FDAM" little-endian
    private const byte KeyXorMask = 0x64;
    private const byte MetadataXorMask = 0x63;
    private const string KeyPrefix = "neteasecloudmusic";
    private const string MetadataPrefix = "163 key(Don't modify):";
    private const string MusicPrefix = "music:";
    private const int MaxKeyLength = 1024;
    private const int MaxMetadataLength = 1 << 20; // 1 MB

    // MP3 format magic: "ID3"
    private const byte Mp3Magic0 = 0x49;
    private const byte Mp3Magic1 = 0x44;
    private const byte Mp3Magic2 = 0x33;
    // FLAC format magic: "fLaC"
    private const byte FlacMagic0 = 0x66;
    private const byte FlacMagic1 = 0x4C;
    private const byte FlacMagic2 = 0x61;
    private const byte FlacMagic3 = 0x43;

    internal static readonly byte[] CoreKey = [0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57];
    internal static readonly byte[] ModifyKey = [0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28];

    /// <summary>
    ///   Parses the NCM header from <paramref name="stream"/>. On success the stream
    ///   is left positioned at the start of the audio payload; on failure an
    ///   <see cref="NcmException"/> subtype is thrown and the stream position is
    ///   unspecified.
    /// </summary>
    public static NcmHeader Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!IsNcmFile(stream))
            throw new NcmFileFormatException("Not a NCM file");

        _ = stream.Seek(2, SeekOrigin.Current); // skip 2-byte gap after magic

        var keyBox = DecryptKey(stream);
        var (metadata, albumPicUrl) = DecryptMetadata(stream);
        var imageData = ReadCoverImage(stream);
        var format = DetectFormat(stream, keyBox);

        return new NcmHeader(format, metadata, imageData, albumPicUrl, keyBox);
    }

    private static bool IsNcmFile(Stream stream)
    {
        byte[] header;
        try
        {
            header = ReadExact(stream, 8);
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        return BitConverter.ToUInt32(header, 0) == NcmMagic1 &&
               BitConverter.ToUInt32(header, 4) == NcmMagic2;
    }

    private static byte[] DecryptKey(Stream stream)
    {
        var lenBytes = ReadExact(stream, 4);
        var keyLen = (int)BitConverter.ToUInt32(lenBytes);
        if (keyLen is <= 0 or > MaxKeyLength)
            throw new NcmFileFormatException("Invalid key length");

        var keyData = ReadExact(stream, keyLen);

        for (var i = 0; i < keyData.Length; i++)
            keyData[i] ^= KeyXorMask;

        byte[] decryptedKeyData;
        try
        {
            decryptedKeyData = AesEcbPure.Decrypt(CoreKey, keyData);
        }
        catch (Exception e)
        {
            throw new NcmDecryptionException("Decrypt key failed", e);
        }

        // Validate and skip "neteasecloudmusic" prefix (17 bytes)
        if (decryptedKeyData.Length < KeyPrefix.Length + 1)
            throw new NcmDecryptionException("Decrypted key too short");
        var prefix = Encoding.UTF8.GetString(decryptedKeyData, 0, KeyPrefix.Length);
        if (prefix != KeyPrefix)
            throw new NcmDecryptionException("Invalid key prefix");

        var keyBytes = decryptedKeyData[KeyPrefix.Length..];
        return NcmAudioStream.BuildKeyBox(keyBytes);
    }

    private static (NeteaseCloudMusicMetadata? Metadata, string? AlbumPicUrl) DecryptMetadata(Stream stream)
    {
        var lenBytes = ReadExact(stream, 4);
        var metadataLen = (int)BitConverter.ToUInt32(lenBytes);

        if (metadataLen <= 0)
        {
            return (null, null);
        }

        if (metadataLen > MaxMetadataLength)
            throw new NcmFileFormatException("Invalid metadata length");

        var modifyData = ReadExact(stream, metadataLen);

        for (var i = 0; i < modifyData.Length; i++)
            modifyData[i] ^= MetadataXorMask;

        var descriptionData = Encoding.UTF8.GetString(modifyData);

        // Validate and skip "163 key(Don't modify):" prefix (22 chars)
        if (!descriptionData.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            throw new NcmDecryptionException("Invalid metadata prefix");

        var swapModifyData = descriptionData[MetadataPrefix.Length..];

        byte[] modifyOutData;
        try
        {
            modifyOutData = Convert.FromBase64String(swapModifyData);
        }
        catch (Exception e)
        {
            throw new NcmDecryptionException("Base64 decode modify data failed", e);
        }

        byte[] modifyDecryptData;
        try
        {
            modifyDecryptData = AesEcbPure.Decrypt(ModifyKey, modifyOutData);
        }
        catch (Exception e)
        {
            throw new NcmDecryptionException("Decrypt modify data failed", e);
        }

        // Validate and skip "music:" prefix (6 bytes)
        if (modifyDecryptData.Length < MusicPrefix.Length)
            throw new NcmDecryptionException("Decrypted metadata too short");
        var musicPrefix = Encoding.UTF8.GetString(modifyDecryptData, 0, MusicPrefix.Length);
        if (musicPrefix != MusicPrefix)
            throw new NcmDecryptionException("Invalid music prefix");

        var metadataString = Encoding.UTF8.GetString(modifyDecryptData[MusicPrefix.Length..]);

        var albumPicUrl = GetAlbumPicUrl(metadataString);

        var metadata = new NeteaseCloudMusicMetadata(metadataString)
        {
            Description = descriptionData
        };
        return (metadata, albumPicUrl);
    }

    private static byte[]? ReadCoverImage(Stream stream)
    {
        try
        {
            _ = stream.Seek(5, SeekOrigin.Current); // skip CRC (4 bytes) + gap (1 byte)
        }
        catch (Exception e)
        {
            throw new NcmFileFormatException("Seek gap failed", e);
        }

        var coverFrameLenBytes = ReadExact(stream, 4);
        var coverFrameDataLenBytes = ReadExact(stream, 4);

        var coverFrameLen = (int)BitConverter.ToUInt32(coverFrameLenBytes);
        var coverFrameDataLen = (int)BitConverter.ToUInt32(coverFrameDataLenBytes);

        if (coverFrameLen < 0 || coverFrameDataLen < 0 || coverFrameDataLen > coverFrameLen)
            throw new NcmFileFormatException("Invalid cover frame length");

        byte[]? imageData = null;
        if (coverFrameDataLen > 0)
        {
            imageData = ReadExact(stream, coverFrameDataLen);
        }
        _ = stream.Seek(coverFrameLen - coverFrameDataLen, SeekOrigin.Current);

        return imageData;
    }

    private static NcmFormat DetectFormat(Stream stream, byte[] keyBox)
    {
        // The audio magic is stored encrypted like the rest of the payload, so it
        // must be decrypted (at relative position 0) before comparing.
        var formatBuffer = ReadExact(stream, 4);
        KeyBoxCipher.Decrypt(keyBox, formatBuffer, 0);

        NcmFormat format = formatBuffer[0] == Mp3Magic0 && formatBuffer[1] == Mp3Magic1 && formatBuffer[2] == Mp3Magic2
            ? NcmFormat.Mp3
            : formatBuffer[0] == FlacMagic0 && formatBuffer[1] == FlacMagic1 &&
                 formatBuffer[2] == FlacMagic2 && formatBuffer[3] == FlacMagic3
                ? NcmFormat.Flac
                : throw new NcmFileFormatException("Unknown audio format");

        // rewind so the stream sits at the start of the audio payload
        _ = stream.Seek(-4, SeekOrigin.Current);
        return format;
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        try
        {
            var buffer = new byte[count];
            stream.ReadExactly(buffer);
            return buffer;
        }
        catch (EndOfStreamException e)
        {
            throw new NcmFileFormatException("Unexpected end of stream", e);
        }
    }

    private static string? GetAlbumPicUrl(string meta)
    {
        try
        {
            var json = JsonNode.Parse(meta) as JsonObject;
            return json?["albumPic"]?.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}