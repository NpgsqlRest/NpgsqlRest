using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace NpgsqlRestTests;

public static partial class Database
{
    // `ierr_` routines are excluded from the default fixture by its NameNotSimilarTo list.
    public static void InfrastructureErrorDisclosureTests()
    {
        script.Append("""
create function ierr_unreachable() returns text language sql as 'select ''x''';
comment on function ierr_unreachable() is '
HTTP GET
connection unreachable';

create function ierr_raised() returns text language plpgsql as 'begin raise exception ''Visible business error''; end';
comment on function ierr_raised() is '
HTTP GET';
""");
    }
}

/// <summary>
/// Security regression: a 500 caused by the infrastructure (here: the database is unreachable) returns a generic
/// error; the message - host, port, pool details - is only logged. It used to be returned as the problem title.
/// Errors raised by PostgreSQL itself (RAISE EXCEPTION) keep their message: it is part of the routine's contract.
/// </summary>
public class InfrastructureErrorDisclosureTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();
        _app.UseNpgsqlRest(new(Database.Create())
        {
            IncludeSchemas = ["public"],
            NameSimilarTo = "ierr[_]%",
            CommentsMode = CommentsMode.ParseAll,
            RequiresAuthorization = false,
            ConnectionRetryOptions = new() { Enabled = false },
            ConnectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["unreachable"] = "Host=127.0.0.1;Port=9;Database=nothing;Username=nobody;Password=secret-password;Timeout=2"
            },
        });
        await _app.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Connection_failure_returns_a_generic_error()
    {
        using var response = await _client.GetAsync("/api/ierr-unreachable");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Be(
            "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.6.1\",\"title\":\"Internal Server Error\",\"status\":500}");
    }

    [Fact]
    public async Task Raised_exception_keeps_its_message()
    {
        using var response = await _client.GetAsync("/api/ierr-raised");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Visible business error");
    }
}
