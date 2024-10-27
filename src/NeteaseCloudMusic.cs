using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ncmdump_net.src
{
    internal class NeteaseCloudMusic
    {
        public readonly byte[] CoreKey = new byte[17];
        public readonly byte[] ModifyKey = new byte[17];
        //public readonly byte[] Png = new byte[8];

        public string FilePath;

        public string? DumpFilePath;

        public enum NcmFormat
        {
            Mp3,
            Flac
        }
        public NcmFormat Format;
        public byte[]? ImageData;
        public FileStream NcmFile;
        public readonly byte[] KeyBox = new byte[256];
        public NeteaseCloudMusicMetadata? Metadata;
        public readonly string? AlbumPicUrl;

        private static readonly HttpClient client = new();

        public int Read(ref byte[] buffer, int size)
        {
            if (buffer == null || buffer.Length < size)
            {
                buffer = new byte[size];
            }
            try
            {
                return NcmFile.Read(buffer, 0, size);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public void OpenFile()
        {
            try
            {
                NcmFile = File.OpenRead(FilePath);
            }
            catch (Exception e)
            {
                throw new Exception("open file failed", e);
            }
        }

        public bool IsNcmFile()
        {
            var header = new byte[4];

            // check magic header 4E455443 4D414446
            if (Read(ref header, 4) != 4)
            {
                return false;
            }
            if (BitConverter.ToUInt32(header) != 0x4E455443)
            {
                return false;
            }
            return Read(ref header, 4) == 4 && BitConverter.ToUInt32(header) == 0x4D414446;
        }

        public void BuildKeyBox(byte[] key, int keyLen)
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

        // Dump encrypted ncm file to normal music file. If `targetDir` is "", the converted file will be saved to the original directory.
        public void Dump(string targetDir)
        {
            DumpFilePath = FilePath;
            var buffer = new byte[0x8000];
            var findFormatFlag = false;

            FileStream? outputStream = null;

            if (targetDir != "")
            {
                // change save dir
                DumpFilePath = Path.Join(targetDir, Path.GetFileName(DumpFilePath));
            }

            while (true)
            {
                var n = Read(ref buffer, 0x8000);

                if (buffer == null || n == 0)
                {
                    break;
                }

                for (var i = 0; i < n; i++)
                {
                    var j = (i + 1) & 0xff;
                    buffer[i] ^= KeyBox[(KeyBox[j] + KeyBox[(KeyBox[j] + j) & 0xff]) & 0xff];
                }

                if (!findFormatFlag)
                {
                    if (buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33)
                    {
                        Format = NcmFormat.Mp3;
                        DumpFilePath = Path.ChangeExtension(DumpFilePath, ".mp3");
                    }
                    else
                    {
                        Format = NcmFormat.Flac;
                        DumpFilePath = Path.ChangeExtension(DumpFilePath, ".flac");
                    }

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

                    findFormatFlag = true;
                }

                outputStream!.Write(buffer);
            }
            outputStream?.Close();
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
            using var tfile = TagLib.File.Create(DumpFilePath);
            tfile.Tag.Title = Metadata?.Name;
            tfile.Tag.Performers = Metadata?.Artist;
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

        // GetDumpFilePath returns the absolute path of dumped music file
        public string GetDumpFilePath()
        {
            if (DumpFilePath == null)
            {
                return "";
            }
            try
            {
                return Path.GetFullPath(DumpFilePath);

            }
            catch (Exception)
            {
                return DumpFilePath;
            }
        }


        public NeteaseCloudMusic(string? filePath)
        {
            CoreKey = [0x68, 0x7A, 0x48, 0x52, 0x41, 0x6D, 0x73, 0x6F, 0x35, 0x6B, 0x49, 0x6E, 0x62, 0x61, 0x78, 0x57, 0];
            ModifyKey = [0x23, 0x31, 0x34, 0x6C, 0x6A, 0x6B, 0x5F, 0x21, 0x5C, 0x5D, 0x26, 0x30, 0x55, 0x3C, 0x27, 0x28, 0];
            //Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            ArgumentNullException.ThrowIfNull(filePath);
            FilePath = filePath;

            OpenFile();

            if (NcmFile == null || !IsNcmFile())
            {
                throw new Exception("not a ncm file");
            }

            // actually this 2 bytes is the version, now we just skip it
            NcmFile.Seek(2, SeekOrigin.Current);

            // the length of the RC4 key, encrypted by AES128
            var n = new byte[4];

            if (Read(ref n, 4) != 4)
            {
                throw new Exception("read key len failed");
            }

            var keyLen = (int)BitConverter.ToUInt32(n);

            var keyData = new byte[keyLen];
            Read(ref keyData, keyLen);

            

            for (var i = 0; i < keyData?.Length; i++)
            {
                keyData[i] ^= 0x64;
            }
            byte[]? decryptedKeyData;
            try
            {
                decryptedKeyData = AesEcbDecrypt(CoreKey[..16], keyData);
            }
            catch (Exception e)
            {
                throw new Exception("decrypt key failed", e);
            }

            BuildKeyBox(decryptedKeyData[17..], decryptedKeyData.Length - 17);

            if (Read(ref n, 4) != 4)
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
                Read(ref modifyData, metadataLen);

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

                byte[]? modifyDecryptData = null;
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
                NcmFile.Seek(5, SeekOrigin.Current);
            }
            catch (Exception)
            {
                throw new Exception("seek gap failed");
            }

            // read the cover frame
            var coverFrameLen = new byte[4];

            if (Read(ref coverFrameLen, coverFrameLen.Length) != 4)
            {
                throw new Exception("read cover frame len failed");
            }

            if (Read(ref n, n.Length) != 4)
            {
                throw new Exception("read cover frame data len failed");
            }

            var coverFrameLenInt = (int)BitConverter.ToUInt32(coverFrameLen);
            var coverFrameDataLen = (int)BitConverter.ToUInt32(n);

            if (coverFrameDataLen > 0)
            {
                ImageData = new byte[coverFrameDataLen];
                Read(ref ImageData, coverFrameDataLen);
            }

            NcmFile.Seek(coverFrameLenInt - coverFrameDataLen, SeekOrigin.Current);
        }

        private static string? GetAlbumPicUrl(string meta)
        {
            var json = JsonObject.Parse(meta) as JsonObject;
            return json?["albumPic"]?.ToString();
        }

        private static byte[] AesEcbDecrypt(byte[]? key, byte[]? src)
        {
            if (key == null || src == null)
            {
                throw new ArgumentNullException("Key or source cannot be null.");
            }

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
    }
}
