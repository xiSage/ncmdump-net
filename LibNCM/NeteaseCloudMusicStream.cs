using System.Text;
using System.Text.Json.Nodes;

namespace LibNCM;

public class NeteaseCloudMusicStream : Stream, TagLib.File.IFileAbstraction
{
    public static readonly byte[] CoreKey = [0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57, 0];
    public static readonly byte[] ModifyKey = [0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28, 0];

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
    public byte[] KeyBox { get; } = new byte[256];
    public NeteaseCloudMusicMetadata? Metadata { get; private set; }
    public string? AlbumPicUrl { get; private set; }

    private List<byte>? _decryptedData;
    private long _position;

    public string FormatExtension => Format.ToString().ToLower();

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length
    {
        get
        {
            if (_decryptedData is not null)
                return _decryptedData.Count;
            if (_outputStream is not null)
                return _outputStream.Length;
            return _rawStream.Length - _rawStreamOffset;
        }
    }

    public override long Position
    {
        get
        {
            if (_decryptedData is not null)
                return _position;
            if (_outputStream is not null)
                return _outputStream.Position;
            return _rawStream.Position - _rawStreamOffset;
        }
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

    private int ReadRaw(Span<byte> buffer)
    {
        return _rawStream.Read(buffer);
    }

    private void ReadRawExact(Span<byte> buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var n = _rawStream.Read(buffer[totalRead..]);
            if (n == 0)
                throw new NcmFileFormatException("Unexpected end of stream");
            totalRead += n;
        }
    }

    private bool IsNcmFile()
    {
        var header = new byte[4];
        return ReadRaw(header) == 4 && BitConverter.ToUInt32(header) == 0x4E455443
            && ReadRaw(header) == 4 && BitConverter.ToUInt32(header) == 0x4D414446;
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
            byte c = (byte)(swap + lastByte + key[keyOffset] & 0xff);
            keyOffset++;
            if (keyOffset >= keyLen)
                keyOffset = 0;
            KeyBox[i] = KeyBox[c];
            KeyBox[c] = swap;
            lastByte = c;
        }
    }

    private void Decrypt(Span<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var j = i + 1 & 0xff;
            buffer[i] ^= KeyBox[KeyBox[j] + KeyBox[KeyBox[j] + j & 0xff] & 0xff];
        }
    }

    public void DumpToMemory()
    {
        var buffer = new byte[0x8000];
        var decrypted = new List<byte>();
        var currentPosition = Position;
        while (true)
        {
            int n;
            try
            {
                n = Read(buffer);
                if (n == 0) break;
            }
            catch (EndOfStreamException)
            {
                break;
            }

            var readData = buffer.AsSpan()[..n];
            decrypted.AddRange(readData);
        }
        _decryptedData = decrypted;
        Position = currentPosition;
    }

    public void DumpToFile(string path, string name)
    {
        FileStream output;
        try
        {
            Directory.CreateDirectory(path);
            output = File.Create(Path.Join(path, $"{name}.{FormatExtension}"));
        }
        catch (Exception e)
        {
            throw new NcmException($"Create output file failed at \"{path}\"", e);
        }
        var buffer = new byte[0x8000];
        var currentPosition = Position;
        while (true)
        {
            int n;
            try
            {
                n = Read(buffer);
                if (n == 0) break;
            }
            catch (EndOfStreamException)
            {
                break;
            }
            var readData = buffer.AsSpan()[..n];
            output.Write(readData);
        }
        Position = currentPosition;
        _outputStream = output;
        _outputStream.Flush();
    }

    public async Task DumpToFileAsync(string path, string name)
    {
        FileStream output;
        try
        {
            Directory.CreateDirectory(path);
            output = File.Create(Path.Join(path, $"{name}.{FormatExtension}"));
        }
        catch (Exception e)
        {
            throw new NcmException($"Create output file failed at \"{path}\"", e);
        }
        var buffer = new byte[0x8000];
        var currentPosition = Position;
        while (true)
        {
            int n;
            try
            {
                n = await ReadAsync(buffer);
                if (n == 0) break;
            }
            catch (EndOfStreamException)
            {
                break;
            }
            var readData = buffer.AsMemory()[..n];
            await output.WriteAsync(readData);
        }
        Position = currentPosition;
        _outputStream = output;
        await _outputStream.FlushAsync();
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
                    var response = await SharedHttpClient.GetAsync(AlbumPicUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        ImageData = await response.Content.ReadAsByteArrayAsync();
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
        if (_rawStream == null || !IsNcmFile())
            throw new NcmFileFormatException("Not a NCM file");

        _rawStream.Seek(2, SeekOrigin.Current);

        var n = new byte[4];

        ReadRawExact(n);
        var keyLen = (int)BitConverter.ToUInt32(n);
        if (keyLen <= 0)
            throw new NcmFileFormatException("Invalid key length");

        var keyData = new byte[keyLen];
        ReadRawExact(keyData);

        for (var i = 0; i < keyData.Length; i++)
            keyData[i] ^= 0x64;

        byte[] decryptedKeyData;
        try
        {
            decryptedKeyData = AesEcbDecrypt(CoreKey[..16], keyData);
        }
        catch (Exception e)
        {
            throw new NcmDecryptionException("Decrypt key failed", e);
        }

        BuildKeyBox(decryptedKeyData[17..], decryptedKeyData.Length - 17);

        ReadRawExact(n);
        var metadataLen = (int)BitConverter.ToUInt32(n);

        if (metadataLen <= 0)
        {
            Metadata = null;
        }
        else
        {
            var modifyData = new byte[metadataLen];
            ReadRawExact(modifyData);

            for (var i = 0; i < modifyData.Length; i++)
                modifyData[i] ^= 0x63;

            var descriptionData = Encoding.UTF8.GetString(modifyData);

            var swapModifyData = descriptionData[22..];

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
                modifyDecryptData = AesEcbDecrypt(ModifyKey[..16], modifyOutData);
            }
            catch (Exception e)
            {
                throw new NcmDecryptionException("Decrypt modify data failed", e);
            }

            var metadataString = Encoding.UTF8.GetString(modifyDecryptData[6..]);

            AlbumPicUrl = GetAlbumPicUrl(metadataString);

            Metadata = new NeteaseCloudMusicMetadata(metadataString)
            {
                Description = descriptionData
            };
        }

        try
        {
            _rawStream.Seek(5, SeekOrigin.Current);
        }
        catch (Exception e)
        {
            throw new NcmFileFormatException("Seek gap failed", e);
        }

        var coverFrameLen = new byte[4];

        ReadRawExact(coverFrameLen);

        ReadRawExact(n);

        var coverFrameLenInt = (int)BitConverter.ToUInt32(coverFrameLen);
        var coverFrameDataLen = (int)BitConverter.ToUInt32(n);

        if (coverFrameDataLen > 0)
        {
            ImageData = new byte[coverFrameDataLen];
            ReadRawExact(ImageData);
        }
        _rawStream.Seek(coverFrameLenInt - coverFrameDataLen, SeekOrigin.Current);

        _rawStreamOffset = _rawStream.Position;

        var formatBuffer = n.AsSpan()[..3];
        if (Read(formatBuffer) != 3)
            throw new NcmFileFormatException("Read format failed");

        if (formatBuffer[0] == 0x49 && formatBuffer[1] == 0x44 && formatBuffer[2] == 0x33)
            Format = NcmFormat.Mp3;
        else
            Format = NcmFormat.Flac;

        _rawStream.Seek(-3, SeekOrigin.Current);
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
        _rawStream = stream;
        Initialize();
    }

    private static string? GetAlbumPicUrl(string meta)
    {
        var json = JsonNode.Parse(meta) as JsonObject;
        return json?["albumPic"]?.ToString();
    }

    private static byte[] AesEcbDecrypt(byte[] key, byte[] src)
    {
        return AesEcbPure.Decrypt(key, src);
    }

    public override void Flush()
    {
        _outputStream?.Flush();
        _rawStream?.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        int n;
        if (_decryptedData is not null)
        {
            var pos = (int)_position;
            if (pos >= _decryptedData.Count)
                throw new EndOfStreamException();

            n = Math.Min(buffer.Length, _decryptedData.Count - pos);
            for (var i = 0; i < n; i++)
                buffer[i] = _decryptedData[pos + i];
            _position = pos + n;
        }
        else if (_outputStream is not null)
        {
            n = _outputStream.Read(buffer);
        }
        else
        {
            n = ReadRaw(buffer);
            Decrypt(buffer[..n]);
        }
        return n;
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
        }
        else
        {
            if (_decryptedData is null)
                DumpToMemory();
            if (Length > value)
            {
                _decryptedData!.RemoveRange((int)value, (int)(Length - value));
            }
            else if (Length < value)
            {
                _decryptedData!.AddRange(new byte[value - Length]);
            }
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
        }
        else if (_decryptedData is not null)
        {
            var pos = (int)_position;
            var endPos = pos + buffer.Length;

            if (endPos > _decryptedData.Count)
                _decryptedData.AddRange(new byte[endPos - _decryptedData.Count]);

            for (var i = 0; i < buffer.Length; i++)
                _decryptedData[pos + i] = buffer[i];

            _position = endPos;
        }
        else
        {
            DumpToMemory();
            var pos = (int)_position;
            var endPos = pos + buffer.Length;

            if (endPos > _decryptedData!.Count)
                _decryptedData.AddRange(new byte[endPos - _decryptedData.Count]);

            for (var i = 0; i < buffer.Length; i++)
                _decryptedData[pos + i] = buffer[i];

            _position = endPos;
        }
    }

    public void CloseStream(Stream stream)
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _outputStream?.Dispose();
            _rawStream?.Dispose();
        }
    }
}
