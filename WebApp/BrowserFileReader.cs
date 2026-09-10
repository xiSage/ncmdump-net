using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace WebApp;

/// <summary>
///   Copies a picked file into one managed buffer using bulk JavaScript interop reads.
///   <para>
///   <c>IBrowserFile.OpenReadStream</c> moves the blob to .NET in 128 KiB round trips, and each
///   round trip costs tens of milliseconds; a multi-megabyte .ncm therefore takes tens of seconds
///   (minutes on Android) to arrive, and it does so silently. Reading a few megabytes per round
///   trip does the same work in a handful of calls.
///   </para>
/// </summary>
public static class BrowserFileReader
{
    /// <summary>Bytes requested per interop round trip.</summary>
    public const int ChunkSize = 1024 * 1024;

    /// <summary>Largest file the web app loads into memory.</summary>
    public const long MaxFileSize = 200L * 1024 * 1024;

    /// <summary>Most files accepted from a single picker selection.</summary>
    public const int MaxFileCount = 100;

    private const string ReadChunkMethod = "ncmApp.readFileChunk";

    /// <summary>
    ///   Reads the file at <paramref name="fileIndex"/> of <paramref name="fileInput"/> into memory.
    /// </summary>
    /// <param name="onProgress">Called after each round trip with the number of bytes read so far.</param>
    public static Task<byte[]> ReadAsync(
        IJSRuntime jsRuntime,
        ElementReference fileInput,
        int fileIndex,
        long size,
        Action<long>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsRuntime);

        return ReadAsync(
            size,
            (offset, count, ct) =>
                jsRuntime.InvokeAsync<byte[]>(ReadChunkMethod, ct, fileInput, fileIndex, offset, count).AsTask(),
            onProgress: onProgress,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    ///   The read loop, with the transport injected so it can be exercised without a browser.
    /// </summary>
    /// <param name="readChunk">Supplies up to <c>count</c> bytes starting at <c>offset</c>.</param>
    public static async Task<byte[]> ReadAsync(
        long size,
        Func<long, int, CancellationToken, Task<byte[]>> readChunk,
        Action<long>? onProgress = null,
        int chunkSize = ChunkSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readChunk);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        if (size > MaxFileSize)
        {
            throw new InvalidOperationException($"文件超过 {MaxFileSize / (1024 * 1024)} MB，无法在浏览器中处理");
        }

        var buffer = new byte[size];
        long read = 0;
        while (read < size)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wanted = (int)Math.Min(chunkSize, size - read);
            var chunk = await readChunk(read, wanted, cancellationToken);
            if (chunk.Length == 0)
            {
                throw new IOException($"文件读取中断（已读取 {read} / {size} 字节）");
            }

            var take = Math.Min(chunk.Length, wanted);
            chunk.AsSpan(0, take).CopyTo(buffer.AsSpan((int)read));
            read += take;
            onProgress?.Invoke(read);
        }

        return buffer;
    }
}