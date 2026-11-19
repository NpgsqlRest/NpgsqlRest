using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRestTests.ConnectionTests;

/// <summary>
/// The parts of RequestContextBatch that need no database: whether Npgsql supports it, the context statement, which
/// SQL-file statements qualify, and how the batch is put together.
/// </summary>
public class RequestContextBatchUnitTests
{
    /// <summary>
    /// Text results for a batch come from a member of Npgsql that is not in its public API. If an Npgsql upgrade
    /// removes or renames it, NpgsqlRest keeps working - the context goes back to a separate round trip - but this
    /// test fails, so the loss is noticed.
    /// </summary>
    [Fact]
    public void This_Npgsql_returns_text_results_for_a_batch()
    {
        RequestContextBatch.IsSupported.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, "select set_config($1,$2,true) is null")]
    [InlineData(2, "select set_config($1,$2,true) is null,set_config($3,$4,true) is null")]
    [InlineData(4, "select set_config($1,$2,true) is null,set_config($3,$4,true) is null,set_config($5,$6,true) is null,set_config($7,$8,true) is null")]
    public void Context_statement_sets_every_key_from_parameters(int count, string expected)
    {
        RequestContextBatch.ContextStatement(count).Should().Be(expected);
        // cached per count
        RequestContextBatch.ContextStatement(count).Should().BeSameAs(RequestContextBatch.ContextStatement(count));
    }

    [Fact]
    public void Context_statement_for_many_keys()
    {
        var statement = RequestContextBatch.ContextStatement(40);
        statement.Should().StartWith("select set_config($1,$2,true) is null,");
        statement.Should().EndWith(",set_config($79,$80,true) is null");
    }

    [Theory]
    // queries and data changes: cannot end the transaction they run in
    [InlineData("select 1", true)]
    [InlineData("SELECT * from t where id = $1", true)]
    [InlineData("  \n\t select 1", true)]
    [InlineData("with x as (select 1) select * from x", true)]
    [InlineData("insert into t values ($1) returning id", true)]
    [InlineData("update t set a = $1", true)]
    [InlineData("delete from t where id = $1", true)]
    [InlineData("merge into t using s on t.id = s.id when matched then do nothing", true)]
    [InlineData("values (1), (2)", true)]
    [InlineData("table t", true)]
    [InlineData("-- a comment\nselect 1", true)]
    [InlineData("/* a /* nested */ comment */ select 1", true)]
    [InlineData("-- one\n/* two */\n-- three\nSeLeCt 1", true)]
    [InlineData("select(1)", true)]
    // everything else is left alone
    [InlineData("call my_proc($1)", false)]
    [InlineData("do $$ begin commit; end $$", false)]
    [InlineData("vacuum t", false)]
    [InlineData("set search_path to x", false)]
    [InlineData("begin", false)]
    [InlineData("commit", false)]
    [InlineData("copy t to stdout", false)]
    [InlineData("create table t (id int)", false)]
    [InlineData("refresh materialized view concurrently m", false)]
    [InlineData("truncate t", false)]
    [InlineData("(select 1) union (select 2)", false)]
    [InlineData("select_all()", false)]
    [InlineData("selects 1", false)]
    [InlineData("select$1", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("-- only a comment", false)]
    [InlineData("/* unterminated select 1", false)]
    [InlineData(null, false)]
    public void Sql_file_statement_qualifies_when_it_is_a_query_or_a_data_change(string? sql, bool expected)
    {
        RequestContextBatch.IsQueryOrDataChange(sql).Should().Be(expected);
    }

    [Fact]
    public void Batch_has_the_context_statement_and_then_the_routine_with_its_parameters()
    {
        using var connection = new NpgsqlConnection();
        using var command = new NpgsqlCommand("select public.my_function($1,$2)", connection) { CommandTimeout = 77 };
        var first = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = 1 };
        var second = new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = "two" };
        command.Parameters.Add(first);
        command.Parameters.Add(second);

        (string Key, object? Value)[] context =
        [
            ("request.user_id", "123"),
            ("request.user_name", DBNull.Value),
        ];
        using var batch = RequestContextBatch.Create(connection, command, context);

        batch.Connection.Should().BeSameAs(connection);
        batch.Timeout.Should().Be(77);
        batch.BatchCommands.Count.Should().Be(2);

        var contextCommand = batch.BatchCommands[0];
        contextCommand.CommandText.Should().Be("select set_config($1,$2,true) is null,set_config($3,$4,true) is null");
        contextCommand.Parameters.Select(p => p.Value).Should().Equal("request.user_id", "123", "request.user_name", DBNull.Value);
        contextCommand.Parameters.Select(p => p.NpgsqlDbType).Should().OnlyContain(type => type == NpgsqlDbType.Text);
        contextCommand.Parameters.Select(p => p.ParameterName).Should().OnlyContain(name => name == "", "positional parameters have no names");

        var routineCommand = batch.BatchCommands[1];
        routineCommand.CommandText.Should().Be("select public.my_function($1,$2)");
        // the very same parameters, in order: they moved from the command to the batch
        routineCommand.Parameters.Count.Should().Be(2);
        routineCommand.Parameters[0].Should().BeSameAs(first);
        routineCommand.Parameters[1].Should().BeSameAs(second);
        command.Parameters.Count.Should().Be(0);
    }

    [Fact]
    public void Batch_for_a_routine_without_parameters()
    {
        using var connection = new NpgsqlConnection();
        using var command = new NpgsqlCommand("select public.no_parameters()", connection);

        (string Key, object? Value)[] context = [("request.ip_address", "127.0.0.1")];
        using var batch = RequestContextBatch.Create(connection, command, context);

        batch.BatchCommands.Count.Should().Be(2);
        batch.BatchCommands[0].CommandText.Should().Be("select set_config($1,$2,true) is null");
        batch.BatchCommands[1].CommandText.Should().Be("select public.no_parameters()");
        batch.BatchCommands[1].Parameters.Count.Should().Be(0);
    }
}
