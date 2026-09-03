namespace LibNCM;

/// <summary>
///   Read-only decrypting view over the audio payload of an NCM file.
///   Positions are relative to the start of the payload; every byte read is
///   decrypted on the fly through the key box derived from the file's key section.
/// </summary>
/// <remarks>
///   Internal by design: consumers go through <see cref="NcmFile"/>, which is the
///   only module that owns streams end to end. Tests reach this class directly
///   via InternalsVisibleTo.
/// </remarks>
internal sealed class NcmAudioStream : Stream
{
    private readonly Stream _raw;
    private readonly byte[] _keyBox;
    private readonly long _start;

    internal NcmAudioStream(Stream raw, byte[] keyBox)
    {
        _raw = raw;
        _keyBox = keyBox;
        _start = raw.Position;
    }

    public override bool CanRead => true;
    public override bool CanSeek => _raw.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _raw.Length - _start;

    public override long Position
    {
        get => _raw.Position - _start;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _raw.Position = value + _start;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var position = _raw.Position - _start;
        var n = _raw.Read(buffer);
        KeyBoxCipher.Decrypt(_keyBox, buffer[..n], position);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var position = _raw.Position - _start;
        var n = await _raw.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        KeyBoxCipher.Decrypt(_keyBox, buffer.Span[..n], position);
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => Position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // No Dispose override: this stream is a read-only view and does NOT own the
    // underlying stream — NcmFile is the sole owner and may create multiple
    // views over the same raw stream.

    internal static byte[] BuildKeyBox(byte[] key)
    {
        var keyBox = new byte[256];
        for (var i = 0; i < 256; i++)
            keyBox[i] = (byte)i;

        byte lastByte = 0;
        byte keyOffset = 0;

        for (var i = 0; i < 256; i++)
        {
            byte swap = keyBox[i];
            byte c = (byte)((swap + lastByte + key[keyOffset]) & 0xff);
            keyOffset++;
            if (keyOffset >= key.Length)
                keyOffset = 0;
            keyBox[i] = keyBox[c];
            keyBox[c] = swap;
            lastByte = c;
        }
        return keyBox;
    }
}