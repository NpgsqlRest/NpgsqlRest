using Microsoft.AspNetCore.Antiforgery;
using NpgsqlRest;
using NpgsqlRest.Auth;

namespace NpgsqlRestClient;

public class DefaultResponseParser(
    NpgsqlRestAuthenticationOptions options,
    string? antiforgeryFieldNameTag,
    string? antiforgeryTokenTag,
    Dictionary<string, string?>? availableClaims,
    Dictionary<string, string?>? availableEnvVars = null)
{
    // Env vars are resolved ONCE at construction. They don't change within the pod's lifetime, so
    // re-reading per request would just add System.Environment overhead with no payoff. A K8s pod
    // restart re-instantiates the middleware (and thus this parser), which re-reads the values.
    private readonly Dictionary<string, string> _envVarReplacements = ResolveEnvVars(availableEnvVars);

    private static Dictionary<string, string> ResolveEnvVars(Dictionary<string, string?>? envVars)
    {
        if (envVars is null || envVars.Count == 0)
        {
            return [];
        }
        var result = new Dictionary<string, string>(envVars.Count * 2);
        foreach (var (name, def) in envVars)
        {
            // present env var wins → configured default → empty string.
            var raw = Environment.GetEnvironmentVariable(name) ?? def;
            // JSON-escape exactly like the claim path below (PgConverters.SerializeString). The
            // substituted value is a complete JSON literal (a quoted, escaped string) so templates
            // use a bare {NAME} token with no surrounding quotes and an accidental quote/backslash in
            // the value cannot break the JS string. NOTE: the relaxed encoder does NOT escape '<'/'>',
            // so this is not a defence against a hostile value - env values are operator-controlled,
            // matching the claim path's trust model.
            // SECURITY: this is an explicit allowlist - only names listed in AvailableEnvVars are ever
            // read. NEVER widen this to the whole environment (Environment.GetEnvironmentVariables) -
            // that would leak secrets (DB passwords, keys) to every client.
            var serialized = PgConverters.SerializeString(raw ?? string.Empty);
            result[name] = serialized;
            // "!NAME" key ⇔ the name resolved to a real value (env set, or a configured default given).
            // The {!NAME}/{!NAME:fallback} template forms key off this in Formatter.TryResolveToken.
            if (raw is not null)
            {
                result[string.Concat("!", name)] = serialized;
            }
        }
        return result;
    }

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

        // Every entry so far is a real claim value - register the "!name" alias each so the
        // {!name}/{!name:fallback} forms resolve to the value (a claim value always beats an inline
        // fallback). Names containing ':' (URI-style claim types) are skipped: the fallback grammar
        // splits on ':' so the bang form can never address them anyway.
        if (replacements.Count > 0)
        {
            foreach (var (name, value) in replacements.ToArray())
            {
                if (name.IndexOf(':') < 0)
                {
                    replacements[string.Concat("!", name)] = value;
                }
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
                if (antiforgeryFieldNameTag.IndexOf(':') < 0)
                {
                    replacements[string.Concat("!", antiforgeryFieldNameTag)] = tokenSet.FormFieldName;
                }
            }
            if (antiforgeryTokenTag is not null && tokenSet.RequestToken is not null)
            {
                replacements.Add(antiforgeryTokenTag, tokenSet.RequestToken);
                if (antiforgeryTokenTag.IndexOf(':') < 0)
                {
                    replacements[string.Concat("!", antiforgeryTokenTag)] = tokenSet.RequestToken;
                }
            }
        }
        // Listed-but-absent claims fall back to their configured default, or Consts.Null (the
        // historical behaviour) when the array form gives no explicit default. Claim values added
        // above from context.User.Claims are already present and win via TryAdd - same for their
        // "!name" aliases, registered only when a real default exists.
        if (availableClaims is not null && availableClaims.Count > 0)
        {
            foreach (var (name, def) in availableClaims)
            {
                replacements.TryAdd(name, def ?? Consts.Null);
                if (def is not null && name.IndexOf(':') < 0)
                {
                    replacements.TryAdd(string.Concat("!", name), def);
                }
            }
        }
        // Feed app-wide env-var values into the same dictionary. Per-request claim values added above
        // WIN over env vars if the names ever collide (TryAdd does not overwrite).
        if (_envVarReplacements.Count > 0)
        {
            foreach (var (name, value) in _envVarReplacements)
            {
                replacements.TryAdd(name, value);
            }
        }
        return Formatter.FormatString(input, replacements);
    }
}