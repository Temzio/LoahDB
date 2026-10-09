using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace LoahDB.Engine;

internal static class LegacyV1Importer
{
    public static void Import(
        LoahPageStore target,
        string legacyCollectionsDirectory,
        LoahOptions options)
    {
        if (!Directory.Exists(legacyCollectionsDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(legacyCollectionsDirectory, "*.loah"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var json = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(options.EncryptionKey))
            {
                json = LoahAuthenticatedCrypto.DecryptString(json, options.EncryptionKey, options.KeyDerivationIterations);
            }

            var root = JObject.Parse(json);
            var entry = target.GetOrCreateCatalogEntry(name);
            if (root["IndexDefinitions"] is JArray indexArray)
            {
                entry.IndexDefinitions = indexArray.ToObject<List<LoahIndexDefinition>>(JsonSerializer.Create(options.SerializerSettings))
                    ?? new List<LoahIndexDefinition>();
            }

            var tree = target.Database.OpenTree(entry.RootPageId);
            if (root["Documents"] is JArray documents)
            {
                foreach (var token in documents)
                {
                    var id = token["Id"]?.ToString();
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    var payload = Encoding.UTF8.GetBytes(token.ToString(Formatting.None));
                    tree.Insert(id, payload);
                }
            }

            entry.RootPageId = tree.RootPageId;
            target.SaveCatalogEntry(name, entry);
        }
    }
}
