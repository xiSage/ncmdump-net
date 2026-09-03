using LibNCM;
using Xunit;

namespace Ncm.Tests;

public class NcmAudioStreamTests
{
    private static (MemoryStream Raw, NcmAudioStream Audio) OpenAudio()
    {
        var bytes = SyntheticNcmBuilder.BuildFile();
        var raw = new MemoryStream(bytes);
        var header = NcmHeaderParser.Parse(raw);   // raw is now positioned at the audio payload
        return (raw, new NcmAudioStream(raw, header.KeyBox));
    }

    [Fact]
    public void ReadsDecryptedPayload()
    {
        var (_, audio) = OpenAudio();
        using (audio)
        {
            var decrypted = ReadAll(audio);
            Assert.Equal(SyntheticNcmBuilder.AudioPlaintext, decrypted);
            Assert.False(audio.CanWrite);
            Assert.True(audio.CanRead);
            Assert.Equal(SyntheticNcmBuilder.AudioPlaintext.Length, audio.Length);
        }
    }

    [Fact]
    public void ReadBeyondEndReturnsZero()
    {
        var (_, audio) = OpenAudio();
        using (audio)
        {
            ReadAll(audio);
            Assert.Equal(0, audio.Read(new byte[16]));
            Assert.Equal(SyntheticNcmBuilder.AudioPlaintext.Length, audio.Position);
        }
    }

    [Fact]
    public void SeekThenRead_StillDecryptsWithRelativePosition()
    {
        var (_, audio) = OpenAudio();
        using (audio)
        {
            var expected = SyntheticNcmBuilder.AudioPlaintext;
            var skip = 37;

            Assert.Equal(skip, audio.Seek(skip, SeekOrigin.Begin));
            var chunk = new byte[expected.Length - skip];
            _ = ReadAll(audio, chunk);

            Assert.Equal(expected.AsSpan(skip).ToArray(), chunk);
        }
    }

    [Fact]
    public void PositionIsRelativeToPayloadStart()
    {
        var (_, audio) = OpenAudio();
        using (audio)
        {
            Assert.Equal(0, audio.Position);
            _ = audio.Read(new byte[10]);
            Assert.Equal(10, audio.Position);
        }
    }

    [Fact]
    public void WriteThrowsNotSupported()
    {
        var (_, audio) = OpenAudio();
        using (audio)
        {
            Assert.Throws<NotSupportedException>(() => audio.WriteByte(1));
            Assert.Throws<NotSupportedException>(() => audio.SetLength(100));
        }
    }

    [Fact]
    public void NegativePositionRejected()
    {
        var (_, audio) = OpenAudio();
        using (audio)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => audio.Position = -1);
        }
    }

    private static byte[] ReadAll(Stream stream, byte[]? into = null)
    {
        into ??= new byte[(int)stream.Length];
        var total = 0;
        var buffer = new byte[0x11]; // odd chunk size to exercise chunked reads
        while (true)
        {
            var n = stream.Read(buffer, 0, buffer.Length);
            if (n == 0) break;
            Array.Copy(buffer, 0, into, total, n);
            total += n;
        }
        return into;
    }
}