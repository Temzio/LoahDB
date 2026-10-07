namespace LoahDB.Engine;

internal sealed class PageBuffer
{
    public readonly byte[] Data = new byte[LoahConstants.PageSize];
    public uint PageId { get; set; }
    public bool IsDirty { get; set; }

    public PageType Type => (PageType)Data[0];

    public void SetType(PageType type) => Data[0] = (byte)type;

    public ushort EntryCount
    {
        get => BitConverter.ToUInt16(Data, 1);
        set => BitConverter.TryWriteBytes(Data.AsSpan(1, 2), value);
    }

    public ushort Lower
    {
        get => BitConverter.ToUInt16(Data, 3);
        set => BitConverter.TryWriteBytes(Data.AsSpan(3, 2), value);
    }

    public ushort Upper
    {
        get => BitConverter.ToUInt16(Data, 5);
        set => BitConverter.TryWriteBytes(Data.AsSpan(5, 2), value);
    }

    public void InitializeSlotted(PageType type)
    {
        Array.Clear(Data);
        SetType(type);
        EntryCount = 0;
        Lower = 8; // slot directory starts after header
        Upper = LoahConstants.PageSize;
    }

    public bool TryInsertRecord(ReadOnlySpan<byte> record, out ushort slotIndex)
    {
        slotIndex = 0;
        if (record.Length > ushort.MaxValue)
        {
            return false;
        }

        var needed = 2 + record.Length;
        if (Lower + needed > Upper)
        {
            return false;
        }

        Upper -= (ushort)record.Length;
        record.CopyTo(Data.AsSpan(Upper, record.Length));
        BitConverter.TryWriteBytes(Data.AsSpan(Lower, 2), Upper);
        Lower += 2;
        slotIndex = EntryCount;
        EntryCount++;
        return true;
    }

    public ReadOnlySpan<byte> GetRecord(ushort slot)
    {
        if (slot >= EntryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        var offsetPos = 8 + slot * 2;
        var recordOffset = BitConverter.ToUInt16(Data, offsetPos);
        if (recordOffset >= Upper)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return Data.AsSpan(recordOffset, Upper + (ushort)(LoahConstants.PageSize - recordOffset) - (ushort)(LoahConstants.PageSize - recordOffset));
    }

    public ReadOnlySpan<byte> GetRecordAtSlot(ushort slot)
    {
        var offsetPos = 8 + slot * 2;
        var recordOffset = BitConverter.ToUInt16(Data, offsetPos);
        var end = slot + 1 < EntryCount
            ? BitConverter.ToUInt16(Data, 8 + (slot + 1) * 2)
            : Upper;
        if (recordOffset < end)
        {
            // records grow upward; find length by next record start or upper
            var nextOffset = slot + 1 < EntryCount
                ? BitConverter.ToUInt16(Data, 8 + (slot + 1) * 2)
                : Upper;
            return Data.AsSpan(recordOffset, nextOffset - recordOffset);
        }

        return Data.AsSpan(recordOffset, Upper - recordOffset);
    }

    public void DeleteSlot(ushort slot)
    {
        if (slot >= EntryCount)
        {
            return;
        }

        for (ushort i = slot; i < EntryCount - 1; i++)
        {
            var from = 8 + (i + 1) * 2;
            var to = 8 + i * 2;
            Data[to] = Data[from];
            Data[to + 1] = Data[from + 1];
        }

        EntryCount--;
        Lower -= 2;
    }
}
