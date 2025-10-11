using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest.Auth;
using System.Data;

namespace NpgsqlRestTests;

public class CreateAndOpenSourceConnectionTests : IDisposable
{
    private static readonly string TestConnectionString = Database.GetIinitialConnectionString();
    private const string TestSchema = "test_schema";
    private readonly List<NpgsqlConnection> _connectionsToDispose = [];

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
        options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);

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
        options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);

        // Assert
        connection.Should().NotBeNull();
        connection!.State.Should().Be(ConnectionState.Open);
        shouldDispose.Should().BeTrue();

        _connectionsToDispose.Add(connection);
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
        var act = () => options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);
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
        var act = () => options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*ConnectionStrings must be provided*");
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
        var act = () => options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);
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
        options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);

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
        options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);

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
        var act = () => options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);
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
        options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);

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
        options.CreateAndOpenSourceConnection(null, null, ref connection, ref shouldDispose);

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
