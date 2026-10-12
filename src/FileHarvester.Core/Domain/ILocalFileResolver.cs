// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ILocalFileResolver.cs
// PURPOSE:  Marker interface for the local filesystem implementation
//           of IFileResolver. Exists so the dispatcher can depend on
//           the local implementation without resolving IFileResolver,
//           which the dispatcher itself implements.
// RELATION: Implemented by FileResolver. Consumed by
//           DispatchingFileResolver.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Marker interface for the local filesystem resolver.
/// </summary>
/// <remarks>
/// <para>
/// The method set is identical to <see cref="IFileResolver"/>. This
/// interface exists so that a dispatcher implementing
/// <see cref="IFileResolver"/> can take the local implementation as a
/// dependency without causing the DI container to resolve the
/// dispatcher for itself.
/// </para>
/// </remarks>
public interface ILocalFileResolver : IFileResolver
{
}
