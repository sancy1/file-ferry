using FluentAssertions;
using Microsoft.Data.Sqlite;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Storage;
using SchemaLence.Platform.Storage;

namespace FileHarvester.Tests.SchemaLence;

public sealed class SqliteSchemaLenceLocalStoreTests
{
    [Fact]
    public async Task SavesAndLoadsNonSecretConnectionProfileAndBackupHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "file-ferry-schema-" + Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "schema-lence.db");
        try
        {
            using var store = new SqliteSchemaLenceLocalStore(databasePath);
            var profile = new DatabaseConnectionProfile(
                "profile-1",
                "Inventory",
                "postgresql",
                "db.example.test",
                5432,
                "inventory",
                "reader",
                "VerifyFull");
            var backup = new BackupHistoryEntry(
                "backup-1",
                "Inventory",
                "inventory",
                "Database",
                "PostgreSQL archive",
                "C:\\backups\\inventory.dump",
                2048,
                DateTimeOffset.Parse(
                    "2025-01-02T03:04:05+00:00",
                    System.Globalization.CultureInfo.InvariantCulture));

            await store.SaveConnectionAsync(profile);
            await store.AddBackupHistoryAsync(backup);

            (await store.GetConnectionsAsync()).Should().ContainSingle().Which.Should().Be(profile);
            (await store.GetBackupHistoryAsync()).Should().ContainSingle().Which.Should().Be(backup);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
