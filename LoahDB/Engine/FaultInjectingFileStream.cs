namespace LoahDB.Engine;

/// <summary>
/// Test hook that fails after a configured number of successful writes or fsync calls.
/// </summary>
internal sealed class FaultInjectingFileStream : Stream
{
    private readonly Stream _inner;
    private int _remainingWriteFailures;
    private int _remainingFlushFailures;

    public FaultInjectingFileStream(Stream inner, int failAfterWrites = int.MaxValue, int failAfterFlushes = int.MaxValue)
    {
        _inner = inner;
        _remainingWriteFailures = failAfterWrites;
        _remainingFlushFailures = failAfterFlushes;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => _inner.CanWrite;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override void Flush()
    {
        if (_remainingFlushFailures <= 0)
        {
            throw new IOException("Injected WAL flush failure.");
        }

        _remainingFlushFailures--;
        _inner.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void SetLength(long value) => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_remainingWriteFailures <= 0)
        {
            throw new IOException("Injected WAL write failure.");
        }

        _remainingWriteFailures--;
        _inner.Write(buffer, offset, count);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
