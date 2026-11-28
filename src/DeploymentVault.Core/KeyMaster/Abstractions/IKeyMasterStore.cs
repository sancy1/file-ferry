// ============================================================================
// filepath: src/DeploymentVault.Core/KeyMaster/Abstractions/IKeyMasterStore.cs
// layer:    Core / Abstractions
// module:   DeploymentVault.Core.KeyMaster.Abstractions
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Session-only history store. In-memory only — nothing is persisted
//           to disk, database, or log. Cleared on app close and on Clear().
// RELATIONSHIPS
//   Implemented by : InMemoryKeyMasterStore (DeploymentVault.Platform.KeyMaster)
//   Used by        : KeyMasterViewModel
//   See also       : GeneratedKey
// ============================================================================

using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Core.KeyMaster.Abstractions;

/// <summary>
/// Session-only store for generated keys. Keeps the last 5 entries for the
/// history panel and a running session counter. Never persists.
/// </summary>
public interface IKeyMasterStore
{
    /// <summary>Adds a key to history (newest first, capped at 5) and bumps the counter.</summary>
    void Add(GeneratedKey key);

    /// <summary>The most recently generated key, or null.</summary>
    GeneratedKey? Latest { get; }

    /// <summary>History entries, newest first, max 5.</summary>
    IReadOnlyList<GeneratedKey> History { get; }

    /// <summary>Total keys generated this session (not capped).</summary>
    int SessionCount { get; }

    /// <summary>Clears history and resets the counter.</summary>
    void Clear();
}
