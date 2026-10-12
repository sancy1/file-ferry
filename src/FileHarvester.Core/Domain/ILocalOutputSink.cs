// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ILocalOutputSink.cs
// PURPOSE:  Marker interface for the local filesystem implementation
//           of IOutputSink. Exists so the dispatcher can depend on
//           the local implementation without resolving IOutputSink,
//           which the dispatcher itself implements.
// RELATION: Implemented by OutputSink. Consumed by
//           DispatchingOutputSink.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Marker interface for the local output sink.
/// </summary>
/// <remarks>
/// <para>
/// The method set is identical to <see cref="IOutputSink"/>. This
/// interface exists so that a dispatcher implementing
/// <see cref="IOutputSink"/> can take the local implementation as a
/// dependency without causing the DI container to resolve the
/// dispatcher for itself.
/// </para>
/// </remarks>
public interface ILocalOutputSink : IOutputSink
{
}
