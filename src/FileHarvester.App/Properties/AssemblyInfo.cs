// ============================================================================
// FILE:     src/FileHarvester.App/Properties/AssemblyInfo.cs
// PURPOSE:  Assembly-level attributes for FileHarvester.App. Grants the
//           test assembly visibility into internal helpers so pure
//           functions like the drop-path extractor can be unit-tested
//           without making them part of the app's public surface.
// RELATION: Read by the C# compiler at build time.
// ============================================================================

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FileHarvester.Tests")]