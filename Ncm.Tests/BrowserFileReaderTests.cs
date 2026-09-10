using WebApp;
using Xunit;

namespace Ncm.Tests;

/// <summary>
///   The browser file read loop. The transport is faked here, so these cover offset
///   arithmetic, boundary cases and error paths; the real JavaScript transport is
///   exercised by the WebApp itself.
/// </summary>
public class BrowserFileReaderTests
{
    /// <summary>Deterministic payload: byte i == (i * 31 + 7) % 251 — never repeats within a test size.</summary>
    private static byte[] Source(int size) => [.. Enumerable.Range(0, size).Select(i => (byte)((i * 31 + 7) % 251))];

    private static Func<long, int, CancellationToken, Task<byte[]>> Transport(byte[] source, List<(long Offset, int Count)>? calls = null)
        => (offset, count, _) =>
        {
            calls?.Add((offset, count));
            var take = (int)Math.Min(count, source.Length - offset);
            return Task.FromResult(source.AsSpan((int)offset, take).ToArray());
        };

    [Fact]
    public async Task AssemblesChunksAtTheRightOffsets()
    {
        var source = Source(1000);
        var calls = new List<(long, int)>();

        var result = await BrowserFileReader.ReadAsync(source.Length, Transport(source, calls), chunkSize: 256);

        Assert.Equal(source, result);
        Assert.Equal([(0L, 256), (256L, 256), (512L, 256), (768L, 232)], calls);
    }

    [Fact]
    public async Task SingleChunkReadsWholeFile()
    {
        var source = Source(300);

        var result = await BrowserFileReader.ReadAsync(source.Length, Transport(source), chunkSize: 1024 * 1024);

        Assert.Equal(source, result);
    }

    /// <summary>
    ///   The point of this reader: one interop round trip per chunk, and chunks measured in
    ///   megabytes. On Android each round trip is what costs, so a file must not need hundreds
    ///   of them — do not shrink <see cref="BrowserFileReader.ChunkSize"/> back to Blazor's
    ///   128 KiB default (see README/CONTEXT: the Android slowness in issue #15).
    /// </summary>
    [Fact]
    public async Task UsesFewLargeRoundTrips()
    {
        Assert.True(BrowserFileReader.ChunkSize >= 1024 * 1024, "chunk size must stay in the megabyte range");

        var source = Source(5 * 1024 * 1024 + 3);
        var calls = new List<(long Offset, int Count)>();

        var result = await BrowserFileReader.ReadAsync(source.Length, Transport(source, calls));

        Assert.Equal(source, result);
        Assert.Equal(6, calls.Count);
        Assert.All(calls.Take(5), c => Assert.Equal(BrowserFileReader.ChunkSize, c.Count));
        Assert.Equal(0L, calls[0].Offset);
        Assert.Equal(5L * 1024 * 1024, calls[^1].Offset);
        Assert.Equal(3, calls[^1].Count);
    }

    [Fact]
    public async Task ReportsProgressUpToTheTotalSize()
    {
        var source = Source(500);
        var progress = new List<long>();

        await BrowserFileReader.ReadAsync(source.Length, Transport(source), progress.Add, chunkSize: 200);

        Assert.Equal([200L, 400L, 500L], progress);
    }

    [Fact]
    public async Task ShortChunksAreTolerated()
    {
        // A transport that hands back less than asked for (arriving in dribs) must still
        // produce the exact bytes, and must never be asked for the same offset twice.
        var source = Source(700);
        var offset = 0L;
        var requests = new List<long>();
        Task<byte[]> Read(long at, int count, CancellationToken _)
        {
            requests.Add(at);
            var take = Math.Min(count, Math.Min(64, source.Length - (int)at));
            var chunk = source.AsSpan((int)at, take).ToArray();
            offset = at + take;
            return Task.FromResult(chunk);
        }

        var result = await BrowserFileReader.ReadAsync(source.Length, Read, chunkSize: 256);

        Assert.Equal(source, result);
        Assert.Equal(source.Length, offset);
        Assert.Equal(requests.OrderBy(o => o), requests);
        Assert.Equal(requests.Distinct().Count(), requests.Count);
    }

    [Fact]
    public async Task EmptyFileYieldsEmptyBufferWithoutTouchingTheTransport()
    {
        var calls = 0;

        var result = await BrowserFileReader.ReadAsync(0, (_, _, _) =>
        {
            calls++;
            return Task.FromResult(Array.Empty<byte>());
        });

        Assert.Empty(result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task StalledTransportFailsInsteadOfLoopingForever()
    {
        var source = Source(10);
        var reads = 0;
        Task<byte[]> Read(long at, int count, CancellationToken _)
        {
            reads++;
            // First chunk arrives, then the browser hands back nothing forever.
            return Task.FromResult(reads == 1 && at == 0 ? source.AsSpan(0, 4).ToArray() : []);
        }

        var ex = await Assert.ThrowsAsync<IOException>(() => BrowserFileReader.ReadAsync(10, Read, chunkSize: 4));

        Assert.Contains("读取中断", ex.Message);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task TransportFailurePropagates()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserFileReader.ReadAsync(10, (_, _, _) => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task OversizedFileIsRejectedBeforeAnyTransfer()
    {
        var calls = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserFileReader.ReadAsync(
                BrowserFileReader.MaxFileSize + 1,
                (_, _, _) =>
                {
                    calls++;
                    return Task.FromResult(Array.Empty<byte>());
                }));

        Assert.Contains("MB", ex.Message);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CancellationStopsTheRead()
    {
        using var cts = new CancellationTokenSource();
        var source = Source(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BrowserFileReader.ReadAsync(
                100,
                (offset, count, _) =>
                {
                    cts.Cancel();
                    return Task.FromResult(source.AsSpan((int)offset, count).ToArray());
                },
                chunkSize: 10,
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task NegativeSizeIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BrowserFileReader.ReadAsync(-1, (_, _, _) => Task.FromResult(Array.Empty<byte>())));
    }

    [Fact]
    public async Task NullTransportIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BrowserFileReader.ReadAsync(10, null!));
    }
}