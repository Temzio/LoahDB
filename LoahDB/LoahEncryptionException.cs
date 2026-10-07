namespace LoahDB;

/// <summary>Thrown when encryption, decryption, or key rotation fails.</summary>
public sealed class LoahEncryptionException : Exception
{
    public LoahEncryptionException(string message) : base(message)
    {
    }

    public LoahEncryptionException(string message, Exception inner) : base(message, inner)
    {
    }
}
