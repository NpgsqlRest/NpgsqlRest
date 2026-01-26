using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NpgsqlRestClient.Fido2;

namespace NpgsqlRestTests.Fido2;


/// <summary>
/// Unit tests for the AssertionValidator class.
/// Tests WebAuthn assertion validation during passkey authentication.
/// </summary>
public class AssertionValidatorTests
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

    static AssertionValidatorTests()
    {
        RandomNumberGenerator.Fill(TestChallenge);
    }

    #region ClientData Validation Tests

    [Fact]
    public void Validate_WithInvalidClientDataJson_ReturnsFailure()
    {
        // Arrange
        var invalidClientDataJson = Encoding.UTF8.GetBytes("not valid json");
        var (authenticatorData, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            invalidClientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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
        // Arrange - Use "webauthn.create" instead of "webauthn.get"
        var clientData = new TestClientData(
            Type: "webauthn.create",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: TestOrigins[0]);
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var (authenticatorData, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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

        var clientData = CreateValidClientData(differentChallenge);
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var (authenticatorData, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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
            Type: "webauthn.get",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: "https://evil.com");
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var (authenticatorData, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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
            Type: "webauthn.get",
            Challenge: AttestationValidator.Base64UrlEncode(TestChallenge),
            Origin: "https://any-origin.com");
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var (authenticatorData, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
            TestChallenge,
            [], // Empty origins list
            TestRpId);

        // Assert - Should fail for different reason (RP ID hash mismatch or signature), not origin
        result.Error.Should().NotContain(ValidationError.OriginMismatch);
    }

    #endregion

    #region Authenticator Data Validation Tests

    [Fact]
    public void Validate_WithTooShortAuthenticatorData_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var shortAuthenticatorData = new byte[10]; // Less than 37 bytes minimum
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            shortAuthenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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
        var authenticatorData = CreateAuthenticatorData("wrong.com", userPresent: true, signCount: 1);
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: false, signCount: 1);
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
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
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, userVerified: false, signCount: 1);
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
            TestChallenge,
            TestOrigins,
            TestRpId,
            requireUserVerification: true);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.UserVerificationRequired);
    }

    #endregion

    #region Sign Count Validation Tests

    [Fact]
    public void Validate_WithSignCountNotIncremented_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, signCount: 5);
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 10, // Higher than new count (5)
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.SignCountNotIncremented);
    }

    [Fact]
    public void Validate_WithSignCountEqual_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, signCount: 5);
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 5, // Same as new count
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.SignCountNotIncremented);
    }

    [Fact]
    public void Validate_WithBothSignCountsZero_SkipsSignCountValidation()
    {
        // Arrange - When both counters are 0, authenticator doesn't support counters
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, signCount: 0);
        var (_, signature, publicKey) = CreateValidAssertionData();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0, // Both are zero
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert - Should fail for signature reasons, not sign count
        result.Error.Should().NotContain(ValidationError.SignCountNotIncremented);
    }

    #endregion

    #region Signature Validation Tests

    [Fact]
    public void Validate_WithInvalidSignature_ReturnsFailure()
    {
        // Arrange
        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, signCount: 1);
        var invalidSignature = new byte[64]; // Invalid signature (all zeros)
        var publicKey = CreateES256CoseKey();

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            invalidSignature,
            publicKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(ValidationError.InvalidSignature);
    }

    [Fact]
    public void Validate_WithValidES256Signature_ReturnsSuccess()
    {
        // Arrange - Create a real signature
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKeyParams = ecdsa.ExportParameters(false);

        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, userVerified: true, signCount: 1);

        // Create signed data: authenticatorData || SHA-256(clientDataJSON)
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, authenticatorData.Length);

        // Sign with DER format (what WebAuthn uses)
        var signature = ecdsa.SignData(signedData, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        // Create COSE key
        var coseKey = CreateES256CoseKeyFromParams(publicKeyParams);

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            coseKey,
            CoseAlgorithm.ES256,
            storedSignCount: 0,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.NewSignCount.Should().Be(1);
        result.UserVerified.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidRS256Signature_ReturnsSuccess()
    {
        // Arrange - Create a real RSA signature
        using var rsa = RSA.Create(2048);
        var publicKeyParams = rsa.ExportParameters(false);

        var clientData = CreateValidClientData();
        var clientDataJson = JsonSerializer.SerializeToUtf8Bytes(clientData, JsonOptions);
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, userVerified: true, signCount: 1);

        // Create signed data: authenticatorData || SHA-256(clientDataJSON)
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, authenticatorData.Length);

        // Sign
        var signature = rsa.SignData(signedData, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Create COSE key
        var coseKey = CreateRS256CoseKeyFromParams(publicKeyParams);

        // Act
        var result = AssertionValidator.Validate(
            authenticatorData,
            clientDataJson,
            signature,
            coseKey,
            CoseAlgorithm.RS256,
            storedSignCount: 0,
            TestChallenge,
            TestOrigins,
            TestRpId);

        // Assert
        result.IsValid.Should().BeTrue();
        result.NewSignCount.Should().Be(1);
    }

    #endregion

    #region Helper Methods

    private static TestClientData CreateValidClientData(byte[]? challenge = null)
    {
        return new TestClientData(
            Type: "webauthn.get",
            Challenge: AttestationValidator.Base64UrlEncode(challenge ?? TestChallenge),
            Origin: TestOrigins[0]);
    }

    private static (byte[] AuthenticatorData, byte[] Signature, byte[] PublicKey) CreateValidAssertionData()
    {
        var authenticatorData = CreateAuthenticatorData(TestRpId, userPresent: true, signCount: 1);
        var signature = new byte[64]; // Dummy signature
        var publicKey = CreateES256CoseKey();

        return (authenticatorData, signature, publicKey);
    }

    private static byte[] CreateAuthenticatorData(
        string rpId,
        bool userPresent = true,
        bool userVerified = false,
        uint signCount = 0)
    {
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(rpId));

        byte flags = 0;
        if (userPresent) flags |= AuthenticatorDataFlags.UP;
        if (userVerified) flags |= AuthenticatorDataFlags.UV;

        var signCountBytes = new byte[4];
        signCountBytes[0] = (byte)(signCount >> 24);
        signCountBytes[1] = (byte)(signCount >> 16);
        signCountBytes[2] = (byte)(signCount >> 8);
        signCountBytes[3] = (byte)signCount;

        var result = new byte[37]; // 32 + 1 + 4
        rpIdHash.CopyTo(result, 0);
        result[32] = flags;
        signCountBytes.CopyTo(result, 33);

        return result;
    }

    private static byte[] CreateES256CoseKey()
    {
        // Generate a real EC key pair to get valid coordinates
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ecParams = ecdsa.ExportParameters(false);
        return CreateES256CoseKeyFromParams(ecParams);
    }

    private static byte[] CreateES256CoseKeyFromParams(ECParameters ecParams)
    {
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

    private static byte[] CreateRS256CoseKeyFromParams(RSAParameters rsaParams)
    {
        var n = rsaParams.Modulus!;
        var e = rsaParams.Exponent!;

        var result = new List<byte>
        {
            0xA4,                               // Map with 4 items
            0x01, 0x03,                         // 1: 3 (kty = RSA)
            0x03, 0x39, 0x01, 0x00,             // 3: -257 (alg = RS256)
        };

        // Add n (modulus)
        result.Add(0x20); // -1 label
        if (n.Length < 256)
        {
            result.Add(0x58);
            result.Add((byte)n.Length);
        }
        else
        {
            result.Add(0x59);
            result.Add((byte)(n.Length >> 8));
            result.Add((byte)(n.Length & 0xFF));
        }
        result.AddRange(n);

        // Add e (exponent)
        result.Add(0x21); // -2 label
        result.Add((byte)(0x40 | e.Length));
        result.AddRange(e);

        return [.. result];
    }

    #endregion
}
