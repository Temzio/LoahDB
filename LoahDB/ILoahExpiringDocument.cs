namespace LoahDB;

/// <summary>
/// Document that can be purged automatically when <see cref="ExpiresAtUtc"/> is in the past.
/// </summary>
public interface ILoahExpiringDocument : ILoahDocument
{
    DateTime? ExpiresAtUtc { get; set; }
}
