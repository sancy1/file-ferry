// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/BuiltInProfiles.cs
// PURPOSE:  The eleven provider profiles shipped with File-Ferry. Each
//           profile declares its storage kind, auth shape, credential
//           fields, and the copyable LLM help-prompt template.
// RELATION: Consumed by the C.2 form engine, the C.2 prompt generator,
//           the C.3 provider dispatcher, and the C.8 cloud UI.
//           Custom profiles are added by the user and stored by
//           ProviderProfileStore in FileHarvester.Platform.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// The provider profiles that ship with the application.
/// </summary>
/// <remarks>
/// <para>
/// All profiles are static, immutable, and constructed once at type load.
/// The list is the starter set; users may add custom profiles, and
/// custom profiles are stored separately.
/// </para>
/// <para>
/// The help-prompt templates use the literal token <c>[DisplayName]</c>.
/// The generator replaces it with the profile's DisplayName. Square
/// brackets are used instead of curly braces to avoid any confusion with
/// .NET format strings or with the codebase's placeholder conventions.
/// </para>
/// </remarks>
public static class BuiltInProfiles
{
    private static readonly ProviderProfile[] Profiles = new[]
    {
        BuildAwsS3(),
        BuildAzureBlob(),
        BuildGoogleCloudStorage(),
        BuildCloudflareR2(),
        BuildBackblazeB2(),
        BuildWasabi(),
        BuildDigitalOceanSpaces(),
        BuildMinio(),
        BuildGoogleDrive(),
        BuildOneDrive(),
        BuildDropbox(),
    };

    /// <summary>Every built-in profile, in stable order.</summary>
    public static IReadOnlyList<ProviderProfile> All => Profiles;

    /// <summary>
    /// Finds a built-in profile by its ID, case-insensitively.
    /// </summary>
    /// <param name="id">The profile ID (for example, <c>aws-s3</c>).</param>
    /// <returns>The profile, or null when no built-in profile matches.</returns>
    public static ProviderProfile? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (var p in Profiles)
        {
            if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // AWS S3
    // ------------------------------------------------------------------

    private static ProviderProfile BuildAwsS3() => new(
        Id: "aws-s3",
        DisplayName: "AWS S3",
        Kind: CloudProviderKind.S3Compatible,
        AuthKind: AuthKind.LongLivedKey,
        CredentialFields: new[]
        {
            new FieldSpec(
                Key: "accessKeyId",
                Label: "Access Key ID",
                Kind: FieldKind.Text,
                Required: true,
                Placeholder: "AKIA...",
                HelpText: "From the IAM console. Starts with AKIA.",
                ValidatePattern: "^AKIA[0-9A-Z]{16}$"),
            new FieldSpec(
                Key: "secretAccessKey",
                Label: "Secret Access Key",
                Kind: FieldKind.Password,
                Required: true,
                Placeholder: null,
                HelpText: "Shown only once when the access key is created.",
                ValidatePattern: null),
            new FieldSpec(
                Key: "region",
                Label: "Region",
                Kind: FieldKind.Text,
                Required: false,
                Placeholder: "us-east-1",
                HelpText: "Optional. Leave empty to auto-detect.",
                ValidatePattern: null),
            new FieldSpec(
                Key: "sessionToken",
                Label: "Session Token",
                Kind: FieldKind.Password,
                Required: false,
                Placeholder: null,
                HelpText: "Optional. Only needed for temporary credentials.",
                ValidatePattern: null),
        },
        DefaultEndpointUrl: null,
        HelpPromptTemplate: AwsPrompt,
        IconKey: "aws");

    // ------------------------------------------------------------------
    // Azure Blob
    // ------------------------------------------------------------------

    private static ProviderProfile BuildAzureBlob() => new(
        Id: "azure-blob",
        DisplayName: "Azure Blob Storage",
        Kind: CloudProviderKind.AzureBlob,
        AuthKind: AuthKind.ConnectionString,
        CredentialFields: new[]
        {
            new FieldSpec(
                Key: "connectionString",
                Label: "Connection String",
                Kind: FieldKind.Multiline,
                Required: true,
                Placeholder: "DefaultEndpointsProtocol=https;AccountName=...",
                HelpText: "From the Azure portal: Storage account, Access keys, Connection string.",
                ValidatePattern: null),
        },
        DefaultEndpointUrl: null,
        HelpPromptTemplate: AzurePrompt,
        IconKey: "azure");

    // ------------------------------------------------------------------
    // Google Cloud Storage
    // ------------------------------------------------------------------

    private static ProviderProfile BuildGoogleCloudStorage() => new(
        Id: "gcs",
        DisplayName: "Google Cloud Storage",
        Kind: CloudProviderKind.GcsObject,
        AuthKind: AuthKind.LongLivedKey,
        CredentialFields: new[]
        {
            new FieldSpec(
                Key: "serviceAccountJson",
                Label: "Service Account JSON",
                Kind: FieldKind.File,
                Required: true,
                Placeholder: null,
                HelpText: "The JSON key file downloaded from the GCP console.",
                ValidatePattern: null),
        },
        DefaultEndpointUrl: null,
        HelpPromptTemplate: GcsPrompt,
        IconKey: "gcp");

    // ------------------------------------------------------------------
    // S3-compatible providers
    // ------------------------------------------------------------------

    private static ProviderProfile BuildCloudflareR2() => S3CompatibleProfile(
        id: "cloudflare-r2",
        displayName: "Cloudflare R2",
        endpointUrl: "https://<account-id>.r2.cloudflarestorage.com",
        iconKey: "r2",
        prompt: R2Prompt);

    private static ProviderProfile BuildBackblazeB2() => S3CompatibleProfile(
        id: "backblaze-b2",
        displayName: "Backblaze B2",
        endpointUrl: "https://s3.<region>.backblazeb2.com",
        iconKey: "b2",
        prompt: B2Prompt);

    private static ProviderProfile BuildWasabi() => S3CompatibleProfile(
        id: "wasabi",
        displayName: "Wasabi",
        endpointUrl: "https://s3.<region>.wasabisys.com",
        iconKey: "wasabi",
        prompt: WasabiPrompt);

    private static ProviderProfile BuildDigitalOceanSpaces() => S3CompatibleProfile(
        id: "digitalocean-spaces",
        displayName: "DigitalOcean Spaces",
        endpointUrl: "https://<region>.digitaloceanspaces.com",
        iconKey: "spaces",
        prompt: SpacesPrompt);

    private static ProviderProfile BuildMinio() => S3CompatibleProfile(
        id: "minio",
        displayName: "MinIO",
        endpointUrl: "http://localhost:9000",
        iconKey: "minio",
        prompt: MinioPrompt);

    private static ProviderProfile S3CompatibleProfile(
        string id,
        string displayName,
        string endpointUrl,
        string iconKey,
        string prompt) => new(
        Id: id,
        DisplayName: displayName,
        Kind: CloudProviderKind.S3Compatible,
        AuthKind: AuthKind.LongLivedKey,
        CredentialFields: new[]
        {
            new FieldSpec(
                Key: "accessKeyId",
                Label: "Access Key ID",
                Kind: FieldKind.Text,
                Required: true,
                Placeholder: null,
                HelpText: "From the provider console.",
                ValidatePattern: null),
            new FieldSpec(
                Key: "secretAccessKey",
                Label: "Secret Access Key",
                Kind: FieldKind.Password,
                Required: true,
                Placeholder: null,
                HelpText: null,
                ValidatePattern: null),
            new FieldSpec(
                Key: "endpointUrl",
                Label: "Endpoint URL",
                Kind: FieldKind.Text,
                Required: true,
                Placeholder: endpointUrl,
                HelpText: "The S3-compatible endpoint for this provider.",
                ValidatePattern: null),
            new FieldSpec(
                Key: "bucket",
                Label: "Bucket",
                Kind: FieldKind.Text,
                Required: true,
                Placeholder: null,
                HelpText: "The bucket these credentials apply to.",
                ValidatePattern: null),
        },
        DefaultEndpointUrl: endpointUrl,
        HelpPromptTemplate: prompt,
        IconKey: iconKey);

    // ------------------------------------------------------------------
    // OAuth providers
    // ------------------------------------------------------------------

    private static ProviderProfile BuildGoogleDrive() => new(
        Id: "gdrive",
        DisplayName: "Google Drive",
        Kind: CloudProviderKind.GoogleDrive,
        AuthKind: AuthKind.OAuth,
        CredentialFields: new[]
        {
            new FieldSpec(
                Key: "clientSecret",
                Label: "Client Secret",
                Kind: FieldKind.Password,
                Required: true,
                Placeholder: null,
                HelpText: "From the Google Cloud console OAuth client. Stored securely on this PC only.",
                ValidatePattern: null),
        },
        DefaultEndpointUrl: null,
        HelpPromptTemplate: GDrivePrompt,
        IconKey: "gdrive");

    private static ProviderProfile BuildOneDrive() => new(
        Id: "onedrive",
        DisplayName: "Microsoft OneDrive",
        Kind: CloudProviderKind.OneDrive,
        AuthKind: AuthKind.OAuth,
        CredentialFields: Array.Empty<FieldSpec>(),
        DefaultEndpointUrl: null,
        HelpPromptTemplate: OneDrivePrompt,
        IconKey: "onedrive");

    private static ProviderProfile BuildDropbox() => new(
        Id: "dropbox",
        DisplayName: "Dropbox",
        Kind: CloudProviderKind.Dropbox,
        AuthKind: AuthKind.OAuth,
        CredentialFields: Array.Empty<FieldSpec>(),
        DefaultEndpointUrl: null,
        HelpPromptTemplate: DropboxPrompt,
        IconKey: "dropbox");

    // ------------------------------------------------------------------
    // Help-prompt templates
    // ------------------------------------------------------------------

    private const string AwsPrompt =
        "I need to obtain credentials for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the IAM console page where I create an access key; " +
        "(2) the numbered steps from login to the credential being displayed; " +
        "(3) what IAM permissions the access key needs for read, list, and upload access to one bucket; " +
        "(4) the exact shape of what I will receive; " +
        "(5) warnings about what NOT to do with this credential. Be concise.";

    private const string AzurePrompt =
        "I need to obtain a connection string for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the Azure portal page; " +
        "(2) the numbered steps from login to the connection string being displayed; " +
        "(3) which storage account permissions I need for read, list, and upload access to one container; " +
        "(4) the exact shape of the connection string; " +
        "(5) warnings about what NOT to do with this credential. Be concise.";

    private const string GcsPrompt =
        "I need to obtain a service account JSON key for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the GCP console page; " +
        "(2) the numbered steps from login to downloading the JSON key; " +
        "(3) which IAM role I should grant for read, list, and upload access to one bucket; " +
        "(4) the exact shape of what the JSON file will contain; " +
        "(5) warnings about what NOT to do with this file. Be concise.";

    private const string R2Prompt =
        "I need to obtain S3-compatible credentials for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the Cloudflare dashboard page; " +
        "(2) the numbered steps from login to the Access Key ID and Secret; " +
        "(3) which R2 permissions I need for read, list, and upload access to one bucket; " +
        "(4) the exact endpoint URL format for my account; " +
        "(5) warnings about what NOT to do with these credentials. Be concise.";

    private const string B2Prompt =
        "I need to obtain S3-compatible credentials for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the Backblaze console page; " +
        "(2) the numbered steps to create an application key; " +
        "(3) which bucket permissions I need; " +
        "(4) the endpoint URL format for my region; " +
        "(5) warnings about what NOT to do with these credentials. Be concise.";

    private const string WasabiPrompt =
        "I need to obtain S3-compatible credentials for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the Wasabi console page; " +
        "(2) the numbered steps to create an access key; " +
        "(3) which permissions I need for read, list, and upload access; " +
        "(4) the endpoint URL format for my region; " +
        "(5) warnings about what NOT to do with these credentials. Be concise.";

    private const string SpacesPrompt =
        "I need to obtain S3-compatible credentials for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) the exact URL of the DigitalOcean control panel page; " +
        "(2) the numbered steps to create a Spaces access key; " +
        "(3) which permissions I need for read, list, and upload access; " +
        "(4) the endpoint URL format for my region; " +
        "(5) warnings about what NOT to do with these credentials. Be concise.";

    private const string MinioPrompt =
        "I need to obtain S3-compatible credentials for [DisplayName] so I can use it in a desktop application. " +
        "Please give me: (1) how to create an access key in the MinIO console; " +
        "(2) the numbered steps; " +
        "(3) which bucket policy I need for read, list, and upload access; " +
        "(4) the default endpoint URL format for a local MinIO instance; " +
        "(5) warnings about what NOT to do with these credentials. Be concise.";

    private const string GDrivePrompt =
        "I want to connect [DisplayName] to a desktop application. " +
        "Please explain: (1) that Google Drive requires the application owner to register an OAuth client; " +
        "(2) which Google Cloud console page is used; " +
        "(3) the OAuth scopes needed for read, list, and upload access; " +
        "(4) what the user sees when they authorize; " +
        "(5) any review or verification Google requires. Be concise.";

    private const string OneDrivePrompt =
        "I want to connect [DisplayName] to a desktop application. " +
        "Please explain: (1) that OneDrive requires the application owner to register an Azure AD app; " +
        "(2) which Microsoft Entra admin center page is used; " +
        "(3) the Microsoft Graph permissions needed for read, list, and upload access; " +
        "(4) what the user sees when they authorize; " +
        "(5) any admin-consent requirements. Be concise.";

    private const string DropboxPrompt =
        "I want to connect [DisplayName] to a desktop application. " +
        "Please explain: (1) that Dropbox requires the application owner to register an app; " +
        "(2) which Dropbox App Console page is used; " +
        "(3) the OAuth scopes needed for read, list, and upload access; " +
        "(4) what the user sees when they authorize; " +
        "(5) any review or verification Dropbox requires. Be concise.";
}
