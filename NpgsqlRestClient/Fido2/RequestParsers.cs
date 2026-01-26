using System.Buffers;
using System.Text.Json;

namespace NpgsqlRestClient.Fido2;

internal static class RequestParsers
{
    public readonly record struct AuthenticationOptionsRequest(string? UserName);

    public readonly record struct RegistrationRequest(
        string? ChallengeId,
        string? CredentialId,
        string? AttestationObject,
        string? ClientDataJSON,
        string[]? Transports,
        string? DeviceName,
        string? UserContext,
        string? AnalyticsData);

    public readonly record struct AuthenticationRequest(
        string? ChallengeId,
        string? CredentialId,
        string? AuthenticatorData,
        string? ClientDataJSON,
        string? Signature,
        string? UserHandle,
        string? AnalyticsData);

    public readonly record struct ClientDataParsed(
        string? Type,
        string? Challenge,
        string? Origin,
        bool? CrossOrigin);

    public static AuthenticationOptionsRequest ParseAuthenticationOptionsRequest(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty)
            return default;

        var reader = new Utf8JsonReader(json);
        string? userName = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("userName"u8))
                {
                    reader.Read();
                    userName = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return new AuthenticationOptionsRequest(userName);
    }

    public static RegistrationRequest ParseRegistrationRequest(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty)
            return default;

        var reader = new Utf8JsonReader(json);
        string? challengeId = null, credentialId = null, attestationObject = null, clientDataJSON = null;
        string? deviceName = null, userContext = null, analyticsData = null;
        string[]? transports = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("challengeId"u8))
                {
                    reader.Read();
                    challengeId = reader.GetString();
                }
                else if (reader.ValueTextEquals("credentialId"u8))
                {
                    reader.Read();
                    credentialId = reader.GetString();
                }
                else if (reader.ValueTextEquals("attestationObject"u8))
                {
                    reader.Read();
                    attestationObject = reader.GetString();
                }
                else if (reader.ValueTextEquals("clientDataJSON"u8))
                {
                    reader.Read();
                    clientDataJSON = reader.GetString();
                }
                else if (reader.ValueTextEquals("transports"u8))
                {
                    reader.Read();
                    transports = ParseStringArray(ref reader);
                }
                else if (reader.ValueTextEquals("deviceName"u8))
                {
                    reader.Read();
                    deviceName = reader.GetString();
                }
                else if (reader.ValueTextEquals("userContext"u8))
                {
                    reader.Read();
                    // Read the entire object as raw JSON
                    userContext = ReadRawJson(ref reader, json);
                }
                else if (reader.ValueTextEquals("analyticsData"u8))
                {
                    reader.Read();
                    analyticsData = ReadRawJson(ref reader, json);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return new RegistrationRequest(
            challengeId, credentialId, attestationObject, clientDataJSON,
            transports, deviceName, userContext, analyticsData);
    }

    public static AuthenticationRequest ParseAuthenticationRequest(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty)
            return default;

        var reader = new Utf8JsonReader(json);
        string? challengeId = null, credentialId = null, authenticatorData = null;
        string? clientDataJSON = null, signature = null, userHandle = null, analyticsData = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("challengeId"u8))
                {
                    reader.Read();
                    challengeId = reader.GetString();
                }
                else if (reader.ValueTextEquals("credentialId"u8))
                {
                    reader.Read();
                    credentialId = reader.GetString();
                }
                else if (reader.ValueTextEquals("authenticatorData"u8))
                {
                    reader.Read();
                    authenticatorData = reader.GetString();
                }
                else if (reader.ValueTextEquals("clientDataJSON"u8))
                {
                    reader.Read();
                    clientDataJSON = reader.GetString();
                }
                else if (reader.ValueTextEquals("signature"u8))
                {
                    reader.Read();
                    signature = reader.GetString();
                }
                else if (reader.ValueTextEquals("userHandle"u8))
                {
                    reader.Read();
                    userHandle = reader.GetString();
                }
                else if (reader.ValueTextEquals("analyticsData"u8))
                {
                    reader.Read();
                    analyticsData = ReadRawJson(ref reader, json);
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return new AuthenticationRequest(
            challengeId, credentialId, authenticatorData, clientDataJSON,
            signature, userHandle, analyticsData);
    }

    public static ClientDataParsed ParseClientData(ReadOnlySpan<byte> json)
    {
        if (json.IsEmpty)
            return default;

        var reader = new Utf8JsonReader(json);
        string? type = null, challenge = null, origin = null;
        bool? crossOrigin = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    type = reader.GetString();
                }
                else if (reader.ValueTextEquals("challenge"u8))
                {
                    reader.Read();
                    challenge = reader.GetString();
                }
                else if (reader.ValueTextEquals("origin"u8))
                {
                    reader.Read();
                    origin = reader.GetString();
                }
                else if (reader.ValueTextEquals("crossOrigin"u8))
                {
                    reader.Read();
                    crossOrigin = reader.GetBoolean();
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        return new ClientDataParsed(type, challenge, origin, crossOrigin);
    }

    private static string[]? ParseStringArray(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            return null;

        var list = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (value != null)
                    list.Add(value);
            }
        }

        return list.Count > 0 ? list.ToArray() : null;
    }

    private static string? ReadRawJson(ref Utf8JsonReader reader, ReadOnlySpan<byte> json)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var startDepth = reader.CurrentDepth;
            var start = (int)reader.TokenStartIndex;
            while (reader.Read() && !(reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == startDepth)) { }
            var end = (int)reader.TokenStartIndex + 1;
            return System.Text.Encoding.UTF8.GetString(json.Slice(start, end - start));
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        return null;
    }
}
