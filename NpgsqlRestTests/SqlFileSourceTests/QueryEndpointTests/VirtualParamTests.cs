using NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.SqlFileSourceTests;

public static partial class SqlFiles
{
    public static void VirtualParamTests()
    {
        // Virtual param used for claim mapping — not in SQL, auto-filled from claims
        File.WriteAllText(Path.Combine(Dir, "virtual_claim.sql"), """
            -- @authorize
            -- @user_parameters
            -- @define_param _user_id
            select 'ok' as result;
            """);

        // Virtual param with type specified
        File.WriteAllText(Path.Combine(Dir, "virtual_typed.sql"), """
            -- @define_param extra_info text
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            """);

        // Multiple virtual params
        File.WriteAllText(Path.Combine(Dir, "virtual_multiple.sql"), """
            -- @define_param tracking_id text
            -- @define_param source text
            -- @param $1 id
            select id, name from sql_describe_test where id = $1;
            """);

    }
}

[Collection("SqlFileSourceFixture")]
public class VirtualParamTests(SqlFileSourceTestFixture test)
{
    [Fact]
    public async Task VirtualClaimParam_EndpointWorks_WithoutPassingParam()
    {
        // _user_id is virtual — not in SQL, but claim mapping fills it
        using var client = test.CreateClient();
        await client.GetAsync("/login");
        using var response = await client.GetAsync("/api/virtual-claim");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Virtual claim param should not block endpoint. Response: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task VirtualTypedParam_EndpointWorks_WithRealParam()
    {
        // Real $1 param works, virtual extra_info is ignored in SQL
        using var response = await test.Client.GetAsync("/api/virtual-typed?id=1&extra_info=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test1");
    }

    [Fact]
    public async Task VirtualMultipleParams_EndpointWorks()
    {
        using var response = await test.Client.GetAsync("/api/virtual-multiple?id=1&tracking_id=abc&source=web");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("test1");
    }

}
