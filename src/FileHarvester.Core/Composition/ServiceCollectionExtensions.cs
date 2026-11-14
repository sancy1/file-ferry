// ============================================================================
// FILE:     src\FileHarvester.Core/Composition/ServiceCollectionExtensions.cs
// PURPOSE:  The single DI entry point for FileHarvester.Core. Registers
//           Portfolio.Resilience, the domain services, and every format
//           writer. The format writer registry collects them all.
// RELATION: Called by the Avalonia shell in App.axaml.cs and by every test
//           fixture.
// ============================================================================

using FileHarvester.Core.Domain;
using FileHarvester.Core.Implementation;
using FileHarvester.Core.Platform;
using FileHarvester.Core.Writers;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Extensions;
using Portfolio.Resilience.Sinks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Resilience.Configuration;

namespace FileHarvester.Core.Composition;

public static class ServiceCollectionExtensions
{
    // ------------------------------------------------------------------
    // Backward-compatible overload. This signature predates CodeViewer
    // and is preserved verbatim so existing compiled consumers keep
    // binding. It delegates to the five-parameter overload below.
    // ------------------------------------------------------------------
    public static IServiceCollection AddFileHarvester(
        this IServiceCollection services,
        ResilienceConfig config,
        ILogSink? extraSink = null,
        Action<ResilienceBuilder>? configureImageTextifyPolicies = null)
        => services.AddFileHarvester(
            config,
            extraSink,
            configureImageTextifyPolicies,
            configureCodeViewerPolicies: null);

    // ------------------------------------------------------------------
    // Additive overload adding the CodeViewer policy callback. Each
    // feature registers only the policies it owns through its own
    // callback; the new parameter changes nothing for callers that do
    // not supply it.
    // ------------------------------------------------------------------
    public static IServiceCollection AddFileHarvester(
        this IServiceCollection services,
        ResilienceConfig config,
        ILogSink? extraSink,
        Action<ResilienceBuilder>? configureImageTextifyPolicies,
        Action<ResilienceBuilder>? configureCodeViewerPolicies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        if (config.EnableFileSink)
        {
            Directory.CreateDirectory(config.LogDirectory);
        }

        services.AddPortfolioResilience(r =>
        {
            if (config.EnableConsoleSink)
            {
                r.AddLogSink(new ConsoleLogSink());
            }

            if (config.EnableFileSink)
            {
                r.AddLogSink(new FileLogSink(config.LogDirectory));
            }

            if (extraSink is not null)
            {
                r.AddLogSink(extraSink);
            }

            r.AddPolicy(PolicyNames.FileRead, p =>
            {
                p.Retry.MaxAttempts           = 4;
                p.Retry.BaseDelayMs           = 80;
                p.Retry.MaxDelayMs            = 1_000;
                p.Retry.JitterRatio           = 0.2;

                p.Circuit.FailureThreshold    = 10;
                p.Circuit.OpenDurationSeconds = 15;
                p.Circuit.SuccessThreshold    = 2;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 3_000;
            });

            r.AddPolicy(PolicyNames.PathSearch, p =>
            {
                p.Retry.MaxAttempts           = 2;
                p.Retry.BaseDelayMs           = 200;
                p.Retry.MaxDelayMs            = 500;
                p.Retry.JitterRatio           = 0.0;

                p.Circuit.FailureThreshold    = 3;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 180_000;
            });

            r.AddPolicy(PolicyNames.BundleWrite, p =>
            {
                p.Retry.MaxAttempts           = 3;
                p.Retry.BaseDelayMs           = 150;
                p.Retry.MaxDelayMs            = 2_000;
                p.Retry.JitterRatio           = 0.1;

                p.Circuit.FailureThreshold    = 5;
                p.Circuit.OpenDurationSeconds = 20;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 15_000;
            });

            r.AddPolicy(PolicyNames.CloudRead, p =>
            {
                p.Retry.MaxAttempts           = 5;
                p.Retry.BaseDelayMs           = 100;
                p.Retry.MaxDelayMs            = 2_000;
                p.Retry.JitterRatio           = 0.2;

                p.Circuit.FailureThreshold    = 5;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 2;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 30_000;

                p.RateLimiter.Enabled         = true;
                p.RateLimiter.Strategy        = RateLimitStrategy.SlidingWindow;
                p.RateLimiter.PermitLimit     = 300;
                p.RateLimiter.WindowSeconds   = 60;
            });

            r.AddPolicy(PolicyNames.CloudWrite, p =>
            {
                p.Retry.MaxAttempts           = 3;
                p.Retry.BaseDelayMs           = 200;
                p.Retry.MaxDelayMs            = 3_000;
                p.Retry.JitterRatio           = 0.15;

                p.Circuit.FailureThreshold    = 3;
                p.Circuit.OpenDurationSeconds = 60;
                p.Circuit.SuccessThreshold    = 2;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 300_000;

                p.RateLimiter.Enabled         = true;
                p.RateLimiter.Strategy        = RateLimitStrategy.SlidingWindow;
                p.RateLimiter.PermitLimit     = 60;
                p.RateLimiter.WindowSeconds   = 60;
            });

            r.AddPolicy(PolicyNames.CloudSearch, p =>
            {
                p.Retry.MaxAttempts           = 3;
                p.Retry.BaseDelayMs           = 150;
                p.Retry.MaxDelayMs            = 1_500;
                p.Retry.JitterRatio           = 0.15;

                p.Circuit.FailureThreshold    = 3;
                p.Circuit.OpenDurationSeconds = 30;
                p.Circuit.SuccessThreshold    = 2;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 60_000;

                p.RateLimiter.Enabled         = true;
                p.RateLimiter.Strategy        = RateLimitStrategy.SlidingWindow;
                p.RateLimiter.PermitLimit     = 120;
                p.RateLimiter.WindowSeconds   = 60;
            });

            r.AddPolicy(PolicyNames.CloudAuth, p =>
            {
                p.Retry.MaxAttempts           = 2;
                p.Retry.BaseDelayMs           = 500;
                p.Retry.MaxDelayMs            = 5_000;
                p.Retry.JitterRatio           = 0.1;

                p.Circuit.FailureThreshold    = 3;
                p.Circuit.OpenDurationSeconds = 300;
                p.Circuit.SuccessThreshold    = 1;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 15_000;

                p.RateLimiter.Enabled         = true;
                p.RateLimiter.Strategy        = RateLimitStrategy.SlidingWindow;
                p.RateLimiter.PermitLimit     = 30;
                p.RateLimiter.WindowSeconds   = 60;
            });

            r.AddPolicy(PolicyNames.CredentialStoreWrite, p =>
            {
                p.Retry.MaxAttempts           = 2;
                p.Retry.BaseDelayMs           = 100;
                p.Retry.MaxDelayMs            = 500;
                p.Retry.JitterRatio           = 0.0;

                p.Circuit.FailureThreshold    = 3;
                p.Circuit.OpenDurationSeconds = 15;
                p.Circuit.SuccessThreshold    = 2;
                p.Circuit.OnlyCountTransient  = true;

                p.Timeout.TimeoutMs           = 5_000;
            });

            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.IO.IOException");

            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.Net.Http.HttpRequestException");
            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.Net.Sockets.SocketException");
            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.TimeoutException");
            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "Amazon.S3.AmazonS3Exception");
            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "Azure.RequestFailedException");
            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "Google.GoogleApiException");

            r.ErrorClassification.PermanentExceptionTypeNames.Add(
                "System.IO.FileNotFoundException");
            r.ErrorClassification.PermanentExceptionTypeNames.Add(
                "System.IO.DirectoryNotFoundException");
            r.ErrorClassification.PermanentExceptionTypeNames.Add(
                "System.UnauthorizedAccessException");
            r.ErrorClassification.PermanentExceptionTypeNames.Add(
                "System.Security.SecurityException");
            r.ErrorClassification.PermanentExceptionTypeNames.Add(
                "System.IO.PathTooLongException");

            // Feature-owned resilience policies. Each feature registers
            // only the policies it owns through its own callback.
            configureImageTextifyPolicies?.Invoke(r);
            configureCodeViewerPolicies?.Invoke(r);

        });

        // ---- Platform adapters --------------------------------------------
        services.AddSingleton<IPathSearchService, WindowsPathSearch>();
        services.AddSingleton<IDefaultPathsProvider, DefaultPathsProvider>();

        // ---- Domain services ----------------------------------------------
        // The local implementations are registered under three
        // interfaces each: their concrete type, their ILocal* marker,
        // and the primary interface. The dispatch extension
        // (AddFileHarvesterCloudDispatch) later replaces the primary
        // interface registration with the dispatcher. Registering the
        // concrete type once and resolving the two interface
        // registrations through it ensures a single instance is shared.
        services.AddSingleton<FileResolver>();
        services.AddSingleton<ILocalFileResolver>(sp => sp.GetRequiredService<FileResolver>());
        services.AddSingleton<IFileResolver>(sp => sp.GetRequiredService<FileResolver>());

        services.AddSingleton<ContentReader>();
        services.AddSingleton<ILocalContentReader>(sp => sp.GetRequiredService<ContentReader>());
        services.AddSingleton<IContentReader>(sp => sp.GetRequiredService<ContentReader>());

        services.AddSingleton<IBundleComposer, BundleComposer>();

        services.AddSingleton<OutputSink>();
        services.AddSingleton<ILocalOutputSink>(sp => sp.GetRequiredService<OutputSink>());
        services.AddSingleton<IOutputSink>(sp => sp.GetRequiredService<OutputSink>());


        // ---- Overwrite confirmation ---------------------------------------
        // The default is fail-closed. The App registers a dialog-based
        // implementation before AddFileHarvester; TryAddSingleton keeps
        // the App's registration when it exists.
        services.TryAddSingleton<IOverwriteConfirmer, AlwaysCancelOverwriteConfirmer>();

        // ---- Format writers -----------------------------------------------
        // Add one line per new writer. The registry collects them all and
        // indexes by extension. Adding a format = one new file + one line.
        services.AddSingleton<IFormatWriter, TxtFormatWriter>();
        services.AddSingleton<IFormatWriter, MarkdownFormatWriter>();
        services.AddSingleton<IFormatWriter, JsonFormatWriter>();
        services.AddSingleton<IFormatWriter, HtmlFormatWriter>();
        services.AddSingleton<IFormatWriter, DocxFormatWriter>();
        services.AddSingleton<IFormatWriter, PdfFormatWriter>();
        services.AddSingleton<IFormatWriter, XlsxFormatWriter>();
        services.AddSingleton<IFormatWriter, CsvFormatWriter>();
        services.AddSingleton<IFormatWriter, ZipFormatWriter>();

        services.AddSingleton<IFormatWriterRegistry, FormatWriterRegistry>();

        return services;
    }
}
