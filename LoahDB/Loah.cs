
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
        readonly string _encryptionKey;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="key">Database name</param>
        /// <param name="root">Database root</param>
        public Loah(string key, string root, string encryptionKey = "")
        {
            _encryptionKey=encryptionKey;
            AppData =Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            FolderPath = Path.Combine(AppData, root);
            FilePath=Path.Combine(FolderPath, key+".loah");
            if (!Directory.Exists(FolderPath))
            {
                Directory.CreateDirectory(FolderPath);
            }
            if (!File.Exists(FilePath))
            {
                File.WriteAllText(FilePath, null);
            }
        }
        /// <summary>
        /// This function helps you to get data from database.
        /// </summary>
        /// <returns>An object of previously defined type</returns>
        public T? Get()
        {
            T? jsonObject = (T?)(object?)null;
            string json = File.ReadAllText(FilePath);
            if (!string.IsNullOrEmpty(_encryptionKey))
            {
                json=CryptoLoah.Decrypt(json,_encryptionKey);
            }
            jsonObject =JsonConvert.DeserializeObject<T>(json);
            Type type = typeof(T);
            if (jsonObject==null&&type.IsGenericType&& !type.ContainsGenericParameters)
            {
                try
                {
                    Type genericTypeDef = type.GetGenericTypeDefinition();
                    if (genericTypeDef==typeof(List<>)||genericTypeDef==typeof(Stack<>)||genericTypeDef==typeof(Queue<>)||genericTypeDef==typeof(HashSet<>))
                    {
                        Type TypeArgument = type.GetGenericArguments()[0];
                        Type closedType = genericTypeDef.MakeGenericType(TypeArgument);
                        object? listInstance = Activator.CreateInstance(closedType);
                        return (T?)listInstance;
                    }
                    else if (genericTypeDef==typeof(Dictionary<,>))
                    {
                        Type keyTypeArgument = type.GetGenericArguments()[0];
                        Type valueTypeArgument = type.GetGenericArguments()[1];
                        Type closedDictType = typeof(Dictionary<,>).MakeGenericType(keyTypeArgument, valueTypeArgument);
                        object? dictInstance = Activator.CreateInstance(closedDictType);
                        return (T?)dictInstance;
                    }
                    else
                    {
                        object? genericInstance = Activator.CreateInstance(type);
                        return (T?)genericInstance;

                    }
                }
                catch (Exception)
                {
                    throw new Exception("Generic type not supported");
                }

            }
            return jsonObject ?? (T?)(object?)null;
        }
        /// <summary>
        /// This function helps you to add and update data into database.
        /// </summary>
        /// <param name="obj"></param>
        public void Set(T obj)
        {
            string convertedJson = JsonConvert.SerializeObject(obj);
            if (!string.IsNullOrEmpty(_encryptionKey))
            {
                convertedJson=CryptoLoah.Encrypt(convertedJson,_encryptionKey);
            }
            File.WriteAllText(FilePath, convertedJson);
        }
        private string FilePath { get; }
        private string FolderPath { get; }
        private string AppData { get; set; }
    }
    public static class LoahDb
    {

        static string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        /// <summary>
        /// This function informs you about the existence of the database with this key and root.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="root"></param>
        /// <returns></returns>
        public static bool Exists(string key, string root)
        {
            var file = Path.Combine(AppData, root, key+".loah");
            return File.Exists(file);
        }
        /// <summary>
        /// This function deletes the database with this key and the root if it exists.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="root"></param>
        public static void Delete(string key, string root)
        {
            if (Exists(key, root))
            {
                var file = Path.Combine(AppData, root, key+".loah");
                File.Delete(file);
            }
        }
        /// <summary>
        /// This function returns a list of keys from input root
        /// </summary>
        /// <param name="root"></param>
        /// <returns></returns>
        public static List<string> KeyList(string root)
        {
            var directory = Path.Combine(AppData, root);
            var keylist = Directory.GetFiles(directory).ToList();
            for (int i = 0; i < keylist.Count; i++)
            {
                keylist[i]=keylist[i].Replace(directory+"\\", "").Replace(".loah", "");
            }
            return keylist;
        }
    }
}