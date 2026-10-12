using System.Collections.ObjectModel;
using System.Windows.Input;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public sealed class LinkJoinerViewModel : ViewModelBase
{
    private int _nextSectionNumber = 1;
    private bool _removeDuplicates;
    private string _resultsText = string.Empty;
    private string _rowDetails = string.Empty;
    private string _statusText = "Add a base location and one or more relative paths, then join.";
    private LinkJoinerResult _lastResult = new(Array.Empty<LinkJoinerRow>());

    public LinkJoinerViewModel()
    {
        Sections = new ObservableCollection<LinkJoinerSectionViewModel>();
        AddSectionCommand = new RelayCommand(AddSection);
        JoinAllCommand = new RelayCommand(JoinAll);
        ClearCommand = new RelayCommand(Clear);
        CloseCommand = new RelayCommand(Close);
        CopyAllCommand = new RelayCommand(
            () => CopyRequested?.Invoke(this, ResultsText),
            () => !string.IsNullOrWhiteSpace(ResultsText));
        SaveResultsCommand = new RelayCommand(
            () => SaveRequested?.Invoke(this, ResultsText),
            () => !string.IsNullOrWhiteSpace(ResultsText));
        SendToHarvestCommand = new RelayCommand(
            () => SendToHarvestRequested?.Invoke(this, ResultsText),
            () => _lastResult.ResultCount > 0);
        AddSection();
        JoinAll();
    }

    public event EventHandler<LinkJoinerSectionViewModel>? BrowseBaseRequested;
    public event EventHandler<LinkJoinerSectionViewModel>? AddFilesRequested;
    public event EventHandler<string>? SendToHarvestRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler<string>? SaveRequested;
    public event EventHandler? CloseRequested;

    public ObservableCollection<LinkJoinerSectionViewModel> Sections { get; }
    public ICommand AddSectionCommand { get; }
    public ICommand JoinAllCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand CopyAllCommand { get; }
    public ICommand SaveResultsCommand { get; }
    public ICommand SendToHarvestCommand { get; }

    public bool RemoveDuplicates
    {
        get => _removeDuplicates;
        set
        {
            if (SetProperty(ref _removeDuplicates, value))
            {
                JoinAll();
            }
        }
    }

    public string ResultsText
    {
        get => _resultsText;
        private set
        {
            if (SetProperty(ref _resultsText, value))
            {
                OnPropertyChanged(nameof(HasResults));
                ((RelayCommand)SendToHarvestCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CopyAllCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveResultsCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string RowDetails
    {
        get => _rowDetails;
        private set => SetProperty(ref _rowDetails, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int InputCount => _lastResult.InputCount;
    public int ResultCount => _lastResult.ResultCount;
    public int AttentionCount => _lastResult.AttentionCount;
    public bool HasResults => !string.IsNullOrEmpty(ResultsText);

    public void JoinAll()
    {
        _lastResult = LinkJoiner.Join(
            Sections.Select(section => section.ToInput()),
            RemoveDuplicates);

        ResultsText = string.Join(Environment.NewLine, _lastResult.Results);
        RowDetails = string.Join(
            Environment.NewLine,
            _lastResult.Rows
                .Where(row => row.Kind is not LinkJoinerRowKind.Joined)
                .Select(row => $"{row.SectionName}, line {row.LineNumber} [{row.Kind}]: "
                    + (row.Message ?? row.Result ?? row.Input)));

        OnPropertyChanged(nameof(InputCount));
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(AttentionCount));
        OnPropertyChanged(nameof(HasResults));

        StatusText = $"Input rows: {InputCount} | Results: {ResultCount} | Needs attention: {AttentionCount}";
        ((RelayCommand)SendToHarvestCommand).RaiseCanExecuteChanged();
    }

    public void SetStatus(string message) => StatusText = message;

    public void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void AddSection()
    {
        var section = CreateSection(_nextSectionNumber++);
        Sections.Add(section);
        section.Name = $"Section {Sections.Count}";
        JoinAll();
    }

    private void RemoveSection(LinkJoinerSectionViewModel section)
    {
        if (Sections.Count <= 1)
        {
            SetStatus("At least one section must remain.");
            return;
        }
        if (Sections.Remove(section))
        {
            for (var index = 0; index < Sections.Count; index++)
            {
                Sections[index].Name = $"Section {index + 1}";
            }
            JoinAll();
        }
    }

    private void Clear()
    {
        foreach (var section in Sections)
        {
            section.BaseLocation = string.Empty;
            section.Paths = string.Empty;
        }
        RemoveDuplicates = false;
        JoinAll();
        SetStatus("Cleared.");
    }

    private LinkJoinerSectionViewModel CreateSection(int number) =>
        new(
            number,
            _ => JoinAll(),
            RemoveSection,
            section => BrowseBaseRequested?.Invoke(this, section),
            section => AddFilesRequested?.Invoke(this, section));
}
