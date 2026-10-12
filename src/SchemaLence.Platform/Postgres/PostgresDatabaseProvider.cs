using Npgsql;
using NpgsqlTypes;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Metadata;
using SchemaLence.Core.Providers;

namespace SchemaLence.Platform.Postgres;

public sealed class PostgresDatabaseProvider : IDatabaseProvider
{
    public string ProviderId => "postgresql";

    public ProviderCapabilities Capabilities { get; } = new(
        SupportsSchemas: true,
        SupportsTableStructure: true,
        SupportsPagedData: true,
        SupportsRelationships: true,
        SupportsVectorMetadata: true,
        SupportsNativeBackup: true,
        ExportFormats: new[] { "Markdown", "JSON", "CSV", "XLSX", "SQL", "PostgreSQL archive" });

    public async Task<ConnectionTestResult> TestConnectionAsync(
        DatabaseConnectionInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = PostgresConnectionParser.ToProfile(Guid.NewGuid().ToString("N"), input);
            var connectionString = PostgresConnectionParser.BuildConnectionString(
                profile, input.Password);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "SELECT current_database(), current_setting('server_version')",
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new ConnectionTestResult(
                true,
                "PostgreSQL " + reader.GetString(1),
                reader.GetString(0),
                "Connection succeeded.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or FormatException)
        {
            return new ConnectionTestResult(
                false,
                string.Empty,
                string.Empty,
                GetSafeConnectionError(ex));
        }
    }

    public async Task<IReadOnlyList<DatabaseNamespace>> ListNamespacesAsync(
        DatabaseConnectionProfile profile,
        string password,
        CancellationToken cancellationToken = default) =>
        await ExecuteWithDiagnosticsAsync(
            "Schema listing",
            () => ListNamespacesCoreAsync(profile, password, cancellationToken))
            .ConfigureAwait(false);

    private static async Task<IReadOnlyList<DatabaseNamespace>> ListNamespacesCoreAsync(
        DatabaseConnectionProfile profile,
        string password,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);
        const string sql = """
            SELECT n.nspname,
                   obj_description(n.oid, 'pg_namespace')
            FROM pg_catalog.pg_namespace AS n
            WHERE n.nspname <> 'information_schema'
              AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\'
              AND has_schema_privilege(n.oid, 'USAGE')
            ORDER BY n.nspname;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var namespaces = new List<DatabaseNamespace>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            namespaces.Add(new DatabaseNamespace(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        return namespaces;
    }

    public async Task<IReadOnlyList<DatabaseObject>> ListObjectsAsync(
        DatabaseConnectionProfile profile,
        string password,
        string? namespaceFilter,
        CancellationToken cancellationToken = default) =>
        await ExecuteWithDiagnosticsAsync(
            "Object listing",
            () => ListObjectsCoreAsync(profile, password, namespaceFilter, cancellationToken))
            .ConfigureAwait(false);

    private static async Task<IReadOnlyList<DatabaseObject>> ListObjectsCoreAsync(
        DatabaseConnectionProfile profile,
        string password,
        string? namespaceFilter,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);
        const string sql = """
            SELECT n.nspname,
                   c.relname,
                   c.relkind,
                   c.reltuples::bigint,
                   pg_total_relation_size(c.oid),
                   obj_description(c.oid, 'pg_class'),
                   has_table_privilege(c.oid, 'SELECT')
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND n.nspname <> 'information_schema'
              AND n.nspname NOT LIKE 'pg\_%' ESCAPE '\'
              AND has_schema_privilege(n.oid, 'USAGE')
              AND (@namespace IS NULL OR n.nspname = @namespace)
            ORDER BY n.nspname, c.relname;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(new NpgsqlParameter<string?>("namespace", NpgsqlDbType.Text)
        {
            TypedValue = namespaceFilter,
        });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        var objects = new List<DatabaseObject>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            objects.Add(new DatabaseObject(
                reader.GetString(0),
                reader.GetString(1),
                MapKind(reader.GetChar(2)),
                reader.IsDBNull(3) || reader.GetInt64(3) < 0 ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetBoolean(6)));
        }
        return objects;
    }

    public async Task<DatabaseObjectStructure> GetStructureAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        CancellationToken cancellationToken = default) =>
        await ExecuteWithDiagnosticsAsync(
            "Structure metadata",
            () => GetStructureCoreAsync(
                profile, password, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);

    private static async Task<DatabaseObjectStructure> GetStructureCoreAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);
        var databaseObject = await ExecuteWithDiagnosticsAsync(
            "Object metadata",
            () => GetObjectAsync(
                connection, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);
        if (databaseObject is null)
        {
            throw new InvalidOperationException(
                "The selected database object is not visible to this connection.");
        }

        var columns = await ExecuteWithDiagnosticsAsync(
            "Column metadata",
            () => ReadColumnsAsync(connection, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);
        var constraints = await ExecuteWithDiagnosticsAsync(
            "Constraint metadata",
            () => ReadConstraintsAsync(connection, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);
        var indexes = await ExecuteWithDiagnosticsAsync(
            "Index metadata",
            () => ReadIndexesAsync(connection, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);
        var triggers = await ExecuteWithDiagnosticsAsync(
            "Trigger metadata",
            () => ReadTriggersAsync(connection, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);
        var options = await ExecuteWithDiagnosticsAsync(
            "Object options metadata",
            () => ReadObjectOptionsAsync(
                connection, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);
        var (rowLevelSecurity, definition) = options;
        var vectorColumns = columns
            .Where(column => column.DataType.StartsWith("vector", StringComparison.OrdinalIgnoreCase))
            .Select(column => new VectorColumnInfo(
                column.Name,
                column.DataType,
                TryReadVectorDimensions(column.DataType)))
            .ToArray();

        return new DatabaseObjectStructure(
            databaseObject,
            columns,
            constraints,
            indexes,
            triggers,
            vectorColumns,
            rowLevelSecurity,
            definition,
            "Metadata is limited to objects visible to the connected PostgreSQL role.");
    }

    public async Task<DatabasePage> GetPageAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        int pageIndex,
        int pageSize,
        string? sortColumn,
        bool sortDescending,
        CancellationToken cancellationToken = default)
        => await ExecuteWithDiagnosticsAsync(
            "Data page",
            () => GetPageCoreAsync(
                profile,
                password,
                namespaceName,
                objectName,
                pageIndex,
                pageSize,
                sortColumn,
                sortDescending,
                cancellationToken))
            .ConfigureAwait(false);

    private static async Task<DatabasePage> GetPageCoreAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        int pageIndex,
        int pageSize,
        string? sortColumn,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (pageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize), "Page size must be between 1 and 500.");
        }

        await using var connection = await OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);
        var columns = await ReadColumnsAsync(
            connection, namespaceName, objectName, cancellationToken).ConfigureAwait(false);
        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                "The selected database object is not visible or has no readable columns.");
        }
        if (!await CanReadAsync(connection, namespaceName, objectName, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The connected PostgreSQL role does not have SELECT permission for this object.");
        }

        var selectedSort = columns.FirstOrDefault(column =>
            string.Equals(column.Name, sortColumn, StringComparison.Ordinal));
        var orderBy = selectedSort is null
            ? string.Empty
            : " ORDER BY " + QuoteIdentifier(selectedSort.Name)
                + (sortDescending ? " DESC" : " ASC") + " NULLS LAST";
        var offset = checked(pageIndex * pageSize);
        var sql = "SELECT * FROM "
            + QuoteIdentifier(namespaceName) + "."
            + QuoteIdentifier(objectName)
            + orderBy
            + " LIMIT @page_size OFFSET @page_offset";
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        command.Parameters.AddWithValue("page_size", pageSize);
        command.Parameters.AddWithValue("page_offset", offset);

        var rowValues = new List<IReadOnlyList<string?>>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var row = new string?[reader.FieldCount];
                for (var field = 0; field < reader.FieldCount; field++)
                {
                    row[field] = await ReadDisplayValueAsync(reader, field, cancellationToken)
                        .ConfigureAwait(false);
                }
                rowValues.Add(row);
            }
        }

        var estimatedRows = await GetEstimatedRowsAsync(
            connection, namespaceName, objectName, cancellationToken).ConfigureAwait(false);
        return new DatabasePage(
            columns.Select(column => column.Name).ToArray(),
            rowValues,
            estimatedRows,
            pageIndex,
            pageSize);
    }

    public async Task<long> CountRowsAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        CancellationToken cancellationToken = default) =>
        await ExecuteWithDiagnosticsAsync(
            "Exact row count",
            () => CountRowsCoreAsync(
                profile, password, namespaceName, objectName, cancellationToken))
            .ConfigureAwait(false);

    private static async Task<long> CountRowsCoreAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);
        if (!await CanReadAsync(connection, namespaceName, objectName, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The connected PostgreSQL role does not have SELECT permission for this object.");
        }

        var sql = "SELECT count(*) FROM "
            + QuoteIdentifier(namespaceName) + "."
            + QuoteIdentifier(objectName);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task StreamRowsAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        int batchSize,
        Func<IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string?>>, Task> writeBatchAsync,
        CancellationToken cancellationToken = default) =>
        await ExecuteWithDiagnosticsAsync(
            "Table data export",
            async () =>
            {
                await StreamRowsCoreAsync(
                    profile,
                    password,
                    namespaceName,
                    objectName,
                    batchSize,
                    writeBatchAsync,
                    cancellationToken).ConfigureAwait(false);
                return true;
            })
            .ConfigureAwait(false);

    private static async Task StreamRowsCoreAsync(
        DatabaseConnectionProfile profile,
        string password,
        string namespaceName,
        string objectName,
        int batchSize,
        Func<IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string?>>, Task> writeBatchAsync,
        CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(batchSize), "Export batch size must be between 1 and 5000.");
        }
        ArgumentNullException.ThrowIfNull(writeBatchAsync);

        await using var connection = await OpenAsync(profile, password, cancellationToken)
            .ConfigureAwait(false);
        if (!await CanReadAsync(connection, namespaceName, objectName, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The connected PostgreSQL role does not have SELECT permission for this object.");
        }

        var sql = "SELECT * FROM "
            + QuoteIdentifier(namespaceName) + "."
            + QuoteIdentifier(objectName);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 0,
        };
        await using var reader = await command.ExecuteReaderAsync(
            System.Data.CommandBehavior.SequentialAccess,
            cancellationToken).ConfigureAwait(false);
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();
        var rows = new List<IReadOnlyList<string?>>(batchSize);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new string?[reader.FieldCount];
            for (var field = 0; field < reader.FieldCount; field++)
            {
                row[field] = await ReadDisplayValueAsync(reader, field, cancellationToken)
                    .ConfigureAwait(false);
            }
            rows.Add(row);
            if (rows.Count == batchSize)
            {
                await writeBatchAsync(columns, rows).ConfigureAwait(false);
                rows.Clear();
            }
        }

        if (rows.Count > 0)
        {
            await writeBatchAsync(columns, rows).ConfigureAwait(false);
        }
        else if (columns.Length > 0)
        {
            await writeBatchAsync(columns, Array.Empty<IReadOnlyList<string?>>())
                .ConfigureAwait(false);
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(
        DatabaseConnectionProfile profile,
        string password,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(
            PostgresConnectionParser.BuildConnectionString(profile, password));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                "SET default_transaction_read_only = on",
                connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<DatabaseObject?> GetObjectAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT n.nspname, c.relname, c.relkind, c.reltuples::bigint,
                   pg_total_relation_size(c.oid),
                   obj_description(c.oid, 'pg_class'),
                   has_table_privilege(c.oid, 'SELECT')
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @name
              AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND has_schema_privilege(n.oid, 'USAGE');
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }
        return new DatabaseObject(
            reader.GetString(0),
            reader.GetString(1),
            MapKind(reader.GetChar(2)),
            reader.IsDBNull(3) || reader.GetInt64(3) < 0 ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetBoolean(6));
    }

    private static async Task<IReadOnlyList<DatabaseColumn>> ReadColumnsAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.attnum,
                   a.attname,
                   format_type(a.atttypid, a.atttypmod),
                   NOT a.attnotnull,
                   pg_get_expr(d.adbin, d.adrelid),
                   a.attgenerated::text,
                   a.attidentity::text,
                   col_description(c.oid, a.attnum),
                   ARRAY(
                     SELECT enum_value.enumlabel::text
                     FROM pg_catalog.pg_enum AS enum_value
                     WHERE enum_value.enumtypid = a.atttypid
                     ORDER BY enum_value.enumsortorder)
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_attribute AS a ON a.attrelid = c.oid
            LEFT JOIN pg_catalog.pg_attrdef AS d
              ON d.adrelid = c.oid AND d.adnum = a.attnum
            WHERE n.nspname = @schema AND c.relname = @name
              AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var columns = new List<DatabaseColumn>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(new DatabaseColumn(
                reader.GetInt16(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) || reader.GetString(5).Length == 0
                    ? null
                    : reader.GetString(5),
                reader.IsDBNull(6) || reader.GetString(6).Length == 0
                    ? null
                    : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetFieldValue<string[]>(8)));
        }
        return columns;
    }

    private static async Task<IReadOnlyList<DatabaseConstraint>> ReadConstraintsAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT constraint_row.conname,
                   CASE constraint_row.contype
                     WHEN 'p' THEN 'PRIMARY KEY'
                     WHEN 'f' THEN 'FOREIGN KEY'
                     WHEN 'u' THEN 'UNIQUE'
                     WHEN 'c' THEN 'CHECK'
                     WHEN 'x' THEN 'EXCLUSION'
                     ELSE constraint_row.contype::text
                   END,
                   pg_get_constraintdef(constraint_row.oid, true),
                   CASE WHEN referenced_namespace.nspname IS NULL THEN NULL
                        ELSE referenced_namespace.nspname || '.' || referenced_table.relname
                   END
            FROM pg_catalog.pg_constraint AS constraint_row
            JOIN pg_catalog.pg_class AS source_table
              ON source_table.oid = constraint_row.conrelid
            JOIN pg_catalog.pg_namespace AS source_namespace
              ON source_namespace.oid = source_table.relnamespace
            LEFT JOIN pg_catalog.pg_class AS referenced_table
              ON referenced_table.oid = constraint_row.confrelid
            LEFT JOIN pg_catalog.pg_namespace AS referenced_namespace
              ON referenced_namespace.oid = referenced_table.relnamespace
            WHERE source_namespace.nspname = @schema
              AND source_table.relname = @name
            ORDER BY constraint_row.conname;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<DatabaseConstraint>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DatabaseConstraint(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return results;
    }

    private static async Task<IReadOnlyList<DatabaseIndex>> ReadIndexesAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT index_class.relname, index_data.indisunique,
                   pg_get_indexdef(index_data.indexrelid)
            FROM pg_catalog.pg_index AS index_data
            JOIN pg_catalog.pg_class AS source_table
              ON source_table.oid = index_data.indrelid
            JOIN pg_catalog.pg_namespace AS source_namespace
              ON source_namespace.oid = source_table.relnamespace
            JOIN pg_catalog.pg_class AS index_class
              ON index_class.oid = index_data.indexrelid
            WHERE source_namespace.nspname = @schema AND source_table.relname = @name
            ORDER BY index_class.relname;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<DatabaseIndex>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DatabaseIndex(
                reader.GetString(0), reader.GetBoolean(1), reader.GetString(2)));
        }
        return results;
    }

    private static async Task<IReadOnlyList<DatabaseTrigger>> ReadTriggersAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT trigger_row.tgname,
                   CASE WHEN (trigger_row.tgtype & 2) <> 0 THEN 'BEFORE'
                        WHEN (trigger_row.tgtype & 64) <> 0 THEN 'INSTEAD OF'
                        ELSE 'AFTER' END,
                   concat_ws(', ',
                     CASE WHEN (trigger_row.tgtype & 4) <> 0 THEN 'INSERT' END,
                     CASE WHEN (trigger_row.tgtype & 8) <> 0 THEN 'DELETE' END,
                     CASE WHEN (trigger_row.tgtype & 16) <> 0 THEN 'UPDATE' END,
                     CASE WHEN (trigger_row.tgtype & 32) <> 0 THEN 'TRUNCATE' END),
                   trigger_row.tgenabled <> 'D',
                   pg_get_triggerdef(trigger_row.oid, true)
            FROM pg_catalog.pg_trigger AS trigger_row
            JOIN pg_catalog.pg_class AS source_table
              ON source_table.oid = trigger_row.tgrelid
            JOIN pg_catalog.pg_namespace AS source_namespace
              ON source_namespace.oid = source_table.relnamespace
            WHERE source_namespace.nspname = @schema
              AND source_table.relname = @name
              AND NOT trigger_row.tgisinternal
            ORDER BY trigger_row.tgname;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var results = new List<DatabaseTrigger>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new DatabaseTrigger(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4)));
        }
        return results;
    }

    private static async Task<(bool RowLevelSecurity, string? Definition)> ReadObjectOptionsAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.relrowsecurity,
                   CASE WHEN c.relkind IN ('v', 'm') THEN pg_get_viewdef(c.oid, true)
                        ELSE NULL END
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @name;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (false, null);
        }
        return (reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private static async Task<bool> CanReadAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT has_table_privilege(
              format('%I.%I', @schema, @name), 'SELECT');
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? false);
    }

    private static async Task<long?> GetEstimatedRowsAsync(
        NpgsqlConnection connection,
        string schema,
        string name,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT GREATEST(c.reltuples::bigint, 0)
            FROM pg_catalog.pg_class AS c
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relname = @name;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("name", name);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull
            ? null
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ReadDisplayValueAsync(
        NpgsqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken)
    {
        if (await reader.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            byte[] bytes => "0x" + Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 256)))
                + (bytes.Length > 256 ? $" … ({bytes.Length} bytes)" : string.Empty),
            Array array => FormatArray(array),
            DateTime dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(
                null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static string FormatArray(Array array)
    {
        var items = array.Cast<object?>().Take(101).ToArray();
        return "[" + string.Join(", ", items.Take(100).Select(item => item?.ToString() ?? "null"))
            + (items.Length > 100 ? ", …" : string.Empty) + "]";
    }

    private static int? TryReadVectorDimensions(string typeName)
    {
        var open = typeName.IndexOf('(');
        var close = typeName.IndexOf(')', open + 1);
        return open >= 0 && close > open
            && int.TryParse(typeName.AsSpan(open + 1, close - open - 1), out var dimensions)
                ? dimensions
                : null;
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Contains('\0'))
        {
            throw new ArgumentException("Database identifiers must not be empty.", nameof(identifier));
        }
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static DatabaseObjectKind MapKind(char kind) => kind switch
    {
        'r' or 'p' => DatabaseObjectKind.Table,
        'v' => DatabaseObjectKind.View,
        'm' => DatabaseObjectKind.MaterializedView,
        'f' => DatabaseObjectKind.ForeignTable,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string GetSafeConnectionError(Exception exception) =>
        exception switch
        {
            NpgsqlException => "Could not connect. Check the host, port, database, credentials, SSL settings, network, and role permissions.",
            ArgumentException or FormatException => "Connection settings are invalid. Check the connection fields and SSL mode.",
            _ => "Connection failed. Check the connection settings and try again.",
        };

    private static async Task<T> ExecuteWithDiagnosticsAsync<T>(
        string operation,
        Func<Task<T>> execute)
    {
        try
        {
            return await execute().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PostgresException exception)
        {
            throw new DatabaseProviderException(operation, exception.SqlState, exception);
        }
        catch (NpgsqlException exception)
        {
            throw new DatabaseProviderException(operation, "database/network error", exception);
        }
        catch (DatabaseProviderException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DatabaseProviderException(
                operation,
                exception.GetType().Name,
                exception);
        }
    }
}
