namespace LibNCM;

/// <summary>
///   Supplies remote album art for <see cref="NcmFile.FixMetadataAsync"/>.
///   A seam so cover fetching can be swapped: production uses
///   <see cref="RemoteCoverArtProvider"/> (real HTTP), tests inject a fake —
///   two adapters make this a real seam.
/// </summary>
public interface ICoverArtProvider
{
    /// <summary>Fetches raw image bytes for a cover URL, or <c>null</c> when unavailable.</summary>
    Task<byte[]?> FetchAsync(string albumPicUrl, CancellationToken cancellationToken = default);
}

/// <summary>Default cover provider backed by a shared <see cref="HttpClient"/>.</summary>
public sealed class RemoteCoverArtProvider : ICoverArtProvider
{
    private static readonly HttpClient SharedHttpClient = new();

    public async Task<byte[]?> FetchAsync(string albumPicUrl, CancellationToken cancellationToken = default)
    {
        var response = await SharedHttpClient.GetAsync(albumPicUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}