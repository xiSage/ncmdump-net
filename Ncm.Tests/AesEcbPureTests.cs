using System.Security.Cryptography;
using LibNCM;
using Xunit;

namespace Ncm.Tests;

public class AesEcbPureTests
{
    // FIPS-197 Appendix C.1: AES-128 encryption vector.
    [Fact]
    public void DecryptNistVector()
    {
        byte[] key = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f];
        byte[] ciphertext =
        [
            0x69, 0xc4, 0xe0, 0xd8, 0x6a, 0x7b, 0x04, 0x30, 0xd8, 0xcd, 0xb7, 0x80, 0x70, 0xb4, 0xc5, 0x5a
        ];
        byte[] expected =
        [
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff
        ];

        Assert.Equal(expected, AesEcbPure.Decrypt(key, ciphertext));
    }

    // Cross-validation against the platform AES for multi-block, PKCS#7-padded input.
    [Theory]
    [InlineData(16)]   // exactly one block (PKCS#7 adds a full pad block)
    [InlineData(40)]   // not block-aligned
    [InlineData(64)]   // multiple blocks, aligned
    [InlineData(65)]   // multiple blocks + 1
    public void DecryptMatchesPlatformAes(int plaintextLength)
    {
        var key = Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i)).ToArray();
        var plaintext = Enumerable.Range(0, plaintextLength).Select(i => (byte)(i * 7 + 3)).ToArray();

        var ciphertext = SyntheticNcmBuilder.AesEcbEncrypt(key, plaintext);
        var decrypted = AesEcbPure.Decrypt(key, ciphertext);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RejectsNon16ByteKey()
    {
        var ex = Assert.Throws<ArgumentException>(() => AesEcbPure.Decrypt(new byte[15], new byte[16]));
        Assert.Contains("16-byte key", ex.Message);
    }

    [Fact]
    public void RejectsInvalidCiphertextLength()
    {
        var key = new byte[16];
        Assert.Throws<ArgumentException>(() => AesEcbPure.Decrypt(key, Array.Empty<byte>()));
        Assert.Throws<ArgumentException>(() => AesEcbPure.Decrypt(key, new byte[15]));
    }
}