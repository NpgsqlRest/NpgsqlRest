using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.SqlFileSource;

namespace NpgsqlRestTests.SqlFileSourceTests;

[CollectionDefinition("SqlFileAdvancedFixture")]
public class SqlFileAdvancedFixtureCollection : ICollectionFixture<SqlFileAdvancedFixture> { }

/// <summary>
/// Test fixture for SQL file endpoints with advanced features:
/// caching, validation, encryption/decryption, and raw mode.
/// </summary>
public class SqlFileAdvancedFixture : IDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;
    private readonly string _sqlDir;

    public HttpClient Client => _client;
    public IDataProtector DataProtector { get; }

    public SqlFileAdvancedFixture()
    {
        var connectionString = Database.Create();

        _sqlDir = Path.Combine(Path.GetTempPath(), "npgsqlrest_sf_adv_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_sqlDir);

        WriteSqlFiles(_sqlDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddDataProtection().SetApplicationName("npgsqlrest-sf-tests");
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Type = null;
                ctx.ProblemDetails.Extensions.Remove("traceId");
            };
        });

        _app = builder.Build();

        var dataProtectionProvider = _app.Services.GetRequiredService<IDataProtectionProvider>();
        DataProtector = dataProtectionProvider.CreateProtector("npgsqlrest-sf-tests");

        _app.UseNpgsqlRest(new NpgsqlRestOptions(connectionString)
        {
            CommentsMode = CommentsMode.ParseAll,
            CacheOptions = new()
            {
                InvalidateCacheSuffix = "invalidate"
            },
            ValidationOptions = new()
            {
                Rules = new()
                {
                    ["not_null"] = new ValidationRule
                    {
                        Type = ValidationType.NotNull,
                        Message = "Parameter '{0}' cannot be null",
                        StatusCode = 400
                    },
                    ["not_empty"] = new ValidationRule
                    {
                        Type = ValidationType.NotEmpty,
                        Message = "Parameter '{0}' cannot be empty",
                        StatusCode = 400
                    },
                    ["required"] = new ValidationRule
                    {
                        Type = ValidationType.Required,
                        Message = "Parameter '{0}' is required",
                        StatusCode = 400
                    },
                    ["email"] = new ValidationRule
                    {
                        Type = ValidationType.Regex,
                        Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
                        Message = "Parameter '{0}' must be a valid email address",
                        StatusCode = 400
                    },
                }
            },
            AuthenticationOptions = new()
            {
                DefaultDataProtector = DataProtector
            },
            EndpointSources =
            [
                new SqlFileSource(new SqlFileSourceOptions
                {
                    FilePattern = _sqlDir.Replace('\\', '/') + "/**/*.sql",
                    CommentsMode = CommentsMode.ParseAll,
                    CommentScope = CommentScope.All,
                    ErrorMode = ParseErrorMode.Skip,
                })
            ],
        });

        _app.StartAsync().GetAwaiter().GetResult();

        var serverAddress = _app.Urls.First();
        _client = new HttpClient { BaseAddress = new Uri(serverAddress), Timeout = TimeSpan.FromHours(1) };
    }

    private static void WriteSqlFiles(string dir)
    {
        // === CACHE TESTS ===

        // 1. Basic caching — same call returns same result
        File.WriteAllText(Path.Combine(dir, "sf_cache_timestamp.sql"), """
            -- HTTP GET
            -- cached
            select clock_timestamp()::text as ts;
            """);

        // 2. Cache with key params — different keys = different cache entries
        File.WriteAllText(Path.Combine(dir, "sf_cache_with_keys.sql"), """
            -- HTTP GET
            -- param $1 key1 text
            -- param $2 key2 text
            -- cached key1, key2
            select $1 || '_' || $2 || '_' || random()::text as result;
            """);

        // 3. Cache with expiration
        File.WriteAllText(Path.Combine(dir, "sf_cache_expires.sql"), """
            -- HTTP GET
            -- cached
            -- cache_expires_in 1 second
            select random()::text as val;
            """);

        // === VALIDATION TESTS ===

        // 4. Validate not_empty
        File.WriteAllText(Path.Combine(dir, "sf_validate_not_empty.sql"), """
            -- HTTP POST
            -- param $1 email text
            -- validate email using not_empty
            select coalesce($1, 'was null') as result;
            """);

        // 5. Validate required (not_null + not_empty)
        File.WriteAllText(Path.Combine(dir, "sf_validate_required.sql"), """
            -- HTTP POST
            -- param $1 name text
            -- validate name using required
            select 'Hello ' || $1 as greeting;
            """);

        // 6. Validate regex (email format)
        File.WriteAllText(Path.Combine(dir, "sf_validate_email.sql"), """
            -- HTTP POST
            -- param $1 email text
            -- validate email using email
            select $1 as valid_email;
            """);

        // === ENCRYPTION TESTS ===

        // 7. Encrypt all text params
        File.WriteAllText(Path.Combine(dir, "sf_encrypt_all.sql"), """
            -- HTTP POST
            -- param $1 a text
            -- param $2 b text
            -- encrypt
            select $1 as a, $2 as b;
            """);

        // 8. Encrypt specific param
        File.WriteAllText(Path.Combine(dir, "sf_encrypt_named.sql"), """
            -- HTTP POST
            -- param $1 secret text
            -- param $2 plain text
            -- encrypt secret
            select $1 as secret, $2 as plain;
            """);

        // 9. Decrypt all columns
        File.WriteAllText(Path.Combine(dir, "sf_decrypt_all.sql"), """
            -- HTTP POST
            -- param $1 a text
            -- param $2 b text
            -- decrypt
            select $1 as a, $2 as b;
            """);

        // === RAW MODE TESTS ===

        // 10. Basic raw mode — no JSON wrapping
        File.WriteAllText(Path.Combine(dir, "sf_raw_basic.sql"), """
            -- HTTP GET
            -- raw
            select 123 as n, true as b, 'hello' as t;
            """);

        // 11. Raw with CSV separators
        File.WriteAllText(Path.Combine(dir, "sf_raw_csv.sql"), """
            -- HTTP GET
            -- raw
            -- separator ,
            -- new_line \n
            -- Content-Type: text/csv
            select 123 as n, true as b, 'hello' as t;
            """);

        // 12. Raw with column headers
        File.WriteAllText(Path.Combine(dir, "sf_raw_csv_headers.sql"), """
            -- HTTP GET
            -- raw
            -- separator ,
            -- new_line \n
            -- column_names
            -- Content-Type: text/csv
            select 123 as n, true as b, 'hello' as t;
            """);

    }

#pragma warning disable CA1816
    public void Dispose()
#pragma warning restore CA1816
    {
        _client.Dispose();
        _app.StopAsync().GetAwaiter().GetResult();
        _app.DisposeAsync().GetAwaiter().GetResult();
        try { if (Directory.Exists(_sqlDir)) Directory.Delete(_sqlDir, true); } catch { }
    }
}
