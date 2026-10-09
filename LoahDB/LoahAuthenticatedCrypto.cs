using System.Security.Cryptography;
using System.Text;

namespace LoahDB;

internal static class LoahAuthenticatedCrypto
{
    public const string GcmStringPrefix = "LOAH2:";
    private const int SaltLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const byte PayloadMarker = (byte)'E';

    public static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations)
    {
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new LoahEncryptionException("Encryption passphrase is required.");
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    public static string EncryptString(string plainText, string passphrase, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var key = DeriveKey(passphrase, salt, iterations);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagLength];
        using (var gcm = new AesGcm(key, TagLength))
        {
            gcm.Encrypt(nonce, plainBytes, cipher, tag);
        }

        var payload = new byte[1 + SaltLength + NonceLength + TagLength + cipher.Length];
        payload[0] = 2;
        salt.CopyTo(payload.AsSpan(1));
        nonce.CopyTo(payload.AsSpan(1 + SaltLength));
        tag.CopyTo(payload.AsSpan(1 + SaltLength + NonceLength));
        cipher.CopyTo(payload.AsSpan(1 + SaltLength + NonceLength + TagLength));
        return GcmStringPrefix + Convert.ToBase64String(payload);
    }

    public static string DecryptString(string cipherText, string passphrase, int iterations)
    {
        if (cipherText.StartsWith(GcmStringPrefix, StringComparison.Ordinal))
        {
            var payload = Convert.FromBase64String(cipherText[GcmStringPrefix.Length..]);
            if (payload.Length < 1 + SaltLength + NonceLength + TagLength)
            {
                throw new LoahEncryptionException("Encrypted payload is too short.");
            }

            var salt = payload.AsSpan(1, SaltLength);
            var nonce = payload.AsSpan(1 + SaltLength, NonceLength);
            var tag = payload.AsSpan(1 + SaltLength + NonceLength, TagLength);
            var cipher = payload.AsSpan(1 + SaltLength + NonceLength + TagLength);
            var key = DeriveKey(passphrase, salt, iterations);
            var plain = new byte[cipher.Length];
            try
            {
                using var gcm = new AesGcm(key, TagLength);
                gcm.Decrypt(nonce, cipher, tag, plain);
            }
            catch (CryptographicException ex)
            {
                throw new LoahEncryptionException("Decryption failed (wrong key or corrupted data).", ex);
            }

            return Encoding.UTF8.GetString(plain);
        }

        try
        {
            return CryptoLoah.DecryptLegacyCbc(cipherText, passphrase);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new LoahEncryptionException("Decryption failed (wrong key or corrupted data).", ex);
        }
    }

    public static byte[] EncryptPayload(ReadOnlySpan<byte> plain, ReadOnlySpan<byte> key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];
        using (var gcm = new AesGcm(key.ToArray(), TagLength))
        {
            gcm.Encrypt(nonce, plain, cipher, tag);
        }

        var result = new byte[1 + NonceLength + cipher.Length + TagLength];
        result[0] = PayloadMarker;
        nonce.CopyTo(result.AsSpan(1));
        cipher.CopyTo(result.AsSpan(1 + NonceLength));
        tag.CopyTo(result.AsSpan(1 + NonceLength + cipher.Length));
        return result;
    }

    public static byte[] DecryptPayload(ReadOnlySpan<byte> stored, ReadOnlySpan<byte> key)
    {
        if (stored.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (stored[0] != PayloadMarker)
        {
            return stored.ToArray();
        }

        if (stored.Length < 1 + NonceLength + TagLength)
        {
            throw new LoahEncryptionException("Encrypted page payload is too short.");
        }

        var nonce = stored.Slice(1, NonceLength);
        var tagStart = stored.Length - TagLength;
        var cipher = stored.Slice(1 + NonceLength, tagStart - (1 + NonceLength));
        var tag = stored.Slice(tagStart, TagLength);
        var plain = new byte[cipher.Length];
        try
        {
            using var gcm = new AesGcm(key.ToArray(), TagLength);
            gcm.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException ex)
        {
            throw new LoahEncryptionException("Decryption failed (wrong key or corrupted data).", ex);
        }

        return plain;
    }
}
