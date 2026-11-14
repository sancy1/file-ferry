// ============================================================================
// FILE:     src/ImageTextify.Platform/Composition/ImageTextifyComposition.cs
// PURPOSE:  Owns the registration of ImageTextify-owned Portfolio.Resilience
//           policies. Called from the App composition root through the
//           AddFileHarvester callback parameter, so FileHarvester.Core
//           does not need to reference ImageTextify.Platform.
// RELATION: Invoked from App.axaml.cs as
//           configureImageTextifyPolicies: ImageTextifyComposition.RegisterResiliencePolicies
//           The single image-textify-model-download policy is registered here
//           in Phase 2.2. image-textify-ocr is registered in Phase 2.4 when
//           the OCR engine first calls it.
// ============================================================================

using ImageTextify.Platform.Composition;
using Portfolio.Resilience.Configuration;

namespace ImageTextify.Platform.Composition;

/// <summary>
/// Composition helpers for ImageTextify-owned resilience policies.
/// </summary>
public static class ImageTextifyComposition
{
    /// <summary>
    /// Registers every ImageTextify-owned resilience policy into the supplied
    /// <see cref="ResilienceBuilder"/>.
    /// </summary>
    /// <param name="builder">
    /// The resilience builder supplied by
    /// <c>FileHarvester.Core.Composition.ServiceCollectionExtensions.AddFileHarvester</c>
    /// through its <c>configureImageTextifyPolicies</c> parameter. Not null.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is null.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Only policies that ImageTextify owns are registered here. The method
    /// does not touch any policy belonging to another feature.
    /// </para>
    /// <para>
    /// Phase 2.2 registers <c>image-textify-model-download</c> only. The
    /// <c>image-textify-ocr</c>, <c>image-textify-pdf-render</c>,
    /// <c>image-textify-media</c>, and <c>image-textify-speech</c> policies
    /// are declared in <see cref="ImageTextifyPolicyNames"/> but remain
    /// unregistered until the phase that first calls them.
    /// </para>
    /// </remarks>
    public static void RegisterResiliencePolicies(ResilienceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPolicy(ImageTextifyPolicyNames.ModelDownload, p =>
        {
            // Bounded retries with backoff. Only transient network and HTTP
            // failures are retried; checksum mismatches and invalid
            // descriptors are permanent and never retried.
            p.Retry.MaxAttempts           = 3;
            p.Retry.BaseDelayMs           = 500;
            p.Retry.MaxDelayMs            = 5_000;
            p.Retry.JitterRatio           = 0.20;

            p.Circuit.FailureThreshold    = 3;
            p.Circuit.OpenDurationSeconds = 60;
            p.Circuit.SuccessThreshold    = 2;
            p.Circuit.OnlyCountTransient  = true;

            // Per-attempt timeout: ten minutes. A model download on a slow
            // connection needs room; the total time is bounded by the retry
            // budget, not by this value alone.
            p.Timeout.TimeoutMs           = 600_000;
        });
    }
}