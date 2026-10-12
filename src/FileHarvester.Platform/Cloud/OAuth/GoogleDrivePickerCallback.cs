using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace FileHarvester.Platform.Cloud.OAuth;

internal sealed record GoogleDrivePickerCallback(
    string? Code,
    string? State,
    string? Error,
    string? Scope,
    IReadOnlyList<string> FileIds)
{
    internal static GoogleDrivePickerCallback Parse(string queryString)
    {
        ArgumentNullException.ThrowIfNull(queryString);

        var query = HttpUtility.ParseQueryString(queryString);
        var idsValues = query.GetValues("picked_file_ids");
        if (idsValues is { Length: > 1 })
        {
            throw new OAuthCallbackException("invalid_picker_response");
        }

        var rawIds = idsValues?.FirstOrDefault() ?? string.Empty;
        var ids = rawIds.Length == 0
            ? Array.Empty<string>()
            : rawIds.Split(',', StringSplitOptions.TrimEntries);

        if (ids.Any(id => id.Length == 0)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new OAuthCallbackException("invalid_picker_response");
        }

        return new GoogleDrivePickerCallback(
            Code: query["code"],
            State: query["state"],
            Error: query["error"],
            Scope: query["scope"],
            FileIds: ids);
    }
}
