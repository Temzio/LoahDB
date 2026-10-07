namespace LoahDB;

internal interface ILoahReferenceQueryable
{
    string CollectionName { get; }
    LoahCollectionSchema? Schema { get; }
    IReadOnlyList<string> AllDocumentIds();
    bool ExistsFieldValue(string fieldPath, string value);
    bool TryGetFieldValue(string documentId, string fieldPath, out string? value);
    IReadOnlyList<string> FindIdsReferencing(string localField, string parentKey);
    bool TrySetFieldNull(string documentId, string fieldPath);
    bool DeleteById(string documentId);
}
