// ============================================================================
// FILE:     src/FileHarvester.Core/Domain/ILocalContentReader.cs
// PURPOSE:  Marker interface for the local filesystem implementation
//           of IContentReader. Exists so the dispatcher can depend on
//           the local implementation without resolving IContentReader,
//           which the dispatcher itself implements.
// RELATION: Implemented by ContentReader. Consumed by
//           DispatchingContentReader.
// ============================================================================

namespace FileHarvester.Core.Domain;

/// <summary>
/// Marker interface for the local content reader.
/// </summary>
/// <remarks>
/// <para>
/// The method set is identical to <see cref="IContentReader"/>. This
/// interface exists so that a dispatcher implementing
/// <see cref="IContentReader"/> can take the local implementation as a
/// dependency without causing the DI container to resolve the
/// dispatcher for itself.
/// </para>
/// </remarks>
public interface ILocalContentReader : IContentReader
{
}
