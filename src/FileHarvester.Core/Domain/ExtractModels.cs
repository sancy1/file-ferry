namespace FileHarvester.Core.Domain;

public enum ExtractCategory
{
    LinksAndPaths,
    Emails,
    Phones,
}

public sealed record ExtractedItem(
    ExtractCategory Category,
    string Value,
    int StartIndex,
    int Length);

public sealed class ExtractionResult
{
    public ExtractionResult(
        IReadOnlyList<ExtractedItem> linksAndPaths,
        IReadOnlyList<ExtractedItem> emails,
        IReadOnlyList<ExtractedItem> phones)
    {
        ArgumentNullException.ThrowIfNull(linksAndPaths);
        ArgumentNullException.ThrowIfNull(emails);
        ArgumentNullException.ThrowIfNull(phones);

        LinksAndPaths = linksAndPaths;
        Emails = emails;
        Phones = phones;
        All = LinksAndPaths
            .Concat(Emails)
            .Concat(Phones)
            .OrderBy(item => item.StartIndex)
            .ThenBy(item => item.Category)
            .ToArray();
    }

    public IReadOnlyList<ExtractedItem> LinksAndPaths { get; }
    public IReadOnlyList<ExtractedItem> Emails { get; }
    public IReadOnlyList<ExtractedItem> Phones { get; }
    public IReadOnlyList<ExtractedItem> All { get; }
}
