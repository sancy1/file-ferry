using FluentAssertions;
using NSubstitute;
using SchemaLence.Core.Connections;
using SchemaLence.Core.Metadata;
using SchemaLence.Core.Providers;
using SchemaLence.Core.Storage;
using SchemaLence.Platform.Storage;
using FileHarvester.App.ViewModels;

namespace FileHarvester.Tests.SchemaLence;

public sealed class SchemaLenceConnectionFlowTests
{
    private static readonly string[] ExampleColumnNames = { "id", "city_name" };
    private static readonly string?[] ExampleGridCells = { "7", "Nairobi" };
    private static readonly IReadOnlyList<string?>[] ExamplePageRows =
    {
        new string?[] { "7", "Nairobi" },
    };

    [Fact]
    public async Task ConnectingSavedProfileLoadsNamespacesAndObjectsOnce()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                new[] { new DatabaseNamespace("public", null) }));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                new[] { CreateObject() }));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;

        await viewModel.ConnectSelectedAsync();

        viewModel.IsConnected.Should().BeTrue();
        viewModel.Namespaces.Should().Contain("public");
        viewModel.Objects.Should().ContainSingle()
            .Which.QualifiedName.Should().Be("public.weather");
        viewModel.StatusText.Should().Contain("Refreshed 1 accessible database object");
        await provider.Received(1).ListObjectsAsync(
            profile, "secret", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectionWithNoVisibleObjectsShowsAnExplicitEmptyState()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                Array.Empty<DatabaseObject>()));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;

        await viewModel.ConnectSelectedAsync();

        viewModel.StatusText.Should().Contain("no accessible tables or views");
        viewModel.Objects.Should().BeEmpty();
        await provider.Received(1).ListObjectsAsync(
            profile, "secret", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectionSurfacesSafePostgresDiagnosticWhenObjectListingFails()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<DatabaseObject>>>(_ =>
                throw new DatabaseProviderException(
                    "Object listing", "42501", new InvalidOperationException()));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;

        await viewModel.ConnectSelectedAsync();

        viewModel.StatusText.Should().Contain("Object listing failed (42501)");
        viewModel.StatusText.Should().NotContain("secret");
    }

    [Fact]
    public async Task SelectingObjectShowsSafePostgresMetadataDiagnostic()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                new[] { CreateObject() }));
        provider.GetStructureAsync(
                profile, "secret", "public", "weather", Arg.Any<CancellationToken>())
            .Returns<Task<DatabaseObjectStructure>>(_ =>
                throw new DatabaseProviderException(
                    "Column metadata", "42501", new InvalidOperationException()));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;
        await viewModel.ConnectSelectedAsync();
        var statusChanged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.StatusText)
                && viewModel.StatusText.Contains("Column metadata failed", StringComparison.Ordinal))
            {
                statusChanged.TrySetResult();
            }
        };

        viewModel.SelectedObjectRow = viewModel.Objects.Single();
        await statusChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Contain("Column metadata failed (42501)");
        viewModel.StatusText.Should().NotContain("secret");
    }

    [Fact]
    public async Task SelectingObjectShowsUnexpectedProviderExceptionType()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                new[] { CreateObject() }));
        provider.GetStructureAsync(
                profile, "secret", "public", "weather", Arg.Any<CancellationToken>())
            .Returns<Task<DatabaseObjectStructure>>(_ =>
                throw new InvalidOperationException("sensitive connection details"));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;
        await viewModel.ConnectSelectedAsync();
        var statusChanged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.StatusText)
                && viewModel.StatusText.Contains("InvalidOperationException", StringComparison.Ordinal))
            {
                statusChanged.TrySetResult();
            }
        };

        viewModel.SelectedObjectRow = viewModel.Objects.Single();
        await statusChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Contain("InvalidOperationException");
        viewModel.StatusText.Should().NotContain("sensitive connection details");
        viewModel.StatusText.Should().NotContain("secret");
    }

    [Fact]
    public async Task SelectedObjectReportIncludesSampleRowAndCountCommandGetsExactCount()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                new[] { CreateObject() }));
        provider.GetStructureAsync(
                profile, "secret", "public", "weather", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DatabaseObjectStructure(
                CreateObject(),
                new[]
                {
                    new DatabaseColumn(1, "id", "integer", false, null, null, null, null),
                    new DatabaseColumn(2, "city_name", "text", false, null, null, null, null),
                },
                Array.Empty<DatabaseConstraint>(),
                Array.Empty<DatabaseIndex>(),
                Array.Empty<DatabaseTrigger>(),
                Array.Empty<VectorColumnInfo>(),
                false,
                null,
                "test metadata")));
        provider.GetPageAsync(
                profile, "secret", "public", "weather", 0, 100, null, false,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DatabasePage(
                ExampleColumnNames,
                ExamplePageRows,
                null,
                0,
                100)));
        provider.CountRowsAsync(
                profile, "secret", "public", "weather", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(12L));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;
        await viewModel.ConnectSelectedAsync();
        var pageLoaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.Page) && viewModel.Page is not null)
            {
                pageLoaded.TrySetResult();
            }
        };

        viewModel.SelectedObjectRow = viewModel.Objects.Single();
        await pageLoaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Rows.Should().ContainSingle();
        viewModel.Rows[0][0].Should().Be("7");
        viewModel.Rows[0][1].Should().Be("Nairobi");
        viewModel.Rows[0][2].Should().BeNull();
        viewModel.StructureReport.Should().Contain("\"city_name\": \"Nairobi\"");
        viewModel.StructureReport.Should().Contain("Exact row count visible to this role: Not counted");

        viewModel.CountRowsCommand.Execute(null);
        await ((AsyncRelayCommand<object>)viewModel.CountRowsCommand).ExecutionTask;

        viewModel.ExactRowCount.Should().Be(12);
        viewModel.StructureReport.Should().Contain("Exact row count visible to this role: 12");
    }

    [Fact]
    public void DataRowIndexerSupportsGridColumnBindings()
    {
        var row = new SchemaLenceDataRowViewModel(ExampleGridCells);

        row[0].Should().Be("7");
        row[1].Should().Be("Nairobi");
        row[-1].Should().BeNull();
        row[2].Should().BeNull();
    }

    [Fact]
    public async Task DataPageFailureRemainsVisibleAfterObjectSelectionCompletes()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                new[] { CreateObject() }));
        provider.GetStructureAsync(
                profile, "secret", "public", "weather", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateStructure()));
        provider.GetPageAsync(
                profile, "secret", "public", "weather", 0, 100, null, false,
                Arg.Any<CancellationToken>())
            .Returns<Task<DatabasePage>>(_ =>
                throw new DatabaseProviderException(
                    "Data page", "42501", new InvalidOperationException()));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;
        await viewModel.ConnectSelectedAsync();
        var statusChanged = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.StatusText)
                && viewModel.StatusText.Contains("Data page failed", StringComparison.Ordinal))
            {
                statusChanged.TrySetResult();
            }
        };

        viewModel.SelectedObjectRow = viewModel.Objects.Single();
        await statusChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StatusText.Should().Contain("Data page failed (42501)");
        viewModel.Structure.Should().NotBeNull();
        viewModel.Page.Should().BeNull();
        viewModel.PageStatus.Should().Contain("Data preview unavailable");
        viewModel.IsDataPreviewUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task ObjectPreviewCommandLoadsStructureAndRows()
    {
        var profile = CreateProfile();
        var localStore = Substitute.For<ISchemaLenceLocalStore>();
        var credentials = Substitute.For<ISchemaLenceCredentialStore>();
        var provider = Substitute.For<IDatabaseProvider>();
        ConfigureConnection(localStore, credentials, provider, profile);
        provider.ListNamespacesAsync(profile, "secret", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseNamespace>>(
                Array.Empty<DatabaseNamespace>()));
        provider.ListObjectsAsync(
                profile, "secret", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseObject>>(
                new[] { CreateObject() }));
        provider.GetStructureAsync(
                profile, "secret", "public", "weather", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateStructure()));
        provider.GetPageAsync(
                profile, "secret", "public", "weather", 0, 100, null, false,
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DatabasePage(
                ExampleColumnNames,
                ExamplePageRows,
                null,
                0,
                100)));

        var viewModel = new SchemaLenceViewModel(
            localStore,
            credentials,
            new[] { provider });
        await viewModel.InitializeAsync();
        viewModel.SelectedConnection = profile;
        await viewModel.ConnectSelectedAsync();
        var selectionFinished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(viewModel.StatusText)
                && viewModel.StatusText.StartsWith("Selected public.weather.", StringComparison.Ordinal))
            {
                selectionFinished.TrySetResult();
            }
        };

        viewModel.VisibleObjects.Single().SelectCommand.Execute(null);
        await selectionFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Structure.Should().NotBeNull();
        viewModel.Rows.Should().ContainSingle();
        viewModel.Rows[0][1].Should().Be("Nairobi");
        await provider.Received(1).GetPageAsync(
            profile, "secret", "public", "weather", 0, 100, null, false,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1200L, "~1,200")]
    [InlineData(-33L, "Estimate unavailable")]
    [InlineData(null, "Estimate unavailable")]
    public void RowEstimateNeverDisplaysNegativeStatistics(long? estimate, string expected)
    {
        var item = new DatabaseObject(
            "public", "weather", DatabaseObjectKind.Table, estimate, null, null);

        var row = new SchemaLenceObjectRowViewModel(item, _ => Task.CompletedTask);

        row.RowEstimate.Should().Be(expected);
    }

    private static void ConfigureConnection(
        ISchemaLenceLocalStore localStore,
        ISchemaLenceCredentialStore credentials,
        IDatabaseProvider provider,
        DatabaseConnectionProfile profile)
    {
        localStore.GetConnectionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<DatabaseConnectionProfile>>(
                new[] { profile }));
        credentials.GetPasswordAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("secret"));
        provider.ProviderId.Returns("postgresql");
        provider.TestConnectionAsync(
                Arg.Any<DatabaseConnectionInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ConnectionTestResult(
                true, "PostgreSQL 17", profile.Database, "Connection succeeded.")));
    }

    private static DatabaseConnectionProfile CreateProfile() =>
        new("profile-id", "Weather App", "postgresql", "localhost", 5432,
            "neondb", "reader", "Require");

    private static DatabaseObject CreateObject() =>
        new("public", "weather", DatabaseObjectKind.Table, 4, 1024, null, true);

    private static DatabaseObjectStructure CreateStructure() =>
        new(
            CreateObject(),
            Array.Empty<DatabaseColumn>(),
            Array.Empty<DatabaseConstraint>(),
            Array.Empty<DatabaseIndex>(),
            Array.Empty<DatabaseTrigger>(),
            Array.Empty<VectorColumnInfo>(),
            false,
            null,
            "test metadata");
}
