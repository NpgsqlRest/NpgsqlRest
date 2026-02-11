using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest;
using NpgsqlRest.CrudSource;
using NpgsqlRest.HttpFiles;
using NpgsqlRest.TsClient;

namespace NpgsqlRestClient;

/// <summary>
/// Provides machine-readable JSON output for CLI commands.
/// Used by pgdev and other tools to programmatically inspect NpgsqlRest.
/// </summary>
public static class CliJson
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string GetVersion<T>() =>
        System.Reflection.Assembly.GetAssembly(typeof(T))?.GetName().Version?.ToString() ?? "-";

    public static JsonObject GetVersionJson()
    {
        var frameworkParts = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.Split(' ', 2);

        return new JsonObject
        {
            ["runtime"] = new JsonObject
            {
                ["name"] = frameworkParts.Length > 0 ? frameworkParts[0] : "-",
                ["version"] = frameworkParts.Length > 1 ? frameworkParts[1] : "-"
            },
            ["platform"] = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            ["versions"] = new JsonObject
            {
                ["NpgsqlRest"] = GetVersion<NpgsqlRestOptions>(),
                ["NpgsqlRestClient"] = GetVersion<Program>(),
                ["NpgsqlRest.HttpFiles"] = GetVersion<HttpFileOptions>(),
                ["NpgsqlRest.TsClient"] = GetVersion<TsClientOptions>(),
                ["NpgsqlRest.CrudSource"] = GetVersion<CrudSource>(),
                ["NpgsqlRest.OpenApi"] = GetVersion<NpgsqlRest.OpenAPI.OpenApiOptions>(),
                ["Npgsql"] = GetVersion<NpgsqlConnection>(),
                ["ExcelDataReader"] = GetVersion<ExcelDataReader.IExcelDataReader>(),
                ["SpreadCheetah"] = GetVersion<SpreadCheetah.Spreadsheet>(),
                ["Serilog.AspNetCore"] = GetVersion<Serilog.AspNetCore.RequestLoggingOptions>(),
                ["Serilog.Sinks.OpenTelemetry"] = GetVersion<Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions>(),
                ["System.Text.Json"] = GetVersion<System.Text.Json.JsonCommentHandling>(),
                ["StackExchange.Redis"] = GetVersion<StackExchange.Redis.ConnectionMultiplexer>(),
                ["Microsoft.Extensions.Caching.Hybrid"] = GetVersion<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
                ["Microsoft.Extensions.Caching.StackExchangeRedis"] = GetVersion<Microsoft.Extensions.Caching.StackExchangeRedis.RedisCache>(),
                ["Microsoft.AspNetCore.Authentication.JwtBearer"] = GetVersion<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(),
                ["AspNetCore.HealthChecks.NpgSql"] = GetVersion<HealthChecks.NpgSql.NpgSqlHealthCheck>()
            },
            ["currentDirectory"] = Directory.GetCurrentDirectory(),
            ["baseDirectory"] = AppContext.BaseDirectory
        };
    }
}
