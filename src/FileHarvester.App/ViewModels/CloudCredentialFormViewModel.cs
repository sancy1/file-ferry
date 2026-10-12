// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/CloudCredentialFormViewModel.cs
// PURPOSE:  The dynamic credential form. Renders one CredentialFieldViewModel
//           per FieldSpec in a ProviderProfile, collects an instance name,
//           validates required fields, format patterns, and instance-name
//           uniqueness on Save, and builds an ephemeral
//           PendingCredentialSubmission.
// RELATION: Constructed by CloudPage.axaml.cs with the selected ProviderProfile
//           and a uniqueness-check delegate. Consumes CredentialFieldViewModel
//           and PendingCredentialSubmission. Holds no Avalonia types. The
//           credential store write happens in the caller, not here.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileHarvester.Core.Domain;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The view model for the dynamic credential form.
/// </summary>
/// <remarks>
/// The form is a pure in-memory transformation: it renders fields from the
/// supplied provider profile, collects an instance name, validates required
/// fields, format patterns, and instance-name uniqueness, and builds an
/// in-memory <see cref="PendingCredentialSubmission"/>. It does not call
/// ICredentialStore, a cloud provider, OAuth, or the network, and it uses no
/// resilience policy. Persistence belongs to the caller.
///
/// Save is routed through <see cref="SaveCommand"/> only. The command awaits
/// <see cref="ValidateAsync"/> and then sets <see cref="LastSaveSucceeded"/>;
/// the dialog code-behind observes that property and owns the modal close
/// decision. No lifecycle events are raised by this type.
///
/// Instance-name uniqueness is checked through an injected delegate. The form
/// does not hold ICredentialStore; the caller supplies the check. A delegate
/// that throws fails the UI thread and terminates the app; the delegate must
/// return false on failure and never throw.
/// </remarks>
public sealed class CloudCredentialFormViewModel : ViewModelBase
{
    private readonly Func<string, CancellationToken, Task<bool>> _checkInstanceNameUniqueAsync;

    private bool _lastSaveSucceeded;
    private string _instanceName = string.Empty;
    private string? _instanceNameError;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudCredentialFormViewModel"/> class
    /// for the supplied profile.
    /// </summary>
    /// <param name="profile">The provider profile whose credential fields are rendered. Must not be null.</param>
    /// <param name="checkInstanceNameUniqueAsync">
    /// The uniqueness check for the instance name. Given a candidate name and
    /// a cancellation token, returns true when the name is free and false when
    /// it is already in use. Must not throw; return false on failure.
    /// </param>
    public CloudCredentialFormViewModel(
        ProviderProfile profile,
        Func<string, CancellationToken, Task<bool>> checkInstanceNameUniqueAsync)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(checkInstanceNameUniqueAsync);

        _checkInstanceNameUniqueAsync = checkInstanceNameUniqueAsync;
        Profile = profile;
        Fields = new ObservableCollection<CredentialFieldViewModel>(
            profile.CredentialFields.Select(spec => new CredentialFieldViewModel(spec)));
        SaveCommand = new AsyncRelayCommand(async () =>
        {
            LastSaveSucceeded = await ValidateAsync().ConfigureAwait(true);
        });
        CancelCommand = new RelayCommand(() =>
        {
            Clear();
        });
    }

    /// <summary>Gets the provider profile the form was built from.</summary>
    public ProviderProfile Profile { get; }

    /// <summary>Gets the form title.</summary>
    public string Title => Profile.DisplayName;

    /// <summary>Gets the ordered list of fields rendered by the form.</summary>
    public ObservableCollection<CredentialFieldViewModel> Fields { get; }

    /// <summary>
    /// Gets the command bound to the Save button. Its execution awaits
    /// <see cref="ValidateAsync"/> and then sets <see cref="LastSaveSucceeded"/>.
    /// </summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// Gets the command bound to the Cancel button. Its execution clears
    /// the form fields and the instance name.
    /// </summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>
    /// Gets or sets the user-chosen instance name. Non-empty and must not
    /// contain a forward slash. Uniqueness is checked by <see cref="ValidateAsync"/>.
    /// </summary>
    public string InstanceName
    {
        get => _instanceName;
        set
        {
            if (SetProperty(ref _instanceName, value))
            {
                InstanceNameError = null;
            }
        }
    }

    /// <summary>
    /// Gets the instance-name validation message. Null or empty when the
    /// instance name is acceptable. Set by <see cref="ValidateAsync"/>.
    /// </summary>
    public string? InstanceNameError
    {
        get => _instanceNameError;
        set
        {
            if (SetProperty(ref _instanceNameError, value))
            {
                OnPropertyChanged(nameof(HasInstanceNameError));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="InstanceNameError"/> is
    /// present and non-empty. Bound to the error text block visibility.
    /// </summary>
    public bool HasInstanceNameError => !string.IsNullOrEmpty(InstanceNameError);

    /// <summary>
    /// Gets a value indicating whether the most recent Save attempt passed
    /// validation. Set by <see cref="SaveCommand"/> after <see cref="ValidateAsync"/>
    /// completes; observed by the dialog code-behind to decide whether to close
    /// with the result.
    /// </summary>
    public bool LastSaveSucceeded
    {
        get => _lastSaveSucceeded;
        private set => SetProperty(ref _lastSaveSucceeded, value);
    }

    /// <summary>
    /// Validates the instance name (non-empty, no forward slash, unique) and
    /// every credential field. Each field is validated by
    /// <see cref="CredentialFieldViewModel.Validate"/>, which covers required
    /// completeness and the optional format pattern. The uniqueness check is
    /// awaited through the injected delegate.
    /// </summary>
    /// <param name="cancellationToken">Cancels the uniqueness check.</param>
    /// <returns>True when every check passes; otherwise false.</returns>
    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var allPass = true;

        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            InstanceNameError = "An instance name is required.";
            allPass = false;
        }
        else if (InstanceName.Contains((char)47))
        {
            InstanceNameError = "The instance name must not contain a forward slash.";
            allPass = false;
        }
        else if (string.Equals(InstanceName, "__client_secret__", StringComparison.Ordinal))
        {
            InstanceNameError = "This name is reserved. Choose a different instance name.";
            allPass = false;
        }
        else
        {
            var unique = await _checkInstanceNameUniqueAsync(
                InstanceName, cancellationToken).ConfigureAwait(true);

            if (!unique)
            {
                InstanceNameError = "An instance with this name already exists for this provider.";
                allPass = false;
            }
            else
            {
                InstanceNameError = null;
            }
        }

        foreach (var field in Fields)
        {
            if (!field.Validate())
            {
                allPass = false;
            }
        }

        return allPass;
    }

    /// <summary>
    /// Builds the ephemeral submission from the current field values. May
    /// contain secret material; the caller must not log, serialize, display,
    /// or throw the result.
    /// </summary>
    /// <returns>A new <see cref="PendingCredentialSubmission"/> for the form profile.</returns>
    public PendingCredentialSubmission BuildResult()
    {
        var values = new Dictionary<string, string>(Fields.Count);
        foreach (var field in Fields)
        {
            values[field.Key] = field.Value;
        }
        return new PendingCredentialSubmission(Profile.Id, InstanceName, values);
    }

    /// <summary>
    /// Clears every field value, the instance name, and every validation
    /// message. Called on Cancel, on window close, and after a successful
    /// result has been consumed by the caller.
    /// </summary>
    public void Clear()
    {
        foreach (var field in Fields)
        {
            field.Clear();
        }

        InstanceName = string.Empty;
        InstanceNameError = null;
    }
}
