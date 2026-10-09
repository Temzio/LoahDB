using System.Globalization;
using System.Text;

namespace LoahDB.Engine;

internal static class IndexKeyEncoding
{
    private const char CompositeSeparator = '\u001f';

    public static string EncodeSingle(object? value)
    {
        if (value is null)
        {
            return "\0N";
        }

        return value switch
        {
            string s => "\0S" + s,
            int i => "\0I" + i.ToString("D20", CultureInfo.InvariantCulture),
            long l => "\0L" + l.ToString("D20", CultureInfo.InvariantCulture),
            double d => "\0D" + BitConverter.DoubleToInt64Bits(d).ToString("D20", CultureInfo.InvariantCulture),
            float f => "\0F" + BitConverter.SingleToInt32Bits(f).ToString("D20", CultureInfo.InvariantCulture),
            bool b => "\0B" + (b ? "1" : "0"),
            DateTime dt => "\0T" + dt.ToUniversalTime().Ticks.ToString("D20", CultureInfo.InvariantCulture),
            DateTimeOffset dto => "\0O" + dto.UtcTicks.ToString("D20", CultureInfo.InvariantCulture),
            _ => "\0X" + (value.ToString() ?? string.Empty),
        };
    }

    public static string EncodeComposite(IEnumerable<object?> components)
    {
        var list = components as IReadOnlyList<object?> ?? components.ToList();
        if (list.Count == 0)
        {
            return string.Empty;
        }

        if (list.Count == 1)
        {
            return EncodeSingle(list[0]);
        }

        return string.Join(CompositeSeparator, list.Select(EncodeSingle));
    }

    public static string EncodeFromUserKey<TKey>(TKey key) => EncodeSingle(key);

    public static string ExtractSortKey(string btreeKey, bool unique)
    {
        if (unique)
        {
            return btreeKey;
        }

        var separator = btreeKey.LastIndexOf('\0');
        return separator < 0 ? btreeKey : btreeKey[..separator];
    }

    public static string MakeUniqueTreeKey(string sortKey, string documentId) => sortKey + "\0" + documentId;

    public static byte[] ToBytes(string key) => Encoding.UTF8.GetBytes(key);

    public static string FromBytes(byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
