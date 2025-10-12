using System.Text.Json.Nodes;
using Microsoft.Extensions.Primitives;
using Npgsql;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest;

public class NpgsqlRestParameter : NpgsqlParameter
{
    public int Ordinal { get; private set; }
    public string ConvertedName { get; private set; }
    public string ActualName { get; private set; }
    public TypeDescriptor TypeDescriptor { get; init; }

    public ParamType ParamType { get; set; } = default!;
    public StringValues? QueryStringValues { get; set; } = null;
    public JsonNode? JsonBodyNode { get; set; } = null;
    public NpgsqlRestParameter? HashOf { get; set; } = null;

    public bool IsUploadMetadata { get; set; } = false;

    public bool IsIpAddress { get; set; } = false;
    public string? UserClaim { get; set; } = null;
    public bool IsUserClaims { get; set; } = false;
    public bool IsFromUserClaims => UserClaim is not null || IsUserClaims is true;

    public NpgsqlRestParameter(
         
        int ordinal, 
        string convertedName, 
        string actualName, 
        TypeDescriptor typeDescriptor)
    {
        Ordinal = ordinal;
        ConvertedName = convertedName;
        ActualName = actualName;
        TypeDescriptor = typeDescriptor;
        NpgsqlDbType = typeDescriptor.ActualDbType;

        if (actualName is not null && 
            Options.AuthenticationOptions.ParameterNameClaimsMapping.TryGetValue(actualName, out var claimName))
        {
            UserClaim = claimName;
        }

        if (actualName is not null && 
            string.Equals(Options.AuthenticationOptions.IpAddressParameterName, actualName, StringComparison.OrdinalIgnoreCase))
        {
            IsIpAddress = true;
        }

        if (actualName is not null && 
            string.Equals(Options.AuthenticationOptions.ClaimsJsonParameterName, actualName, StringComparison.OrdinalIgnoreCase))
        {
            IsUserClaims = true;
        }

        if (Options.UploadOptions.UseDefaultUploadMetadataParameter is true)
        {
            if (string.Equals(Options.UploadOptions.DefaultUploadMetadataParameterName, actualName, StringComparison.OrdinalIgnoreCase))
            {
                IsUploadMetadata = true;
            }
        }
    }

    internal string GetCacheStringValue()
    {
        if (Value == DBNull.Value)
        {
            return string.Empty;
        }
        if (TypeDescriptor.IsArray)
        {
            return string.Join(",", Value as object[] ?? []);
        }
        return Value?.ToString() ?? string.Empty;
    }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
#pragma warning disable CS8603 // Possible null reference return.
    public NpgsqlRestParameter NpgsqlRestParameterMemberwiseClone() => MemberwiseClone() as NpgsqlRestParameter;
    private NpgsqlRestParameter() { }
#pragma warning restore CS8603 // Possible null reference return.
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

    private static readonly NpgsqlRestParameter TextParam = new()
    {
        NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text,
        Value = DBNull.Value,
    };

    public static NpgsqlParameter CreateParamWithType(NpgsqlTypes.NpgsqlDbType type)
    {
        var result = TextParam.NpgsqlRestParameterMemberwiseClone();
        result.NpgsqlDbType = type;
        return result;
    }

    public static NpgsqlParameter CreateTextParam(object? value)
    {
        var result = TextParam.NpgsqlRestParameterMemberwiseClone();
        if (value is null)
        {
            return result;
        }
        result.Value = value;
        return result;
    }
}