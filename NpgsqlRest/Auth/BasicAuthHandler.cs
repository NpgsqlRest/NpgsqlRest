using System;
using System.Net;
using System.Security.Claims;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.Auth;

public static class BasicAuthHandler
{
    public static async Task HandleAsync(
        HttpContext context, 
        RoutineEndpoint endpoint,
        NpgsqlRestOptions options, 
        NpgsqlConnection connection,
        ILogger? logger)
    {
        var realm =
            string.IsNullOrEmpty(endpoint.BasicAuth?.Realm) ? 
                string.IsNullOrEmpty(options.AuthenticationOptions.BasicAuth.Realm) ? BasicAuthOptions.DefaultRealm : options.AuthenticationOptions.BasicAuth.Realm : 
                endpoint.BasicAuth.Realm;
        
        if (context.Request.IsSsl() is false)
        {
            if (options.AuthenticationOptions.BasicAuth.SslRequirement == SslRequirement.Required)
            {
                logger?.LogError("Basic authentication with SslRequirement 'Required' cannot be used when SSL is disabled.");
                await Challenge(context, realm);
                return;
            }
            if (options.AuthenticationOptions.BasicAuth.SslRequirement == SslRequirement.Warning)
            {
                logger?.LogWarning("Using Basic Authentication when SSL is disabled.");
            }
            else if (options.AuthenticationOptions.BasicAuth.SslRequirement == SslRequirement.Ignore)
            {
                logger?.LogDebug("WARNING: Using Basic Authentication when SSL is disabled.");
            }
        }
        
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) is false)
        {
            logger?.LogWarning("No Authorization header found in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
            return;
        }

        var authValue = authHeader.FirstOrDefault();
        if (string.IsNullOrEmpty(authValue) || !authValue.StartsWith("Basic "))
        {
            logger?.LogWarning("Authorization header value missing or malformed found in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
            return;
        }

        ReadOnlySpan<char> decodedCredentials;
        try
        {
            decodedCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(authValue["Basic ".Length..]))
                .AsSpan();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to decode Basic Authentication credentials in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
            return;
        }

        var colonIndex = decodedCredentials.IndexOf(':');
        if (colonIndex == -1)
        {
            logger?.LogWarning("Authorization header value malformed found in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
            return;
        }
        var username = decodedCredentials[..colonIndex].ToString();
        var password = decodedCredentials[(colonIndex + 1)..].ToString();
        
        if (string.IsNullOrEmpty(username) is true || string.IsNullOrEmpty(password) is true)
        {
            logger?.LogWarning("Username or password missing in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
            return;
        }

        string? basicAuthPassword = null;
        if (endpoint.BasicAuth?.Users.ContainsKey(username) is true)
        {
            basicAuthPassword = endpoint.BasicAuth.Users[username];
        }
        else if (options.AuthenticationOptions.BasicAuth?.Users.ContainsKey(username) is true)
        {
            basicAuthPassword = options.AuthenticationOptions.BasicAuth.Users[username];
        }
        
        bool? passwordValid = null;
        string? challengeCommand = endpoint.BasicAuth?.ChallengeCommand ?? options.AuthenticationOptions.BasicAuth?.ChallengeCommand;
        
        if (basicAuthPassword is not null)
        {
            if (options.AuthenticationOptions.BasicAuth?.UseDefaultPasswordHasher is true)
            {
                if (options.AuthenticationOptions.PasswordHasher is null)
                {
                    logger?.LogError("PasswordHasher not configured for Basic Authentication Realm {realm}. Request: {Path}",
                        realm,
                        string.Concat(endpoint.Method.ToString(), endpoint.Url));
                    await Challenge(context, realm);
                    return;
                }
                passwordValid =
                    options.AuthenticationOptions.PasswordHasher?.VerifyHashedPassword(basicAuthPassword, password);
            }
            else
            {
                passwordValid = string.Equals(basicAuthPassword, password, StringComparison.Ordinal);
            }
        }
        else
        {
            if (string.IsNullOrEmpty(challengeCommand) is true)
            {
                // misconfigured: no user with password configured and no challenge command
                logger?.LogError("No Basic Authentication user configured for user {username} in request with Basic Authentication Realm {realm}. Request: {Path}",
                    username,
                    realm,
                    string.Concat(endpoint.Method.ToString(), endpoint.Url));
                await Challenge(context, realm);
                return;
            }
        }
        
        if (string.IsNullOrEmpty(challengeCommand) is false)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = challengeCommand;
            var paramCount = command.CommandText.PgCountParams();
            if (paramCount >= 1)
            {
                command.Parameters.Add(NpgsqlRestParameter.CreateTextParam(username));
            }
            if (paramCount >= 2)
            {
                command.Parameters.Add(NpgsqlRestParameter.CreateTextParam(password));
            }
            if (paramCount >= 3)
            {
                command.Parameters.Add(new NpgsqlParameter
                {
                    NpgsqlDbType = NpgsqlDbType.Boolean,
                    Value = passwordValid.HasValue ? passwordValid.Value : DBNull.Value
                });
            }
            if (paramCount >= 4)
            {
                command.Parameters.Add(NpgsqlRestParameter.CreateTextParam(realm));
            }
            if (paramCount >= 5)
            {
                command.Parameters.Add(NpgsqlRestParameter.CreateTextParam(endpoint.Url));
            }

            await LoginHandler.HandleAsync(
                command, 
                context, 
                options, 
                endpoint.RetryStrategy,
                logger, 
                tracePath: string.Concat(endpoint.Method.ToString(), " ", endpoint.Url),
                performHashVerification: false, 
                assignUserPrincipalToContext: true);

            if (context.Response.StatusCode == (int)HttpStatusCode.OK)
            {
                return;
            }

            logger?.LogError("ChallengeCommand denied user {username} in request with Basic Authentication Realm {realm}. Request: {Path}",
                username,
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
            return;
        }

        if (passwordValid is true)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(options.AuthenticationOptions.DefaultNameClaimType, username)
                ],
                options.AuthenticationOptions.DefaultAuthenticationType,
                nameType: options.AuthenticationOptions.DefaultNameClaimType,
                roleType: options.AuthenticationOptions.DefaultRoleClaimType));
            context.User = principal;
        }
        else
        {
            logger?.LogWarning("Invalid password for user {username} in request with Basic Authentication Realm {realm}. Request: {Path}",
                username,
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Url));
            await Challenge(context, realm);
        }
    }

    private static async Task Challenge(HttpContext context, string realm)
    {
        context.Response.StatusCode = 401;
        context.Response.Headers.Append("WWW-Authenticate", string.Concat("Basic realm=\"", realm, "\""));
        await context.Response.WriteAsync("Unauthorized");
    }
}