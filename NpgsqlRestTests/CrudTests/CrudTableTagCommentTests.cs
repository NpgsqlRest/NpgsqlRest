namespace NpgsqlRestTests;

public static partial class Database
{
    public static void CrudTableTagCommentTests()
    {
        script.Append("""
        create table crud_commented_table (
            id int primary key,
            name text
        );

        comment on table crud_commented_table is 'This is a commented table
        for select
        HTTP GET /select_commented_table
        for update
        authorize
        for returning
        disabled
        for insert_on_conflict_do_update, insert_on_conflict_do_nothing
        disabled
        ';

        create table crud_select_only (
            id int primary key,
            name text
        );

        comment on table crud_select_only is '
        disabled
        enabled select, delete
        ';


        create table crud_commented_table_on_conflict1 (
            id int primary key,
            name text
        );
        
        comment on table crud_commented_table_on_conflict1 is 'This is a commented table
        HTTP
        disabled
        for on_conflict
        enabled
        ';
        """);
    }
}

[Collection("TestFixture")]
public class CrudTableTagCommentTests(TestFixture test)
{
    #region crud_commented_table Tests

    [Fact]
    public async Task Test_crud_commented_table_api_get()
    {
        // Prepare
        var id = 1;

        // Act
        using var select1 = await test.Client.GetAsync($"/api/crud-commented-table/?id={id}");

        // Assert
        select1.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_crud_commented_table_custom_get()
    {
        // Prepare
        var id = 1;

        // Act
        using var select2 = await test.Client.GetAsync($"/select_commented_table/?id={id}");

        // Assert
        select2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_crud_commented_table_update()
    {
        // Prepare
        using var updateBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var update = await test.Client.PostAsync("/api/crud-commented-table/", updateBody);

        // Assert
        update.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Test_crud_commented_table_update_returning()
    {
        // Prepare
        using var updateReturningBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var updateReturning = await test.Client.PostAsync("/api/crud-commented-table/returning/", updateReturningBody);

        // Assert
        updateReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_delete()
    {
        // Prepare
        var id = 1;

        // Act
        using var delete = await test.Client.DeleteAsync($"/api/crud-commented-table/?id={id}");

        // Assert
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Test_crud_commented_table_delete_returning()
    {
        // Prepare
        var id = 1;

        // Act
        using var deleteReturning = await test.Client.DeleteAsync($"/api/crud-commented-table/returning/?id={id}");

        // Assert
        deleteReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_insert()
    {
        // Prepare
        using var insertBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insert = await test.Client.PutAsync("/api/crud-commented-table/", insertBody);

        // Assert
        insert.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Test_crud_commented_table_insert_returning()
    {
        // Prepare
        using var insertReturningBody = new StringContent("{\"id\":2,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertReturning = await test.Client.PutAsync("/api/crud-commented-table/returning/", insertReturningBody);

        // Assert
        insertReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_insert_on_conflict_do_nothing()
    {
        // Prepare
        using var insertOnConflictDoNothingBody = new StringContent("{\"id\":3,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoNothing = await test.Client.PutAsync("/api/crud-commented-table/on-conflict-do-nothing/", insertOnConflictDoNothingBody);

        // Assert
        insertOnConflictDoNothing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_insert_on_conflict_do_nothing_returning()
    {
        // Prepare
        using var insertOnConflictDoNothingReturningBody = new StringContent("{\"id\":4,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoNothingReturning = await test.Client.PutAsync("/api/crud-commented-table/on-conflict-do-nothing/returning/", insertOnConflictDoNothingReturningBody);

        // Assert
        insertOnConflictDoNothingReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_insert_on_conflict_do_update()
    {
        // Prepare
        using var insertOnConflictDoUpdateBody = new StringContent("{\"id\":5,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoUpdate = await test.Client.PutAsync("/api/crud-commented-table/on-conflict-do-update/", insertOnConflictDoUpdateBody);

        // Assert
        insertOnConflictDoUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_insert_on_conflict_do_update_returning()
    {
        // Prepare
        using var insertOnConflictDoUpdateReturningBody = new StringContent("{\"id\":6,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoUpdateReturning = await test.Client.PutAsync("/api/crud-commented-table/on-conflict-do-update/returning/", insertOnConflictDoUpdateReturningBody);

        // Assert
        insertOnConflictDoUpdateReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region crud_select_only Tests

    [Fact]
    public async Task Test_crud_select_only_select()
    {
        // Prepare
        var id = 1;

        // Act
        using var select = await test.Client.GetAsync($"/api/crud-select-only/?id={id}");

        // Assert
        select.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_crud_select_only_update()
    {
        // Prepare
        using var updateBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var update = await test.Client.PostAsync("/api/crud-select-only/", updateBody);

        // Assert
        update.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_crud_select_only_update_returning()
    {
        // Prepare
        using var updateReturningBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var updateReturning = await test.Client.PostAsync("/api/crud-select-only/returning/", updateReturningBody);

        // Assert
        updateReturning.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_crud_select_only_delete()
    {
        // Prepare
        var id = 1;

        // Act
        using var delete = await test.Client.DeleteAsync($"/api/crud-select-only/?id={id}");

        // Assert
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Test_crud_select_only_delete_returning()
    {
        // Prepare
        var id = 1;

        // Act
        using var deleteReturning = await test.Client.DeleteAsync($"/api/crud-select-only/returning/?id={id}");

        // Assert
        deleteReturning.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_crud_select_only_insert()
    {
        // Prepare
        using var insertBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insert = await test.Client.PutAsync("/api/crud-select-only/", insertBody);

        // Assert
        insert.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_crud_select_only_insert_returning()
    {
        // Prepare
        using var insertReturningBody = new StringContent("{\"id\":2,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertReturning = await test.Client.PutAsync("/api/crud-select-only/returning/", insertReturningBody);

        // Assert
        insertReturning.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Test_crud_select_only_insert_on_conflict_do_nothing()
    {
        // Prepare
        using var insertOnConflictDoNothingBody = new StringContent("{\"id\":3,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoNothing = await test.Client.PutAsync("/api/crud-select-only/on-conflict-do-nothing/", insertOnConflictDoNothingBody);

        // Assert
        insertOnConflictDoNothing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_select_only_insert_on_conflict_do_nothing_returning()
    {
        // Prepare
        using var insertOnConflictDoNothingReturningBody = new StringContent("{\"id\":4,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoNothingReturning = await test.Client.PutAsync("/api/crud-select-only/on-conflict-do-nothing/returning/", insertOnConflictDoNothingReturningBody);

        // Assert
        insertOnConflictDoNothingReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_select_only_insert_on_conflict_do_update()
    {
        // Prepare
        using var insertOnConflictDoUpdateBody = new StringContent("{\"id\":5,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoUpdate = await test.Client.PutAsync("/api/crud-select-only/on-conflict-do-update/", insertOnConflictDoUpdateBody);

        // Assert
        insertOnConflictDoUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_select_only_insert_on_conflict_do_update_returning()
    {
        // Prepare
        using var insertOnConflictDoUpdateReturningBody = new StringContent("{\"id\":6,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoUpdateReturning = await test.Client.PutAsync("/api/crud-select-only/on-conflict-do-update/returning/", insertOnConflictDoUpdateReturningBody);

        // Assert
        insertOnConflictDoUpdateReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region crud_commented_table_on_conflict1 Tests

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_select()
    {
        // Prepare
        var id = 1;

        // Act
        using var select = await test.Client.GetAsync($"/crud-commented-table-on-conflict1/?id={id}");

        // Assert
        select.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_update()
    {
        // Prepare
        using var updateBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var update = await test.Client.PostAsync("/crud-commented-table-on-conflict1/", updateBody);

        // Assert
        update.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_update_returning()
    {
        // Prepare
        using var updateReturningBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var updateReturning = await test.Client.PostAsync("/crud-commented-table-on-conflict1/", updateReturningBody);

        // Assert
        updateReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_delete()
    {
        // Prepare
        var id = 1;

        // Act
        using var delete = await test.Client.DeleteAsync($"/crud-commented-table-on-conflict1/?id={id}");

        // Assert
        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_delete_returning()
    {
        // Prepare
        var id = 1;

        // Act
        using var deleteReturning = await test.Client.DeleteAsync($"/api/crud-commented-table-on-conflict1/returning/?id={id}");

        // Assert
        deleteReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_insert()
    {
        // Prepare
        using var insertBody = new StringContent("{\"id\":1,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insert = await test.Client.PutAsync("/api/crud-commented-table-on-conflict1/", insertBody);

        // Assert
        insert.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_insert_returning()
    {
        // Prepare
        using var insertReturningBody = new StringContent("{\"id\":2,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertReturning = await test.Client.PutAsync("/api/crud-commented-table-on-conflict1/returning/", insertReturningBody);

        // Assert
        insertReturning.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_insert_on_conflict_do_nothing()
    {
        // Prepare
        using var insertOnConflictDoNothingBody = new StringContent("{\"id\":3,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoNothing = await test.Client.PutAsync("/api/crud-commented-table-on-conflict1/on-conflict-do-nothing/", insertOnConflictDoNothingBody);

        // Assert
        insertOnConflictDoNothing.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_insert_on_conflict_do_nothing_returning()
    {
        // Prepare
        using var insertOnConflictDoNothingReturningBody = new StringContent("{\"id\":4,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoNothingReturning = await test.Client.PutAsync("/api/crud-commented-table-on-conflict1/on-conflict-do-nothing/returning/", insertOnConflictDoNothingReturningBody);

        // Assert
        insertOnConflictDoNothingReturning.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_insert_on_conflict_do_update()
    {
        // Prepare
        using var insertOnConflictDoUpdateBody = new StringContent("{\"id\":5,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoUpdate = await test.Client.PutAsync("/api/crud-commented-table-on-conflict1/on-conflict-do-update/", insertOnConflictDoUpdateBody);

        // Assert
        insertOnConflictDoUpdate.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Test_crud_commented_table_on_conflict1_insert_on_conflict_do_update_returning()
    {
        // Prepare
        using var insertOnConflictDoUpdateReturningBody = new StringContent("{\"id\":6,\"name\":\"some name\"}", Encoding.UTF8, "application/json");

        // Act
        using var insertOnConflictDoUpdateReturning = await test.Client.PutAsync("/api/crud-commented-table-on-conflict1/on-conflict-do-update/returning/", insertOnConflictDoUpdateReturningBody);

        // Assert
        insertOnConflictDoUpdateReturning.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
