using System.Text;
using Newtonsoft.Json;

namespace LoahDB.Engine;

/// <summary>
/// Single-file page store with free-list and LRU cache.
/// </summary>
internal sealed class LoahPageDatabase : IDisposable
{
    private readonly string _filePath;
    private readonly FileStream _stream;
    private readonly PageCache _cache;
    private readonly WriteAheadLog _wal;
    private readonly object _writeLock = new();
    private StoreWriterLock? _writerLock;
    private int _writeBatchDepth;
    private uint _pageCount;
    private uint _freeListHead;
    private uint _catalogRoot;
    private int _schemaVersion;

    public LoahPageDatabase(string filePath, int pageCacheCapacity, Stream? walStreamOverride = null)
    {
        _filePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var exists = File.Exists(filePath);
        _stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        _cache = new PageCache(pageCacheCapacity, WritePageToDisk, CanEvictPage);
        _wal = new WriteAheadLog(filePath + "-wal", walStreamOverride);
        if (!exists || _stream.Length == 0)
        {
            InitializeNewFile();
        }
        else
        {
            RecoverFromWal();
            LoadHeader();
        }
    }

    internal bool IsWriteBatchActive => _writeBatchDepth > 0;

    public uint PageCount => _pageCount;

    internal string DatabaseFilePath => _filePath;

    public int SchemaVersion => _schemaVersion;

    public BPlusTree OpenTree(uint rootPageId) => new BPlusTree(this, rootPageId);

    public BPlusTree CreateTree()
    {
        var root = AllocatePage();
        var page = GetPage(root);
        BTreeRecords.TryWriteLeaf(page, Array.Empty<(string, byte[])>());
        MarkDirty(root);
        Flush();
        return new BPlusTree(this, root);
    }

    public uint CatalogRoot => _catalogRoot;

    public BPlusTree OpenCatalog() => OpenTree(_catalogRoot);

    public void SetSchemaVersion(int version)
    {
        _schemaVersion = version;
        WriteHeader();
        Flush();
    }

    public byte[] ReadValue(ReadOnlySpan<byte> stored) => ValueEncoding.Decode(this, stored);

    public byte[] StoreValue(byte[] value) => ValueEncoding.Encode(this, value);

    public byte[] ReadOverflowChain(uint firstPageId)
    {
        using var ms = new MemoryStream();
        var pageId = firstPageId;
        while (pageId != 0)
        {
            var page = GetPage(pageId);
            if (page.Type != PageType.Overflow)
            {
                break;
            }

            var chunkLen = BitConverter.ToUInt32(page.Data, 5);
            ms.Write(page.Data, 9, (int)chunkLen);
            pageId = BitConverter.ToUInt32(page.Data, 1);
        }

        return ms.ToArray();
    }

    public uint AllocateOverflowChain(byte[] value)
    {
        const int chunkCapacity = LoahConstants.PageSize - 9;
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < value.Length; offset += chunkCapacity)
        {
            var len = Math.Min(chunkCapacity, value.Length - offset);
            var chunk = new byte[len];
            Array.Copy(value, offset, chunk, 0, len);
            chunks.Add(chunk);
        }

        var pageIds = chunks.Select(_ => AllocatePage()).ToList();
        for (var i = 0; i < pageIds.Count; i++)
        {
            var page = GetPage(pageIds[i]);
            page.SetType(PageType.Overflow);
            var next = i + 1 < pageIds.Count ? pageIds[i + 1] : 0u;
            BitConverter.TryWriteBytes(page.Data.AsSpan(1, 4), next);
            BitConverter.TryWriteBytes(page.Data.AsSpan(5, 4), (uint)chunks[i].Length);
            chunks[i].CopyTo(page.Data, 9);
            MarkDirty(pageIds[i]);
        }

        return pageIds[0];
    }

    internal void CompleteModification()
    {
        if (_writeBatchDepth > 0)
        {
            return;
        }

        Flush();
    }

    internal void BeginWriteTransaction(TimeSpan lockTimeout)
    {
        if (_writeBatchDepth == 0)
        {
            _writerLock = StoreWriterLock.Acquire(_filePath, lockTimeout);
        }

        _writeBatchDepth++;
    }

    internal void CommitWriteTransaction()
    {
        if (_writeBatchDepth == 0)
        {
            throw new InvalidOperationException("No active page-store write transaction.");
        }

        _writeBatchDepth--;
        if (_writeBatchDepth > 0)
        {
            return;
        }

        try
        {
            var headerBytes = new byte[LoahConstants.PageSize];
            WriteHeaderBytes(headerBytes);
            var dirty = _cache.GetDirtyPages().Where(p => p.PageId != 0).ToList();

            foreach (var page in dirty)
            {
                _wal.AppendPage(page.PageId, page.Data);
            }

            _wal.AppendHeader(headerBytes);
            _wal.AppendCommit();
            _wal.FlushToDisk();

            ApplyWalReplay(new WalReplayResult(true,
                dirty.ToDictionary(p => p.PageId, p => p.Data.ToArray()),
                headerBytes));

            _wal.AppendCheckpoint();

            var cachedHeader = GetPage(0);
            headerBytes.CopyTo(cachedHeader.Data, 0);
            cachedHeader.IsDirty = false;
            foreach (var page in dirty)
            {
                page.IsDirty = false;
            }
        }
        catch
        {
            _cache.Clear();
            LoadHeader();
            throw;
        }
        finally
        {
            _writerLock?.Dispose();
            _writerLock = null;
        }
    }

    internal void RollbackWriteTransaction()
    {
        if (_writeBatchDepth == 0)
        {
            throw new InvalidOperationException("No active page-store write transaction.");
        }

        _writeBatchDepth--;
        if (_writeBatchDepth > 0)
        {
            return;
        }

        try
        {
            _cache.Clear();
            LoadHeader();
        }
        finally
        {
            _writerLock?.Dispose();
            _writerLock = null;
        }
    }

    internal void Flush()
    {
        if (_writeBatchDepth > 0)
        {
            return;
        }

        lock (_writeLock)
        {
            var headerBytes = new byte[LoahConstants.PageSize];
            WriteHeaderBytes(headerBytes);
            _stream.Seek(0, SeekOrigin.Begin);
            _stream.Write(headerBytes, 0, headerBytes.Length);

            var cachedHeader = GetPage(0);
            headerBytes.CopyTo(cachedHeader.Data, 0);
            cachedHeader.IsDirty = false;

            foreach (var page in _cache.GetDirtyPages())
            {
                if (page.PageId == 0)
                {
                    continue;
                }

                WritePageToDisk(page);
            }

            _stream.Flush(flushToDisk: true);
        }
    }

    private void WritePageToDisk(PageBuffer page)
    {
        if (page.PageId == 0)
        {
            return;
        }

        _stream.Seek(page.PageId * LoahConstants.PageSize, SeekOrigin.Begin);
        _stream.Write(page.Data, 0, LoahConstants.PageSize);
        page.IsDirty = false;
        _cache.ClearDirty(page.PageId);
    }

    public PageBuffer GetPage(uint pageId) =>
        _cache.GetOrAdd(pageId, LoadPage);

    public void MarkDirty(uint pageId) => _cache.MarkDirty(pageId);

    public uint AllocatePage()
    {
        lock (_writeLock)
        {
            if (_freeListHead != 0)
            {
                var pageId = _freeListHead;
                var page = GetPage(pageId);
                _freeListHead = BitConverter.ToUInt32(page.Data, 1);
                page.InitializeSlotted(PageType.Free);
                MarkDirty(pageId);
                return pageId;
            }

            var newId = _pageCount;
            _pageCount++;
            var buffer = GetPage(newId);
            buffer.PageId = newId;
            buffer.InitializeSlotted(PageType.Free);
            _stream.SetLength(_pageCount * LoahConstants.PageSize);
            WriteHeader();
            MarkDirty(newId);
            return newId;
        }
    }

    public void FreePage(uint pageId)
    {
        var page = GetPage(pageId);
        page.InitializeSlotted(PageType.Free);
        BitConverter.TryWriteBytes(page.Data.AsSpan(1, 4), _freeListHead);
        _freeListHead = pageId;
        MarkDirty(pageId);
        WriteHeader();
    }

    public void Dispose()
    {
        if (_writeBatchDepth > 0)
        {
            RollbackWriteTransaction();
        }

        Flush();
        _wal.Dispose();
        _stream.Dispose();
    }

    private bool CanEvictPage(PageBuffer page) => !(page.IsDirty && _writeBatchDepth > 0);

    private void RecoverFromWal()
    {
        var replay = _wal.ReplayCommitted();
        if (!replay.HasCommittedTransaction)
        {
            return;
        }

        ApplyWalReplay(replay);
        _wal.AppendCheckpoint();
    }

    private void ApplyWalReplay(WalReplayResult replay)
    {
        lock (_writeLock)
        {
            foreach (var (pageId, bytes) in replay.Pages)
            {
                _stream.Seek(pageId * LoahConstants.PageSize, SeekOrigin.Begin);
                _stream.Write(bytes, 0, bytes.Length);
            }

            if (replay.HeaderPage is not null)
            {
                _stream.Seek(0, SeekOrigin.Begin);
                _stream.Write(replay.HeaderPage, 0, replay.HeaderPage.Length);
            }

            _stream.Flush(flushToDisk: true);
        }

        _cache.Clear();
    }

    private void InitializeNewFile()
    {
        _pageCount = 1;
        _freeListHead = 0;
        _schemaVersion = 1;
        _stream.SetLength(LoahConstants.PageSize);
        var header = GetPage(0);
        WriteHeaderToPage(header);
        MarkDirty(0);
        var catalogRoot = AllocatePage();
        _catalogRoot = catalogRoot;
        BTreeRecords.TryWriteLeaf(GetPage(catalogRoot), Array.Empty<(string, byte[])>());
        MarkDirty(catalogRoot);
        WriteHeader();
        Flush();
    }

    private void LoadHeader()
    {
        _pageCount = (uint)(_stream.Length / LoahConstants.PageSize);
        var header = LoadPage(0);
        ValidateHeader(header);
        _freeListHead = BitConverter.ToUInt32(header.Data, 20);
        _catalogRoot = BitConverter.ToUInt32(header.Data, 24);
        _schemaVersion = BitConverter.ToInt32(header.Data, 28);
    }

    private void ValidateHeader(PageBuffer header)
    {
        if (!header.Data.AsSpan(0, LoahConstants.MagicBytes.Length).SequenceEqual(LoahConstants.MagicBytes))
        {
            throw new InvalidDataException("Invalid LoahDB page file (bad magic).");
        }

        var storedCrc = BitConverter.ToUInt32(header.Data, LoahConstants.HeaderCrcOffset);
        var computed = ComputeHeaderCrc(header.Data);
        if (storedCrc != computed)
        {
            throw new InvalidDataException("Invalid LoahDB page file (header checksum mismatch).");
        }
    }

    private void WriteHeader()
    {
        var header = GetPage(0);
        WriteHeaderToPage(header);
        MarkDirty(0);
    }

    private void WriteHeaderToPage(PageBuffer header)
    {
        WriteHeaderBytes(header.Data);
        header.SetType(PageType.Header);
    }

    private void WriteHeaderBytes(Span<byte> destination)
    {
        LoahConstants.MagicBytes.CopyTo(destination);
        BitConverter.TryWriteBytes(destination.Slice(8, 2), LoahConstants.FormatVersion);
        BitConverter.TryWriteBytes(destination.Slice(10, 2), (ushort)LoahConstants.PageSize);
        BitConverter.TryWriteBytes(destination.Slice(12, 8), _pageCount);
        BitConverter.TryWriteBytes(destination.Slice(20, 4), _freeListHead);
        BitConverter.TryWriteBytes(destination.Slice(24, 4), _catalogRoot);
        BitConverter.TryWriteBytes(destination.Slice(28, 4), _schemaVersion);
        var crc = ComputeHeaderCrc(destination.Slice(0, LoahConstants.HeaderCrcOffset).ToArray());
        BitConverter.TryWriteBytes(destination.Slice(LoahConstants.HeaderCrcOffset, 4), crc);
    }

    private static uint ComputeHeaderCrc(byte[] data)
    {
        var span = data.AsSpan(0, LoahConstants.HeaderCrcOffset);
        return Crc32.Compute(span);
    }

    private PageBuffer LoadPage(uint pageId)
    {
        var buffer = new PageBuffer { PageId = pageId };
        _stream.Seek(pageId * LoahConstants.PageSize, SeekOrigin.Begin);
        var read = _stream.Read(buffer.Data, 0, LoahConstants.PageSize);
        if (read < LoahConstants.PageSize)
        {
            Array.Clear(buffer.Data, read, LoahConstants.PageSize - read);
        }

        return buffer;
    }

    internal void EnsureReadable()
    {
        var header = LoadPage(0);
        ValidateHeader(header);
    }

    internal IEnumerable<(string Collection, string DocId, byte[] Payload)> EnumerateAllCollectionDocuments()
    {
        var catalog = OpenCatalog();
        foreach (var (name, metaBytes) in catalog.Scan())
        {
            var json = Encoding.UTF8.GetString(metaBytes);
            var entry = JsonConvert.DeserializeObject<CollectionCatalogEntry>(json);
            if (entry is null)
            {
                continue;
            }

            var tree = OpenTree(entry.RootPageId);
            foreach (var (docId, payload) in tree.Scan())
            {
                yield return (name, docId, payload);
            }
        }
    }

    internal void CheckpointForBackup()
    {
        Flush();
        _wal.AppendCheckpoint();
    }

    internal void Vacuum()
    {
        lock (_writeLock)
        {
            Flush();
            _freeListHead = 0;
            WriteHeader();
            Flush();
            _stream.SetLength(_pageCount * LoahConstants.PageSize);
            _wal.AppendCheckpoint();
        }
    }
}
