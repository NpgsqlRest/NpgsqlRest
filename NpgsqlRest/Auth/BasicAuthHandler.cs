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
        NpgsqlConnection connection)
    {
        var realm =
            string.IsNullOrEmpty(endpoint.BasicAuth?.Realm) ? 
                string.IsNullOrEmpty(Options.AuthenticationOptions.BasicAuth.Realm) ? BasicAuthOptions.DefaultRealm : Options.AuthenticationOptions.BasicAuth.Realm : 
                endpoint.BasicAuth.Realm;
        
        if (context.Request.IsSsl() is false)
        {
            if (Options.AuthenticationOptions.BasicAuth.SslRequirement == SslRequirement.Required)
            {
                Logger?.LogError("Basic authentication with SslRequirement 'Required' cannot be used when SSL is disabled.");
                await Challenge(context, realm);
                return;
            }
            if (Options.AuthenticationOptions.BasicAuth.SslRequirement == SslRequirement.Warning)
            {
                Logger?.LogWarning("Using Basic Authentication when SSL is disabled.");
            }
            else if (Options.AuthenticationOptions.BasicAuth.SslRequirement == SslRequirement.Ignore)
            {
                Logger?.LogDebug("WARNING: Using Basic Authentication when SSL is disabled.");
            }
        }
        
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader) is false)
        {
            Logger?.LogWarning("No Authorization header found in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
            await Challenge(context, realm);
            return;
        }

        var authValue = authHeader.FirstOrDefault();
        if (string.IsNullOrEmpty(authValue) || !authValue.StartsWith("Basic "))
        {
            Logger?.LogWarning("Authorization header value missing or malformed found in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
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
            Logger?.LogError(ex, "Failed to decode Basic Authentication credentials in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
            await Challenge(context, realm);
            return;
        }

        var colonIndex = decodedCredentials.IndexOf(':');
        if (colonIndex == -1)
        {
            Logger?.LogWarning("Authorization header value malformed found in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
            await Challenge(context, realm);
            return;
        }
        var username = decodedCredentials[..colonIndex].ToString();
        var password = decodedCredentials[(colonIndex + 1)..].ToString();
        
        if (string.IsNullOrEmpty(username) is true || string.IsNullOrEmpty(password) is true)
        {
            Logger?.LogWarning("Username or password missing in request with Basic Authentication Realm {realm}. Request: {Path}",
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
            await Challenge(context, realm);
            return;
        }

        string? basicAuthPassword = null;
        if (endpoint.BasicAuth?.Users.ContainsKey(username) is true)
        {
            basicAuthPassword = endpoint.BasicAuth.Users[username];
        }
        else if (Options.AuthenticationOptions.BasicAuth?.Users.ContainsKey(username) is true)
        {
            basicAuthPassword = Options.AuthenticationOptions.BasicAuth.Users[username];
        }
        
        bool? passwordValid = null;
        string? challengeCommand = endpoint.BasicAuth?.ChallengeCommand ?? Options.AuthenticationOptions.BasicAuth?.ChallengeCommand;
        
        if (basicAuthPassword is not null)
        {
            if (Options.AuthenticationOptions.BasicAuth?.UseDefaultPasswordHasher is true)
            {
                if (Options.AuthenticationOptions.PasswordHasher is null)
                {
                    Logger?.LogError("PasswordHasher not configured for Basic Authentication Realm {realm}. Request: {Path}",
                        realm,
                        string.Concat(endpoint.Method.ToString(), endpoint.Path));
                    await Challenge(context, realm);
                    return;
                }
                passwordValid =
                    Options.AuthenticationOptions.PasswordHasher?.VerifyHashedPassword(basicAuthPassword, password);
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
                Logger?.LogError("No Basic Authentication user configured for user {username} in request with Basic Authentication Realm {realm}. Request: {Path}",
                    username,
                    realm,
                    string.Concat(endpoint.Method.ToString(), endpoint.Path));
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
                command.Parameters.Add(NpgsqlRestParameter.CreateTextParam(endpoint.Path));
            }

            await LoginHandler.HandleAsync(
                command, 
                context, 
                endpoint.RetryStrategy,
                tracePath: string.Concat(endpoint.Method.ToString(), " ", endpoint.Path),
                performHashVerification: false, 
                assignUserPrincipalToContext: true);

            if (context.Response.StatusCode == (int)HttpStatusCode.OK)
            {
                return;
            }

            Logger?.LogError("ChallengeCommand denied user {username} in request with Basic Authentication Realm {realm}. Request: {Path}",
                username,
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
            await Challenge(context, realm);
            return;
        }

        if (passwordValid is true)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(Options.AuthenticationOptions.DefaultNameClaimType, username)
                ],
                Options.AuthenticationOptions.DefaultAuthenticationType,
                nameType: Options.AuthenticationOptions.DefaultNameClaimType,
                roleType: Options.AuthenticationOptions.DefaultRoleClaimType));
            context.User = principal;
        }
        else
        {
            Logger?.LogWarning("Invalid password for user {username} in request with Basic Authentication Realm {realm}. Request: {Path}",
                username,
                realm,
                string.Concat(endpoint.Method.ToString(), endpoint.Path));
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