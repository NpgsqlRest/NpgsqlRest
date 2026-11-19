using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace NpgsqlRestClient;

public class BearerTokenConfig
{
    public string? Scheme { get; set; }
    public string? RefreshPath { get; set; }
}

public class TokenRefreshAuth
{
    public TokenRefreshAuth(BearerTokenConfig? bearerTokenConfig, WebApplication app, ILogger? logger)
    {
        var tokenConfig = bearerTokenConfig;
        
        if (bearerTokenConfig is null || 
            string.IsNullOrEmpty(bearerTokenConfig.RefreshPath) is true || 
            string.IsNullOrEmpty(bearerTokenConfig.Scheme) is true)
        {
            return;
        }

        // Use local variables from parameters - no instance references captured
        var refreshPath = bearerTokenConfig.RefreshPath;
        var scheme = bearerTokenConfig.Scheme;

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Equals(refreshPath, StringComparison.OrdinalIgnoreCase) is false)
            {
                await next(context);
                return;
            }

            if (string.Equals(context.Request.Method, "POST", StringComparison.OrdinalIgnoreCase) is false)
            {
                await next(context);
                return;
            }

            var bearerTokenOptions = app.Services.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.BearerToken.BearerTokenOptions>>();
            var refreshTokenProtector = bearerTokenOptions.Get(scheme).RefreshTokenProtector;
            var timeProvider = app.Services.GetRequiredService<TimeProvider>();

            string refreshToken;
            IResult result;

            try
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                var node = JsonNode.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                refreshToken = node!["refresh"]?.ToString() ?? throw new ArgumentException("refresh token is null");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to read refresh token from request body.");
                result = Results.BadRequest(context.Response);
                await result.ExecuteAsync(context);
                return;
            }

            // The refresh token is the credential: new tokens are issued for the principal INSIDE the refresh ticket.
            // (This used to sign in context.User - the caller of the refresh request - so any valid access token
            // combined with any unexpired refresh token produced new tokens for the access token's owner, and an
            // expired access token made a perfectly valid refresh token useless.)
            var refreshTicket = refreshTokenProtector.Unprotect(refreshToken);
            if (refreshTicket?.Properties?.ExpiresUtc is not { } expiresUtc ||
                timeProvider.GetUtcNow() >= expiresUtc ||
                refreshTicket.Principal.Identity?.IsAuthenticated is not true)
            {
                result = Results.Challenge();
                await result.ExecuteAsync(context);
                return;
            }

            if (Results.SignIn(principal: refreshTicket.Principal, authenticationScheme: scheme) is not SignInHttpResult signInResult)
            {
                logger?.LogError("Failed in constructing user identity for authentication.");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return;
            }
            await signInResult.ExecuteAsync(context);
        });
    }
}
