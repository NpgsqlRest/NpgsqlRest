using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;

namespace NpgsqlRestClient.Fido2;

/// <summary>
/// Tamper protection for the passkey <c>userContext</c> round trip.
///
/// The options endpoints return a <c>userContext</c> object (produced by the database, plus the
/// <c>userHandle</c>) that the browser passes back unchanged to the completion endpoints, which hand it to the
/// completion SQL command. Without protection a client could edit it on the way back - for example change the
/// user id to attach its own passkey to someone else's account. The options endpoints therefore add a sealed
/// copy of the issued context (ASP.NET Core Data Protection), bound to the challenge id and - for adding a
/// passkey to an existing account - to the signed-in user. The completion endpoints accept the request only
/// when the seal is valid for that challenge and user, and then use the sealed (server-issued) copy, never the
/// client-supplied object.
/// </summary>
public static class PasskeyUserContext
{
    public const string SealPropertyName = "_protected";
    private const string Purpose = "NpgsqlRest.Passkey.UserContext.v1";

    /// <summary>
    /// Writes the <c>userContext</c> property: the issued context (database JSON + <c>userHandle</c>) plus the seal.
    /// </summary>
    public static void Write(
        Utf8JsonWriter writer,
        HttpContext context,
        string? userContextJson,
        string? userHandle,
        string? challengeId,
        string? boundUserId)
    {
        var issued = BuildIssuedJson(userContextJson, userHandle);
        writer.WritePropertyName("userContext");
        using var doc = JsonDocument.Parse(issued);
        writer.WriteStartObject();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(prop.Name, SealPropertyName, StringComparison.Ordinal))
            {
                continue;
            }
            prop.WriteTo(writer);
        }
        writer.WriteString(SealPropertyName, GetProtector(context).Protect(Payload(challengeId, boundUserId, issued)));
        writer.WriteEndObject();
    }

    /// <summary>
    /// Returns the server-issued <c>userContext</c> JSON when <paramref name="returnedUserContext"/> carries a valid
    /// seal for <paramref name="challengeId"/> and <paramref name="boundUserId"/>; otherwise <c>null</c>.
    /// </summary>
    public static string? Open(HttpContext context, string? returnedUserContext, string? challengeId, string? boundUserId)
    {
        if (string.IsNullOrEmpty(returnedUserContext))
        {
            return null;
        }
        try
        {
            var sealedValue = JsonNode.Parse(returnedUserContext)?[SealPropertyName]?.GetValue<string>();
            if (string.IsNullOrEmpty(sealedValue))
            {
                return null;
            }
            var payload = GetProtector(context).Unprotect(sealedValue);
            var first = payload.IndexOf('\n');
            var second = first < 0 ? -1 : payload.IndexOf('\n', first + 1);
            if (second < 0)
            {
                return null;
            }
            if (string.Equals(payload[..first], challengeId ?? "", StringComparison.Ordinal) is false ||
                string.Equals(payload[(first + 1)..second], boundUserId ?? "", StringComparison.Ordinal) is false)
            {
                return null;
            }
            return payload[(second + 1)..];
        }
        catch
        {
            // malformed JSON, non-string seal, or a seal that fails cryptographic validation
            return null;
        }
    }

    /// <summary>
    /// The identity a sealed add-passkey context is bound to: the signed-in user's id claim (falling back to the
    /// name claim), so a context issued to one user cannot be completed by another.
    /// </summary>
    public static string? GetBoundUserId(HttpContext context, NpgsqlRest.Auth.NpgsqlRestAuthenticationOptions authOptions)
    {
        if (context.User.Identity?.IsAuthenticated is not true)
        {
            return null;
        }
        return context.User.FindFirst(authOptions.DefaultUserIdClaimType)?.Value
            ?? context.User.FindFirst(authOptions.DefaultNameClaimType)?.Value;
    }

    private static string BuildIssuedJson(string? userContextJson, string? userHandle)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (!string.IsNullOrEmpty(userContextJson))
            {
                using var doc = JsonDocument.Parse(userContextJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, SealPropertyName, StringComparison.Ordinal) ||
                        string.Equals(prop.Name, "userHandle", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    prop.WriteTo(writer);
                }
            }
            writer.WriteString("userHandle", userHandle);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Payload(string? challengeId, string? boundUserId, string issuedJson) =>
        string.Concat(challengeId ?? "", "\n", boundUserId ?? "", "\n", issuedJson);

    private static IDataProtector GetProtector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
}
