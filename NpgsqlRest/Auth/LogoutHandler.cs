using Npgsql;

namespace NpgsqlRest.Auth;

public static class LogoutHandler
{
    public static async Task HandleAsync(NpgsqlCommand command, RoutineEndpoint endpoint, HttpContext context, CancellationToken cancellationToken = default)
    {
        var path = string.Concat(endpoint.Method.ToString(), " ", endpoint.Path);
        command.LogCommand(path);

        if (endpoint.Routine.IsVoid)
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            await Results.SignOut().ExecuteAsync(context);
            await context.Response.CompleteAsync();
            return;
        }

        List<string> schemes = new(5);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader!.ReadAsync(cancellationToken))
        {
            for (int i = 0; i < reader?.FieldCount; i++)
            {
                if (await reader.IsDBNullAsync(i, cancellationToken) is true)
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
        await Results.SignOut(authenticationSchemes: schemes.Count == 0 ? null : schemes).ExecuteAsync(context);
        await context.Response.CompleteAsync();
    }
}