using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace NpgsqlRestClient;

// AOT-compatible JSON serialization context
[JsonSerializable(typeof(JwtErrorResponse))]
[JsonSerializable(typeof(JwtTokenResponse))]
internal partial class JwtJsonContext : JsonSerializerContext
{
}

internal class JwtErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = null!;
}

internal class JwtTokenResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = null!;

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = null!;

    [JsonPropertyName("tokenType")]
    public string TokenType { get; set; } = "Bearer";

    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("refreshExpiresIn")]
    public int RefreshExpiresIn { get; set; }
}

public class JwtTokenConfig
{
    public string Scheme { get; set; } = JwtBearerDefaults.AuthenticationScheme;
    public string Secret { get; set; } = null!;
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public int ExpireMinutes { get; set; } = 60;
    public int RefreshExpireDays { get; set; } = 7;
    public bool ValidateIssuer { get; set; }
    public bool ValidateAudience { get; set; }
    public bool ValidateLifetime { get; set; } = true;
    public bool ValidateIssuerSigningKey { get; set; } = true;
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
    public string? RefreshPath { get; set; }

    public SymmetricSecurityKey GetSecurityKey() => new(Encoding.UTF8.GetBytes(Secret));

    public SigningCredentials GetSigningCredentials() => new(GetSecurityKey(), SecurityAlgorithms.HmacSha256);

    public TokenValidationParameters GetTokenValidationParameters() => new()
    {
        ValidateIssuer = ValidateIssuer,
        ValidateAudience = ValidateAudience,
        ValidateLifetime = ValidateLifetime,
        ValidateIssuerSigningKey = ValidateIssuerSigningKey,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = GetSecurityKey(),
        ClockSkew = ClockSkew
    };
}

public class JwtTokenGenerator
{
    private readonly JwtTokenConfig _config;

    public JwtTokenGenerator(JwtTokenConfig config)
    {
        _config = config;
    }

    public (string accessToken, string refreshToken, DateTime accessExpires, DateTime refreshExpires) GenerateTokens(ClaimsPrincipal principal)
    {
        var accessExpires = DateTime.UtcNow.AddMinutes(_config.ExpireMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_config.RefreshExpireDays);

        var accessToken = GenerateAccessToken(principal.Claims, accessExpires);
        var refreshToken = GenerateRefreshToken(principal.Claims, refreshExpires);

        return (accessToken, refreshToken, accessExpires, refreshExpires);
    }

    public string GenerateAccessToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _config.Issuer,
            Audience = _config.Audience,
            SigningCredentials = _config.GetSigningCredentials()
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken(IEnumerable<Claim> claims, DateTime expires)
    {
        // Add a claim to identify this as a refresh token
        var refreshClaims = claims.ToList();
        refreshClaims.Add(new Claim("token_type", "refresh"));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(refreshClaims),
            Expires = expires,
            Issuer = _config.Issuer,
            Audience = _config.Audience,
            SigningCredentials = _config.GetSigningCredentials()
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public ClaimsPrincipal? ValidateRefreshToken(string refreshToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = _config.GetTokenValidationParameters();

        // For refresh tokens, we want to validate even if the access token would be expired
        // but the refresh token itself should not be expired
        validationParameters.ValidateLifetime = true;

        try
        {
            var principal = tokenHandler.ValidateToken(refreshToken, validationParameters, out var validatedToken);

            // Verify this is a refresh token
            var tokenTypeClaim = principal.FindFirst("token_type");
            if (tokenTypeClaim?.Value != "refresh")
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}

public class JwtRefreshAuth
{
    public JwtRefreshAuth(JwtTokenConfig? jwtConfig, WebApplication app, ILogger? logger)
    {
        if (jwtConfig is null ||
            string.IsNullOrEmpty(jwtConfig.RefreshPath) ||
            string.IsNullOrEmpty(jwtConfig.Secret))
        {
            return;
        }

        var refreshPath = jwtConfig.RefreshPath;
        var tokenGenerator = new JwtTokenGenerator(jwtConfig);

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.Equals(refreshPath, StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            if (!string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            string refreshToken;
            try
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                var node = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                refreshToken = node!["refreshToken"]?.ToString() ?? throw new ArgumentException("refreshToken is null");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to read refresh token from request body.");
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new JwtErrorResponse { Error = "Invalid request body" },
                    JwtJsonContext.Default.JwtErrorResponse));
                return;
            }

            var principal = tokenGenerator.ValidateRefreshToken(refreshToken);
            if (principal is null)
            {
                logger?.LogWarning("Invalid or expired refresh token.");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new JwtErrorResponse { Error = "Invalid or expired refresh token" },
                    JwtJsonContext.Default.JwtErrorResponse));
                return;
            }

            // Filter out the token_type claim when creating new tokens
            var claims = principal.Claims.Where(c => c.Type != "token_type").ToList();
            var newPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, jwtConfig.Scheme));

            var (accessToken, newRefreshToken, accessExpires, refreshExpires) = tokenGenerator.GenerateTokens(newPrincipal);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "application/json";

            var response = new JwtTokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                TokenType = "Bearer",
                ExpiresIn = (int)(accessExpires - DateTime.UtcNow).TotalSeconds,
                RefreshExpiresIn = (int)(refreshExpires - DateTime.UtcNow).TotalSeconds
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(response, JwtJsonContext.Default.JwtTokenResponse));
        });

        logger?.LogDebug("JWT refresh endpoint registered at {RefreshPath}", refreshPath);
    }
}

/// <summary>
/// Static class that provides JWT token generation for login endpoints.
/// This is used by the NpgsqlRest login handler when JWT authentication scheme is specified.
/// </summary>
public static class JwtLoginHandler
{
    private static JwtTokenConfig? _config;
    private static JwtTokenGenerator? _tokenGenerator;

    /// <summary>
    /// Initializes the JWT login handler with the given configuration.
    /// Must be called before any login attempts with JWT scheme.
    /// </summary>
    public static void Initialize(JwtTokenConfig config)
    {
        _config = config;
        _tokenGenerator = new JwtTokenGenerator(config);
    }

    /// <summary>
    /// Gets whether JWT login handling is configured.
    /// </summary>
    public static bool IsConfigured => _config is not null && _tokenGenerator is not null;

    /// <summary>
    /// Gets the configured JWT scheme name, or null if not configured.
    /// </summary>
    public static string? Scheme => _config?.Scheme;

    /// <summary>
    /// Generates JWT tokens for the given claims principal and writes them to the response.
    /// Returns true if handled, false if JWT is not configured.
    /// </summary>
    public static async Task<bool> HandleLoginAsync(HttpContext context, ClaimsPrincipal principal)
    {
        if (_config is null || _tokenGenerator is null)
        {
            return false;
        }

        var (accessToken, refreshToken, accessExpires, refreshExpires) = _tokenGenerator.GenerateTokens(principal);

        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json";

        var response = new JwtTokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = (int)(accessExpires - DateTime.UtcNow).TotalSeconds,
            RefreshExpiresIn = (int)(refreshExpires - DateTime.UtcNow).TotalSeconds
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JwtJsonContext.Default.JwtTokenResponse));
        return true;
    }
}
