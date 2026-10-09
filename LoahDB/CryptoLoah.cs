using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("LoahDB.Tests")]
namespace LoahDB;

public class CryptoLoah
{
    protected internal static string Encrypt(string plainText, string key) =>
        LoahAuthenticatedCrypto.EncryptString(plainText, key, 100_000);

    protected internal static string Decrypt(string cipherText, string key) =>
        LoahAuthenticatedCrypto.DecryptString(cipherText, key, 100_000);

    internal static string DecryptLegacyCbc(string cipherText, string key)
    {
        var cipherBytes = Convert.FromBase64String(cipherText);
        var keyBytes = AdjustKeySize(key, 256);
        const int ivLength = 16;
        if (cipherBytes.Length < ivLength)
        {
            throw new LoahEncryptionException("Legacy ciphertext is too short.");
        }

        var ivBytes = cipherBytes.AsSpan(0, ivLength);
        var encrypted = cipherBytes.AsSpan(ivLength);

        using var aesAlg = Aes.Create();
        aesAlg.Key = keyBytes;
        aesAlg.IV = ivBytes.ToArray();

        using var decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
        using var msDecrypt = new MemoryStream(encrypted.ToArray());
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt);
        return srDecrypt.ReadToEnd();
    }

    private static byte[] AdjustKeySize(string key, int bitSize)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var bytes = bitSize / 8;
        var adjustedKey = new byte[bytes];
        Array.Copy(keyBytes, adjustedKey, Math.Min(keyBytes.Length, bytes));
        return adjustedKey;
    }
}
