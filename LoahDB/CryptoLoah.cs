using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("LoahDB.Tests")]
namespace LoahDB
{

    public class CryptoLoah
    {
        protected internal static string Encrypt(string plainText, string key)
        {
            byte[] encrypted;
            byte[] keyBytes = AdjustKeySize(key, 256);
            byte[] ivBytes;

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = keyBytes;
                aesAlg.GenerateIV(); // Generate a new IV for each encryption operation
                ivBytes = aesAlg.IV;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    // Write the IV to the beginning of the encrypted stream
                    msEncrypt.Write(ivBytes, 0, ivBytes.Length);

                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                        {
                            swEncrypt.Write(plainText);
                        }
                    }
                    encrypted = msEncrypt.ToArray();
                }
            }

            // Combine IV and ciphertext into a single Base64 encoded string
            byte[] combinedBytes = new byte[ivBytes.Length + encrypted.Length];
            Array.Copy(ivBytes, 0, combinedBytes, 0, ivBytes.Length);
            Array.Copy(encrypted, 0, combinedBytes, ivBytes.Length, encrypted.Length);

            return Convert.ToBase64String(combinedBytes);
        }


        protected internal static string Decrypt(string cipherText, string key)
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            string plaintext = null;
            byte[] keyBytes = AdjustKeySize(key, 256);
            byte[] ivBytes = new byte[16]; // Assuming IV size for AES is 128 bits (16 bytes)

            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = keyBytes;

                // Extract IV from the beginning of the ciphertext
                Array.Copy(cipherBytes, ivBytes, ivBytes.Length);
                var cipherList = cipherBytes.ToList();
                cipherList.RemoveRange(0,ivBytes.Length);
                cipherBytes=cipherList.ToArray();
                aesAlg.IV = ivBytes;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

                using (MemoryStream msDecrypt = new MemoryStream(cipherBytes, ivBytes.Length, cipherBytes.Length - ivBytes.Length))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                        {
                            plaintext = srDecrypt.ReadToEnd();
                        }
                    }
                }
            }
            return plaintext/*.Substring(ivBytes.ToString().Length)*/;
        }


        private static byte[] AdjustKeySize(string key, int bitSize)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            int bytes = bitSize / 8;
            byte[] adjustedKey = new byte[bytes];
            Array.Copy(keyBytes, adjustedKey, Math.Min(keyBytes.Length, bytes));
            return adjustedKey;
        }
    }
}
