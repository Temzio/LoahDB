using System.Text;

namespace LoahDB.Engine;

internal static class BTreeRecords
{
    private const int HeaderSize = 16;

    public static List<(string Key, byte[] Value)> ReadAll(PageBuffer page)
    {
        var result = new List<(string, byte[])>();
        if (page.Type != PageType.BTreeLeaf && page.Type != PageType.BTreeInternal)
        {
            return result;
        }

        var count = BitConverter.ToUInt16(page.Data, 1);
        var offset = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            if (offset + 4 > LoahConstants.PageSize)
            {
                break;
            }

            var keyLen = BitConverter.ToUInt16(page.Data, offset);
            offset += 2;
            var valLen = BitConverter.ToUInt32(page.Data, offset);
            offset += 4;
            if (offset + keyLen + valLen > LoahConstants.PageSize)
            {
                break;
            }

            var key = Encoding.UTF8.GetString(page.Data, offset, keyLen);
            offset += keyLen;
            var value = page.Data.AsSpan(offset, (int)valLen).ToArray();
            offset += (int)valLen;
            result.Add((key, value));
        }

        return result;
    }

    public static bool TryWriteLeaf(PageBuffer page, IReadOnlyList<(string Key, byte[] Value)> entries)
    {
        page.InitializeSlotted(PageType.BTreeLeaf);
        var offset = HeaderSize;
        foreach (var (key, value) in entries)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            if (keyBytes.Length > LoahConstants.BTreeMaxKeyBytes)
            {
                throw new InvalidOperationException("Document id exceeds maximum key length.");
            }

            var needed = 2 + 4 + keyBytes.Length + value.Length;
            if (offset + needed > LoahConstants.PageSize)
            {
                return false;
            }

            BitConverter.TryWriteBytes(page.Data.AsSpan(offset, 2), (ushort)keyBytes.Length);
            offset += 2;
            BitConverter.TryWriteBytes(page.Data.AsSpan(offset, 4), (uint)value.Length);
            offset += 4;
            keyBytes.CopyTo(page.Data, offset);
            offset += keyBytes.Length;
            value.CopyTo(page.Data, offset);
            offset += value.Length;
        }

        BitConverter.TryWriteBytes(page.Data.AsSpan(1, 2), (ushort)entries.Count);
        return true;
    }

    public static bool TryWriteInternal(PageBuffer page, IReadOnlyList<(string Key, uint ChildPage)> entries)
    {
        page.InitializeSlotted(PageType.BTreeInternal);
        var offset = HeaderSize;
        foreach (var (key, child) in entries)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var needed = 2 + 4 + keyBytes.Length;
            if (offset + needed > LoahConstants.PageSize)
            {
                return false;
            }

            BitConverter.TryWriteBytes(page.Data.AsSpan(offset, 2), (ushort)keyBytes.Length);
            offset += 2;
            BitConverter.TryWriteBytes(page.Data.AsSpan(offset, 4), child);
            offset += 4;
            keyBytes.CopyTo(page.Data, offset);
            offset += keyBytes.Length;
        }

        BitConverter.TryWriteBytes(page.Data.AsSpan(1, 2), (ushort)entries.Count);
        return true;
    }

    public static List<(string Key, uint Child)> ReadInternal(PageBuffer page)
    {
        var result = new List<(string, uint)>();
        var count = BitConverter.ToUInt16(page.Data, 1);
        var offset = HeaderSize;
        for (var i = 0; i < count; i++)
        {
            var keyLen = BitConverter.ToUInt16(page.Data, offset);
            offset += 2;
            var child = BitConverter.ToUInt32(page.Data, offset);
            offset += 4;
            var key = Encoding.UTF8.GetString(page.Data, offset, keyLen);
            offset += keyLen;
            result.Add((key, child));
        }

        return result;
    }
}
