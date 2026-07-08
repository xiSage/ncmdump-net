using LibNCM;

namespace WebApp;

public class NcmDecryptService
{
    private const int YieldInterval = 0x80000;

    public static async Task<NcmDecryptResult> DecryptAsync(byte[] ncmData, string fileName)
    {
        try
        {
            NeteaseCloudMusicMetadata? metadata;
            NeteaseCloudMusicStream.NcmFormat format;
            byte[]? imageData;

            var decryptedStream = new MemoryStream(ncmData.Length);
            var buffer = new byte[0x8000];
            var totalRead = 0;

            using (var ms = new MemoryStream(ncmData))
            using (var ncm = new NeteaseCloudMusicStream(ms))
            {
                while (true)
                {
                    int n;
                    try
                    {
                        n = ncm.Read(buffer, 0, buffer.Length);
                        if (n == 0) break;
                    }
                    catch (EndOfStreamException)
                    {
                        break;
                    }

                    decryptedStream.Write(buffer, 0, n);
                    totalRead += n;

                    if (totalRead >= YieldInterval)
                    {
                        totalRead = 0;
                        await Task.Yield();
                    }
                }

                metadata = ncm.Metadata;
                format = ncm.Format;
                imageData = ncm.ImageData;
            }

            var decryptedBytes = decryptedStream.ToArray();
            byte[] resultBytes = ApplyMetadata(metadata, format, imageData, decryptedBytes);

            var outputFileName = Path.GetFileNameWithoutExtension(fileName) + "." + format.ToString().ToLowerInvariant();

            return new NcmDecryptResult
            {
                Success = true,
                Data = resultBytes,
                FileName = outputFileName,
                Format = format.ToString().ToLowerInvariant(),
                Metadata = metadata
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

    private static byte[] ApplyMetadata(NeteaseCloudMusicMetadata? metadata, NeteaseCloudMusicStream.NcmFormat format, byte[]? imageData, byte[] decryptedBytes)
    {
        if (metadata is null && imageData is null) return decryptedBytes;

        try
        {
            using var outputStream = new MemoryStream();
            outputStream.Write(decryptedBytes);
            outputStream.Position = 0;

            var ext = format.ToString().ToLowerInvariant();
            var fileAbstraction = new MemoryStreamFileAbstraction($"output.{ext}", outputStream);

            using var tagFile = TagLib.File.Create(fileAbstraction);

            if (metadata is { } m)
            {
                tagFile.Tag.Title = m.Name;
                tagFile.Tag.Performers = [.. m.Artist];
                tagFile.Tag.Album = m.Album;
            }

            if (imageData?.Length > 0)
            {
                tagFile.Tag.Pictures = [new TagLib.Picture(imageData)];
            }

            tagFile.Save();

            return outputStream.ToArray();
        }
        catch
        {
            return decryptedBytes;
        }
    }

    private class MemoryStreamFileAbstraction(string name, MemoryStream stream) : TagLib.File.IFileAbstraction
    {

        public string Name { get; } = name;
        public Stream ReadStream => stream;
        public Stream WriteStream => stream;

        public void CloseStream(Stream stream) { }
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
