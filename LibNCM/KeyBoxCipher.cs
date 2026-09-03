namespace LibNCM;

/// <summary>
///   The position-dependent XOR cipher that maps an NCM audio payload to its
///   plaintext and back. Shared by the header parser (format detection reads
///   decrypted bytes) and the decrypting stream.
/// </summary>
internal static class KeyBoxCipher
{
    public static void Decrypt(byte[] keyBox, Span<byte> buffer, long position)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var j = (int)((position + i + 1) & 0xff);
            buffer[i] ^= keyBox[(keyBox[j] + keyBox[(keyBox[j] + j) & 0xff]) & 0xff];
        }
    }
}