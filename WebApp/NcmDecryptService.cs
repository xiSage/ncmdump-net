using LibNCM;

namespace WebApp;

/// <summary>
///   Thin Web adapter over the LibNCM facade: decodes an in-memory .ncm file,
///   embeds NCM metadata (no remote cover fetching in the browser context), and
///   returns the tagged audio bytes. All format/decryption/tagging logic lives
///   in LibNCM — this class only marshals bytes.
/// </summary>
public class NcmDecryptService
{
    public static async Task<NcmDecryptResult> DecryptAsync(byte[] ncmData, string fileName)
    {
        try
        {
            await using var ncm = NcmFile.Open(new MemoryStream(ncmData));
            _ = await ncm.DumpToBytesAsync();

            if (ncm.Metadata is not null || ncm.ImageData is { Length: > 0 })
            {
                await ncm.FixMetadataAsync(fetchCoverArt: false);
            }

            var resultBytes = await ncm.DumpToBytesAsync();

            return new NcmDecryptResult
            {
                Success = true,
                Data = resultBytes,
                FileName = ncm.OutputFileNameFor(fileName),
                Format = ncm.FormatExtension,
                Metadata = ncm.Metadata
            };
        }
        catch (Exception ex)
        {
            return new NcmDecryptResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
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