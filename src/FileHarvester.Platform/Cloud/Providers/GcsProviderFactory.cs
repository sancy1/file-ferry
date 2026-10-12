// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/Providers/GcsProviderFactory.cs
// PURPOSE:  Constructs a StorageClient from a non-secret settings record and
//           an in-memory service-account JSON credential string retrieved
//           from ICredentialStore by the caller. The factory is static and
//           pure — no I/O, no logging, no resilience, no DI. It exists to
//           make the GCS provider testable without live credentials and to
//           centralize the credential-validation contract.
// RELATION: Called by the profile-to-settings adapter in C.8. Consumed by
//           GcsProviderFactoryTests. Not called by GcsObjectProvider itself —
//           the provider receives an already-constructed IGcsClient.
// ============================================================================

using System.Globalization;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;

namespace FileHarvester.Platform.Cloud.Providers;

/// <summary>
/// Builds Google Cloud Storage clients from configuration and credentials.
/// </summary>
/// <remarks>
/// <para>
/// The service-account JSON is validated for shape before it is handed to
/// the Google SDK. This is the SDK's own recommendation: the XML doc for
/// GoogleCredential.FromJson states that externally-sourced credential
/// configurations must be validated before being provided to any Google
/// API. The validation checks for the required fields and the
/// service-account type, and throws without echoing any field value.
/// </para>
/// <para>
/// The raw JSON string is not retained by the factory. A static class has
/// no instance state; the method returns only the StorageClient. No field
/// on the returned client or on any object the factory creates holds the
/// JSON. See GcsProviderFactoryTests for the behavioral assertion.
/// </para>
/// </remarks>
public static class GcsProviderFactory
{
    /// <summary>
    /// Constructs a <see cref="StorageClient"/> for the given settings and
    /// service-account JSON credential.
    /// </summary>
    /// <param name="settings">
    /// Non-secret connection settings. Must not be null. ProjectId must
    /// not be null, empty, or whitespace-only.
    /// </param>
    /// <param name="serviceAccountJson">
    /// The service-account credential as a JSON string. Must not be null,
    /// empty, or whitespace-only. Must be syntactically valid JSON, must
    /// declare type as service_account, and must contain client_email,
    /// private_key, and project_id.
    /// </param>
    /// <returns>A new <see cref="StorageClient"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// settings or serviceAccountJson is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// serviceAccountJson is empty or whitespace-only; is not valid JSON;
    /// is not a service-account credential; or is missing a required field.
    /// The message names the offending field but never contains a value.
    /// </exception>
    public static StorageClient CreateClient(
        GcsConnectionSettings settings,
        string serviceAccountJson)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(serviceAccountJson);

        if (string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            throw new ArgumentException(
                "Service-account JSON must not be null, empty, or whitespace-only.",
                nameof(serviceAccountJson));
        }

        if (string.IsNullOrWhiteSpace(settings.ProjectId))
        {
            throw new ArgumentException(
                "ProjectId must not be null, empty, or whitespace-only.",
                nameof(settings));
        }

        ValidateServiceAccountJson(serviceAccountJson);

        // GoogleCredential.FromJson parses the JSON in memory. The string
        // is not written to disk and is not retained after this method
        // returns. The credential object itself is passed to
        // StorageClientBuilder, which uses it to sign requests.
        var credential = CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson).ToGoogleCredential();

        var builder = new StorageClientBuilder
        {
            Credential = credential,
        };

        if (!string.IsNullOrWhiteSpace(settings.CustomEndpointHost))
        {
            builder.BaseUri = "https://" + settings.CustomEndpointHost!.Trim() + "/storage/v1/";
        }

        return builder.Build();
    }

    // ------------------------------------------------------------------
    // Credential validation — shape check only, no value echo.
    // ------------------------------------------------------------------

    /// <summary>
    /// Validates that the JSON declares a service-account credential and
    /// carries the three required fields. Throws ArgumentException on any
    /// failure. Never includes a field value in a message.
    /// </summary>
    private static void ValidateServiceAccountJson(string serviceAccountJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(serviceAccountJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException(
                "Service-account JSON is not valid JSON.",
                nameof(serviceAccountJson));
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Service-account JSON must be a JSON object.",
                    nameof(serviceAccountJson));
            }

            if (!root.TryGetProperty("type", out var typeProp)
                || typeProp.ValueKind != JsonValueKind.String
                || !string.Equals(typeProp.GetString(), "service_account", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Credential is not a service-account credential (type field is missing or not service_account).",
                    nameof(serviceAccountJson));
            }

            RequireStringProperty(root, "client_email", serviceAccountJson);
            RequireStringProperty(root, "private_key", serviceAccountJson);
            RequireStringProperty(root, "project_id", serviceAccountJson);
        }
    }

    private static void RequireStringProperty(
        JsonElement root,
        string propertyName,
        string serviceAccountJson)
    {
        if (!root.TryGetProperty(propertyName, out var prop)
            || prop.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(prop.GetString()))
        {
            // The message names the field but never its value. The json
            // string itself is not included in the exception.
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Service-account JSON is missing required field: {0}.",
                    propertyName),
                nameof(serviceAccountJson));
        }
    }
}
