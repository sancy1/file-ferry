// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ProviderProfile.cs
// PURPOSE:  The schema for one cloud provider. Names the provider,
//           declares its storage kind and auth shape, lists the
//           credential fields the user must fill in, and carries the
//           LLM help prompt template.
// RELATION: Part of the C.2 dynamic form engine. Built-in profiles
//           live in BuiltInProfiles (C.2). Custom profiles are stored
//           by ProviderProfileStore (C.2). Consumed by the C.3
//           dispatcher to choose the adapter.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The schema for one cloud provider.
/// </summary>
/// <param name="Id">
/// A stable identifier (for example, "aws-s3" or "cloudflare-r2").
/// Used as the credential-store key prefix.
/// </param>
/// <param name="DisplayName">
/// The user-visible name (for example, "AWS S3").
/// </param>
/// <param name="Kind">The storage kind.</param>
/// <param name="AuthKind">The authentication shape.</param>
/// <param name="CredentialFields">
/// The fields the user must fill in. Never null; may be empty for
/// OAuth providers where the flow supplies the credential.
/// </param>
/// <param name="DefaultEndpointUrl">
/// Optional default endpoint. Used by S3-compatible providers where
/// the user must supply an endpoint URL for a non-AWS service.
/// </param>
/// <param name="HelpPromptTemplate">
/// The template for the copyable LLM prompt. Contains the
/// {DisplayName} token, which the prompt generator replaces.
/// </param>
/// <param name="IconKey">
/// Optional key for a provider icon. Null means the UI uses a
/// default icon.
/// </param>
public sealed record ProviderProfile(
    string Id,
    string DisplayName,
    CloudProviderKind Kind,
    AuthKind AuthKind,
    IReadOnlyList<FieldSpec> CredentialFields,
    string? DefaultEndpointUrl,
    string HelpPromptTemplate,
    string? IconKey);
