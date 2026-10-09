namespace LoahDB.Engine;

internal static class ValueEncoding
{
    private const byte InlineTag = 0;
    private const byte OverflowTag = 1;

    public static byte[] Encode(LoahPageDatabase db, byte[] value)
    {
        value = db.ProtectPayload(value);
        if (value.Length <= LoahConstants.BTreeInlineValueMaxBytes)
        {
            var result = new byte[value.Length + 1];
            result[0] = InlineTag;
            value.CopyTo(result, 1);
            return result;
        }

        var overflowPage = db.AllocateOverflowChain(value);
        var pointer = new byte[5];
        pointer[0] = OverflowTag;
        BitConverter.TryWriteBytes(pointer.AsSpan(1, 4), overflowPage);
        return pointer;
    }

    public static byte[] Decode(LoahPageDatabase db, ReadOnlySpan<byte> stored)
    {
        if (stored.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (stored[0] == InlineTag)
        {
            return db.UnprotectPayload(stored.Slice(1).ToArray());
        }

        if (stored[0] == OverflowTag && stored.Length >= 5)
        {
            var pageId = BitConverter.ToUInt32(stored.Slice(1, 4));
            return db.UnprotectPayload(db.ReadOverflowChain(pageId));
        }

        return db.UnprotectPayload(stored.ToArray());
    }
}
