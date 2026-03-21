using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

[Collection("TestFixture")]
public class CustomTypeReturnTests
{
    [Fact]
    public void Describe_SelectWithWholeCustomTypeColumn_ReturnsTypeInfo()
    {
        // Using GetName/GetDataTypeName instead of GetColumnSchema avoids .NET type mapping,
        // so custom composite type columns are described successfully.
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "SELECT id, data FROM sql_file_custom_table WHERE id = $1";
        int paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse(result.Error ?? "");
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(2);
        result.Columns[0].Name.Should().Be("id");
        result.Columns[0].DataTypeName.Should().Be("integer");
        result.Columns[1].Name.Should().Be("data");
        // Custom type — verify the type name and OID are captured
        result.Columns[1].DataTypeName.Should().NotBeNullOrEmpty();
        result.Columns[1].TypeOid.Should().BeGreaterThan(0u);
    }

    [Fact]
    public void Describe_CustomTypeColumn_ResolvesViaCompositeTypeCache()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        // Initialize composite type cache
        CompositeTypeCache.Initialize(conn, s => s);

        var sql = "SELECT id, data FROM sql_file_custom_table WHERE id = $1";
        int paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse(result.Error ?? "");

        // The data column should have composite type info
        var dataTypeName = result.Columns![1].DataTypeName;

        // Create a TypeDescriptor and resolve it
        var descriptor = new TypeDescriptor(dataTypeName);
        bool resolved = CompositeTypeCache.ResolveTypeDescriptor(descriptor);

        // The composite type should be resolved from the cache
        resolved.Should().BeTrue($"Type name '{dataTypeName}' should resolve from CompositeTypeCache");
        descriptor.CompositeFieldNames.Should().NotBeNull();
        descriptor.CompositeFieldNames!.Length.Should().Be(3); // val1, val2, val3
    }

    [Fact]
    public void Describe_SelectCustomTypeFields_ReturnsScalarColumns()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "SELECT id, (data).val1, (data).val2, (data).val3 FROM sql_file_custom_table WHERE id = $1";
        int paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse(result.Error ?? "");
        result.Columns.Should().NotBeNull();
        result.Columns!.Length.Should().Be(4);
        result.Columns[0].Name.Should().Be("id");
        result.Columns[1].Name.Should().Be("val1");
        result.Columns[2].Name.Should().Be("val2");
        result.Columns[3].Name.Should().Be("val3");
    }

    [Fact]
    public void Describe_ArrayOfCustomType_ReturnsTypeInfo()
    {
        using var conn = Database.CreateConnection();
        conn.Open();

        var sql = "SELECT id, items FROM sql_file_custom_array_table WHERE id = $1";
        int paramCount = SqlFileDescriber.FindMaxParamIndex(sql);
        var result = SqlFileDescriber.Describe(conn, sql, paramCount);

        result.HasError.Should().BeFalse(result.Error ?? "");
        result.Columns!.Length.Should().Be(2);

        var itemsType = result.Columns[1].DataTypeName;
        var itemsOid = result.Columns[1].TypeOid;

        // Array of custom type — type name should be resolved and end with []
        itemsType.Should().NotBe("-.-", $"OID {itemsOid} should resolve to a type name");
    }
}
