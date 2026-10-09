using System.Text;

namespace LoahDB.Engine;

internal sealed class PageIntegrityChecker
{
    private readonly LoahPageDatabase _database;

    public PageIntegrityChecker(LoahPageDatabase database) => _database = database;

    public LoahIntegrityReport Check()
    {
        var report = new LoahIntegrityReport();
        try
        {
            _database.EnsureReadable();
            report.PagesChecked = (int)_database.PageCount;
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.Message);
        }

        foreach (var (collection, docId, _) in _database.EnumerateAllCollectionDocuments())
        {
            report.DocumentsChecked++;
            if (string.IsNullOrEmpty(docId))
            {
                report.Errors.Add($"Collection '{collection}' contains a document with an empty id.");
            }
        }

        return report;
    }
}
