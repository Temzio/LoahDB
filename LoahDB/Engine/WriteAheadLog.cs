namespace LoahDB.Engine;

internal sealed class WriteAheadLog : IDisposable
{
    private readonly Stream _stream;
    private readonly string _path;

    public WriteAheadLog(string walPath, Stream? streamOverride = null)
    {
        _path = walPath;
        if (streamOverride is not null)
        {
            _stream = streamOverride;
        }
        else
        {
            var directory = Path.GetDirectoryName(walPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _stream = new FileStream(walPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
    }

    public void AppendPage(uint pageId, ReadOnlySpan<byte> pageData)
    {
        if (pageData.Length != LoahConstants.PageSize)
        {
            throw new ArgumentException("Page data must be exactly one page.", nameof(pageData));
        }

        Span<byte> payload = stackalloc byte[4 + LoahConstants.PageSize];
        BitConverter.TryWriteBytes(payload.Slice(0, 4), pageId);
        pageData.CopyTo(payload.Slice(4));
        WriteFrame(WalFrameType.Page, payload);
    }

    public void AppendHeader(ReadOnlySpan<byte> headerPage)
    {
        if (headerPage.Length != LoahConstants.PageSize)
        {
            throw new ArgumentException("Header must be one page.", nameof(headerPage));
        }

        WriteFrame(WalFrameType.Header, headerPage);
    }

    public void AppendCommit() => WriteFrame(WalFrameType.Commit, ReadOnlySpan<byte>.Empty);

    public void AppendCheckpoint()
    {
        WriteFrame(WalFrameType.Checkpoint, ReadOnlySpan<byte>.Empty);
        TruncateAfterCheckpoint();
    }

    public void FlushToDisk()
    {
        if (_stream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
        else
        {
            _stream.Flush();
        }
    }

    public WalReplayResult ReplayCommitted()
    {
        var pendingPages = new Dictionary<uint, byte[]>();
        byte[]? pendingHeader = null;
        Dictionary<uint, byte[]>? lastCommittedPages = null;
        byte[]? lastCommittedHeader = null;
        _stream.Seek(0, SeekOrigin.Begin);
        while (TryReadFrame(out var type, out var payload))
        {
            switch (type)
            {
                case WalFrameType.Page:
                    if (payload.Length < 4 + LoahConstants.PageSize)
                    {
                        return WalReplayResult.Invalid;
                    }

                    var pageId = BitConverter.ToUInt32(payload, 0);
                    pendingPages[pageId] = payload.AsSpan(4, LoahConstants.PageSize).ToArray();
                    break;
                case WalFrameType.Header:
                    if (payload.Length != LoahConstants.PageSize)
                    {
                        return WalReplayResult.Invalid;
                    }

                    pendingHeader = payload.ToArray();
                    break;
                case WalFrameType.Commit:
                    lastCommittedPages = new Dictionary<uint, byte[]>(pendingPages);
                    lastCommittedHeader = pendingHeader;
                    pendingPages.Clear();
                    pendingHeader = null;
                    break;
                case WalFrameType.Checkpoint:
                    pendingPages.Clear();
                    pendingHeader = null;
                    lastCommittedPages = null;
                    lastCommittedHeader = null;
                    break;
            }
        }

        return lastCommittedPages is not null
            ? new WalReplayResult(true, lastCommittedPages, lastCommittedHeader)
            : new WalReplayResult(false, new Dictionary<uint, byte[]>(), null);
    }

    public void Dispose()
    {
        _stream.Dispose();
        if (File.Exists(_path) && new FileInfo(_path).Length == 0)
        {
            try
            {
                File.Delete(_path);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private void WriteFrame(WalFrameType type, ReadOnlySpan<byte> payload)
    {
        var bodyLength = 4 + payload.Length + 4;
        var buffer = new byte[4 + bodyLength];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), bodyLength);
        BitConverter.TryWriteBytes(buffer.AsSpan(4, 4), (uint)type);
        payload.CopyTo(buffer.AsSpan(8));
        var crc = Crc32.Compute(buffer.AsSpan(4, 4 + payload.Length));
        BitConverter.TryWriteBytes(buffer.AsSpan(8 + payload.Length, 4), crc);
        _stream.Write(buffer, 0, buffer.Length);
    }

    private bool TryReadFrame(out WalFrameType type, out byte[] payload)
    {
        type = default;
        payload = Array.Empty<byte>();
        Span<byte> lenBytes = stackalloc byte[4];
        if (_stream.Read(lenBytes) != 4)
        {
            return false;
        }

        var bodyLength = BitConverter.ToInt32(lenBytes);
        if (bodyLength < 8)
        {
            return false;
        }

        var body = new byte[bodyLength];
        var read = 0;
        while (read < bodyLength)
        {
            var chunk = _stream.Read(body, read, bodyLength - read);
            if (chunk == 0)
            {
                return false;
            }

            read += chunk;
        }

        type = (WalFrameType)BitConverter.ToUInt32(body, 0);
        payload = body.AsSpan(4, bodyLength - 8).ToArray();
        var storedCrc = BitConverter.ToUInt32(body, bodyLength - 4);
        var computed = Crc32.Compute(body.AsSpan(0, bodyLength - 4));
        if (storedCrc != computed)
        {
            type = default;
            payload = Array.Empty<byte>();
            return false;
        }

        return true;
    }

    private void TruncateAfterCheckpoint()
    {
        _stream.SetLength(0);
        _stream.Seek(0, SeekOrigin.Begin);
        FlushToDisk();
    }
}

internal readonly record struct WalReplayResult(
    bool HasCommittedTransaction,
    Dictionary<uint, byte[]> Pages,
    byte[]? HeaderPage)
{
    public static WalReplayResult Invalid => new(false, new Dictionary<uint, byte[]>(), null);
}
