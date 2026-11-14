// ============================================================================
// FILE:     src/Prompt.Platform/Storage/SqliteCategoryStore.cs
// PURPOSE:  SQLite implementation of ICategoryStore and ISubCategoryStore.
//           Implements CRUD via parameterized SQL; wraps every operation in
//           the prompt-store resilience policy; gets connections from
//           PromptSchemaBootstrap (which sets PRAGMA foreign_keys = ON).
// RELATION: Implements ICategoryStore and ISubCategoryStore. Consumed by
//           PromptListViewModel (P2.3). Uses PromptSchemaBootstrap (P1.1).
// NOTES:    - Delete cascade is enforced by the schema's ON DELETE SET NULL
//             foreign key, which works because PromptSchemaBootstrap.OpenAsync
//             executes PRAGMA foreign_keys = ON (F-081).
//           - UpdateAsync is PUT (full replace via INSERT ... ON CONFLICT
//             DO UPDATE), not PATCH.
//           - ListAsync orders by SortOrder, then Name COLLATE NOCASE.
// ============================================================================

using System.Globalization;
using Microsoft.Data.Sqlite;
using Portfolio.Resilience.Abstractions;
using Prompt.Core.Domain;
using Prompt.Platform.Composition;

namespace Prompt.Platform.Storage;

/// <summary>
/// SQLite implementation of <see cref="ICategoryStore"/> and
/// <see cref="ISubCategoryStore"/>.
/// </summary>
public sealed class SqliteCategoryStore : ICategoryStore, ISubCategoryStore
{
    private readonly PromptSchemaBootstrap _bootstrap;
    private readonly IResilienceExecutor _executor;

    /// <summary>
    /// Builds a store against the given bootstrap and executor.
    /// </summary>
    public SqliteCategoryStore(PromptSchemaBootstrap bootstrap, IResilienceExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(executor);
        _bootstrap = bootstrap;
        _executor = executor;
    }

    /// <inheritdoc/>
    public Task<Category> CreateAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO Categories (Id, Name, Description, SortOrder, CreatedAt, UpdatedAt)
                    VALUES ($id, $name, $desc, $sort, $created, $updated)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = excluded.Name,
                        Description = excluded.Description,
                        SortOrder = excluded.SortOrder,
                        UpdatedAt = excluded.UpdatedAt;
                    """;
                cmd.Parameters.AddWithValue("$id", category.Id.ToString());
                cmd.Parameters.AddWithValue("$name", category.Name);
                cmd.Parameters.AddWithValue("$desc", (object?)category.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$sort", category.SortOrder);
                cmd.Parameters.AddWithValue("$created", category.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$updated", category.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return category;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, Name, Description, SortOrder, CreatedAt, UpdatedAt
                    FROM Categories
                    ORDER BY SortOrder ASC, Name COLLATE NOCASE ASC;
                    """;
                var list = new List<Category>();
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(ReadCategory(reader));
                }
                return (IReadOnlyList<Category>)list;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Category?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, Name, Description, SortOrder, CreatedAt, UpdatedAt
                    FROM Categories WHERE Id = $id;
                    """;
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return (Category?)ReadCategory(reader);
                }
                return null;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Category> UpdateAsync(Category category, CancellationToken cancellationToken = default)
        => CreateAsync(category, cancellationToken);

    /// <inheritdoc/>
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Categories WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    // ---- ISubCategoryStore ----

    Task<SubCategory> ISubCategoryStore.CreateAsync(SubCategory subCategory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subCategory);
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO SubCategories (Id, CategoryId, Name, Description, SortOrder, CreatedAt, UpdatedAt)
                    VALUES ($id, $cat, $name, $desc, $sort, $created, $updated)
                    ON CONFLICT(Id) DO UPDATE SET
                        CategoryId = excluded.CategoryId,
                        Name = excluded.Name,
                        Description = excluded.Description,
                        SortOrder = excluded.SortOrder,
                        UpdatedAt = excluded.UpdatedAt;
                    """;
                cmd.Parameters.AddWithValue("$id", subCategory.Id.ToString());
                cmd.Parameters.AddWithValue("$cat", subCategory.CategoryId.ToString());
                cmd.Parameters.AddWithValue("$name", subCategory.Name);
                cmd.Parameters.AddWithValue("$desc", (object?)subCategory.Description ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$sort", subCategory.SortOrder);
                cmd.Parameters.AddWithValue("$created", subCategory.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$updated", subCategory.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return subCategory;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    Task<IReadOnlyList<SubCategory>> ISubCategoryStore.ListAsync(Guid? categoryId, CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                if (categoryId.HasValue)
                {
                    cmd.CommandText = """
                        SELECT Id, CategoryId, Name, Description, SortOrder, CreatedAt, UpdatedAt
                        FROM SubCategories WHERE CategoryId = $cat
                        ORDER BY SortOrder ASC, Name COLLATE NOCASE ASC;
                        """;
                    cmd.Parameters.AddWithValue("$cat", categoryId.Value.ToString());
                }
                else
                {
                    cmd.CommandText = """
                        SELECT Id, CategoryId, Name, Description, SortOrder, CreatedAt, UpdatedAt
                        FROM SubCategories
                        ORDER BY CategoryId ASC, SortOrder ASC, Name COLLATE NOCASE ASC;
                        """;
                }
                var list = new List<SubCategory>();
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    list.Add(ReadSubCategory(reader));
                }
                return (IReadOnlyList<SubCategory>)list;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    Task<SubCategory?> ISubCategoryStore.GetAsync(Guid id, CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT Id, CategoryId, Name, Description, SortOrder, CreatedAt, UpdatedAt
                    FROM SubCategories WHERE Id = $id;
                    """;
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    return (SubCategory?)ReadSubCategory(reader);
                }
                return null;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    Task<SubCategory> ISubCategoryStore.UpdateAsync(SubCategory subCategory, CancellationToken cancellationToken)
        => ((ISubCategoryStore)this).CreateAsync(subCategory, cancellationToken);

    Task ISubCategoryStore.DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        return _executor.ExecuteAsync(
            PromptPolicyNames.PromptStore,
            async ct =>
            {
                await using var conn = await _bootstrap.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM SubCategories WHERE Id = $id;";
                cmd.Parameters.AddWithValue("$id", id.ToString());
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return true;
            },
            fallback: null, idempotencyKey: null, timeBudgetMs: null, ct: cancellationToken);
    }

    // ---- readers ----

    private static Category ReadCategory(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetInt32(3),
        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture));

    private static SubCategory ReadSubCategory(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        Guid.Parse(reader.GetString(1)),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt32(4),
        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture));
}