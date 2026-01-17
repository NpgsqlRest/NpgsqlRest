using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Auth;
using NpgsqlRest.CrudSource;
using NpgsqlRest.TsClient;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.OpenAPI;
using NpgsqlRest.UploadHandlers;

namespace NpgsqlRestTests.Setup;

public class Program
{
    /// <summary>
    /// Output path for TsClient generated files (used by tests)
    /// </summary>
    public static string TsClientOutputPath { get; } = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "TsClient");

    /// <summary>
    /// Output path for HttpFiles generated files (used by tests)
    /// </summary>
    public static string HttpFilesOutputPath { get; } = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "HttpFiles");

    /// <summary>
    /// Output path for OpenApi generated files (used by tests)
    /// </summary>
    public static string OpenApiOutputPath { get; } = Path.Combine(Path.GetTempPath(), "NpgsqlRestTests", "OpenApi");

    static async Task ValidateAsync(ParameterValidationValues p)
    {
        if (p.Routine.Name == "case_jsonpath_param" && p.Parameter.Value?.ToString() == "XXX")
        {
            p.Context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await p.Context.Response.WriteAsync($"Parameter {p.Parameter.ActualName} is not valid.");
        }

        if (string.Equals(p.Parameter.ParameterName, "_user_id", StringComparison.Ordinal))
        {
            if (p.Context.User.Identity?.IsAuthenticated is true)
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
            if (p.Context.User.Identity?.IsAuthenticated is true)
            {
                p.Parameter.Value = p.Context.User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray();
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

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);
            };
            options.AddFixedWindowLimiter("max 2 per second", config =>
            {
                config.PermitLimit = 2;
                config.Window = TimeSpan.FromSeconds(1);
                config.AutoReplenishment = true;
            });
        });
        
        builder
            .Services
            .AddAuthentication()
            //.AddBearerToken();
            .AddCookie();
        
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                // Remove the type field completely
                ctx.ProblemDetails.Type = null;
                
                // Remove the traceId extension
                ctx.ProblemDetails.Extensions.Remove("traceId");
            };
        });

        var app = builder.Build();
        app.UseRateLimiter();

        var authOptions = new NpgsqlRestAuthenticationOptions
        {
            DefaultUserIdClaimType = "name_identifier",
            DefaultNameClaimType = "name",
            DefaultRoleClaimType = "role",
            ContextKeyClaimsMapping = new()
            {
                { "request.user_id", "name_identifier" },
                { "request.user_name", "name" },
                { "request.user_roles" , "role" },
            },
            ParameterNameClaimsMapping = new()
            {
                { "_user_id", "name_identifier" },
                { "_user_name", "name" },
                { "_user_roles", "role" }
            },
        };
        app.MapGet("/login", () => Results.SignIn(new ClaimsPrincipal(new ClaimsIdentity(
            claims: new[]
            {
                new Claim(authOptions.DefaultNameClaimType, "user"),
                new Claim(authOptions.DefaultRoleClaimType, "role1"),
                new Claim(authOptions.DefaultRoleClaimType, "role2"),
                new Claim(authOptions.DefaultRoleClaimType, "role3"),
            },
            authenticationType: CookieAuthenticationDefaults.AuthenticationScheme))));


        var errorHandlingOptions = new ErrorHandlingOptions();
        errorHandlingOptions.ErrorCodePolicies.Add("test_err_policy", new Dictionary<string, ErrorCodeMappingOptions>()
        {
            ["22012"] = new() { StatusCode = 409, Title = "Conflict - Custom Policy" }
        });
        
        app.UseNpgsqlRest(new(connectionString)
        {
            //NameSimilarTo = "get_conn1_connection_name_p",
            //SchemaSimilarTo = "custom_param_schema",
            IncludeSchemas = ["public", "custom_param_schema", "my_schema", "custom_table_param_schema", "tsclient_test", "polp_schema"],

            // TsClient configuration for testing - uses tsclient_module annotations for per-function files
            EndpointCreateHandlers = [
                new TsClient(new TsClientOptions
                {
                    FilePath = Path.Combine(TsClientOutputPath, "{0}.ts"),
                    FileOverwrite = true,
                    BySchema = true, // Required for tsclient_module to generate separate files
                    IncludeHost = false,
                    CreateSeparateTypeFile = false,
                    CommentHeader = CommentHeader.Simple,
                    HeaderLines = [], // No auto-generated timestamp for consistent test assertions
                    // Only process tsclient_test schema to avoid issues with custom sources that have null definitions
                    SkipSchemas = ["public", "custom_param_schema", "my_schema", "custom_table_param_schema", ""],
                    IncludeStatusCode = false
                }),
                // HttpFiles configuration for testing path parameters
                new HttpFile(new HttpFileOptions
                {
                    Option = HttpFileOption.File,
                    NamePattern = Path.Combine(HttpFilesOutputPath, "{0}"),
                    FileOverwrite = true,
                    CommentHeader = CommentHeader.None
                }),
                // OpenApi configuration for testing path parameters
                new OpenApi(new OpenApiOptions
                {
                    FileName = Path.Combine(OpenApiOutputPath, "openapi.json"),
                    FileOverwrite = true,
                    DocumentTitle = "NpgsqlRest Test API",
                    AddCurrentServer = false
                }),
            ],
            CommentsMode = CommentsMode.ParseAll,
            ValidateParametersAsync = ValidateAsync,
            ConnectionStrings = new Dictionary<string, string>()
            {
                { "conn1", Database.CreateAdditional("conn1") },
                { "conn2", Database.CreateAdditional("conn2") },
                { "polp_conn", Database.CreatePolpConnection() }
            },
            CommandCallbackAsync = async (endpoint, command, context) =>
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
                DefaultUserIdClaimType = "name_identifier",
                DefaultNameClaimType = "name",
                DefaultRoleClaimType = "role",
                ContextKeyClaimsMapping = new()
                {
                    { "request.user_id", "name_identifier" },
                    { "request.user_name", "name" },
                    { "request.user_roles" , "role" },
                },
                ParameterNameClaimsMapping = new()
                {
                    { "_user_id", "name_identifier" },
                    { "_user_name", "name" },
                    { "_user_roles", "role" }
                },

                PasswordVerificationFailedCommand = "call failed_login($1,$2,$3)",
                PasswordVerificationSucceededCommand = "call succeeded_login($1,$2,$3)",
                ClaimsJsonContextKey = "request.user_claims",
                BasicAuth = new BasicAuthOptions()
                {
                    SslRequirement = SslRequirement.Ignore
                }
            },

            UploadOptions = new()
            {
                UseDefaultUploadMetadataParameter = true,
                DefaultUploadMetadataParameterName = "_default_upload_metadata",
                UseDefaultUploadMetadataContextKey = true,
                //DefaultUploadMetadataContextKey = "request.upload_metadata",
            },
            
            ErrorHandlingOptions = errorHandlingOptions,
            HttpClientOptions = new()
            {
                Enabled = true
            },
            CacheOptions = new()
            {
                InvalidateCacheSuffix = "invalidate"
            },
            ProxyOptions = new()
            {
                Enabled = true,
                Host = "http://localhost:50954" // ProxyWireMockFixture.Port
            },
            ValidationOptions = new()
            {
                Rules = new()
                {
                    // NotNull - checks for null/DBNull values
                    ["not_null"] = new ValidationRule
                    {
                        Type = ValidationType.NotNull,
                        Message = "Parameter '{0}' cannot be null",
                        StatusCode = 400
                    },
                    // NotEmpty - checks for empty string (null values pass)
                    ["not_empty"] = new ValidationRule
                    {
                        Type = ValidationType.NotEmpty,
                        Message = "Parameter '{0}' cannot be empty",
                        StatusCode = 400
                    },
                    // Required - combines NotNull and NotEmpty
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
                    // Custom rule for testing custom messages
                    ["product_code"] = new ValidationRule
                    {
                        Type = ValidationType.Regex,
                        Pattern = @"^[A-Z]{3}-\d{4}$",
                        Message = "Product code must be in format XXX-0000 (3 uppercase letters, dash, 4 digits)",
                        StatusCode = 400
                    },
                    // Rule for testing message format placeholders: {0}=original, {1}=converted, {2}=rule name
                    ["format_test"] = new ValidationRule
                    {
                        Type = ValidationType.NotNull,
                        Message = "Original: {0}, Converted: {1}, Rule: {2}",
                        StatusCode = 400
                    }
                }
            }
        });
        app.Run();
    }
}
