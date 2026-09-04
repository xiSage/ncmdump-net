namespace LibNCM;

public sealed class NcmProcessOptions
{
    /// <summary>Fetch remote album art when the file has no embedded cover (default true).</summary>
    public bool FetchCoverArt { get; set; } = true;
}

public sealed class NcmProcessResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MetadataWarning { get; set; }
    public string OutputFileName { get; set; } = "";
    public string? OutputPath { get; set; }
    public byte[]? Data { get; set; }
    public NcmFormat Format { get; set; }
    public NeteaseCloudMusicMetadata? Metadata { get; set; }
}

/// <summary>
///   The processing orchestration facade: opens, decrypts, writes output, embeds
///   NCM metadata, and reports a result. This is the single place where "process
///   one NCM file" lives; CLI, GUI and Web hosts call it instead of wiring the
///   steps themselves.
/// </summary>
/// <remarks>
///   Error model mirrors host needs: a fatal failure (bad file, I/O, cancellation)
///   yields <see cref="NcmProcessResult.Success"/>=false with an
///   <see cref="NcmProcessResult.ErrorMessage"/>; a metadata write failure is
///   <em>not</em> fatal — the decrypted audio is already produced, so it is
///   reported via <see cref="NcmProcessResult.MetadataWarning"/>.
/// </remarks>
public static class NcmProcessor
{
    /// <summary>
    ///   Processes a file on disk: decrypts to <paramref name="outputDir"/>\<paramref name="outputName"/>
    ///   with the format extension appended, then embeds NCM metadata.
    /// </summary>
    public static async Task<NcmProcessResult> ProcessAsync(
        string inputPath,
        string outputDir,
        string outputName,
        NcmProcessOptions? options = null,
        ICoverArtProvider? coverArtProvider = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new NcmProcessOptions();

        NcmFile? ncm = null;
        try
        {
            ncm = NcmFile.Open(inputPath, coverArtProvider);
            await ncm.DumpToFileAsync(outputDir, outputName, cancellationToken).ConfigureAwait(false);

            var outputPath = Path.Join(outputDir, $"{outputName}.{ncm.FormatExtension}");
            var result = new NcmProcessResult
            {
                Success = true,
                Format = ncm.Format,
                Metadata = ncm.Metadata,
                OutputFileName = $"{outputName}.{ncm.FormatExtension}",
                OutputPath = outputPath,
                MetadataWarning = await TryFixMetadataAsync(ncm, options, cancellationToken).ConfigureAwait(false)
            };
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new NcmProcessResult { Success = false, ErrorMessage = e.Message };
        }
        finally
        {
            ncm?.Dispose();
        }
    }

    /// <summary>
    ///   Processes an in-memory NCM file (e.g. from a browser upload): decrypts to
    ///   bytes, optionally embeds NCM metadata, and returns the tagged payload.
    ///   The input stream's ownership transfers to this method.
    /// </summary>
    public static async Task<NcmProcessResult> ProcessToBytesAsync(
        Stream input,
        string sourceFileName,
        NcmProcessOptions? options = null,
        ICoverArtProvider? coverArtProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        options ??= new NcmProcessOptions();

        NcmFile? ncm = null;
        try
        {
            ncm = NcmFile.Open(input, coverArtProvider);
            _ = await ncm.DumpToBytesAsync(cancellationToken).ConfigureAwait(false);

            var result = new NcmProcessResult
            {
                Success = true,
                Format = ncm.Format,
                Metadata = ncm.Metadata,
                OutputFileName = ncm.OutputFileNameFor(sourceFileName),
                MetadataWarning = await TryFixMetadataAsync(ncm, options, cancellationToken).ConfigureAwait(false),
                Data = await ncm.DumpToBytesAsync(cancellationToken).ConfigureAwait(false)
            };
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new NcmProcessResult { Success = false, ErrorMessage = e.Message };
        }
        finally
        {
            ncm?.Dispose();
        }
    }

    private static async Task<string?> TryFixMetadataAsync(NcmFile ncm, NcmProcessOptions options, CancellationToken cancellationToken)
    {
        try
        {
            await ncm.FixMetadataAsync(options.FetchCoverArt, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (NcmMetadataException e)
        {
            return e.Message;
        }
    }
}