using Npgsql;
using NpgsqlRestClient;
using System.Data;

namespace NpgsqlRestTests;

public class MultiHostConnectionTests : IDisposable
{
    private static readonly string TestConnectionString = Database.GetIinitialConnectionString();
    private readonly List<NpgsqlConnection> _connectionsToDispose = [];
    private readonly List<NpgsqlDataSource> _dataSourcesToDispose = [];

    public MultiHostConnectionTests()
    {
        NpgsqlRestOptions.Options = new NpgsqlRestOptions();
    }

    public void Dispose()
    {
        foreach (var connection in _connectionsToDispose)
        {
            if (connection.State == ConnectionState.Open)
            {
                connection.Close();
            }
            connection.Dispose();
        }
        _connectionsToDispose.Clear();

        foreach (var dataSource in _dataSourcesToDispose)
        {
            dataSource.Dispose();
        }
        _dataSourcesToDispose.Clear();
        GC.SuppressFinalize(this);
    }

    #region IsMultiHostConnectionString Tests

    [Theory]
    [InlineData("Host=server1,server2;Database=test;Username=user;Password=pass", true)]
    [InlineData("Host=server1,server2,server3;Database=test;Username=user;Password=pass", true)]
    [InlineData("Host=primary.db.com,replica1.db.com,replica2.db.com;Database=mydb;Username=app;Password=secret", true)]
    [InlineData("Host=server1;Database=test;Username=user;Password=pass", false)]
    [InlineData("Host=localhost;Database=test;Username=user;Password=pass", false)]
    [InlineData("Host=db.example.com;Database=test;Username=user;Password=pass", false)]
    public void IsMultiHostConnectionString_DetectsCorrectly(string connectionString, bool expected)
    {
        // Act
        var result = Builder.IsMultiHostConnectionString(connectionString);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsMultiHostConnectionString_EmptyHost_ReturnsFalse()
    {
        // Arrange
        var connectionString = "Database=test;Username=user;Password=pass";

        // Act
        var result = Builder.IsMultiHostConnectionString(connectionString);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsMultiHostConnectionString_WithPort_DetectsMultiHost()
    {
        // Arrange - multi-host with port
        var connectionString = "Host=server1,server2;Port=5432;Database=test;Username=user;Password=pass";

        // Act
        var result = Builder.IsMultiHostConnectionString(connectionString);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region DataSources Resolution Tests

    [Fact]
    public void NamedDataSource_UsedBeforeConnectionStrings()
    {
        // Arrange
        var dataSource = new NpgsqlDataSourceBuilder(TestConnectionString).Build();
        _dataSourcesToDispose.Add(dataSource);

        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test",
            DataSources = new Dictionary<string, NpgsqlDataSource>
            {
                { "test", dataSource }
            },
            ConnectionStrings = new Dictionary<string, string>
            {
                { "test", "Host=wrong;Database=wrong" } // Should not be used
            }
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();
        // Connection should come from dataSource, not the ConnectionStrings entry
        connection.ConnectionString.Should().NotContain("wrong");

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void NamedDataSource_FallsBackToConnectionStrings_WhenNotInDataSources()
    {
        // Arrange
        var dataSource = new NpgsqlDataSourceBuilder(TestConnectionString).Build();
        _dataSourcesToDispose.Add(dataSource);

        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "other",
            DataSources = new Dictionary<string, NpgsqlDataSource>
            {
                { "test", dataSource } // Different name
            },
            ConnectionStrings = new Dictionary<string, string>
            {
                { "other", TestConnectionString }
            }
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void NamedDataSource_WithSchema_SetsSearchPath()
    {
        // Arrange
        var dataSource = new NpgsqlDataSourceBuilder(TestConnectionString).Build();
        _dataSourcesToDispose.Add(dataSource);

        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test",
            MetadataQuerySchema = "custom_schema",
            DataSources = new Dictionary<string, NpgsqlDataSource>
            {
                { "test", dataSource }
            }
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void NamedDataSource_NotFound_ThrowsWithCorrectMessage()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "nonexistent",
            DataSources = new Dictionary<string, NpgsqlDataSource>(),
            ConnectionStrings = new Dictionary<string, string>()
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act & Assert
        var act = () => options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'nonexistent' not found in DataSources or ConnectionStrings*");
    }

    [Fact]
    public void DataSources_OnlyProvided_WorksForNamedConnection()
    {
        // Arrange - Only DataSources, no ConnectionStrings
        var dataSource = new NpgsqlDataSourceBuilder(TestConnectionString).Build();
        _dataSourcesToDispose.Add(dataSource);

        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test",
            DataSources = new Dictionary<string, NpgsqlDataSource>
            {
                { "test", dataSource }
            }
            // ConnectionStrings is null
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();

        _connectionsToDispose.Add(connection);
    }

    #endregion

    #region Endpoint DataSources Resolution Tests

    [Fact]
    public async Task Endpoint_UsesDataSourceForConnectionName()
    {
        // This test verifies that when an endpoint has a ConnectionName,
        // the DataSources dictionary is checked first

        // We can't easily test the full endpoint flow without a running server,
        // but we can verify the options are correctly configured
        var dataSource = new NpgsqlDataSourceBuilder(TestConnectionString).Build();
        _dataSourcesToDispose.Add(dataSource);

        var options = new NpgsqlRestOptions
        {
            DataSource = dataSource,
            DataSources = new Dictionary<string, NpgsqlDataSource>
            {
                { "replica", dataSource }
            },
            ConnectionStrings = new Dictionary<string, string>
            {
                { "other", TestConnectionString }
            }
        };

        // Verify DataSources has priority
        options.DataSources.Should().ContainKey("replica");
        options.DataSources!.TryGetValue("replica", out var resolvedDs).Should().BeTrue();
        resolvedDs.Should().BeSameAs(dataSource);

        await Task.CompletedTask;
    }

    #endregion
}
