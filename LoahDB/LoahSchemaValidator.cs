using LoahDB.Engine;
using Newtonsoft.Json.Linq;

namespace LoahDB;

internal static class LoahSchemaValidator
{
    public static void Validate(object document, LoahCollectionSchema schema)
    {
        var token = JObject.FromObject(document);
        foreach (var field in schema.RequiredFields)
        {
            if (!TryGetToken(token, field, out var value) || value.Type == JTokenType.Null)
            {
                throw new LoahSchemaException($"Required field '{field}' is missing or null.");
            }
        }

        foreach (var (field, expectedType) in schema.FieldTypes)
        {
            if (!TryGetToken(token, field, out var value) || value.Type == JTokenType.Null)
            {
                continue;
            }

            if (!MatchesType(value, expectedType))
            {
                throw new LoahSchemaException(
                    $"Field '{field}' has type '{value.Type}' but schema expects '{expectedType}'.");
            }
        }
    }

    public static void ValidateReferences(
        LoahStore store,
        string collectionName,
        object document,
        LoahCollectionSchema schema)
    {
        var token = JObject.FromObject(document);
        foreach (var reference in schema.References)
        {
            if (!TryGetToken(token, reference.LocalField, out var local) || local.Type == JTokenType.Null)
            {
                continue;
            }

            var key = local.ToString();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (!store.TryResolveReference(reference.ReferencedCollection, reference.ReferencedField, key))
            {
                throw new LoahSchemaException(
                    $"Reference violation: '{collectionName}.{reference.LocalField}' = '{key}' was not found in collection '{reference.ReferencedCollection}'.");
            }
        }
    }

    private static bool MatchesType(JToken value, LoahSchemaType expected) => expected switch
    {
        LoahSchemaType.String => value.Type == JTokenType.String,
        LoahSchemaType.Int => value.Type == JTokenType.Integer,
        LoahSchemaType.Long => value.Type == JTokenType.Integer,
        LoahSchemaType.Double => value is JValue { Type: JTokenType.Float or JTokenType.Integer },
        LoahSchemaType.Bool => value.Type == JTokenType.Boolean,
        LoahSchemaType.DateTime => value.Type is JTokenType.Date or JTokenType.String,
        LoahSchemaType.Object => value.Type == JTokenType.Object,
        _ => true,
    };

    private static bool TryGetToken(JObject root, string path, out JToken token)
    {
        token = root.SelectToken(path) ?? root[path];
        return token is not null;
    }
}

/// <summary>Thrown when a document violates the collection schema.</summary>
public sealed class LoahSchemaException : Exception
{
    public LoahSchemaException(string message) : base(message)
    {
    }
}
