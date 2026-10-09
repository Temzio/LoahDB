using System.Security.Cryptography;
using System.Text;

namespace LoahDB.Tests;

public class LoahPhase6Should : IDisposable
{
    private readonly string _root = "Phase6_" + Guid.NewGuid().ToString("N");
    private readonly LoahOptions _options;

    public LoahPhase6Should()
    {
        _options = new LoahOptions
        {
            BasePath = Path.Combine(Path.GetTempPath(), "LoahDBTests"),
            StorageFormat = LoahStorageFormat.PageFile,
            EncryptionKey = "phase6-test-passphrase",
        };
    }

    public void Dispose()
    {
        var db = Path.Combine(_options.BasePath, _root + ".loahdb");
        if (File.Exists(db))
        {
            File.Delete(db);
        }

        var wal = db + "-wal";
        if (File.Exists(wal))
        {
            File.Delete(wal);
        }
    }

    [Fact]
    public void NewEncryptedStoreRoundTripsDocuments()
    {
        using (var store = new LoahStore(_root, _options))
        {
            store.Collection<Person>("users").Insert(new Person { Name = "Ada", Email = "ada@example.com" });
        }

        using var reopened = new LoahStore(_root, _options);
        Assert.Equal("Ada", reopened.Collection<Person>("users").All()[0].Name);
    }

    [Fact]
    public void WrongKeyFailsOnOpen()
    {
        using (var store = new LoahStore(_root, _options))
        {
            store.Collection<Person>("users").Insert(new Person { Name = "Ada" });
        }

        var bad = _options.Clone();
        bad.EncryptionKey = "wrong-key";
        Assert.Throws<LoahEncryptionException>(() =>
        {
            using var reopened = new LoahStore(_root, bad);
        });
    }

    [Fact]
    public void EnableEncryptionOnExistingPlaintextStore()
    {
        var plain = _options.Clone();
        plain.EncryptionKey = null;
        using (var store = new LoahStore(_root, plain))
        {
            store.Collection<Person>("users").Insert(new Person { Name = "Before" });
            store.Options.EncryptionKey = _options.EncryptionKey;
            store.EnableEncryption();
        }

        using var encrypted = new LoahStore(_root, _options);
        Assert.Equal("Before", encrypted.Collection<Person>("users").All()[0].Name);
    }

    [Fact]
    public void RotateEncryptionKeyKeepsData()
    {
        using (var store = new LoahStore(_root, _options))
        {
            store.Collection<Person>("users").Insert(new Person { Name = "RotateMe" });
            store.RotateEncryptionKey("brand-new-passphrase-2026");
        }

        var rotated = _options.Clone();
        rotated.EncryptionKey = "brand-new-passphrase-2026";
        using var reopened = new LoahStore(_root, rotated);
        Assert.Equal("RotateMe", reopened.Collection<Person>("users").All()[0].Name);
    }

    [Fact]
    public void GcmStringFormatIsAuthenticated()
    {
        var cipher = LoahAuthenticatedCrypto.EncryptString("secret", _options.EncryptionKey!, _options.KeyDerivationIterations);
        Assert.StartsWith(LoahAuthenticatedCrypto.GcmStringPrefix, cipher, StringComparison.Ordinal);
        var tampered = cipher[..^4] + "AAAA";
        Assert.Throws<LoahEncryptionException>(() =>
            LoahAuthenticatedCrypto.DecryptString(tampered, _options.EncryptionKey!, _options.KeyDerivationIterations));
    }

    [Fact]
    public void LegacyCbcCiphertextStillDecrypts()
    {
        var legacy = EncryptLegacyCbc("legacy payload", _options.EncryptionKey!);
        var plain = LoahAuthenticatedCrypto.DecryptString(legacy, _options.EncryptionKey!, _options.KeyDerivationIterations);
        Assert.Equal("legacy payload", plain);
    }

    private static string EncryptLegacyCbc(string plainText, string key)
    {
        var keyBytes = new byte[32];
        Array.Copy(Encoding.UTF8.GetBytes(key), keyBytes, Math.Min(32, Encoding.UTF8.GetByteCount(key)));
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        ms.Write(aes.IV);
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private sealed class Person : LoahDocument
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
    }
}
