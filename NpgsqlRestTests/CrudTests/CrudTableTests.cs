namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CrudTableTests()
    {
        script.Append("""
        create table crud_table1 (
            id int primary key,
            name text
        );

        create table crud_table1_nokeys (
            id int,
            name text
        );
        """);
    }
}

[Collection("TestFixture")]
public class CrudTableTests(TestFixture test)
{
    [Fact]
    public async Task Test_crud_table1()
    {
        using var select = await test.Client.GetAsync("/api/crud-table1/?id=1");
        select.StatusCode.Should().Be(HttpStatusCode.OK);

        using var updateBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var update = await test.Client.PostAsync("/api/crud-table1/", updateBody);
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var updateReturningBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var updateReturning = await test.Client.PostAsync("/api/crud-table1/returning/", updateReturningBody);
        updateReturning.StatusCode.Should().Be(HttpStatusCode.OK);

        using var delete = await test.Client.DeleteAsync("/api/crud-table1/?id=1");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var deleteReturning = await test.Client.DeleteAsync("/api/crud-table1/returning/?id=1");
        deleteReturning.StatusCode.Should().Be(HttpStatusCode.OK);

        using var insertBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insert = await test.Client.PutAsync("/api/crud-table1/", insertBody);
        insert.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var insertReturningBody = new StringContent("{\"id\":2,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertReturning = await test.Client.PutAsync("/api/crud-table1/returning/", insertReturningBody);
        insertReturning.StatusCode.Should().Be(HttpStatusCode.OK);

        using var insertOnConflictDoNothingBody = new StringContent("{\"id\":3,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoNothing = await test.Client.PutAsync("/api/crud-table1/on-conflict-do-nothing/", insertOnConflictDoNothingBody);
        insertOnConflictDoNothing.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var insertOnConflictDoNothingReturningBody = new StringContent("{\"id\":4,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoNothingReturning = await test.Client.PutAsync("/api/crud-table1/on-conflict-do-nothing/returning/", insertOnConflictDoNothingReturningBody);
        insertOnConflictDoNothingReturning.StatusCode.Should().Be(HttpStatusCode.OK);

        using var insertOnConflictDoUpdateBody = new StringContent("{\"id\":5,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoUpdate = await test.Client.PutAsync("/api/crud-table1/on-conflict-do-update/", insertOnConflictDoUpdateBody);
        insertOnConflictDoUpdate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var insertOnConflictDoUpdateReturningBody = new StringContent("{\"id\":6,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoUpdateReturning = await test.Client.PutAsync("/api/crud-table1/on-conflict-do-update/returning/", insertOnConflictDoUpdateReturningBody);
        insertOnConflictDoUpdateReturning.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_crud_table1_nokeys()
    {
        using var select = await test.Client.GetAsync("/api/crud-table1-nokeys/?id=1");
        select.StatusCode.Should().Be(HttpStatusCode.OK);

        using var updateBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var update = await test.Client.PostAsync("/api/crud-table1-nokeys/", updateBody);
        update.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        using var updateReturningBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var updateReturning = await test.Client.PostAsync("/api/crud-table1-nokeys/returning/", updateReturningBody);
        updateReturning.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        using var delete = await test.Client.DeleteAsync("/api/crud-table1-nokeys/?id=1");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var deleteReturning = await test.Client.DeleteAsync("/api/crud-table1-nokeys/returning/?id=1");
        deleteReturning.StatusCode.Should().Be(HttpStatusCode.OK);

        using var insertBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insert = await test.Client.PutAsync("/api/crud-table1-nokeys/", insertBody);
        insert.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var insertReturningBody = new StringContent("{\"id\":2,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertReturning = await test.Client.PutAsync("/api/crud-table1-nokeys/returning/", insertReturningBody);
        insertReturning.StatusCode.Should().Be(HttpStatusCode.OK);

        using var insertOnConflictDoNothingBody = new StringContent("{\"id\":3,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoNothing = await test.Client.PutAsync("/api/crud-table1-nokeys/on-conflict-do-nothing/", insertOnConflictDoNothingBody);
        insertOnConflictDoNothing.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var insertOnConflictDoNothingReturningBody = new StringContent("{\"id\":4,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoNothingReturning = await test.Client.PutAsync("/api/crud-table1-nokeys/on-conflict-do-nothing/returning/", insertOnConflictDoNothingReturningBody);
        insertOnConflictDoNothingReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var insertOnConflictDoUpdateBody = new StringContent("{\"id\":5,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoUpdate = await test.Client.PutAsync("/api/crud-table1-nokeys/on-conflict-do-update/", insertOnConflictDoUpdateBody);
        insertOnConflictDoUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var insertOnConflictDoUpdateReturningBody = new StringContent("{\"id\":6,\"name\":\"some name\"}", Encoding.UTF8, "application/json");
        using var insertOnConflictDoUpdateReturning = await test.Client.PutAsync("/api/crud-table1-nokeys/on-conflict-do-update/returning/", insertOnConflictDoUpdateReturningBody);
        insertOnConflictDoUpdateReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}