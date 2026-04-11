using LibNCM;
using TagLib;

namespace WebApp;

public class NcmDecryptService
{
    public NcmDecryptResult Decrypt(byte[] ncmData, string fileName)
    {
        try
        {
            NeteaseCloudMusicMetadata? metadata;
            NeteaseCloudMusicStream.NcmFormat format;
            byte[]? imageData;

            var decryptedList = new List<byte>();
            var buffer = new byte[0x8000];

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

                    decryptedList.AddRange(buffer.AsSpan()[..n]);
                }

                metadata = ncm.Metadata;
                format = ncm.Format;
                imageData = ncm.ImageData;
            }

            var decryptedBytes = decryptedList.ToArray();
            byte[] resultBytes = ApplyMetadata(metadata, format, imageData, decryptedBytes);

            var outputFileName = Path.GetFileNameWithoutExtension(fileName) + "." + format.ToString().ToLower();

            return new NcmDecryptResult
            {
                Success = true,
                Data = resultBytes,
                FileName = outputFileName,
                Format = format.ToString().ToLower(),
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

    private byte[] ApplyMetadata(NeteaseCloudMusicMetadata? metadata, NeteaseCloudMusicStream.NcmFormat format, byte[]? imageData, byte[] decryptedBytes)
    {
        if (metadata is null && imageData is null) return decryptedBytes;

        try
        {
            using var outputStream = new MemoryStream();
            outputStream.Write(decryptedBytes);
            outputStream.Position = 0;

            var ext = format.ToString().ToLower();
            var fileAbstraction = new MemoryStreamFileAbstraction($"output.{ext}", outputStream);

            using var tagFile = TagLib.File.Create(fileAbstraction);

            if (metadata is { } m)
            {
                tagFile.Tag.Title = m.Name;
                tagFile.Tag.Performers = m.Artist.ToArray();
                tagFile.Tag.Album = m.Album;
            }

            if (imageData?.Length > 0)
            {
                tagFile.Tag.Pictures = new[] { new TagLib.Picture(imageData) };
            }

            tagFile.Save();

            return outputStream.ToArray();
        }
        catch
        {
            return decryptedBytes;
        }
    }

    private class MemoryStreamFileAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly MemoryStream _stream;

        public string Name { get; }
        public Stream ReadStream => _stream;
        public Stream WriteStream => _stream;

        public MemoryStreamFileAbstraction(string name, MemoryStream stream)
        {
            Name = name;
            _stream = stream;
        }

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
