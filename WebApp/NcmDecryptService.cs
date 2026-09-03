using LibNCM;

namespace WebApp;

/// <summary>
///   Thin Web adapter over the LibNCM facade: decodes an in-memory .ncm file and
///   returns the tagged audio bytes. All decryption/format/tagging logic lives in
///   LibNCM — this class only marshals bytes into the Blazor DTO.
/// </summary>
public class NcmDecryptService
{
    public static async Task<NcmDecryptResult> DecryptAsync(byte[] ncmData, string fileName)
    {
        using var ms = new MemoryStream(ncmData);
        // The browser context never fetches remote cover art.
        var result = await NcmProcessor.ProcessToBytesAsync(
            ms, fileName, new NcmProcessOptions { FetchCoverArt = false });

        return new NcmDecryptResult
        {
            Success = result.Success,
            Data = result.Data,
            FileName = result.OutputFileName,
            Format = result.Success ? result.Format.ToString().ToLowerInvariant() : null,
            Metadata = result.Metadata,
            ErrorMessage = result.Success ? null : result.ErrorMessage
        };
    }
}

public class NcmDecryptResult
{
    public bool Success { get; set; }
    public byte[]? Data { get; set; }
    public string? FileName { get; set; }
    public string? Format { get; set; }
    public NeteaseCloudMusicMetadata? Metadata { get; set; }
    public string? ErrorMessage { get; set; }
}