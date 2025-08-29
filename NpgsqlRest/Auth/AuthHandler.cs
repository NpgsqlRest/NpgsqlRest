using System;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.Auth;

public static class AuthHandler
{
    public static async Task HandleLoginAsync(
        NpgsqlCommand command,
        HttpContext context,
        NpgsqlRestOptions options,
        ILogger? logger,
        string tracePath = "HandleLoginAsync",
        bool performHashVerification = true,
        bool assignUserPrincipalToContext = false)
    {
        var connection = command.Connection;
        string? scheme = null;
        string? message = null;
        string? userId = null;
        string? userName = null;
        var opts = options.AuthenticationOptions;
        List<Claim> claims = new(10);
        var verificationPerformed = false;
        var verificationFailed = false;
        
        logger?.TraceCommand(command, tracePath);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync() is false)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }
            if (reader.FieldCount == 0)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }

            var schema = await reader.GetColumnSchemaAsync();
            for (int i = 0; i < reader?.FieldCount; i++)
            {
                var column = schema[i];
                var colName = column.ColumnName;
                var isArray = column.NpgsqlDbType.HasValue && 
                              (column.NpgsqlDbType.Value & NpgsqlDbType.Array) == NpgsqlDbType.Array;
                if (opts.StatusColumnName is not null)
                {
                    if (string.Equals(colName, opts.StatusColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (column.NpgsqlDbType == NpgsqlDbType.Boolean)
                        {
                            var ok = reader?.GetBoolean(i);
                            if (ok is false)
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                            }
                        }
                        else if (column.NpgsqlDbType is NpgsqlDbType.Integer or NpgsqlDbType.Smallint or NpgsqlDbType.Bigint)
                        {
                            var status = reader?.GetInt32(i) ?? 200;
                            if (status != (int)HttpStatusCode.OK)
                            {
                                context.Response.StatusCode = status;
                            }
                        }
                        else
                        {
                            logger?.WrongStatusType(command.CommandText);
                            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                            await context.Response.CompleteAsync();
                            return;
                        }
                        continue;
                    }
                }

                if (opts.SchemeColumnName is not null)
                {
                    if (string.Equals(colName, opts.SchemeColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        scheme = reader?.GetValue(i).ToString();
                        continue;
                    }
                }

                if (opts.MessageColumnName is not null)
                {
                    if (string.Equals(colName, opts.MessageColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        message = reader?.GetValue(i).ToString();
                        continue;
                    }
                }
                var (userNameCurrent, userIdCurrent) = AddClaimFromReader(opts, i, isArray, reader!, claims, colName);
                if (userNameCurrent is not null)
                {
                    userName = userNameCurrent;
                }
                if (userIdCurrent is not null)
                {
                    userId = userIdCurrent;
                }
            }
            
            // hash verification last
            if (performHashVerification is true)
            {
                if (opts?.HashColumnName is not null &&
                    opts.PasswordHasher is not null &&
                    opts?.PasswordParameterNameContains is not null)
                {
                    for (int i = 0; i < reader!.FieldCount; i++)
                    {
                        if (string.Equals(reader.GetName(i), opts.HashColumnName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (reader?.IsDBNull(i) is false)
                            {
                                var hash = reader?.GetValue(i).ToString();
                                if (hash is not null)
                                {
                                    // find the password parameter
                                    var foundPasswordParameter = false;
                                    for (var j = 0; j < command.Parameters.Count; j++)
                                    {
                                        var parameter = command.Parameters[j];
                                        var name = (parameter as NpgsqlRestParameter)?.ActualName;
                                        if (name is not null && name.Contains(opts.PasswordParameterNameContains, // found password parameter
                                            StringComparison.OrdinalIgnoreCase))
                                        {
                                            foundPasswordParameter = true;
                                            var pass = parameter?.Value?.ToString();
                                            if (pass is not null && parameter?.Value != DBNull.Value)
                                            {
                                                verificationPerformed = true;
                                                if (opts.PasswordHasher?.VerifyHashedPassword(hash, pass) is false)
                                                {
                                                    logger?.VerifyPasswordFailed(tracePath, userId, userName);
                                                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                                    await context.Response.CompleteAsync();
                                                    verificationFailed = true;
                                                }
                                            }
                                            break;
                                        }
                                    }
                                    if (foundPasswordParameter is false)
                                    {
                                        logger?.CantFindPasswordParameter(tracePath,
                                            command.Parameters.Select(p => (p as NpgsqlRestParameter)?.ActualName)?.ToArray(),
                                            opts.PasswordParameterNameContains);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        if (verificationPerformed is true)
        {
            if (verificationFailed is true)
            {
                if (string.IsNullOrEmpty(opts?.PasswordVerificationFailedCommand) is false)
                {
                    await using var failedCommand = connection?.CreateCommand();
                    if (failedCommand is not null)
                    {
                        failedCommand.CommandText = opts.PasswordVerificationFailedCommand;
                        var paramCount = failedCommand.CommandText.PgCountParams();
                        if (paramCount >= 1)
                        {
                            failedCommand.Parameters.Add(NpgsqlRestParameter.CreateTextParam(scheme));
                        }
                        if (paramCount >= 2)
                        {
                            failedCommand.Parameters.Add(NpgsqlRestParameter.CreateTextParam(userId));
                        }
                        if (paramCount >= 3)
                        {
                            failedCommand.Parameters.Add(NpgsqlRestParameter.CreateTextParam(userName));
                        }
                        logger?.TraceCommand(failedCommand, tracePath);
                        await failedCommand.ExecuteNonQueryAsync();
                    }
                }
                return;
            }
            else
            {
                if (string.IsNullOrEmpty(opts?.PasswordVerificationSucceededCommand) is false)
                {
                    await using var succeededCommand = connection?.CreateCommand();
                    if (succeededCommand is not null)
                    {
                        succeededCommand.CommandText = opts.PasswordVerificationSucceededCommand;
                        var paramCount = succeededCommand.CommandText.PgCountParams();

                        if (paramCount >= 1)
                        {
                            succeededCommand.Parameters.Add(NpgsqlRestParameter.CreateTextParam(scheme));
                        }
                        if (paramCount >= 2)
                        {
                            succeededCommand.Parameters.Add(NpgsqlRestParameter.CreateTextParam(userId));
                        }
                        if (paramCount >= 3)
                        {
                            succeededCommand.Parameters.Add(NpgsqlRestParameter.CreateTextParam(userName));
                        }
                        logger?.TraceCommand(succeededCommand, tracePath);
                        await succeededCommand.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        if (context.Response.StatusCode == (int)HttpStatusCode.OK)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims, 
                scheme ?? opts?.DefaultAuthenticationType,
                nameType: opts?.DefaultNameClaimType,
                roleType: opts?.DefaultRoleClaimType));

            if (assignUserPrincipalToContext is false)
            {
                if (Results.SignIn(principal: principal, authenticationScheme: scheme) is not SignInHttpResult result)
                {
                    logger?.LogError("Failed in constructing user identity for authentication.");
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    return;
                }
                await result.ExecuteAsync(context);
            }
            else
            {
                context.User = principal;
            }
        }

        if (assignUserPrincipalToContext is false)
        {
            if (message is not null)
            {
                await context.Response.WriteAsync(message);
            }
        }
    }

    public static (string? userName, string? userId) AddClaimFromReader(
        NpgsqlRestAuthenticationOptions options,
        int i,
        //TypeDescriptor descriptor,
        bool isArray,
        NpgsqlDataReader reader,
        List<Claim> claims, 
        string colName)
    {
        if (string.Equals(colName, options.HashColumnName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(colName, options.MessageColumnName, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        string? claimType;
        string? userName = null;
        string? userId = null;

        claimType = colName;

        if (reader?.IsDBNull(i) is true)
        {
            claims.Add(new Claim(claimType, ""));
            if (string.Equals(claimType, options.DefaultNameClaimType, StringComparison.Ordinal))
            {
                userName = null;
            }
            else if (string.Equals(claimType, options.DefaultUserIdClaimType, StringComparison.Ordinal))
            {
                userId = null;
            }
        }
        else if (isArray)
        {
            object[]? values = reader?.GetValue(i) as object[];
            for (int j = 0; j < values?.Length; j++)
            {
                claims.Add(new Claim(claimType, values[j]?.ToString() ?? ""));
            }
        }
        else
        {
            string? value = reader?.GetValue(i)?.ToString();
            claims.Add(new Claim(claimType, value ?? ""));
            if (string.Equals(claimType, options.DefaultNameClaimType, StringComparison.Ordinal))
            {
                userName = value;
            }
            else if (string.Equals(claimType, options.DefaultUserIdClaimType, StringComparison.Ordinal))
            {
                userId = value;
            }
        }

        return (userName, userId);
    }

    public static async Task HandleLogoutAsync(NpgsqlCommand command, RoutineEndpoint endpoint, HttpContext context, ILogger? logger)
    {
        var path = string.Concat(endpoint.Method.ToString(), " ", endpoint.Url);
        logger?.TraceCommand(command, path);
        
        if (endpoint.Routine.IsVoid)
        {
            await command.ExecuteNonQueryAsync();
            await Results.SignOut().ExecuteAsync(context);
            await context.Response.CompleteAsync();
            return;
        }

        List<string> schemes = new(5);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        while (await reader.ReadAsync())
        {
            for (int i = 0; i < reader?.FieldCount; i++)
            {
                if (await reader.IsDBNullAsync(i) is true)
                {
                    continue;
                }

                var descriptor = endpoint.Routine.ColumnsTypeDescriptor[i];
                if (descriptor.IsArray)
                {
                    object[]? values = reader?.GetValue(i) as object[];
                    for (int j = 0; j < values?.Length; j++)
                    {
                        var value = values[j]?.ToString();
                        if (value is not null)
                        {
                            schemes.Add(value);
                        }
                    }
                }
                else
                {
                    string? value = reader?.GetValue(i)?.ToString();
                    if (value is not null)
                    {
                        schemes.Add(value);
                    }
                }
            }
        }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        await Results.SignOut(authenticationSchemes: schemes.Count == 0 ? null : schemes).ExecuteAsync(context);
        await context.Response.CompleteAsync();
    }

    public static async Task HandleBasicAuthAsync(
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

            await HandleLoginAsync(
                command, 
                context, 
                options, 
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