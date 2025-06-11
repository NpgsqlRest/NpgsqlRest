using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NpgsqlRest.CrudSource;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;
using NpgsqlRest.UploadHandlers;

#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace NpgsqlRestTests;

public class EmptyLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }
}

public class Program
{
    static async Task ValidateAsync(ParameterValidationValues p)
    {
        if (p.Routine.Name == "case_jsonpath_param" && p.Parameter.Value?.ToString() == "XXX")
        {
            p.Context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await p.Context.Response.WriteAsync($"Paramater {p.Parameter.ActualName} is not valid.");
        }

        if (string.Equals(p.Parameter.ParameterName, "_user_id", StringComparison.Ordinal))
        {
            if (p.Context?.User?.Identity?.IsAuthenticated is true)
            {
                p.Parameter.Value = p.Context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            }
            else
            {
                p.Parameter.Value = DBNull.Value;
            }
        }

        if (string.Equals(p.Parameter.ParameterName, "_user_roles", StringComparison.Ordinal))
        {
            if (p.Context?.User?.Identity?.IsAuthenticated is true)
            {
                p.Parameter.Value = p.Context.User?.Claims?.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray() ?? [];
            }
            else
            {
                p.Parameter.Value = DBNull.Value;
            }
        }
    }

    public static void Main()
    {
        var uploadHandlerOptions = new UploadHandlerOptions();
        var files = Directory.GetFiles(uploadHandlerOptions.FileSystemPath, "*.csv");
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting file {file}: {ex.Message}");
            }
        }

        var connectionString = Database.Create();
        // disable SQL rewriting to ensure that NpgsqlRest works with this option on.
        AppContext.SetSwitch("Npgsql.EnableSqlRewriting", false);

        var builder = WebApplication.CreateBuilder([]);

        builder
            .Services
            .AddAuthentication()
            //.AddBearerToken();
            .AddCookie();

        var app = builder.Build();

        app.MapGet("/login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[]
            {
                new Claim(ClaimTypes.Name, "user"),
                new Claim(ClaimTypes.Role, "role1"),
                new Claim(ClaimTypes.Role, "role2"),
                new Claim(ClaimTypes.Role, "role3"),
            },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));

        app.UseNpgsqlRest(new(connectionString)
        {
            //NameSimilarTo = "get_conn1_connection_name_p",
            //SchemaSimilarTo = "custom_param_schema",
            CommentsMode = CommentsMode.ParseAll,
            ValidateParametersAsync = ValidateAsync,
            ConnectionStrings = new Dictionary<string, string>()
            {
                { "conn1", Database.CreateAdditional("conn1") },
                { "conn2", Database.CreateAdditional("conn2") }
            },
            Logger = new EmptyLogger(),
            CommandCallbackAsync = async (RoutineEndpoint endpoint, NpgsqlCommand command, HttpContext context) =>
            {
                if (string.Equals(endpoint.Routine.Name , "get_csv_data"))
                {
                    context.Response.ContentType = "text/csv";
                    await using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var line = $"{reader[0]},{reader[1]},{reader.GetDateTime(2):s},{reader.GetBoolean(3).ToString().ToLowerInvariant()}\n";
                        await context.Response.WriteAsync(line);
                    }
                }
            },

            SourcesCreated = sources =>
            {
                //sources.Clear();
                sources.Add(new CrudSource());
                sources.Add(new TestSource());
            },

            CustomRequestHeaders = new()
            {
                { "custom-header1", "custom-header1-value" }
            },

            AuthenticationOptions = new()
            {
                PasswordVerificationFailedCommand = "call failed_login($1,$2,$3)",
                PasswordVerificationSucceededCommand = "call succeeded_login($1,$2,$3)",
                UserClaimsContextKey = "request.user_claims",
            },

            UploadOptions = new()
            {
                UseDefaultUploadMetadataParameter = true,
                DefaultUploadMetadataParameterName = "_default_upload_metadata",
                UseDefaultUploadMetadataContextKey = true,
                //DefaultUploadMetadataContextKey = "request.upload_metadata",
            }
        });
        app.Run();
    }
}
