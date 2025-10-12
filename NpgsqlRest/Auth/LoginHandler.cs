﻿using System;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;
using NpgsqlTypes;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.Auth;

public static class LoginHandler
{
    public static async Task HandleAsync(
        NpgsqlCommand command,
        HttpContext context,
        
        RetryStrategy? retryStrategy,
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
        List<Claim> claims = new(10);
        var verificationPerformed = false;
        var verificationFailed = false;
        
        logger?.TraceCommand(command, tracePath);
        await using (NpgsqlDataReader reader = await command.ExecuteReaderWithRetryAsync(retryStrategy, logger))
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
                if (Options.AuthenticationOptions.StatusColumnName is not null)
                {
                    if (string.Equals(colName, Options.AuthenticationOptions.StatusColumnName, StringComparison.OrdinalIgnoreCase))
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

                if (Options.AuthenticationOptions.SchemeColumnName is not null)
                {
                    if (string.Equals(colName, Options.AuthenticationOptions.SchemeColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        scheme = reader?.GetValue(i).ToString();
                        continue;
                    }
                }

                if (Options.AuthenticationOptions.MessageColumnName is not null)
                {
                    if (string.Equals(colName, Options.AuthenticationOptions.MessageColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        message = reader?.GetValue(i).ToString();
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
            if (performHashVerification is true)
            {
                if (Options.AuthenticationOptions?.HashColumnName is not null &&
                    Options.AuthenticationOptions.PasswordHasher is not null &&
                    Options.AuthenticationOptions?.PasswordParameterNameContains is not null)
                {
                    for (int i = 0; i < reader!.FieldCount; i++)
                    {
                        if (string.Equals(reader.GetName(i), Options.AuthenticationOptions.HashColumnName, StringComparison.OrdinalIgnoreCase))
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
                                        if (name is not null && name.Contains(Options.AuthenticationOptions.PasswordParameterNameContains, // found password parameter
                                            StringComparison.OrdinalIgnoreCase))
                                        {
                                            foundPasswordParameter = true;
                                            var pass = parameter?.Value?.ToString();
                                            if (pass is not null && parameter?.Value != DBNull.Value)
                                            {
                                                verificationPerformed = true;
                                                if (Options.AuthenticationOptions.PasswordHasher?.VerifyHashedPassword(hash, pass) is false)
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
                                            Options.AuthenticationOptions.PasswordParameterNameContains);
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
                        logger?.TraceCommand(failedCommand, tracePath);
                        await failedCommand.ExecuteNonQueryWithRetryAsync(retryStrategy, logger);
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
                        logger?.TraceCommand(succeededCommand, tracePath);
                        await succeededCommand.ExecuteNonQueryWithRetryAsync(retryStrategy, logger);
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

    private static (string? userName, string? userId) AddClaimFromReader(
        int i,
        bool isArray,
        NpgsqlDataReader reader,
        List<Claim> claims, 
        string colName)
    {
        if (string.Equals(colName, Options.AuthenticationOptions.HashColumnName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(colName, Options.AuthenticationOptions.MessageColumnName, StringComparison.OrdinalIgnoreCase))
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