using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void OverloadWithCustomType()
    {
        script.Append(@"
            create table authors_ov1 (
                author_id int not null generated always as identity primary key,
                first_name text,
                last_name text
            );

            create function get_authors_ov1(
                _author_id int
            )
            returns setof authors_ov1
            language sql
            as $$
            select author_id, first_name, last_name
            from authors_ov1
            where _author_id is null or author_id = _author_id;
            $$;
            comment on function get_authors_ov1(int) is 'HTTP GET';

            create function get_authors_ov1(
                _author authors_ov1
            )
            returns setof authors_ov1
            language sql
            as $$
            select author_id, first_name, last_name
            from authors_ov1
            where 
                (_author.author_id is null or author_id = _author.author_id)
                and (_author.first_name is null or first_name = _author.first_name)
                and (_author.last_name is null or last_name = _author.last_name);
            $$;
            comment on function get_authors_ov1(authors_ov1) is 'HTTP GET'; 
        ");
    }
}

[Collection("TestFixture")]
public class OverloadWithCustomType(TestFixture test)
{
    
    [Fact]
    public async Task Test_get_authors_ov1()
    {
        using var response = await test.Client.GetAsync($"/api/get-authors-ov1?authorId=1");
        var content = await response.Content.ReadAsStringAsync();

        response?.StatusCode.Should().Be(HttpStatusCode.OK);
        response?.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        content.Should().Be("[]");
    }
}
