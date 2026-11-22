// ============================================================================
// filepath: src/DeploymentVault.Core/Abstractions/IDeploymentVaultStore.cs
// layer:    Contracts (Abstractions)
// module:   DeploymentVault.Core.Abstractions
// since:    Phase A (Deployment Vault registry)
// purpose:  The persistence contract for the Deployment Vault. Every
//           operation is async because the implementation is backed by
//           SQLite (Microsoft.Data.Sqlite opens connections asynchronously).
//           No implementation types appear here.
//
// RELATIONSHIPS
//   Implements : n/a
//   Depends on : Application, InfrastructureRecord, VaultQuery
//   Used by    : SqliteDeploymentVaultStore (implements it),
//                DeploymentVaultViewModel (consumes it),
//                FileHarvester.App's App.axaml.cs (resolves it from DI),
//                SqliteDeploymentVaultStoreTests (tests against it)
//   See also   : Application, InfrastructureRecord, VaultQuery
//
// NOTES
//   - Every method accepts a CancellationToken. Every implementation must
//     honor it. The VM passes a linked token from the page's lifetime.
//   - Every write operation is transactional. Every read operation is not.
//     A read that joins two tables (ListRecordsAsync joined to Application
//     is not required in Phase A) is still not transactional; SQLite's
//     connection isolation is sufficient for a local single-user store.
//   - There is no PatchAsync. Updates are whole-record. This is a deliberate
//     simplification for Phase A; a future partial-update need adds a
//     PatchAsync, not changes the existing UpdateAsync.
//   - Errors throw. There is no result-envelope type. The VM catches and
//     surfaces. Failures are not retried; a local SQLite file failure is
//     not a transient network failure.
//   - Soft delete vs hard delete: SoftDelete* sets `DeletedAtUtc` to now.
//     Restore* sets it back to null. HardDelete* issues an actual SQL DELETE.
//     The schema has ON DELETE CASCADE from InfrastructureRecord to
//     Application, so hard-deleting an application removes its records.
// ============================================================================

namespace DeploymentVault.Core.Abstractions;

using DeploymentVault.Core.Domain;

/// <summary>
/// The persistence contract for the Deployment Vault registry.
/// </summary>
public interface IDeploymentVaultStore
{
    // ---- Applications --------------------------------------------------

    /// <summary>
    /// Lists applications matching the query. Returns the requested page,
    /// sorted per <see cref="VaultQuery.SortBy"/>.
    /// </summary>
    /// <param name="query">The typed query. Never null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The page of applications. Never null; may be empty.</returns>
    Task<IReadOnlyList<Application>> ListApplicationsAsync(
        VaultQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single application by id, regardless of soft-delete state.
    /// </summary>
    /// <param name="id">The application id. Not Guid.Empty.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The application, or null if no row matches the id.</returns>
    Task<Application?> GetApplicationAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new application. Throws if an active (non-deleted) row
    /// with the same name (case-insensitive) already exists.
    /// </summary>
    /// <param name="application">The application to insert. Id must be populated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The inserted application as stored.</returns>
    Task<Application> CreateApplicationAsync(
        Application application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing application. All fields are written. Throws if
    /// no row with the id exists.
    /// </summary>
    /// <param name="application">The updated application. Id must match an existing row.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The updated application as stored.</returns>
    Task<Application> UpdateApplicationAsync(
        Application application, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes an application by setting its tombstone to now. The
    /// row remains readable; its records are not affected.
    /// </summary>
    /// <param name="id">The application id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SoftDeleteApplicationAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted application by clearing its tombstone.
    /// </summary>
    /// <param name="id">The application id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task RestoreApplicationAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes an application. Cascades to its records.
    /// </summary>
    /// <param name="id">The application id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task HardDeleteApplicationAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the active (non-deleted) records under an application.
    /// Used by the application card to show a record count without
    /// loading the records.
    /// </summary>
    /// <param name="applicationId">The application id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The count. Never negative.</returns>
    Task<int> CountRecordsAsync(
        Guid applicationId, CancellationToken cancellationToken = default);

    // ---- Records -------------------------------------------------------

    /// <summary>
    /// Lists records under an application, matching the query.
    /// </summary>
    /// <param name="applicationId">The parent application id.</param>
    /// <param name="query">The typed query. Never null.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The page of records. Never null; may be empty.</returns>
    Task<IReadOnlyList<InfrastructureRecord>> ListRecordsAsync(
        Guid applicationId, VaultQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single record by id, regardless of soft-delete state.
    /// </summary>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The record, or null if no row matches the id.</returns>
    Task<InfrastructureRecord?> GetRecordAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new record. Throws if the applicationId does not exist
    /// (the FK constraint fires).
    /// </summary>
    /// <param name="record">The record to insert. Id must be populated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The inserted record as stored.</returns>
    Task<InfrastructureRecord> CreateRecordAsync(
        InfrastructureRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing record. All fields are written. Throws if no
    /// row with the id exists.
    /// </summary>
    /// <param name="record">The updated record.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The updated record as stored.</returns>
    Task<InfrastructureRecord> UpdateRecordAsync(
        InfrastructureRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a record by setting its tombstone to now.
    /// </summary>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task SoftDeleteRecordAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted record by clearing its tombstone.
    /// </summary>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task RestoreRecordAsync(
        Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a record.
    /// </summary>
    /// <param name="id">The record id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task HardDeleteRecordAsync(
        Guid id, CancellationToken cancellationToken = default);
}