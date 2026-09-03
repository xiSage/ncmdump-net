namespace LibNCM;

/// <summary>
///   Output audio container format of an NCM file.
/// </summary>
public enum NcmFormat
{
    Mp3,
    Flac
}

/// <summary>
///   Parsed NCM header — the immutable result of <see cref="NcmHeaderParser.Parse"/>.
///   Holds everything a consumer needs to know about the file <em>before</em> the
///   audio payload: container format, metadata, embedded album art, and remote
///   cover URL. The decryption key box is deliberately internal: it is part of
///   the next stage (the decrypting stream), not of this public data surface.
/// </summary>
public sealed class NcmHeader
{
    internal NcmHeader(
        NcmFormat format,
        NeteaseCloudMusicMetadata? metadata,
        byte[]? imageData,
        string? albumPicUrl,
        byte[] keyBox)
    {
        Format = format;
        Metadata = metadata;
        ImageData = imageData;
        AlbumPicUrl = albumPicUrl;
        KeyBox = keyBox;
    }

    public NcmFormat Format { get; }

    public NeteaseCloudMusicMetadata? Metadata { get; }

    public byte[]? ImageData { get; internal set; }

    public string? AlbumPicUrl { get; }

    /// <summary>256-byte decryption state derived from the file's key section.</summary>
    internal byte[] KeyBox { get; }
}