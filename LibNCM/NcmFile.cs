namespace LibNCM;

/// <summary>
///   The public facade for working with one NCM file. Opening parses the header
///   once; the audio payload is then available either as bytes in memory
///   (<see cref="DumpToBytesAsync"/>) or written to disk (<see cref="DumpToFileAsync"/>).
///   Metadata can be written back into the decrypted audio via
///   <see cref="FixMetadataAsync"/>.
/// </summary>
/// <remarks>
///   Interface discipline: no implicit state jumps. Output methods are idempotent,
///   may be called in any order, and re-reading after <c>FixMetadataAsync</c>
///   returns the updated payload. Cancellation flows through every async method
///   (including remote cover fetching).
/// </remarks>
public sealed class NcmFile : IDisposable, IAsyncDisposable
{
    private static readonly HttpClient SharedHttpClient = new();

    private readonly Stream _rawStream;
    private readonly NcmHeader _header;
    private readonly long _audioOffset;
    private byte[]? _audioBytes;
    private string? _outputFilePath;

    private NcmFile(Stream rawStream, NcmHeader header)
    {
        _rawStream = rawStream;
        _header = header;
        // After parsing, the stream sits at the start of the audio payload.
        _audioOffset = rawStream.Position;
    }

    /// <summary>Opens and parses an NCM file from a path.</summary>
    /// <exception cref="NcmFileFormatException">The file is not a valid NCM file.</exception>
    public static NcmFile Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        Stream stream;
        try
        {
            stream = File.OpenRead(filePath);
        }
        catch (Exception e)
        {
            throw new NcmFileFormatException("Open file failed", e);
        }

        NcmHeader header;
        try
        {
            header = NcmHeaderParser.Parse(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
        return new NcmFile(stream, header);
    }

    /// <summary>
    ///   Opens and parses an NCM file from a stream. The stream must support
    ///   seeking; ownership transfers to the returned <see cref="NcmFile"/>.
    /// </summary>
    /// <exception cref="NcmFileFormatException">The stream is not a valid NCM file.</exception>
    public static NcmFile Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = NcmHeaderParser.Parse(stream);
        return new NcmFile(stream, header);
    }

    public NcmFormat Format => _header.Format;

    public NeteaseCloudMusicMetadata? Metadata => _header.Metadata;

    public byte[]? ImageData => _header.ImageData;

    public string? AlbumPicUrl => _header.AlbumPicUrl;

    public string FormatExtension => Format.ToString().ToLowerInvariant();

    /// <summary>Derives the output file name for a source file name.</summary>
    public string OutputFileNameFor(string sourceName)
        => Path.GetFileNameWithoutExtension(sourceName) + "." + FormatExtension;

    /// <summary>
    ///   Materializes the decrypted audio payload in memory and returns it.
    ///   Idempotent: subsequent calls return the same bytes, updated by
    ///   <see cref="FixMetadataAsync"/> if one ran in between.
    /// </summary>
    public async Task<byte[]> DumpToBytesAsync(CancellationToken cancellationToken = default)
    {
        if (_audioBytes is not null)
            return _audioBytes;

        using var output = new MemoryStream();
        using var audio = CreateAudioStream();
        await audio.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        _audioBytes = output.ToArray();
        return _audioBytes;
    }

    /// <summary>
    ///   Writes the decrypted audio to <paramref name="outputDir"/>\<paramref name="name"/>
    ///   with the format extension appended. Idempotent: calling again re-writes the
    ///   same file (or a different target) — no handles are leaked between calls.
    /// </summary>
    public async Task DumpToFileAsync(string outputDir, string name, CancellationToken cancellationToken = default)
    {
        _ = Directory.CreateDirectory(outputDir);
        var path = Path.Join(outputDir, $"{name}.{FormatExtension}");

        if (_audioBytes is not null)
        {
            await File.WriteAllBytesAsync(path, _audioBytes, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var output = File.Create(path);
            using var audio = CreateAudioStream();
            await audio.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
        _outputFilePath = path;
    }

    /// <summary>
    ///   Writes NCM metadata (and optionally the embedded/remote cover image) into
    ///   the decrypted audio. When audio was already dumped to disk the file is
    ///   tagged in place; otherwise the in-memory payload is tagged and updated.
    ///   No-op when there is neither metadata nor cover art to write.
    /// </summary>
    public async Task FixMetadataAsync(bool fetchCoverArt, CancellationToken cancellationToken = default)
    {
        if (fetchCoverArt && _header.ImageData is not { Length: > 0 } && !string.IsNullOrEmpty(_header.AlbumPicUrl))
        {
            try
            {
                var response = await SharedHttpClient.GetAsync(_header.AlbumPicUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    _header.ImageData = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                throw new NcmMetadataException("Fetch album image failed", e);
            }
        }

        if (_header.Metadata is null && _header.ImageData is not { Length: > 0 })
            return;

        try
        {
            if (_outputFilePath is not null)
            {
                using var fs = new FileStream(_outputFilePath, FileMode.Open, FileAccess.ReadWrite);
                using var abstraction = new StreamFileAbstraction(_outputFilePath, fs);
                WriteTags(abstraction);
            }
            else
            {
                var bytes = await DumpToBytesAsync(cancellationToken).ConfigureAwait(false);
                using var ms = new MemoryStream();
                ms.Write(bytes);
                ms.Position = 0; // TagLib needs an expandable stream to append tag blocks
                using var abstraction = new StreamFileAbstraction("output." + FormatExtension, ms);
                WriteTags(abstraction);
                _audioBytes = ms.ToArray();
            }
        }
        catch (NcmMetadataException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new NcmMetadataException("Save metadata failed", e);
        }
    }

    private NcmAudioStream CreateAudioStream()
    {
        _rawStream.Position = _audioOffset;
        return new NcmAudioStream(_rawStream, _header.KeyBox);
    }

    private void WriteTags(TagLib.File.IFileAbstraction abstraction)
    {
        using var tfile = TagLib.File.Create(abstraction);
        tfile.Tag.Title = _header.Metadata?.Name;
        tfile.Tag.Performers = _header.Metadata?.Artist.ToArray();
        tfile.Tag.Album = _header.Metadata?.Album;
        tfile.Tag.Description = _header.Metadata?.Description;

        if (_header.ImageData is { Length: > 0 })
        {
            tfile.Tag.Pictures = [new TagLib.Picture(_header.ImageData)];
        }
        tfile.Save();
    }

    public void Dispose()
    {
        _rawStream.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _rawStream.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private sealed class StreamFileAbstraction(string name, Stream stream) : TagLib.File.IFileAbstraction, IDisposable
    {
        public string Name { get; } = name;
        public Stream ReadStream => stream;
        public Stream WriteStream => stream;

        public void CloseStream(Stream stream)
        {
            // Stream lifecycle is managed by NcmFile.
        }

        public void Dispose()
        {
            // Stream lifecycle is managed by NcmFile.
        }
    }
}