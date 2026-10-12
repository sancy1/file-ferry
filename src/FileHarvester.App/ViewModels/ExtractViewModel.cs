using System.Collections.ObjectModel;
using System.Windows.Input;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

public enum ExtractResultFilter
{
    All,
    LinksAndPaths,
    Emails,
    Phones,
}

public sealed class ExtractViewModel : ViewModelBase
{
    private string _inputText = string.Empty;
    private string _statusText = "Paste or type text, then choose an extraction action.";
    private ExtractionResult _result = new(
        Array.Empty<ExtractedItem>(),
        Array.Empty<ExtractedItem>(),
        Array.Empty<ExtractedItem>());
    private ExtractResultFilter _activeFilter;
    private bool _includeCategoryHeadings = true;
    private string _resultInputText = string.Empty;

    public ExtractViewModel()
    {
        VisibleItems = new ObservableCollection<ExtractResultItemViewModel>();
        ExtractAllCommand = new RelayCommand(() => Extract(ExtractResultFilter.All));
        ExtractLinksCommand = new RelayCommand(() => Extract(ExtractResultFilter.LinksAndPaths));
        ExtractEmailsCommand = new RelayCommand(() => Extract(ExtractResultFilter.Emails));
        ExtractPhonesCommand = new RelayCommand(() => Extract(ExtractResultFilter.Phones));
        ShowAllCommand = new RelayCommand(() => SelectFilter(ExtractResultFilter.All));
        ShowLinksCommand = new RelayCommand(() => SelectFilter(ExtractResultFilter.LinksAndPaths));
        ShowEmailsCommand = new RelayCommand(() => SelectFilter(ExtractResultFilter.Emails));
        ShowPhonesCommand = new RelayCommand(() => SelectFilter(ExtractResultFilter.Phones));
        PasteCommand = new RelayCommand(() => PasteRequested?.Invoke(this, EventArgs.Empty));
        ClearInputCommand = new RelayCommand(ClearInput);
        ClearResultsCommand = new RelayCommand(ClearResults);
        CopyActiveCategoryCommand = new RelayCommand(
            () => CopyRequested?.Invoke(this, GetActiveText()),
            () => _activeFilter != ExtractResultFilter.All
                && ActiveItems().Any()
                && _resultInputText == InputText);
        CopyAllCommand = new RelayCommand(
            () => CopyRequested?.Invoke(this, FormatItems(_result.All)),
            () => _result.All.Count > 0 && _resultInputText == InputText);
        SaveResultsCommand = new RelayCommand(
            () => SaveRequested?.Invoke(this, BuildSaveText()),
            () => _result.All.Count > 0 && _resultInputText == InputText);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? PasteRequested;
    public event EventHandler<string>? CopyRequested;
    public event EventHandler<string>? SaveRequested;
    public event EventHandler? CloseRequested;

    public ObservableCollection<ExtractResultItemViewModel> VisibleItems { get; }

    public ICommand ExtractAllCommand { get; }
    public ICommand ExtractLinksCommand { get; }
    public ICommand ExtractEmailsCommand { get; }
    public ICommand ExtractPhonesCommand { get; }
    public ICommand ShowAllCommand { get; }
    public ICommand ShowLinksCommand { get; }
    public ICommand ShowEmailsCommand { get; }
    public ICommand ShowPhonesCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand ClearInputCommand { get; }
    public ICommand ClearResultsCommand { get; }
    public ICommand CopyActiveCategoryCommand { get; }
    public ICommand CopyAllCommand { get; }
    public ICommand SaveResultsCommand { get; }
    public ICommand CloseCommand { get; }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (SetProperty(ref _inputText, value))
            {
                OnPropertyChanged(nameof(CharacterCount));
                OnPropertyChanged(nameof(LineCount));
                OnPropertyChanged(nameof(ResultsAreCurrent));
                ((RelayCommand)CopyAllCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CopyActiveCategoryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)SaveResultsCommand).RaiseCanExecuteChanged();
                foreach (var item in VisibleItems)
                {
                    item.CopyCommand.RaiseCanExecuteChanged();
                }
                if (_result.All.Count > 0 && _resultInputText != value)
                {
                    StatusText = "Input changed since extraction; extract again to refresh these results.";
                }
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public ExtractResultFilter ActiveFilter
    {
        get => _activeFilter;
        private set
        {
            if (SetProperty(ref _activeFilter, value))
            {
                OnPropertyChanged(nameof(IsAllFilterActive));
                OnPropertyChanged(nameof(IsLinksFilterActive));
                OnPropertyChanged(nameof(IsEmailsFilterActive));
                OnPropertyChanged(nameof(IsPhonesFilterActive));
                UpdateVisibleItems();
                ((RelayCommand)CopyActiveCategoryCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IncludeCategoryHeadings
    {
        get => _includeCategoryHeadings;
        set => SetProperty(ref _includeCategoryHeadings, value);
    }

    public int CharacterCount => InputText.Length;
    public int LineCount => CountLines(InputText);

    public int AllCount => _result.All.Count;
    public int LinksAndPathsCount => _result.LinksAndPaths.Count;
    public int EmailsCount => _result.Emails.Count;
    public int PhonesCount => _result.Phones.Count;
    public bool HasResults => VisibleItems.Count > 0;
    public bool HasNoMatches => VisibleItems.Count == 0;
    public bool ResultsAreCurrent => _result.All.Count == 0 || _resultInputText == InputText;
    public string NoMatchesText => _result.All.Count == 0
        ? "No matches found. Try another input or extraction category."
        : "No matches in this category. Choose another filter.";

    public bool IsAllFilterActive => ActiveFilter == ExtractResultFilter.All;
    public bool IsLinksFilterActive => ActiveFilter == ExtractResultFilter.LinksAndPaths;
    public bool IsEmailsFilterActive => ActiveFilter == ExtractResultFilter.Emails;
    public bool IsPhonesFilterActive => ActiveFilter == ExtractResultFilter.Phones;

    public void SelectFilter(ExtractResultFilter filter) => ActiveFilter = filter;

    public void SetInputText(string text)
    {
        InputText = text;
        StatusText = _result.All.Count > 0 && !ResultsAreCurrent
            ? "Clipboard text pasted. Existing results are out of date; extract again before copying or saving."
            : "Clipboard text pasted. Choose an extraction action when ready.";
    }

    public void SetStatus(string message) => StatusText = message;

    public void CopySelected(IEnumerable<ExtractResultItemViewModel> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        if (!ResultsAreCurrent)
        {
            StatusText = "Input changed since extraction; extract again before copying results.";
            return;
        }
        var text = FormatItems(selectedItems.Select(item =>
            new ExtractedItem(item.Category, item.Value, item.StartIndex, item.Value.Length)));
        if (text.Length == 0)
        {
            StatusText = "Select one or more matches first.";
            return;
        }
        CopyRequested?.Invoke(this, text);
    }

    public string BuildSaveText()
    {
        if (!IncludeCategoryHeadings)
        {
            return FormatItems(_result.All);
        }

        var groups = new[]
        {
            (Title: $"Links & Paths ({LinksAndPathsCount})", Items: _result.LinksAndPaths),
            (Title: $"Emails ({EmailsCount})", Items: _result.Emails),
            (Title: $"Phones ({PhonesCount})", Items: _result.Phones),
        };

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            groups.Select(group => group.Title + Environment.NewLine
                + (group.Items.Count == 0
                    ? "(No matches)"
                    : FormatItems(group.Items))));
    }

    private void Extract(ExtractResultFilter filter)
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            _result = EmptyResult();
            _resultInputText = InputText;
            ActiveFilter = filter;
            UpdateVisibleItems();
            UpdateCounts();
            StatusText = "Paste or type text before extracting.";
            return;
        }

        _result = Extractor.Extract(InputText);
        _resultInputText = InputText;
        ActiveFilter = filter;
        UpdateVisibleItems();
        UpdateCounts();
        StatusText = _result.All.Count == 0
            ? "No matches found."
            : $"Matches found: {AllCount} | Links & Paths: {LinksAndPathsCount}"
                + $" | Emails: {EmailsCount} | Phones: {PhonesCount}";
    }

    private void ClearInput()
    {
        InputText = string.Empty;
        StatusText = "Input cleared. Existing results are unchanged and no longer match the input.";
    }

    private void ClearResults()
    {
        _result = EmptyResult();
        _resultInputText = string.Empty;
        ActiveFilter = ExtractResultFilter.All;
        UpdateVisibleItems();
        UpdateCounts();
        StatusText = "Results cleared.";
    }

    private IEnumerable<ExtractedItem> ActiveItems() =>
        ActiveFilter switch
        {
            ExtractResultFilter.LinksAndPaths => _result.LinksAndPaths,
            ExtractResultFilter.Emails => _result.Emails,
            ExtractResultFilter.Phones => _result.Phones,
            _ => _result.All,
        };

    private string GetActiveText() => FormatItems(ActiveItems());

    private void UpdateVisibleItems()
    {
        VisibleItems.Clear();
        foreach (var item in ActiveItems())
        {
            VisibleItems.Add(new ExtractResultItemViewModel(
                item,
                value =>
                {
                    if (ResultsAreCurrent)
                    {
                        CopyRequested?.Invoke(this, value);
                    }
                    else
                    {
                        StatusText = "Input changed since extraction; extract again before copying results.";
                    }
                },
                () => ResultsAreCurrent));
        }
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(LinksAndPathsCount));
        OnPropertyChanged(nameof(EmailsCount));
        OnPropertyChanged(nameof(PhonesCount));
        OnPropertyChanged(nameof(ResultsAreCurrent));
        ((RelayCommand)CopyAllCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CopyActiveCategoryCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SaveResultsCommand).RaiseCanExecuteChanged();
    }

    private static string FormatItems(IEnumerable<ExtractedItem> items) =>
        string.Join(Environment.NewLine, items.Select(item => item.Value));

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
            {
                lines++;
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (text[index] == '\n')
            {
                lines++;
            }
        }
        return lines;
    }

    private static ExtractionResult EmptyResult() => new(
        Array.Empty<ExtractedItem>(),
        Array.Empty<ExtractedItem>(),
        Array.Empty<ExtractedItem>());
}
