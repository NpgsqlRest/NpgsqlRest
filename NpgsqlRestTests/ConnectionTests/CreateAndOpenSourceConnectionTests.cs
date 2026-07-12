using Npgsql;
using System.Data;

namespace NpgsqlRestTests;

public class CreateAndOpenSourceConnectionTests : IDisposable
{
    private static readonly string TestConnectionString = Database.GetIinitialConnectionString();
    private const string TestSchema = "test_schema";
    private readonly List<NpgsqlConnection> _connectionsToDispose = [];

    public CreateAndOpenSourceConnectionTests()
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
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void NamedConnectionString_WithSchema_CreatesConnectionWithSchema()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test",
            MetadataQuerySchema = TestSchema,
            ConnectionStrings = new Dictionary<string, string>
            {
                { "test", TestConnectionString }
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
        connection.ConnectionString.Should().Contain($"Search Path={TestSchema}");

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void NamedConnectionString_WithoutSchema_CreatesConnection()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test",
            ConnectionStrings = new Dictionary<string, string>
            {
                { "test", TestConnectionString }
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
    public void PerSourceConnectionName_OverridesMetadataQueryConnectionName()
    {
        // Arrange: MetadataQueryConnectionName points at a broken entry; the source-level name must win.
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "broken",
            ConnectionStrings = new Dictionary<string, string>
            {
                { "broken", "Host=host.invalid;Database=x;Username=x;Password=x" },
                { "sourceconn", TestConnectionString }
            }
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose, "TestSource", "sourceconn");

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void PerSourceConnectionName_Unknown_ThrowsArgumentException()
    {
        // Arrange: a misconfigured source connection must fail startup, not silently discover
        // from the wrong database.
        var options = new NpgsqlRestOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                { "test", TestConnectionString }
            }
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        var act = () => options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose, "TestSource", "nonexistent");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'nonexistent' not found in DataSources or ConnectionStrings*");
    }

    [Fact]
    public void NamedConnectionString_NotFound_ThrowsArgumentException()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "nonexistent",
            ConnectionStrings = new Dictionary<string, string>
            {
                { "test", TestConnectionString }
            }
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act & Assert
        var act = () => options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*'nonexistent' not found*");
    }

    [Fact]
    public void NamedConnectionString_NoConnectionStrings_ThrowsArgumentException()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test"
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act & Assert
        var act = () => options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ConnectionStrings or DataSources must be provided*");
    }

    [Fact]
    public void ServiceProviderMode_WithoutServiceProvider_ThrowsArgumentException()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            ServiceProviderMode = ServiceProviderObject.NpgsqlDataSource,
            ConnectionString = TestConnectionString
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act & Assert
        var act = () => options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ServiceProvider must be provided*");
    }

    [Fact]
    public void ConnectionString_WithSchema_CreatesConnectionWithSchema()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            ConnectionString = TestConnectionString,
            MetadataQuerySchema = TestSchema
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();
        connection.ConnectionString.Should().Contain($"Search Path={TestSchema}");

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void ConnectionString_WithoutSchema_CreatesConnection()
    {
        // Arrange
        var options = new NpgsqlRestOptions
        {
            ConnectionString = TestConnectionString
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
    public void NoConnectionString_ThrowsArgumentException()
    {
        // Arrange
        var options = new NpgsqlRestOptions();
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act & Assert
        var act = () => options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ConnectionString must be provided*");
    }

    [Fact]
    public void ConnectionString_WithSchemaAlreadyInConnectionString_DoesNotDuplicateSchema()
    {
        // Arrange
        var connectionStringWithSchema = $"{TestConnectionString};Search Path={TestSchema}";
        var options = new NpgsqlRestOptions
        {
            ConnectionString = connectionStringWithSchema,
            MetadataQuerySchema = TestSchema
        };
        NpgsqlConnection? connection = null;
        bool shouldDispose = false;

        // Act
        options.CreateAndOpenSourceConnection(null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();

        // Verify schema is not duplicated
        var searchPathCount = connection.ConnectionString.Split("Search Path", StringSplitOptions.None).Length - 1;
        searchPathCount.Should().Be(1, "Search Path should appear only once");

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void NamedConnectionString_WithSchemaAlreadyInConnectionString_DoesNotDuplicateSchema()
    {
        // Arrange
        var connectionStringWithSchema = $"{TestConnectionString};Search Path={TestSchema}";
        var options = new NpgsqlRestOptions
        {
            MetadataQueryConnectionName = "test",
            MetadataQuerySchema = TestSchema,
            ConnectionStrings = new Dictionary<string, string>
            {
                { "test", connectionStringWithSchema }
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

        // Verify schema is not duplicated
        var searchPathCount = connection.ConnectionString.Split("Search Path", StringSplitOptions.None).Length - 1;
        searchPathCount.Should().Be(1, "Search Path should appear only once");

        _connectionsToDispose.Add(connection);
    }

    [Fact]
    public void HasSearchPathInConnectionString_FindsExistingSearchPath()
    {
        // Arrange
        var connectionStringWithSchema = $"{TestConnectionString};Search Path={TestSchema}";

        // Act - using reflection to access private method
        var method = typeof(Ext).GetMethod("HasSearchPathInConnectionString",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (bool)method!.Invoke(null, [connectionStringWithSchema, TestSchema])!;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasSearchPathInConnectionString_CaseInsensitive()
    {
        // Arrange
        var connectionStringWithSchema = $"{TestConnectionString};search path={TestSchema.ToUpper()}";

        // Act - using reflection to access private method
        var method = typeof(Ext).GetMethod("HasSearchPathInConnectionString",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (bool)method!.Invoke(null, [connectionStringWithSchema, TestSchema])!;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void HasSearchPathInConnectionString_ReturnsFalseWhenNotFound()
    {
        // Arrange
        var connectionString = TestConnectionString;

        // Act - using reflection to access private method
        var method = typeof(Ext).GetMethod("HasSearchPathInConnectionString",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (bool)method!.Invoke(null, [connectionString, TestSchema])!;

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void HasSearchPathInConnectionString_ReturnsFalseForDifferentSchema()
    {
        // Arrange
        var connectionStringWithSchema = $"{TestConnectionString};Search Path=different_schema";

        // Act - using reflection to access private method
        var method = typeof(Ext).GetMethod("HasSearchPathInConnectionString",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var result = (bool)method!.Invoke(null, [connectionStringWithSchema, TestSchema])!;

        // Assert
        result.Should().BeFalse();
    }
}
