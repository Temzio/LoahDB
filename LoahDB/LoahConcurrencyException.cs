namespace LoahDB;

/// <summary>
/// Thrown when a database lock cannot be acquired within the configured timeout.
/// </summary>
public sealed class LoahConcurrencyException : Exception
{
    public LoahConcurrencyException(string message) : base(message)
    {
    }

    public LoahConcurrencyException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
