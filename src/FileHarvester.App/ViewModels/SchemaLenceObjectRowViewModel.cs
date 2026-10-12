using System.Windows.Input;
using SchemaLence.Core.Metadata;

namespace FileHarvester.App.ViewModels;

public sealed class SchemaLenceObjectRowViewModel
{
    public SchemaLenceObjectRowViewModel(
        DatabaseObject databaseObject,
        Func<DatabaseObject, Task> select)
    {
        DatabaseObject = databaseObject ?? throw new ArgumentNullException(nameof(databaseObject));
        ArgumentNullException.ThrowIfNull(select);
        SelectCommand = new AsyncRelayCommand(() => select(DatabaseObject));
    }

    public DatabaseObject DatabaseObject { get; }
    public string QualifiedName => DatabaseObject.Namespace + "." + DatabaseObject.Name;
    public string KindLabel => DatabaseObject.Kind.ToString();
    public string RowEstimate => DatabaseObject.EstimatedRows is { } rows && rows >= 0
        ? $"~{rows:N0}"
        : "Estimate unavailable";
    public string SizeLabel => DatabaseObject.SizeBytes is { } size
        ? FormatSize(size)
        : "Size unavailable";
    public ICommand SelectCommand { get; }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }
}
