namespace LoahDB;

/// <summary>Result of <see cref="LoahStore.CheckIntegrity"/>.</summary>
public sealed class LoahIntegrityReport
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public int PagesChecked { get; set; }
    public int DocumentsChecked { get; set; }
}
