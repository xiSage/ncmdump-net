using MoreLinq.Extensions;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ncmdump_net.src
{
    public class NeteaseCloudMusicStream : Stream, TagLib.File.IFileAbstraction
    {
        public static readonly byte[] CoreKey = [0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57, 0];
        public static readonly byte[] ModifyKey = [0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28, 0];
        //public readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        public string? FilePath { get; private set; }

        public enum NcmFormat
        {
            Mp3,
            Flac
        }
        public NcmFormat Format { get; private set; }
        public byte[]? ImageData { get; private set; }
        private readonly Stream _rawStream;
        private FileStream? _outputStream = null;
        public byte[] KeyBox { get; } =  new byte[256];
        public NeteaseCloudMusicMetadata? Metadata { get; private set; }
        public string? AlbumPicUrl { get; private set; }

        private List<byte>? _decryptedData = null;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length
        {
            get
            {
                if (_decryptedData is not null)
                {
                    return _decryptedData.Count;
                }
                else if (_outputStream is not null)
                {
                    return _outputStream.Length;
                }
                else
                {
                    return _rawStream.Length - _rawStreamOffset;
                }
            }
        }

        public override long Position
        {
            get
            {
                if (_decryptedData is not null)
                {
                    return _position;
                }
                else if (_outputStream is not null)
                {
                    return _outputStream.Position;
                }
                else
                {
                    return _rawStream.Position - _rawStreamOffset;
                }
            }
            set
            {
                if (_outputStream is not null)
                {
                    _outputStream.Position = value;
                }
                else if (_decryptedData is null)
                {
                    _rawStream.Position = value + _rawStreamOffset;
                }
                _position = value;
            }
        }

        private long _position = 0;


        public string Name => FilePath is null ? "ncm" + Format.ToString().ToLower() : Path.GetFileNameWithoutExtension(FilePath) + "." + Format.ToString().ToLower();

        public Stream ReadStream => this;

        public Stream WriteStream => this;

        private long _rawStreamOffset = 0;

        private int ReadRaw(Span<byte> buffer)
        {
            return _rawStream.Read(buffer);
        }

        private bool IsNcmFile()
        {
            var header = new byte[4];

            // check magic header 4E455443 4D414446

            return ReadRaw(header) == 4 && BitConverter.ToUInt32(header) == 0x4E455443 && ReadRaw(header) == 4 && BitConverter.ToUInt32(header) == 0x4D414446;

        }

        private void BuildKeyBox(byte[] key, int keyLen)
        {
            for (var i = 0; i < 256; i++)
            {
                KeyBox[i] = (byte)i;
            }

            byte lastByte = 0;
            byte keyOffset = 0;

            for (var i = 0; i < 256; i++)
            {
                byte swap = KeyBox[i];
                byte c = (byte)((swap + lastByte + key[keyOffset]) & 0xff);
                keyOffset++;
                if (keyOffset >= keyLen)
                {
                    keyOffset = 0;
                }
                KeyBox[i] = KeyBox[c];
                KeyBox[c] = swap;
                lastByte = c;
            }
        }

        /*
        public string MimeType()
        {
            return ImageData.StartsWith(Png) ? "image/png" : "image/jpeg";
        }
        */


        private void Decrypt(Span<byte> buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                var j = (i + 1) & 0xff;
                buffer[i] ^= KeyBox[(KeyBox[j] + KeyBox[(KeyBox[j] + j) & 0xff]) & 0xff];
            }
        }

        // Decrypt raw data and store into memory
        public void DumpToMemory()
        {
            var buffer = new byte[0x8000];
            _decryptedData = [];
            /*
            if (targetDir != "")
            {
                // change save dir
                DumpFilePath = Path.Join(targetDir, Path.GetFileName(DumpFilePath));
            }
            */
            var currentPosition = Position;
            while (true)
            {
                int n;
                try
                {
                    n = Read(buffer);
                }
                catch (Exception e)
                {
                    if (e is EndOfStreamException)
                    {
                        break;
                    }
                    throw;
                }

                var readData = buffer.AsSpan()[..n];
                _decryptedData.AddRange(readData);
                /*
                try
                {
                    var path = Path.GetDirectoryName(DumpFilePath);
                    if (path is not null)
                    {
                        Directory.CreateDirectory(path);
                    }
                    outputStream = File.Create(DumpFilePath);
                }
                catch (Exception)
                {
                    throw new Exception($"create output file failed at \"{DumpFilePath}\"");
                }
                */

                //outputStream!.Write(buffer, 0, n);
            }
            Position = currentPosition;
        }

        public void DumpToFile(string path, string name)
        {
            FileStream? output;
            try
            {
                Directory.CreateDirectory(path);
                output = File.Create(Path.Join(path, $"{name}.{Format.ToString().ToLower()}"));
            }
            catch (Exception)
            {
                throw new Exception($"create output file failed at \"{path}\"");
            }
            var buffer = new byte[0x8000];
            var currentPosition = Position;
            while (true)
            {
                int n;
                try
                {
                    n = Read(buffer);
                    if (n == 0)
                    {
                        break;
                    }
                }
                catch (Exception e)
                {
                    if (e is EndOfStreamException)
                    {
                        break;
                    }
                    throw;
                }
                var readData = buffer.AsSpan()[..n];
                output.Write(readData);
            }
            Position = currentPosition;
            _outputStream = output;
            _outputStream.Flush();
        }
        // FixMetadata will fix the missing metadata for target music file, the source of the metadata comes from origin ncm file.
        // Since NeteaseCloudMusic version 3.0, the album cover image is no longer embedded in the ncm file. If the parameter is true, it means downloading the image from the NetEase server and embedding it into the target music file (network connection required)
        public void FixMetadata(bool fetchAlbumImageFromRemote)
        {
            if ((ImageData is null || ImageData?.Length <= 0) && fetchAlbumImageFromRemote)
            {
                // get the album pic from url
                try
                {
                    HttpClient client = new();
                    var response = client.GetAsync(AlbumPicUrl).Result;
                    if (response != null)
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var bodyBytes = response.Content.ReadAsByteArrayAsync().Result;
                            ImageData = bodyBytes;
                        }
                    }
                }
                catch (Exception e)
                {
                    throw new Exception("fetch album image failed", e);
                }
            }
            using var tfile = TagLib.File.Create(this);
            tfile.Tag.Title = Metadata?.Name;
            tfile.Tag.Performers = Metadata?.Artist.ToArray();
            tfile.Tag.Album = Metadata?.Album;

            if (ImageData?.Length > 0)
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
                throw new Exception("save metadata failed", e);
            }

        }

        private void Initalize()
        {
            if (_rawStream == null || !IsNcmFile())
            {
                throw new Exception("not a ncm file");
            }

            // actually this 2 bytes is the version, now we just skip it
            _rawStream.Seek(2, SeekOrigin.Current);

            // the length of the RC4 key, encrypted by AES128
            var n = new byte[4];

            if (ReadRaw(n) != 4)
            {
                throw new Exception("read key len failed");
            }

            var keyLen = (int)BitConverter.ToUInt32(n);

            var keyData = new byte[keyLen];
            ReadRaw(keyData);



            for (var i = 0; i < keyData?.Length; i++)
            {
                keyData[i] ^= 0x64;
            }
            byte[]? decryptedKeyData;
            try
            {
                decryptedKeyData = AesEcbDecrypt(CoreKey[..16], keyData!);
            }
            catch (Exception e)
            {
                throw new Exception("decrypt key failed", e);
            }

            BuildKeyBox(decryptedKeyData[17..], decryptedKeyData.Length - 17);

            if (ReadRaw(n) != 4)
            {
                throw new Exception("read metadata len failed");
            }

            var metadataLen = (int)BitConverter.ToUInt32(n);

            if (metadataLen <= 0)
            {
                // process meta here
                Metadata = null;
            }
            else
            {
                // read metadata
                var modifyData = new byte[metadataLen];
                ReadRaw(modifyData);

                for (var i = 0; i < modifyData.Length; i++)
                {
                    modifyData[i] ^= 0x63;
                }

                // escape `163 key(Don't modify):`
                var swapModifyData = Encoding.UTF8.GetString(modifyData[22..]);

                byte[]? modifyOutData;
                try
                {
                    modifyOutData = Convert.FromBase64String(swapModifyData);
                }
                catch (Exception)
                {
                    throw new Exception("base64 decode modify data failed");
                }

                byte[]? modifyDecryptData;
                try
                {
                    modifyDecryptData = AesEcbDecrypt(ModifyKey[..16], modifyOutData);
                }
                catch (Exception)
                {
                    throw new Exception("decrypt modify data failed");
                }

                // scape `music:`
                var metadataString = Encoding.UTF8.GetString(modifyDecryptData[6..]);

                // extract the album pic url
                AlbumPicUrl = GetAlbumPicUrl(metadataString);

                Metadata = new NeteaseCloudMusicMetadata(metadataString);
            }

            // skip the 5 bytes gap
            try
            {
                _rawStream.Seek(5, SeekOrigin.Current);
            }
            catch (Exception)
            {
                throw new Exception("seek gap failed");
            }

            // read the cover frame
            var coverFrameLen = new byte[4];

            if (ReadRaw(coverFrameLen) != 4)
            {
                throw new Exception("read cover frame len failed");
            }

            if (ReadRaw(n) != 4)
            {
                throw new Exception("read cover frame data len failed");
            }

            var coverFrameLenInt = (int)BitConverter.ToUInt32(coverFrameLen);
            var coverFrameDataLen = (int)BitConverter.ToUInt32(n);

            if (coverFrameDataLen > 0)
            {
                ImageData = new byte[coverFrameDataLen];
                ReadRaw(ImageData);
            }
            // skip the cover frame data
            _rawStream.Seek(coverFrameLenInt - coverFrameDataLen, SeekOrigin.Current);

            _rawStreamOffset = _rawStream.Position;

            // determine the format of the music file
            var buffer = n.AsSpan()[0..3];
            if (ReadRaw(buffer) != 3)
            {
                throw new Exception("read format failed");
            }
            if (buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33)
            {
                Format = NcmFormat.Mp3;
            }
            else
            {
                Format = NcmFormat.Flac;
            }
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
                throw new Exception("open file failed", e);
            }
            Initalize();
        }

        public NeteaseCloudMusicStream(Stream stream)
        {
            _rawStream = stream;
            Initalize();
        }

        private static string? GetAlbumPicUrl(string meta)
        {
            var json = JsonObject.Parse(meta) as JsonObject;
            return json?["albumPic"]?.ToString();
        }

        private static byte[] AesEcbDecrypt(byte[] key, byte[] src)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB; // 设置 AES 模式为 ECB
            aes.Key = key; // 设置密钥
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            using var memoryStream = new MemoryStream(src);
            using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);
            using var resultStream = new MemoryStream();
            cryptoStream.CopyTo(resultStream); // 解密数据
            byte[] decryptedData = resultStream.ToArray();
            return decryptedData; // 返回解密后的数据
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan()[offset..count]);
        }

        public override int Read(Span<byte> buffer)
        {
            int n;
            if (_decryptedData is not null)
            {
                if (_position + buffer.Length > _decryptedData.Count)
                {
                    n = _decryptedData.Count - (int)Position;
                }
                else
                {
                    n = buffer.Length;
                }
                if (n > 0)
                {
                    _decryptedData.GetRange((int)Position, n).CopyTo(buffer);
                    Position += n;
                }
                else
                {
                    throw new EndOfStreamException();
                }
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
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }
            return Position;
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
                {
                    DumpToMemory();
                }
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
            Write(buffer.AsSpan()[offset..count]);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_outputStream is not null)
            {
                _outputStream.Write(buffer);
            }
            else if (_decryptedData is not null)
            {
                _decryptedData.AddRange(buffer);
            }
            else
            {
                DumpToMemory();
                _decryptedData!.AddRange(buffer);
            }
        }

        public void CloseStream(Stream stream)
        {
        }

        protected override void Dispose(bool disposing)
        {
            _outputStream?.Dispose();
            _rawStream?.Dispose();
        }
    }
}
