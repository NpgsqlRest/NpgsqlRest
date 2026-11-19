using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void SqlFileRequestContextBatchTests()
        {
            script.Append("""
            create table sfrcb_log (v text primary key, user_id text);
            create procedure sfrcb_proc() language plpgsql as $$ begin null; end $$;
            """);
        }
    }
}

namespace NpgsqlRestTests.SqlFileSourceTests
{
    /// <summary>
    /// SQL files and the request context that is sent together with the statement (RequestContextBatch): a file with
    /// a single query or data change qualifies, anything else keeps the session-level context.
    /// </summary>
    public class SqlFileRequestContextBatchTests : IDisposable
    {
        private const string ContextQuery = """
            -- HTTP GET
            -- @user_context
            -- @param $1 p text
            select $1 || ':' || coalesce(nullif(current_setting('request.user_id', true), ''), '<none>') as value;
            """;

        private const string ContextInsert = """
            -- HTTP POST
            -- @user_context
            -- @param $1 v text
            insert into sfrcb_log (v, user_id) values ($1, current_setting('request.user_id', true)) returning v, user_id;
            """;

        private const string Peek = """
            -- HTTP GET
            select coalesce(nullif(current_setting('request.user_id', true), ''), '<none>') as value;
            """;

        private readonly List<string> _directories = [];

        private string WriteFiles(params (string Name, string Sql)[] files)
        {
            var directory = Path.Combine(Path.GetTempPath(), "npgsqlrest_sfrcb_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(directory);
            _directories.Add(directory);
            foreach (var (name, sql) in files)
            {
                File.WriteAllText(Path.Combine(directory, name), sql);
            }
            return directory;
        }

        private static NpgsqlRestOptions CreateOptions(string connectionString, string directory) => new(connectionString)
        {
            CommentsMode = CommentsMode.OnlyWithHttpTag,
            RequiresAuthorization = false,
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = directory.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.OnlyWithHttpTag,
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Exit,
                })
            ],
        };

        /// <summary>Builds the endpoints of the given files (without serving them) and returns the options.</summary>
        private async Task<NpgsqlRestOptions> BuildAsync(params (string Name, string Sql)[] files)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            await using var app = builder.Build();

            var options = CreateOptions(Database.Create(), WriteFiles(files));
            var currentOptions = NpgsqlRestOptions.Options;
            var currentLogger = NpgsqlRestOptions.Logger;
            try
            {
                app.UseNpgsqlRest(options);
            }
            finally
            {
                NpgsqlRestOptions.Options = currentOptions;
                NpgsqlRestOptions.Logger = currentLogger;
            }
            return options;
        }

        [Fact]
        public async Task Query_and_data_change_files_send_their_context_with_the_statement()
        {
            // One connection that is never reset: what a request leaves on it, the next request sees.
            var connectionString = new NpgsqlConnectionStringBuilder(Database.Create())
            {
                NoResetOnClose = true,
                MaxPoolSize = 1,
                // its own pool (Npgsql pools by connection string), so no other test's requests use this connection
                ApplicationName = "sql-file-request-context-batch-tests",
            }.ConnectionString;

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddAuthentication().AddCookie();
            await using var app = builder.Build();
            app.MapGet("/sfrcb-login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
                claims: [new Claim("user_id", "123"), new Claim("user_name", "admin")],
                authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

            var options = CreateOptions(connectionString, WriteFiles(
                ("sfrcb_context.sql", ContextQuery),
                ("sfrcb_insert.sql", ContextInsert),
                ("sfrcb_peek.sql", Peek)));
            app.UseNpgsqlRest(options);
            await app.StartAsync();

            options.ConnectionResetRequired.Should().BeFalse();

            using var user = new HttpClient(new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() })
            {
                BaseAddress = new Uri(app.Urls.First())
            };
            using var anonymous = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            using (var login = await user.GetAsync("/sfrcb-login"))
            {
                login.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using (var response = await user.GetAsync("/api/sfrcb-context?p=a"))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("[\"a:123\"]");
            }
            using (var response = await anonymous.GetAsync("/api/sfrcb-peek"))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("[\"<none>\"]");
            }
            using (var body = new StringContent("{\"v\":\"row 1\"}", Encoding.UTF8, "application/json"))
            using (var response = await user.PostAsync("/api/sfrcb-insert", body))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("[{\"v\":\"row 1\",\"userId\":\"123\"}]");
            }
            using (var response = await anonymous.GetAsync("/api/sfrcb-context?p=b"))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("[\"b:<none>\"]");
            }
            using (var response = await anonymous.GetAsync("/api/sfrcb-peek"))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (await response.Content.ReadAsStringAsync()).Should().Be("[\"<none>\"]");
            }

            await app.StopAsync();
        }

        [Fact]
        public async Task Reset_is_not_required_for_query_and_data_change_files()
        {
            (await BuildAsync(("sfrcb_context.sql", ContextQuery), ("sfrcb_insert.sql", ContextInsert), ("sfrcb_peek.sql", Peek)))
                .ConnectionResetRequired.Should().BeFalse();
        }

        [Fact]
        public async Task Reset_is_required_when_a_file_with_several_statements_uses_user_context()
        {
            // its statements are executed one by one: settings local to the first would not reach the second
            (await BuildAsync(
                ("sfrcb_context.sql", ContextQuery),
                ("sfrcb_multi.sql", """
                    -- HTTP GET
                    -- @user_context
                    select current_setting('request.user_id', true) as a;
                    select current_setting('request.user_name', true) as b;
                    """)))
                .ConnectionResetRequired.Should().BeTrue();
        }

        [Fact]
        public async Task Reset_is_required_when_a_do_block_uses_user_context()
        {
            // a do block can commit, which would end local settings
            (await BuildAsync(
                ("sfrcb_context.sql", ContextQuery),
                ("sfrcb_do.sql", """
                    -- HTTP POST
                    -- @user_context
                    do $$ begin perform current_setting('request.user_id', true); end $$;
                    """)))
                .ConnectionResetRequired.Should().BeTrue();
        }

        [Fact]
        public async Task Reset_is_required_when_a_procedure_call_uses_user_context()
        {
            (await BuildAsync(
                ("sfrcb_context.sql", ContextQuery),
                ("sfrcb_call.sql", """
                    -- HTTP POST
                    -- @user_context
                    call sfrcb_proc();
                    """)))
                .ConnectionResetRequired.Should().BeTrue();
        }

        public void Dispose()
        {
            foreach (var directory in _directories)
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
            }
            GC.SuppressFinalize(this);
        }
    }
}
