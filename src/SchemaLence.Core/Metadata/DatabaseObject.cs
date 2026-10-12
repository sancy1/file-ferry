namespace SchemaLence.Core.Metadata;

public enum DatabaseObjectKind
{
    Table,
    View,
    MaterializedView,
    ForeignTable,
    Collection,
}

public sealed record DatabaseNamespace(string Name, string? Description);

public sealed record DatabaseObject(
    string Namespace,
    string Name,
    DatabaseObjectKind Kind,
    long? EstimatedRows,
    long? SizeBytes,
    string? Description,
    bool CanReadData = true);

public sealed record DatabaseColumn(
    int Ordinal,
    string Name,
    string DataType,
    bool IsNullable,
    string? DefaultExpression,
    string? GeneratedKind,
    string? IdentityKind,
    string? Description,
    IReadOnlyList<string>? AllowedValues = null);

public sealed record DatabaseConstraint(
    string Name,
    string Kind,
    string Definition,
    string? ReferencedObject = null);

public sealed record DatabaseIndex(
    string Name,
    bool IsUnique,
    string Definition);

public sealed record DatabaseTrigger(
    string Name,
    string Timing,
    string Events,
    bool IsEnabled,
    string Definition);

public sealed record VectorColumnInfo(
    string ColumnName,
    string TypeName,
    int? Dimensions);

public sealed record DatabaseObjectStructure(
    DatabaseObject DatabaseItem,
    IReadOnlyList<DatabaseColumn> Columns,
    IReadOnlyList<DatabaseConstraint> Constraints,
    IReadOnlyList<DatabaseIndex> Indexes,
    IReadOnlyList<DatabaseTrigger> Triggers,
    IReadOnlyList<VectorColumnInfo> VectorColumns,
    bool RowLevelSecurityEnabled,
    string? Definition,
    string MetadataNote);

public sealed record DatabasePage(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    long? EstimatedTotalRows,
    int PageIndex,
    int PageSize);

public sealed record ProviderCapabilities(
    bool SupportsSchemas,
    bool SupportsTableStructure,
    bool SupportsPagedData,
    bool SupportsRelationships,
    bool SupportsVectorMetadata,
    bool SupportsNativeBackup,
    IReadOnlyList<string> ExportFormats);
