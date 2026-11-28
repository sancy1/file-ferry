// ============================================================================
// filepath: src/DeploymentVault.Platform/KeyMaster/Storage/InMemoryKeyMasterStore.cs
// layer:    Platform / Storage
// module:   DeploymentVault.Platform.KeyMaster.Storage
// since:    KeyMaster (Deployment Vault sub-app)
// purpose:  Session-only history store. Holds the last 5 generated keys
//           (newest first) and a running session counter. No persistence —
//           nothing touches disk, database, or log. Cleared on Clear() and
//           lost on app close.
// RELATIONSHIPS
//   Implements : IKeyMasterStore
//   Used by    : KeyMasterViewModel
//   See also   : GeneratedKey
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;

namespace DeploymentVault.Platform.KeyMaster.Storage;

/// <summary>
/// In-memory session store for generated keys.
/// </summary>
public sealed class InMemoryKeyMasterStore : IKeyMasterStore
{
    private const int HistoryCap = 5;
    private readonly List<GeneratedKey> _history = new(HistoryCap);
    private int _sessionCount;

    /// <inheritdoc/>
    public GeneratedKey? Latest => _history.Count > 0 ? _history[0] : null;

    /// <inheritdoc/>
    public IReadOnlyList<GeneratedKey> History => _history.ToArray();

    /// <inheritdoc/>
    public int SessionCount => _sessionCount;

    /// <inheritdoc/>
    public void Add(GeneratedKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _history.Insert(0, key);
        if (_history.Count > HistoryCap)
        {
            _history.RemoveAt(_history.Count - 1);
        }
        _sessionCount++;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _history.Clear();
        _sessionCount = 0;
    }
}
