
using Newtonsoft.Json;
using System.Collections;

namespace LoahDB
{
    /// <summary>
    /// By creating a new object of this class, a new file for your information named key is created in the specified root and you can read or change its information.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Loah<T>
    {
        private readonly LoahStorage _storage;
        private readonly string _filePath;

        /// <summary>
        /// Creates or opens a single-document database file.
        /// </summary>
        /// <param name="key">Database name</param>
        /// <param name="root">Database root</param>
        /// <param name="encryptionKey">Optional AES encryption key</param>
        public Loah(string key, string root, string encryptionKey = "")
            : this(key, root, CreateOptions(encryptionKey))
        {
        }

        /// <summary>
        /// Creates or opens a single-document database file with full configuration.
        /// </summary>
        public Loah(string key, string root, LoahOptions options)
        {
            _storage = new LoahStorage(options);
            _filePath = _storage.ResolvePath(root, key);
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, string.Empty);
            }
        }

        /// <summary>
        /// This function helps you to get data from database.
        /// </summary>
        /// <returns>An object of previously defined type</returns>
        public T? Get()
        {
            var jsonObject = _storage.Read<T>(_filePath);
            if (jsonObject is not null)
            {
                return jsonObject;
            }

            return CreateDefaultInstance();
        }

        public async Task<T?> GetAsync(CancellationToken cancellationToken = default)
        {
            var jsonObject = await _storage.ReadAsync<T>(_filePath, cancellationToken);
            if (jsonObject is not null)
            {
                return jsonObject;
            }

            return CreateDefaultInstance();
        }

        /// <summary>
        /// This function helps you to add and update data into database.
        /// </summary>
        /// <param name="obj"></param>
        public void Set(T obj) => _storage.Write(_filePath, obj);

        public Task SetAsync(T obj, CancellationToken cancellationToken = default) =>
            _storage.WriteAsync(_filePath, obj, cancellationToken);

        private T? CreateDefaultInstance()
        {
            Type type = typeof(T);
            if (!type.IsGenericType || type.ContainsGenericParameters)
            {
                return default;
            }

            try
            {
                Type genericTypeDef = type.GetGenericTypeDefinition();
                if (genericTypeDef == typeof(List<>) || genericTypeDef == typeof(Stack<>) ||
                    genericTypeDef == typeof(Queue<>) || genericTypeDef == typeof(HashSet<>))
                {
                    Type typeArgument = type.GetGenericArguments()[0];
                    Type closedType = genericTypeDef.MakeGenericType(typeArgument);
                    object? listInstance = Activator.CreateInstance(closedType);
                    return (T?)listInstance;
                }

                if (genericTypeDef == typeof(Dictionary<,>))
                {
                    Type keyTypeArgument = type.GetGenericArguments()[0];
                    Type valueTypeArgument = type.GetGenericArguments()[1];
                    Type closedDictType = typeof(Dictionary<,>).MakeGenericType(keyTypeArgument, valueTypeArgument);
                    object? dictInstance = Activator.CreateInstance(closedDictType);
                    return (T?)dictInstance;
                }

                object? genericInstance = Activator.CreateInstance(type);
                return (T?)genericInstance;
            }
            catch (Exception)
            {
                throw new Exception("Generic type not supported");
            }
        }

        private static LoahOptions CreateOptions(string encryptionKey) =>
            new LoahOptions { EncryptionKey = string.IsNullOrEmpty(encryptionKey) ? null : encryptionKey };
    }

    public static class LoahDb
    {
        private static LoahOptions DefaultOptions => new();

        /// <summary>
        /// This function informs you about the existence of the database with this key and root.
        /// </summary>
        public static bool Exists(string key, string root) =>
            Exists(key, root, DefaultOptions);

        public static bool Exists(string key, string root, LoahOptions options) =>
            new LoahStorage(options).Exists(new LoahStorage(options).ResolvePath(root, key));

        /// <summary>
        /// This function deletes the database with this key and the root if it exists.
        /// </summary>
        public static void Delete(string key, string root) =>
            Delete(key, root, DefaultOptions);

        public static void Delete(string key, string root, LoahOptions options)
        {
            var storage = new LoahStorage(options);
            storage.Delete(storage.ResolvePath(root, key));
        }

        /// <summary>
        /// This function returns a list of keys from input root
        /// </summary>
        public static List<string> KeyList(string root) =>
            KeyList(root, DefaultOptions);

        public static List<string> KeyList(string root, LoahOptions options) =>
            new LoahStorage(options).ListKeys(root).ToList();
    }
}
