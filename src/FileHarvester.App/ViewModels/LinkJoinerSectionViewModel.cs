using System.Windows.Input;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class LinkJoinerSectionViewModel : ViewModelBase
{
    private readonly Action<LinkJoinerSectionViewModel> _changed;
    private string _name;
    private string _baseLocation = string.Empty;
    private string _paths = string.Empty;
    private bool _normalizeSeparators = true;

    internal LinkJoinerSectionViewModel(
        int number,
        Action<LinkJoinerSectionViewModel> changed,
        Action<LinkJoinerSectionViewModel> remove,
        Action<LinkJoinerSectionViewModel> browseFolder,
        Action<LinkJoinerSectionViewModel> browseFiles)
    {
        _name = $"Section {number}";
        _changed = changed;
        RemoveCommand = new RelayCommand(() => remove(this));
        BrowseBaseCommand = new RelayCommand(() => browseFolder(this));
        AddFilesCommand = new RelayCommand(() => browseFiles(this));
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                _changed(this);
            }
        }
    }

    public string BaseLocation
    {
        get => _baseLocation;
        set
        {
            if (SetProperty(ref _baseLocation, value))
            {
                _changed(this);
            }
        }
    }

    public string Paths
    {
        get => _paths;
        set
        {
            if (SetProperty(ref _paths, value))
            {
                _changed(this);
            }
        }
    }

    public bool NormalizeSeparators
    {
        get => _normalizeSeparators;
        set
        {
            if (SetProperty(ref _normalizeSeparators, value))
            {
                _changed(this);
            }
        }
    }

    public ICommand RemoveCommand { get; }
    public ICommand BrowseBaseCommand { get; }
    public ICommand AddFilesCommand { get; }

    internal LinkJoinerSectionInput ToInput() =>
        new(Name, BaseLocation, Paths, NormalizeSeparators);

    public void AppendAbsolutePaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var additions = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (additions.Length == 0)
        {
            return;
        }

        Paths = string.IsNullOrWhiteSpace(Paths)
            ? string.Join(Environment.NewLine, additions)
            : Paths.TrimEnd('\r', '\n') + Environment.NewLine + string.Join(Environment.NewLine, additions);
    }
}
