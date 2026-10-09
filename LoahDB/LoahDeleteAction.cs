namespace LoahDB;

/// <summary>Behavior when a referenced (parent) document is deleted.</summary>
public enum LoahDeleteAction
{
    Restrict,
    Cascade,
    SetNull,
}
