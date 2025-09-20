using Microsoft.AspNetCore.Antiforgery;
using NpgsqlRest;
using NpgsqlRest.Auth;

namespace NpgsqlRestClient;

public class DefaultResponseParser(
    NpgsqlRestAuthenticationOptions options,
    string? antiforgeryFieldNameTag,
    string? antiforgeryTokenTag,
    string[]? availableClaimTypes)
{
    public ReadOnlySpan<char> Parse(
        ReadOnlySpan<char> input, 
        HttpContext context, 
        AntiforgeryTokenSet? tokenSet)
    {
        Dictionary<string, string> replacements = [];
        HashSet<string> arrayTypes = new(10)
        {
            options.DefaultRoleClaimType
        };
        foreach (var claim in context.User.Claims)
        {
            if (replacements.TryGetValue(claim.Type, out var existingValue))
            {
                replacements[claim.Type] = string.Concat(existingValue, ",", PgConverters.SerializeString(claim.Value));
                if (arrayTypes.Contains(claim.Type) is false)
                {
                    arrayTypes.Add(claim.Type);
                }
            }
            else
            {
                replacements.Add(claim.Type, PgConverters.SerializeString(claim.Value));
            }
        }
        foreach (var item in arrayTypes)
        {
            if (replacements.TryGetValue(item, out var existingValue) is true)
            {
                replacements[item] = string.Concat(Consts.OpenBracket, existingValue, Consts.CloseBracket);
            }
            else
            {
                replacements[item] = string.Concat(Consts.OpenBracket, Consts.CloseBracket);
            }
        }

        if (replacements.ContainsKey(options.DefaultUserIdClaimType) is false)
        {
            replacements.Add(options.DefaultUserIdClaimType, Consts.Null);
        }
        if (replacements.ContainsKey(options.DefaultNameClaimType) is false)
        {
            replacements.Add(options.DefaultNameClaimType, Consts.Null);
        }

        if (tokenSet is not null && (antiforgeryFieldNameTag is not null || antiforgeryTokenTag is not null))
        {
            if (antiforgeryFieldNameTag is not null)
            {
                replacements.Add(antiforgeryFieldNameTag, tokenSet.FormFieldName);
            }
            if (antiforgeryTokenTag is not null && tokenSet.RequestToken is not null)
            {
                replacements.Add(antiforgeryTokenTag, tokenSet.RequestToken);
            }
        }
        if (availableClaimTypes is not null && availableClaimTypes.Length > 0)
        {
            for (int i = 0; i < availableClaimTypes.Length; i++)
            {
                replacements.TryAdd(availableClaimTypes[i], Consts.Null);
            }
        }
        return Formatter.FormatString(input, replacements);
    }
}