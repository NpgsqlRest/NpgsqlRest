using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using NpgsqlTypes;

namespace NpgsqlRest.Auth;

public static class LoginHandler
{
    public static async Task HandleAsync(
        NpgsqlCommand command,
        HttpContext context,
        RetryStrategy? retryStrategy,
        CancellationToken cancellationToken = default,
        string tracePath = "HandleLoginAsync",
        bool performHashVerification = true,
        bool assignUserPrincipalToContext = false)
    {
        var connection = command.Connection;
        string? scheme = null;
        string? body = null;
        string? userId = null;
        string? userName = null;
        List<Claim> claims = new(10);
        var verificationPerformed = false;
        var verificationFailed = false;
        
        // Login and Basic Auth challenge commands carry the password: honor ObfuscateAuthParameterLogValues at Trace
        // level too (the Trace command log used to print every parameter value).
        command.LogCommand(tracePath, obfuscateValues: Options.AuthenticationOptions.ObfuscateAuthParameterLogValues);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderWithRetryAsync(retryStrategy, cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken) is false)
            {
                // Unknown user: when this login verifies passwords, spend the same work as a wrong password would
                // (one hash verification), so response time does not tell whether the account exists.
                if (performHashVerification is true &&
                    Options.AuthenticationOptions?.HashColumnName is { } hashColumnName &&
                    Options.AuthenticationOptions.PasswordHasher is { } dummyHasher &&
                    HasColumn(reader, hashColumnName) &&
                    FindPassword(command, out _) is { } attemptedPassword)
                {
                    dummyHasher.VerifyHashedPassword(GetDummyHash(dummyHasher), attemptedPassword);
                }
                await Results.Problem(
                    type: null,
                    statusCode: (int)HttpStatusCode.Unauthorized,
                    title: "Unauthorized",
                    detail: null).ExecuteAsync(context);
                return;
            }
            if (reader.FieldCount == 0)
            {
                await Results.Problem(
                    type: null,
                    statusCode: (int)HttpStatusCode.Unauthorized,
                    title: "Unauthorized",
                    detail: null).ExecuteAsync(context);
                return;
            }

            var schema = reader.GetColumnSchema();
            for (int i = 0; i < reader?.FieldCount; i++)
            {
                var column = schema[i];
                var colName = column.ColumnName;
                var isArray = column.NpgsqlDbType.HasValue && 
                              (column.NpgsqlDbType.Value & NpgsqlDbType.Array) == NpgsqlDbType.Array;
                if (Options.AuthenticationOptions.StatusColumnName is not null)
                {
                    if (string.Equals(colName, Options.AuthenticationOptions.StatusColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (column.NpgsqlDbType == NpgsqlDbType.Boolean)
                        {
                            var ok = reader?.GetBoolean(i);
                            if (ok is false)
                            {
                                await Results.Problem(
                                    type: null,
                                    statusCode: (int)HttpStatusCode.Unauthorized,
                                    title: "Unauthorized",
                                    detail: null).ExecuteAsync(context);
                                return;
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
                            Logger?.WrongStatusType(command.CommandText);
                            await Results.Problem(
                                type: null,
                                statusCode: (int)HttpStatusCode.InternalServerError,
                                title: "Internal Server Error",
                                detail: null).ExecuteAsync(context);
                            return;
                        }
                        continue;
                    }
                }

                if (Options.AuthenticationOptions.SchemeColumnName is not null)
                {
                    if (string.Equals(colName, Options.AuthenticationOptions.SchemeColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        scheme = reader?.GetValue(i).ToString();
                        continue;
                    }
                }

                if (Options.AuthenticationOptions.BodyColumnName is not null)
                {
                    if (string.Equals(colName, Options.AuthenticationOptions.BodyColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        body = reader?.GetValue(i).ToString();
                        continue;
                    }
                }
                var (userNameCurrent, userIdCurrent) = AddClaimFromReader(i, isArray, reader!, claims, colName);
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
            if (performHashVerification is true && Options.AuthenticationOptions?.HashColumnName is not null)
            {
                int hashOrdinal = -1;
                for (int i = 0; i < reader!.FieldCount; i++)
                {
                    if (string.Equals(reader.GetName(i), Options.AuthenticationOptions.HashColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        hashOrdinal = i;
                        break;
                    }
                }

                // A hash column in the login result means the password MUST be verified here. Every case in
                // which the verification cannot run - no stored hash (NULL), no password value (missing, JSON
                // null), no parameter matching PasswordParameterNameContains, no PasswordHasher configured -
                // fails the login. Previously these cases skipped the check and signed the user in.
                if (hashOrdinal >= 0)
                {
                    verificationPerformed = true;
                    string? hash = reader!.IsDBNull(hashOrdinal) ? null : reader.GetValue(hashOrdinal).ToString();
                    var pass = FindPassword(command, out var foundPasswordParameter);
                    if (foundPasswordParameter is false)
                    {
                        Logger?.CantFindPasswordParameter(tracePath,
                            command.Parameters.Select(p => (p as NpgsqlRestParameter)?.ActualName)?.ToArray(),
                            Options.AuthenticationOptions.PasswordParameterNameContains ?? "");
                    }

                    var hasher = Options.AuthenticationOptions.PasswordHasher;
                    if (hash is null || pass is null || hasher is null || hasher.VerifyHashedPassword(hash, pass) is false)
                    {
                        Logger?.VerifyPasswordFailed(tracePath, userId, userName);
                        // Same response as an unknown user (no row): a different status (this used to be 404) told
                        // callers which accounts exist.
                        await Results.Problem(
                            type: null,
                            statusCode: (int)HttpStatusCode.Unauthorized,
                            title: "Unauthorized",
                            detail: null).ExecuteAsync(context);
                        verificationFailed = true;
                    }
                }
            }
        }

        if (verificationPerformed is true)
        {
            if (verificationFailed is true)
            {
                if (string.IsNullOrEmpty(Options.AuthenticationOptions?.PasswordVerificationFailedCommand) is false)
                {
                    await using var failedCommand = connection?.CreateCommand();
                    if (failedCommand is not null)
                    {
                        failedCommand.CommandText = Options.AuthenticationOptions.PasswordVerificationFailedCommand;
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
                        failedCommand.LogCommand(tracePath);
                        await failedCommand.ExecuteNonQueryWithRetryAsync(retryStrategy, cancellationToken);
                    }
                }
                return;
            }
            else
            {
                if (string.IsNullOrEmpty(Options.AuthenticationOptions?.PasswordVerificationSucceededCommand) is false)
                {
                    await using var succeededCommand = connection?.CreateCommand();
                    if (succeededCommand is not null)
                    {
                        succeededCommand.CommandText = Options.AuthenticationOptions.PasswordVerificationSucceededCommand;
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
                        succeededCommand.LogCommand(tracePath);
                        await succeededCommand.ExecuteNonQueryWithRetryAsync(retryStrategy, cancellationToken);
                    }
                }
            }
        }

        if (context.Response.StatusCode == (int)HttpStatusCode.OK)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                scheme ?? Options.AuthenticationOptions?.DefaultAuthenticationType,
                nameType: Options.AuthenticationOptions?.DefaultNameClaimType,
                roleType: Options.AuthenticationOptions?.DefaultRoleClaimType));

            if (assignUserPrincipalToContext is false)
            {
                // Check if a custom login handler is configured
                if (Options.AuthenticationOptions?.CustomLoginHandler is not null)
                {
                    var handled = await Options.AuthenticationOptions.CustomLoginHandler(context, principal, scheme);
                    if (handled)
                    {
                        // Custom handler handled the response, skip default SignIn
                        return;
                    }
                }

                if (Results.SignIn(principal: principal, authenticationScheme: scheme) is not SignInHttpResult result)
                {
                    Logger?.LogError("Failed in constructing user identity for authentication.");
                    await Results.Problem(
                        type: null,
                        statusCode: (int)HttpStatusCode.InternalServerError,
                        title: "Internal Server Error",
                        detail: null).ExecuteAsync(context);
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
            if (body is not null)
            {
                context.Response.ContentType = Options.AuthenticationOptions?.ResponseTypeColumnName ?? "application/json";
                await context.Response.WriteAsync(body, cancellationToken);
            }
        }
    }

    private static bool HasColumn(NpgsqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The value of the password parameter (name contains PasswordParameterNameContains), or null.</summary>
    private static string? FindPassword(NpgsqlCommand command, out bool found)
    {
        found = false;
        var passwordParameterNameContains = Options.AuthenticationOptions.PasswordParameterNameContains;
        if (passwordParameterNameContains is null)
        {
            return null;
        }
        for (var j = 0; j < command.Parameters.Count; j++)
        {
            var parameter = command.Parameters[j];
            var name = (parameter as NpgsqlRestParameter)?.ActualName;
            if (name is not null && name.Contains(passwordParameterNameContains, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return parameter.Value is null || parameter.Value == DBNull.Value ? null : parameter.Value.ToString();
            }
        }
        return null;
    }

    private static (IPasswordHasher Hasher, string Hash)? _dummyHash;

    /// <summary>A hash of a random password, made once per hasher: verifying against it costs what a real check costs.</summary>
    private static string GetDummyHash(IPasswordHasher hasher)
    {
        var current = _dummyHash;
        if (current is { } d && ReferenceEquals(d.Hasher, hasher))
        {
            return d.Hash;
        }
        var hash = hasher.HashPassword(Guid.NewGuid().ToString("N"));
        _dummyHash = (hasher, hash);
        return hash;
    }

    private static (string? userName, string? userId) AddClaimFromReader(
        int i,
        bool isArray,
        NpgsqlDataReader reader,
        List<Claim> claims, 
        string colName)
    {
        if (string.Equals(colName, Options.AuthenticationOptions.HashColumnName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(colName, Options.AuthenticationOptions.BodyColumnName, StringComparison.OrdinalIgnoreCase))
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
            if (string.Equals(claimType, Options.AuthenticationOptions.DefaultNameClaimType, StringComparison.Ordinal))
            {
                userName = null;
            }
            else if (string.Equals(claimType, Options.AuthenticationOptions.DefaultUserIdClaimType, StringComparison.Ordinal))
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
            if (string.Equals(claimType, Options.AuthenticationOptions.DefaultNameClaimType, StringComparison.Ordinal))
            {
                userName = value;
            }
            else if (string.Equals(claimType, Options.AuthenticationOptions.DefaultUserIdClaimType, StringComparison.Ordinal))
            {
                userId = value;
            }
        }

        return (userName, userId);
    }
}