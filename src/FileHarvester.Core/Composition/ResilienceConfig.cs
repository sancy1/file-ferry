// ============================================================================
// FILE:     src/FileHarvester.Core/Composition/ResilienceConfig.cs
// PURPOSE:  Immutable configuration record for the resilience pipeline's
//           logging behavior. Carries the log directory path and toggles
//           for the two built-in sinks.
// RELATION: Consumed by ServiceCollectionExtensions.AddFileHarvester() to
//           wire FileLogSink and ConsoleLogSink. Constructed by the Avalonia
//           shell in Program.cs / App.axaml.cs (Phase 3.0) and by the
//           bootstrap smoke test (Phase 0.4).
// ============================================================================

namespace FileHarvester.Core.Composition;

/// <summary>
/// Configuration for the resilience pipeline's logging side channel.
/// Passed to <see cref="ServiceCollectionExtensions.AddFileHarvester"/>.
/// </summary>
/// <param name="LogDirectory">
/// Directory where <c>FileLogSink</c> writes daily-rotating JSON Lines
/// files. The directory is created on first write if it does not exist.
/// </param>
/// <param name="EnableConsoleSink">
/// When <c>true</c>, a <c>ConsoleLogSink</c> is registered alongside the
/// file sink. Useful for development and for capturing events in
/// container/debugger output. Default: <c>true</c>.
/// </param>
/// <param name="EnableFileSink">
/// When <c>true</c>, a <c>FileLogSink</c> is registered. Provides a
/// durable audit trail of every pipeline decision. Default: <c>true</c>.
/// </param>
public sealed record ResilienceConfig(
    string LogDirectory,
    bool EnableConsoleSink = true,
    bool EnableFileSink = true);
