// ============================================================================
// FILE:     src/FileHarvester.Platform/Cloud/OAuth/IOAuthLoopbackListener.cs
// PURPOSE:  The mockable seam over the OAuth loopback listener. The flow
//           depends on this interface, not the concrete class, so the
//           flow is testable without a real socket. The concrete
//           OAuthLoopbackListener remains the production implementation
//           and is exercised directly against a real socket by its own
//           tests.
// RELATION: Implemented by OAuthLoopbackListener. Consumed by
//           GoogleOAuthFlow (C.6.3) and by GoogleOAuthFlowTests.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileHarvester.Platform.Cloud.OAuth;

/// <summary>
/// A loopback listener that captures one OAuth callback.
/// </summary>
internal interface IOAuthLoopbackListener : IAsyncDisposable
{
    /// <summary>
    /// The port the listener is bound to. Valid after the first call to
    /// <see cref="WaitForCallbackAsync"/> has bound the listener.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// Waits for exactly one callback on the configured path and returns
    /// its parsed code, state, and error.
    /// </summary>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The parsed callback result. Never null.</returns>
    Task<OAuthCallbackResult> WaitForCallbackAsync(CancellationToken ct);
}
