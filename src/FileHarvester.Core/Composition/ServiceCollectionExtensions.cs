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

namespace FileHarvester.Core.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFileHarvester(
        this IServiceCollection services,
        ResilienceConfig config,
        ILogSink? extraSink = null)
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

            r.ErrorClassification.TransientExceptionTypeNames.Add(
                "System.IO.IOException");

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
        });

        // ---- Platform adapters --------------------------------------------
        services.AddSingleton<IPathSearchService, WindowsPathSearch>();
        services.AddSingleton<IDefaultPathsProvider, DefaultPathsProvider>();

        // ---- Domain services ----------------------------------------------
        services.AddSingleton<IFileResolver, FileResolver>();
        services.AddSingleton<IContentReader, ContentReader>();
        services.AddSingleton<IBundleComposer, BundleComposer>();
        services.AddSingleton<IOutputSink, OutputSink>();

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
