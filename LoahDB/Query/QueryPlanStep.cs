using System.Linq.Expressions;

namespace LoahDB.Query;

internal abstract record QueryPlanStep
{
    public abstract void WriteExplain(TextWriter writer, int indent);
}

internal sealed record CollectionScanStep : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}CollectionScan");
}

internal sealed record IndexSeekStep(string IndexName, string PropertyPath, object? Value) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}IndexSeek({IndexName} on {PropertyPath} = {Value})");
}

internal sealed record IndexRangeStep(
    string IndexName,
    string PropertyPath,
    object? MinInclusive,
    object? MaxInclusive,
    bool Descending) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}IndexRange({IndexName} on {PropertyPath} [{MinInclusive}..{MaxInclusive}] desc={Descending})");
}

internal sealed record FilterStep(Expression Filter) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Filter({Filter})");
}

internal sealed record SortStep(string PropertyPath, bool Descending) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Sort({PropertyPath} desc={Descending})");
}

internal sealed record SortExpressionStep(Expression KeySelector, bool Descending) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Sort(expr desc={Descending})");
}

internal sealed record SkipStep(int Count) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Skip({Count})");
}

internal sealed record TakeStep(int Count) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Take({Count})");
}

internal sealed record ProjectStep(Expression Selector) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Project({Selector})");
}

internal sealed record GroupByStep(Expression KeySelector) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}GroupBy({KeySelector})");
}

internal sealed record JoinStep(string InnerCollection, string OuterPath, string InnerPath, bool InnerIndexSeek) : QueryPlanStep
{
    public override void WriteExplain(TextWriter writer, int indent) =>
        writer.WriteLine($"{new string(' ', indent)}Join({InnerCollection} on {OuterPath}={InnerPath}, inner={InnerIndexSeek switch { true => "IndexSeek", false => "Scan" }})");
}

internal sealed class QueryPlan
{
    public List<QueryPlanStep> Steps { get; } = new();

    public string Explain()
    {
        using var writer = new StringWriter();
        writer.WriteLine("LoahDB Query Plan:");
        foreach (var step in Steps)
        {
            step.WriteExplain(writer, 2);
        }

        return writer.ToString();
    }
}
