using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NpgsqlRestClient.Fido2;

namespace NpgsqlRestTests.Fido2;


/// <summary>
/// Unit tests for the AttestationValidator class.
/// Tests WebAuthn attestation validation during passkey registration.
/// </summary>
public class AttestationValidatorTests
{
    // Test-only DTO for creating clientDataJSON
    private record TestClientData(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("challenge")] string? Challenge,
        [property: JsonPropertyName("origin")] string? Origin,
        [property: JsonPropertyName("crossOrigin")] bool? CrossOrigin = null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string TestRpId = "example.com";
    private static readonly string[] TestOrigins = ["https://example.com"];
    private static readonly byte[] TestChallenge = new byte[32];

    static AttestationValidatorTests()
    {
        RandomNumberGenerator.Fill(TestChallenge);
    }

    #region ClientData Validation Tests

    [Fact]
    public void Validate_WithInvalidClientDataJson_ReturnsFailure()
    {
        // Arrange
        var invalidClientDataJson = Encoding.UTF8.GetBytes("not valid json");
        var attestationObject = CreateValidAttestationObject();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            invalidClientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.InvalidClientData);
    }

    [Fact]
    public void Validate_WithWrongType_ReturnsFailure()
    {
        // Arrange - Use "webauthn.get" instead of "webauthn.create"
        var clientData = new TestClientData(
            Type: "webauthn.get",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: TestOrigins[0]);
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateValidAttestationObject();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.InvalidType);
    }

    [Fact]
    public void Validate_WithChallengeMismatch_ReturnsFailure()
    {
        // Arrange
        var differentChallenge = new byte[32];
        RandomNumberGenerator.Fill(differentChallenge);

        var clientData = new TestClientData(
            Type: "webauthn.create",
            Challenge: AttestationValidator.Base64UrlEncode(differentChallenge),
            Origin: TestOrigins[0]);
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateValidAttestationObject();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.ChallengeMismatch);
    }

    [Fact]
    public void Validate_WithOriginMismatch_ReturnsFailure()
    {
        // Arrange
        var clientData = new TestClientData(
            Type: "webauthn.create",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: "https://evil.com");
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateValidAttestationObject();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.OriginMismatch);
    }

    [Fact]
    public void Validate_WithEmptyOriginsList_AcceptsAnyOrigin()
    {
        // Arrange
        var clientData = new TestClientData(
            Type: "webauthn.create",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: "https://any-origin.com");
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateValidAttestationObjectWithAuthData();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            [], // Empty origins list
            TestRpId);

        // Assert - Should fail for different reason (RP ID hash mismatch), not origin
        result.Error.Should().NotContain(ValidationError.OriginMismatch);
    }

    #endregion

    #region Attestation Object Validation Tests

    [Fact]
    public void Validate_WithInvalidAttestationObject_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        byte[] invalidAttestationObject = [0xFF, 0xFE, 0xFD]; // Invalid CBOR

        // Act
        var result = AttestationValidator.Validate(
            invalidAttestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.InvalidAttestationObject);
    }

    [Fact]
    public void Validate_WithInvalidAuthenticatorData_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateAttestationObjectWithShortAuthData();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.InvalidAuthenticatorData);
    }

    [Fact]
    public void Validate_WithRpIdHashMismatch_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateAttestationObjectWithWrongRpId();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.RpIdHashMismatch);
    }

    [Fact]
    public void Validate_WithUserNotPresent_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateAttestationObjectWithoutUserPresent();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.UserNotPresent);
    }

    [Fact]
    public void Validate_WithUserVerificationRequired_AndNotVerified_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var attestationObject = CreateAttestationObjectWithUserPresentOnly();

        // Act
        var result = AttestationValidator.Validate(
            attestationObject,
            clientDataJson,
            TestChallenge,
            TestOrigins,
            TestRpId,
            requireUserVerification: true);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.UserVerificationRequired);
    }

    #endregion

    #region Base64Url Encoding Tests

    [Theory]
    [InlineData("", null)]
    [InlineData("dGVzdA", "test")]
    [InlineData("SGVsbG8gV29ybGQ", "Hello World")]
    public void Base64UrlDecode_WithValidInput_ReturnsExpectedBytes(string input, string? expectedString)
    {
        // Act
        var result = AttestationValidator.Base64UrlDecode(input);

        // Assert
        if (expectedString == null)
        {
            result.Should().BeNull();
        }
        else
        {
            result.Should().NotBeNull();
            Encoding.UTF8.GetString(result!).Should().Be(expectedString);
        }
    }

    [Fact]
    public void Base64UrlDecode_WithNullInput_ReturnsNull()
    {
        // Act
        var result = AttestationValidator.Base64UrlDecode(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Base64UrlEncode_ThenDecode_ReturnsOriginalBytes()
    {
        // Arrange
        var original = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        // Act
        var encoded = AttestationValidator.Base64UrlEncode(original);
        var decoded = AttestationValidator.Base64UrlDecode(encoded);

        // Assert
        decoded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Base64UrlEncode_RemovesPadding()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02 }; // Would normally produce padding

        // Act
        var encoded = AttestationValidator.Base64UrlEncode(data);

        // Assert
        encoded.Should().NotContain("=");
    }

    [Fact]
    public void Base64UrlEncode_ReplacesUrlUnsafeCharacters()
    {
        // Arrange - Data that would produce + and / in standard base64
        var data = new byte[] { 0xFB, 0xFF, 0xFE };

        // Act
        var encoded = AttestationValidator.Base64UrlEncode(data);

        // Assert
        encoded.Should().NotContain("+");
        encoded.Should().NotContain("/");
    }

    #endregion

    #region Helper Methods

    private static TestClientData CreateValidClientData()
    {
        return new TestClientData(
            Type: "webauthn.create",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: TestOrigins[0]);
    }

    private static byte[] CreateValidAttestationObject()
    {
        // Minimal CBOR attestation object with empty authData
        return
        [
            0xA3,                                       // Map with 3 items
            0x63, 0x66, 0x6D, 0x74,                     // Text string "fmt"
            0x64, 0x6E, 0x6F, 0x6E, 0x65,               // Text string "none"
            0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61, // Text string "authData"
            0x40,                                       // Empty byte string
            0x67, 0x61, 0x74, 0x74, 0x53, 0x74, 0x6D, 0x74, // Text string "attStmt"
            0xA0                                        // Empty map
        ];
    }

    private static byte[] CreateValidAttestationObjectWithAuthData()
    {
        // Create authenticator data with correct RP ID hash
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var flags = (byte)(AuthenticatorDataFlags.UP | AuthenticatorDataFlags.AT); // User present + attested credential data
        var signCount = new byte[] { 0x00, 0x00, 0x00, 0x00 }; // 0

        // Minimal attested credential data
        var aaguid = new byte[16]; // Empty AAGUID
        var credentialIdLength = new byte[] { 0x00, 0x10 }; // 16 bytes
        var credentialId = new byte[16];
        RandomNumberGenerator.Fill(credentialId);

        // Minimal COSE key (ES256)
        var coseKey = CreateMinimalES256CoseKey();

        var authData = new List<byte>();
        authData.AddRange(rpIdHash);         // 32 bytes
        authData.Add(flags);                  // 1 byte
        authData.AddRange(signCount);         // 4 bytes
        authData.AddRange(aaguid);            // 16 bytes
        authData.AddRange(credentialIdLength); // 2 bytes
        authData.AddRange(credentialId);      // 16 bytes
        authData.AddRange(coseKey);           // variable

        return CreateAttestationObjectWithAuthData(authData.ToArray());
    }

    private static byte[] CreateAttestationObjectWithShortAuthData()
    {
        // Auth data shorter than 37 bytes (minimum)
        var shortAuthData = new byte[10];
        return CreateAttestationObjectWithAuthData(shortAuthData);
    }

    private static byte[] CreateAttestationObjectWithWrongRpId()
    {
        var wrongRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes("wrong.com"));
        var flags = (byte)(AuthenticatorDataFlags.UP | AuthenticatorDataFlags.AT);
        var signCount = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var aaguid = new byte[16];
        var credentialIdLength = new byte[] { 0x00, 0x10 };
        var credentialId = new byte[16];
        var coseKey = CreateMinimalES256CoseKey();

        var authData = new List<byte>();
        authData.AddRange(wrongRpIdHash);
        authData.Add(flags);
        authData.AddRange(signCount);
        authData.AddRange(aaguid);
        authData.AddRange(credentialIdLength);
        authData.AddRange(credentialId);
        authData.AddRange(coseKey);

        return CreateAttestationObjectWithAuthData(authData.ToArray());
    }

    private static byte[] CreateAttestationObjectWithoutUserPresent()
    {
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var flags = (byte)AuthenticatorDataFlags.AT; // No user present flag
        var signCount = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var aaguid = new byte[16];
        var credentialIdLength = new byte[] { 0x00, 0x10 };
        var credentialId = new byte[16];
        var coseKey = CreateMinimalES256CoseKey();

        var authData = new List<byte>();
        authData.AddRange(rpIdHash);
        authData.Add(flags);
        authData.AddRange(signCount);
        authData.AddRange(aaguid);
        authData.AddRange(credentialIdLength);
        authData.AddRange(credentialId);
        authData.AddRange(coseKey);

        return CreateAttestationObjectWithAuthData(authData.ToArray());
    }

    private static byte[] CreateAttestationObjectWithUserPresentOnly()
    {
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(TestRpId));
        var flags = (byte)(AuthenticatorDataFlags.UP | AuthenticatorDataFlags.AT); // UP but no UV
        var signCount = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        var aaguid = new byte[16];
        var credentialIdLength = new byte[] { 0x00, 0x10 };
        var credentialId = new byte[16];
        var coseKey = CreateMinimalES256CoseKey();

        var authData = new List<byte>();
        authData.AddRange(rpIdHash);
        authData.Add(flags);
        authData.AddRange(signCount);
        authData.AddRange(aaguid);
        authData.AddRange(credentialIdLength);
        authData.AddRange(credentialId);
        authData.AddRange(coseKey);

        return CreateAttestationObjectWithAuthData(authData.ToArray());
    }

    private static byte[] CreateAttestationObjectWithAuthData(byte[] authData)
    {
        // Build CBOR attestation object
        var result = new List<byte>
        {
            0xA3, // Map with 3 items
            0x63, 0x66, 0x6D, 0x74, // "fmt"
            0x64, 0x6E, 0x6F, 0x6E, 0x65, // "none"
            0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61 // "authData"
        };

        // Add auth data byte string
        if (authData.Length < 24)
        {
            result.Add((byte)(0x40 | authData.Length));
        }
        else if (authData.Length < 256)
        {
            result.Add(0x58);
            result.Add((byte)authData.Length);
        }
        else
        {
            result.Add(0x59);
            result.Add((byte)(authData.Length >> 8));
            result.Add((byte)(authData.Length & 0xFF));
        }
        result.AddRange(authData);

        // Add attStmt
        result.AddRange([0x67, 0x61, 0x74, 0x74, 0x53, 0x74, 0x6D, 0x74]); // "attStmt"
        result.Add(0xA0); // Empty map

        return [.. result];
    }

    private static byte[] CreateMinimalES256CoseKey()
    {
        // Generate a real EC key pair to get valid coordinates
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ecParams = ecdsa.ExportParameters(false);

        var result = new List<byte>
        {
            0xA5,                   // Map with 5 items
            0x01, 0x02,             // 1: 2 (kty = EC2)
            0x03, 0x26,             // 3: -7 (alg = ES256)
            0x20, 0x01,             // -1: 1 (crv = P-256)
            0x21, 0x58, 0x20        // -2: byte string (32 bytes) for X
        };
        result.AddRange(ecParams.Q.X!);
        result.AddRange([0x22, 0x58, 0x20]); // -3: byte string (32 bytes) for Y
        result.AddRange(ecParams.Q.Y!);

        return [.. result];
    }

    #endregion
}
