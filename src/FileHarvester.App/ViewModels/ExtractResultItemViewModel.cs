using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class ExtractResultItemViewModel
{
    public ExtractResultItemViewModel(
        ExtractedItem item,
        Action<string> copy,
        Func<bool> canCopy)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(copy);
        ArgumentNullException.ThrowIfNull(canCopy);

        Category = item.Category;
        Value = item.Value;
        StartIndex = item.StartIndex;
        CopyCommand = new RelayCommand(() => copy(Value), canCopy);
    }

    public ExtractCategory Category { get; }
    public string Value { get; }
    public int StartIndex { get; }
    public RelayCommand CopyCommand { get; }

    public string CategoryLabel => Category switch
    {
        ExtractCategory.LinksAndPaths => "Link / path",
        ExtractCategory.Emails => "Email-like",
        ExtractCategory.Phones => "Phone-like",
        _ => "Match",
    };
}
