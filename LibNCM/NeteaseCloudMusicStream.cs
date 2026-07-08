using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibNCM;

public class NeteaseCloudMusicStream : Stream, TagLib.File.IFileAbstraction
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

    public static readonly byte[] CoreKey = [0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57];
    public static readonly byte[] ModifyKey = [0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28];

    private static readonly HttpClient SharedHttpClient = new();

    public string? FilePath { get; private set; }

    public enum NcmFormat
    {
        Mp3,
        Flac
    }
    public NcmFormat Format { get; private set; }
    public byte[]? ImageData { get; private set; }
    private readonly Stream _rawStream;
    private FileStream? _outputStream;
    private byte[] KeyBox { get; } = new byte[256];
    public NeteaseCloudMusicMetadata? Metadata { get; private set; }
    public string? AlbumPicUrl { get; private set; }

    private List<byte>? _decryptedData;
    private long _position;

    public string FormatExtension => Format.ToString().ToLowerInvariant();

    public override bool CanRead => true;

    public override bool CanSeek => true;

    // TagLib needs write access to save metadata; writing triggers DumpToMemory if not yet dumped.
    public override bool CanWrite => true;

    public override long Length => _decryptedData is not null
                ? _decryptedData.Count
                : _outputStream is not null ? _outputStream.Length : _rawStream.Length - _rawStreamOffset;

    public override long Position
    {
        get => _decryptedData is not null
                ? _position
                : _outputStream is not null ? _outputStream.Position : _rawStream.Position - _rawStreamOffset;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            if (_decryptedData is not null)
            {
                _position = value;
            }
            else if (_outputStream is not null)
            {
                _outputStream.Position = value;
            }
            else
            {
                _rawStream.Position = value + _rawStreamOffset;
            }
        }
    }

    public string Name => FilePath is null
        ? "ncm" + FormatExtension
        : Path.GetFileNameWithoutExtension(FilePath) + "." + FormatExtension;

    public Stream ReadStream => this;

    public Stream WriteStream => this;

    private long _rawStreamOffset;

    private void ReadRawExact(Span<byte> buffer)
    {
        try
        {
            _rawStream.ReadExactly(buffer);
        }
        catch (EndOfStreamException e)
        {
            throw new NcmFileFormatException("Unexpected end of stream", e);
        }
    }

    private bool IsNcmFile()
    {
        var header = new byte[8];
        try
        {
            ReadRawExact(header);
        }
        catch (NcmFileFormatException)
        {
            return false;
        }
        return BitConverter.ToUInt32(header, 0) == NcmMagic1 &&
               BitConverter.ToUInt32(header, 4) == NcmMagic2;
    }

    private void BuildKeyBox(byte[] key, int keyLen)
    {
        for (var i = 0; i < 256; i++)
            KeyBox[i] = (byte)i;

        byte lastByte = 0;
        byte keyOffset = 0;

        for (var i = 0; i < 256; i++)
        {
            byte swap = KeyBox[i];
            byte c = (byte)((swap + lastByte + key[keyOffset]) & 0xff);
            keyOffset++;
            if (keyOffset >= keyLen)
                keyOffset = 0;
            KeyBox[i] = KeyBox[c];
            KeyBox[c] = swap;
            lastByte = c;
        }
    }

    private void Decrypt(Span<byte> buffer, long position)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var j = (int)((position + i + 1) & 0xff);
            buffer[i] ^= KeyBox[(KeyBox[j] + KeyBox[(KeyBox[j] + j) & 0xff]) & 0xff];
        }
    }

    public void DumpToMemory()
    {
        if (_decryptedData is not null) return;

        var currentPosition = Position;
        var buffer = new byte[0x8000];
        var decrypted = new List<byte>();
        while (true)
        {
            var n = Read(buffer);
            if (n == 0) break;
            decrypted.AddRange(buffer.AsSpan()[..n]);
        }

        // If we read from the output stream, dispose it to avoid handle leak.
        _outputStream?.Dispose();
        _outputStream = null;

        _decryptedData = decrypted;
        _position = currentPosition;
    }

    public void DumpToFile(string path, string name)
    {
        FileStream output;
        try
        {
            _ = Directory.CreateDirectory(path);
            output = File.Create(Path.Join(path, $"{name}.{FormatExtension}"));
        }
        catch (Exception e)
        {
            throw new NcmException($"Create output file failed at \"{path}\"", e);
        }

        try
        {
            var buffer = new byte[0x8000];
            while (true)
            {
                var n = Read(buffer);
                if (n == 0) break;
                output.Write(buffer.AsSpan()[..n]);
            }
            _outputStream = output;
            _outputStream.Flush();
            _outputStream.Position = 0;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public async Task DumpToFileAsync(string path, string name)
    {
        FileStream output;
        try
        {
            _ = Directory.CreateDirectory(path);
            output = File.Create(Path.Join(path, $"{name}.{FormatExtension}"));
        }
        catch (Exception e)
        {
            throw new NcmException($"Create output file failed at \"{path}\"", e);
        }

        try
        {
            var buffer = new byte[0x8000];
            while (true)
            {
                var n = await ReadAsync(buffer).ConfigureAwait(false);
                if (n == 0) break;
                await output.WriteAsync(buffer.AsMemory()[..n]).ConfigureAwait(false);
            }
            _outputStream = output;
            await _outputStream.FlushAsync().ConfigureAwait(false);
            _outputStream.Position = 0;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public void FixMetadata(bool fetchAlbumImageFromRemote)
    {
        FixMetadataAsync(fetchAlbumImageFromRemote).GetAwaiter().GetResult();
    }

    public async Task FixMetadataAsync(bool fetchAlbumImageFromRemote)
    {
        if ((ImageData is null || ImageData.Length <= 0) && fetchAlbumImageFromRemote)
        {
            if (!string.IsNullOrEmpty(AlbumPicUrl))
            {
                try
                {
                    var response = await SharedHttpClient.GetAsync(AlbumPicUrl).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        ImageData = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    throw new NcmMetadataException("Fetch album image failed", e);
                }
            }
        }
        using var tfile = TagLib.File.Create(this);
        tfile.Tag.Title = Metadata?.Name;
        tfile.Tag.Performers = Metadata?.Artist.ToArray();
        tfile.Tag.Album = Metadata?.Album;
        tfile.Tag.Description = Metadata?.Description;

        if (ImageData is { Length: > 0 })
        {
            var pic = new TagLib.Picture(ImageData);
            tfile.Tag.Pictures = [pic];
        }
        try
        {
            tfile.Save();
        }
        catch (Exception e)
        {
            throw new NcmMetadataException("Save metadata failed", e);
        }
    }

    private void Initialize()
    {
        if (!IsNcmFile())
            throw new NcmFileFormatException("Not a NCM file");

        _ = _rawStream.Seek(2, SeekOrigin.Current); // skip 2-byte gap after magic

        DecryptKey();
        DecryptMetadata();
        ReadCoverImage();
        DetectFormat();
    }

    private void DecryptKey()
    {
        var lenBytes = new byte[4];
        ReadRawExact(lenBytes);
        var keyLen = (int)BitConverter.ToUInt32(lenBytes);
        if (keyLen is <= 0 or > MaxKeyLength)
            throw new NcmFileFormatException("Invalid key length");

        var keyData = new byte[keyLen];
        ReadRawExact(keyData);

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
        BuildKeyBox(keyBytes, keyBytes.Length);
    }

    private void DecryptMetadata()
    {
        var lenBytes = new byte[4];
        ReadRawExact(lenBytes);
        var metadataLen = (int)BitConverter.ToUInt32(lenBytes);

        if (metadataLen <= 0)
        {
            Metadata = null;
            return;
        }

        if (metadataLen > MaxMetadataLength)
            throw new NcmFileFormatException("Invalid metadata length");

        var modifyData = new byte[metadataLen];
        ReadRawExact(modifyData);

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

        AlbumPicUrl = GetAlbumPicUrl(metadataString);

        Metadata = new NeteaseCloudMusicMetadata(metadataString)
        {
            Description = descriptionData
        };
    }

    private void ReadCoverImage()
    {
        try
        {
            _ = _rawStream.Seek(5, SeekOrigin.Current); // skip CRC (4 bytes) + gap (1 byte)
        }
        catch (Exception e)
        {
            throw new NcmFileFormatException("Seek gap failed", e);
        }

        var coverFrameLenBytes = new byte[4];
        var coverFrameDataLenBytes = new byte[4];

        ReadRawExact(coverFrameLenBytes);
        ReadRawExact(coverFrameDataLenBytes);

        var coverFrameLen = (int)BitConverter.ToUInt32(coverFrameLenBytes);
        var coverFrameDataLen = (int)BitConverter.ToUInt32(coverFrameDataLenBytes);

        if (coverFrameLen < 0 || coverFrameDataLen < 0 || coverFrameDataLen > coverFrameLen)
            throw new NcmFileFormatException("Invalid cover frame length");

        if (coverFrameDataLen > 0)
        {
            ImageData = new byte[coverFrameDataLen];
            ReadRawExact(ImageData);
        }
        _ = _rawStream.Seek(coverFrameLen - coverFrameDataLen, SeekOrigin.Current);

        _rawStreamOffset = _rawStream.Position;
    }

    private void DetectFormat()
    {
        var formatBuffer = new byte[4];
        if (Read(formatBuffer) != 4)
            throw new NcmFileFormatException("Read format failed");

        Format = formatBuffer[0] == Mp3Magic0 && formatBuffer[1] == Mp3Magic1 && formatBuffer[2] == Mp3Magic2
            ? NcmFormat.Mp3
            : formatBuffer[0] == FlacMagic0 && formatBuffer[1] == FlacMagic1 &&
                 formatBuffer[2] == FlacMagic2 && formatBuffer[3] == FlacMagic3
                ? NcmFormat.Flac
                : throw new NcmFileFormatException("Unknown audio format");

        _ = _rawStream.Seek(-4, SeekOrigin.Current);
    }

    public NeteaseCloudMusicStream(string filePath)
    {
        FilePath = filePath;
        try
        {
            _rawStream = File.OpenRead(FilePath);
        }
        catch (Exception e)
        {
            throw new NcmFileFormatException("Open file failed", e);
        }
        Initialize();
    }

    public NeteaseCloudMusicStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _rawStream = stream;
        Initialize();
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

    public override void Flush()
    {
        _outputStream?.Flush();
        _rawStream.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (_decryptedData is not null)
        {
            var pos = (int)_position;
            if (pos >= _decryptedData.Count)
                return 0;
            var n = Math.Min(buffer.Length, _decryptedData.Count - pos);
            for (var i = 0; i < n; i++)
                buffer[i] = _decryptedData[pos + i];
            _position = pos + n;
            return n;
        }

        if (_outputStream is not null)
            return _outputStream.Read(buffer);

        var pos2 = _rawStream.Position - _rawStreamOffset;
        var n2 = _rawStream.Read(buffer);
        Decrypt(buffer[..n2], pos2);
        return n2;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_decryptedData is not null)
        {
            var pos = (int)_position;
            if (pos >= _decryptedData.Count)
                return 0;
            var n = Math.Min(buffer.Length, _decryptedData.Count - pos);
            for (var i = 0; i < n; i++)
                buffer.Span[i] = _decryptedData[pos + i];
            _position = pos + n;
            return n;
        }

        if (_outputStream is not null)
            return await _outputStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

        var pos2 = _rawStream.Position - _rawStreamOffset;
        var n2 = await _rawStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Decrypt(buffer.Span[..n2], pos2);
        return n2;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
    }

    public override void SetLength(long value)
    {
        if (_outputStream is not null)
        {
            _outputStream.SetLength(value);
            return;
        }

        if (_decryptedData is null)
            DumpToMemory();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, int.MaxValue, nameof(value));
        var intValue = (int)value;
        if (Length > value)
        {
            _decryptedData!.RemoveRange(intValue, (int)(Length - value));
        }
        else if (Length < value)
        {
            _decryptedData!.AddRange(new byte[value - Length]);
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_outputStream is not null)
        {
            _outputStream.Write(buffer);
            return;
        }

        if (_decryptedData is null)
            DumpToMemory();

        if (_position > int.MaxValue)
            throw new InvalidOperationException("Position exceeds int.MaxValue for in-memory stream");

        var pos = (int)_position;
        var endPos = pos + buffer.Length;

        if (endPos > _decryptedData!.Count)
            _decryptedData.AddRange(new byte[endPos - _decryptedData.Count]);

        for (var i = 0; i < buffer.Length; i++)
            _decryptedData[pos + i] = buffer[i];

        _position = endPos;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_outputStream is not null)
            return _outputStream.WriteAsync(buffer, cancellationToken);
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    // TagLib.File.IFileAbstraction.CloseStream: stream lifecycle is managed by this class.
    public void CloseStream(Stream stream)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _outputStream?.Dispose();
            _rawStream.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_outputStream is not null)
            await _outputStream.DisposeAsync().ConfigureAwait(false);
        await _rawStream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
