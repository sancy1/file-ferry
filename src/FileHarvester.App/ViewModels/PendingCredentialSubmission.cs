// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/PendingCredentialSubmission.cs
// PURPOSE:  The ephemeral, secret-bearing result produced by the dynamic
//           credential form. Carries the profile id, the instance name,
//           and the entered field values. Discarded by the immediate caller
//           after the credential store write in Step 5.
// RELATION: Constructed by CloudCredentialFormViewModel.BuildResult.
//           Consumed by CloudPage.axaml.cs, which writes the credential
//           through ICredentialStore and then drops the record.
// ============================================================================

using System.Collections.Generic;

namespace FileHarvester.App.ViewModels;

/// <summary>
/// An ephemeral, secret-bearing submission produced by the cloud credential
/// form. Carries the profile identifier, the user-chosen instance name, and
/// the values entered for the profile's credential fields, including any
/// password or secret value.
/// </summary>
/// <remarks>
/// This type is ephemeral. It exists only in memory during the modal result
/// handoff from the credential dialog to its immediate caller. It is never
/// logged, serialized, placed in an exception, or included in any UI text.
/// The caller passes the values to <c>ICredentialStore.WriteAsync</c> under
/// the durable-write policy and then drops the reference.
///
/// A .NET <see cref="string"/> cannot be securely wiped in place. "Clearing"
/// here means dropping references promptly; this type does not claim
/// cryptographic memory erasure.
///
/// The <see cref="Values"/> dictionary may contain secret material. Callers
/// must not log it, serialize it, display it, or throw it.
/// </remarks>
/// <param name="ProfileId">The identifier of the <c>ProviderProfile</c> the values belong to.</param>
/// <param name="InstanceName">The user-chosen instance name. Must be non-empty and must not contain a forward slash.</param>
/// <param name="Values">
/// The entered field values, keyed by each <c>FieldSpec.Key</c>. May contain
/// secret material; handle in memory only.
/// </param>
public sealed record PendingCredentialSubmission(
    string ProfileId,
    string InstanceName,
    IReadOnlyDictionary<string, string> Values);
