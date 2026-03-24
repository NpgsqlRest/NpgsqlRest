using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void VirtualParamCustomTests()
    {
        // Virtual param used as custom parameter placeholder
        // format is passed via HTTP query string, substituted into table_format annotation
        File.WriteAllText(Path.Combine(Dir, "virtual_format.sql"), """
            -- @define_param format text
            -- table_format = {format}
            -- @param $1 department_id
            select id, name from sql_describe_test where id = $1;
            """);
    }
}

[Collection("SqlFileSourceFixture")]
public class VirtualParamCustomTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task VirtualParam_AsCustomPlaceholder_PassedViaQueryString()
    {
        // format=json passed in query string, feeds into table_format = {format}
        // Without a registered table_format handler for "json", it falls through to default JSON rendering
        using var response = await test.Client.GetAsync("/api/virtual-format?department_id=1&format=json");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Response: {await response.Content.ReadAsStringAsync()}");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test1");
    }

    [Fact]
    public async Task VirtualParam_WithDifferentFormatValue()
    {
        // format=excel passed — virtual param value is available for custom parameter substitution
        using var response = await test.Client.GetAsync("/api/virtual-format?department_id=1&format=excel");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Response: {await response.Content.ReadAsStringAsync()}");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test1");
    }
}
