// ============================================================================
// FILE:     src/ImageTextify.Core/Models/ImageTextifyModelDescriptor.cs
// PURPOSE:  The pure description of one downloadable OCR or speech model.
//           Carries identity, source, integrity, and licence fields. Never
//           performs I/O; the store implementation validates and uses it.
// RELATION: Consumed by IModelStore and by the model manifest.
// ============================================================================

namespace ImageTextify.Core.Models;

/// <summary>
/// The description of one downloadable ImageTextify model.
/// </summary>
/// <param name="Id">
/// The stable identifier of the model. Must match the pattern
/// <c>^[a-z0-9][a-z0-9-]*$</c>. Used as the local storage folder name.
/// Never null or empty.
/// </param>
/// <param name="DisplayName">
/// A human-readable name shown in the disclosure dialog. Never null or
/// empty.
/// </param>
/// <param name="SourceUrl">
/// The HTTPS URL from which the model is downloaded. Must use the https
/// scheme. Never null or empty. The store implementation rejects any URL
/// that is not https.
/// </param>
/// <param name="SizeBytes">
/// The exact size of the model in bytes. Positive. Used for progress
/// reporting and as a defensive check.
/// </param>
/// <param name="Sha256Hex">
/// The expected SHA-256 of the model file as 64 lowercase hexadecimal
/// characters. The store computes the actual hash and compares. A mismatch
/// is a permanent failure and the partial file is deleted.
/// </param>
/// <param name="LicenseName">
/// The name of the model's license, for example "Apache-2.0". Shown in the
/// disclosure dialog. Never null or empty.
/// </param>
/// <param name="LicenseUrl">
/// The URL of the license text. Must use the https scheme. Never null or
/// empty.
/// </param>
/// <param name="Purpose">
/// A one-line description of what the model is for, shown in the
/// disclosure dialog. Never null or empty.
/// </param>
public sealed record ImageTextifyModelDescriptor(
    string Id,
    string DisplayName,
    string SourceUrl,
    long SizeBytes,
    string Sha256Hex,
    string LicenseName,
    string LicenseUrl,
    string Purpose);