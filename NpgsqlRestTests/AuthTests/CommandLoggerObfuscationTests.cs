using Npgsql;
using NpgsqlTestsSetup = NpgsqlRestTests.Setup;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Security regression: the Trace-level command log can hide parameter values. Login (and Basic Auth challenge)
/// commands log with ObfuscateAuthParameterLogValues (default on); they used to print the password in clear text.
/// </summary>
public class CommandLoggerObfuscationTests
{
    private static NpgsqlCommand LoginCommand()
    {
        var command = new NpgsqlCommand("select * from login($1, $2)");
        command.Parameters.Add(new NpgsqlParameter { Value = "alice" });
        command.Parameters.Add(new NpgsqlParameter { Value = "S3cret!" });
        return command;
    }

    [Fact]
    public void Obfuscated_values_are_not_logged()
    {
        var collector = new NpgsqlTestsSetup.LogCollector();
        var logger = new NpgsqlTestsSetup.CollectingLogger("test", collector);
        using var command = LoginCommand();

        CommandLogger.LogCommand(command, logger, "Login", obfuscateValues: true);

        collector.Snapshot().Single().Message.Should().Be("Login:\n$1=***\n$2=***\nselect * from login($1, $2)");
    }

    [Fact]
    public void Values_are_logged_by_default()
    {
        var collector = new NpgsqlTestsSetup.LogCollector();
        var logger = new NpgsqlTestsSetup.CollectingLogger("test", collector);
        using var command = LoginCommand();

        CommandLogger.LogCommand(command, logger, "Routine");

        collector.Snapshot().Single().Message.Should().Be("Routine:\n$1=\"alice\"\n$2=\"S3cret!\"\nselect * from login($1, $2)");
    }
}

/// <summary>
/// Security regression: a request body that fails to parse as JSON is not written to the log (only its length). It
/// used to be logged in full at Warning level - including a login's password, and any size the client chose.
/// </summary>
public class MalformedJsonBodyLogTests
{
    [Fact]
    public void Malformed_body_is_not_logged()
    {
        var collector = new NpgsqlTestsSetup.LogCollector();
        var logger = new NpgsqlTestsSetup.CollectingLogger("test", collector);

        logger.CouldNotParseJson("{\"user\":\"alice\",\"pass\":\"S3cret!\"", "/api/login", "unexpected end");

        collector.Snapshot().Single().Message.Should().Be(
            "Could not parse JSON body (32 characters), skipping path /api/login. Error: unexpected end");
    }
}
