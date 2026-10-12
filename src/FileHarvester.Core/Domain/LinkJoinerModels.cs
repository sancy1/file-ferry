namespace FileHarvester.Core.Domain;

public sealed record LinkJoinerSectionInput(
    string Name,
    string BaseLocation,
    string Paths,
    bool NormalizeSeparators = true);

public enum LinkJoinerRowKind
{
    Joined,
    AlreadyPrefixed,
    PassThrough,
    Blank,
    Invalid,
    Duplicate,
}

public sealed record LinkJoinerRow(
    string SectionName,
    int LineNumber,
    string Input,
    string? Result,
    LinkJoinerRowKind Kind,
    string? Message);

public sealed record LinkJoinerResult(IReadOnlyList<LinkJoinerRow> Rows)
{
    public int InputCount => Rows.Count;
    public int ResultCount => Rows.Count(row =>
        row.Kind is LinkJoinerRowKind.Joined
            or LinkJoinerRowKind.AlreadyPrefixed
            or LinkJoinerRowKind.PassThrough);
    public int AttentionCount => Rows.Count(row =>
        row.Kind is LinkJoinerRowKind.Blank
            or LinkJoinerRowKind.Invalid
            or LinkJoinerRowKind.Duplicate);
    public IReadOnlyList<string> Results => Rows
        .Where(row => row.Result is not null)
        .Select(row => row.Result!)
        .ToArray();
}
