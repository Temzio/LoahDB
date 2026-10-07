namespace LoahDB.Engine;

internal static class LoahConstants
{
    public static ReadOnlySpan<byte> MagicBytes => "LOAHDB\0"u8;
    public const ushort FormatVersion = 1;
    public const int PageSize = 4096;
    public const int HeaderSaltOffset = 32;
    public const int HeaderSaltLength = 32;
    public const int HeaderCrcOffset = 64;
    public const int BTreeMaxKeyBytes = 512;
    public const int BTreeInlineValueMaxBytes = 3500;
}
