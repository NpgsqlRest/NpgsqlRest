using NpgsqlRestClient.Fido2;

namespace NpgsqlRestTests.Fido2;

/// <summary>
/// Unit tests for the CborDecoder class.
/// Tests CBOR decoding functionality for WebAuthn attestation objects.
/// </summary>
public class CborDecoderTests
{
    #region DecodeAttestationObject Tests

    [Fact]
    public void DecodeAttestationObject_WithValidNoneFormat_ReturnsAttestationObject()
    {
        // Arrange - Minimal attestation object with "none" format
        // CBOR map with: fmt="none", authData=empty bytes, attStmt=empty map
        var cborData = CreateMinimalAttestationObjectCbor();

        // Act
        var result = CborDecoder.DecodeAttestationObject(cborData);

        // Assert
        result.Should().NotBeNull();
        result!.Fmt.Should().Be("none");
        result.AuthData.Should().NotBeNull();
    }

    [Fact]
    public void DecodeAttestationObject_WithNullInput_ReturnsNull()
    {
        // Act
        var result = CborDecoder.DecodeAttestationObject(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DecodeAttestationObject_WithEmptyInput_ReturnsNull()
    {
        // Act
        var result = CborDecoder.DecodeAttestationObject([]);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DecodeAttestationObject_WithInvalidCbor_ReturnsNull()
    {
        // Arrange - Invalid CBOR data (not a map)
        byte[] invalidData = [0x00, 0x01, 0x02];

        // Act
        var result = CborDecoder.DecodeAttestationObject(invalidData);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DecodeAttestationObject_WithPackedFormat_ParsesCorrectly()
    {
        // Arrange - Attestation object with "packed" format
        var cborData = CreateAttestationObjectCborWithFormat("packed");

        // Act
        var result = CborDecoder.DecodeAttestationObject(cborData);

        // Assert
        result.Should().NotBeNull();
        result!.Fmt.Should().Be("packed");
    }

    [Fact]
    public void DecodeAttestationObject_WithAuthData_ParsesCorrectly()
    {
        // Arrange - Attestation object with actual authData
        var authData = new byte[37]; // Minimum valid authData size
        System.Security.Cryptography.RandomNumberGenerator.Fill(authData);
        var cborData = CreateAttestationObjectWithAuthData(authData);

        // Act
        var result = CborDecoder.DecodeAttestationObject(cborData);

        // Assert
        result.Should().NotBeNull();
        result!.AuthData.Should().BeEquivalentTo(authData);
    }

    #endregion

    #region CredentialPublicKey Tests

    [Fact]
    public void CredentialPublicKey_Decode_WithES256Key_ReturnsValidKey()
    {
        // Arrange - COSE key for ES256 (ECDSA with P-256)
        var cborData = CreateES256CoseKeyCbor();

        // Act
        var result = CredentialPublicKey.Decode(cborData);

        // Assert
        result.Should().NotBeNull();
        result.Algorithm.Should().Be(COSEAlgorithmIdentifier.ES256);
        result.AlgorithmInt.Should().Be(CoseAlgorithm.ES256);
    }

    [Fact]
    public void CredentialPublicKey_Decode_WithRS256Key_ReturnsValidKey()
    {
        // Arrange - COSE key for RS256 (RSA with SHA-256)
        var cborData = CreateRS256CoseKeyCbor();

        // Act
        var result = CredentialPublicKey.Decode(cborData);

        // Assert
        result.Should().NotBeNull();
        result.Algorithm.Should().Be(COSEAlgorithmIdentifier.RS256);
        result.AlgorithmInt.Should().Be(CoseAlgorithm.RS256);
    }

    [Fact]
    public void CredentialPublicKey_Decode_WithInvalidCbor_ThrowsException()
    {
        // Arrange - Invalid CBOR data
        byte[] invalidData = [0xFF, 0xFE, 0xFD];

        // Act & Assert
        var action = () => CredentialPublicKey.Decode(invalidData);
        action.Should().Throw<Exception>();
    }

    [Fact]
    public void CredentialPublicKey_IsSupportedAlgorithm_ES256_ReturnsTrue()
    {
        CredentialPublicKey.IsSupportedAlgorithm(CoseAlgorithm.ES256).Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_IsSupportedAlgorithm_RS256_ReturnsTrue()
    {
        CredentialPublicKey.IsSupportedAlgorithm(CoseAlgorithm.RS256).Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_IsSupportedAlgorithm_PS256_ReturnsTrue()
    {
        CredentialPublicKey.IsSupportedAlgorithm(CoseAlgorithm.PS256).Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_IsSupportedAlgorithm_ES384_ReturnsTrue()
    {
        CredentialPublicKey.IsSupportedAlgorithm(CoseAlgorithm.ES384).Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_IsSupportedAlgorithm_ES512_ReturnsTrue()
    {
        CredentialPublicKey.IsSupportedAlgorithm(CoseAlgorithm.ES512).Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_IsSupportedAlgorithm_UnsupportedAlgorithm_ReturnsFalse()
    {
        CredentialPublicKey.IsSupportedAlgorithm(9999).Should().BeFalse();
    }

    [Fact]
    public void CredentialPublicKey_Verify_WithValidES256Signature_ReturnsTrue()
    {
        // Arrange - Create a real ECDSA key pair
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var publicKeyParams = ecdsa.ExportParameters(false);
        var coseKey = CreateES256CoseKeyFromParams(publicKeyParams);

        var data = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(data);

        // Sign the data with DER format (what WebAuthn uses)
        var signature = ecdsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence);

        var credentialPublicKey = CredentialPublicKey.Decode(coseKey);

        // Act
        var result = credentialPublicKey.Verify(data, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_Verify_WithValidRS256Signature_ReturnsTrue()
    {
        // Arrange - Create a real RSA key pair
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var publicKeyParams = rsa.ExportParameters(false);
        var coseKey = CreateRS256CoseKeyFromParams(publicKeyParams);

        var data = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(data);

        // Sign with PKCS1 padding
        var signature = rsa.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);

        var credentialPublicKey = CredentialPublicKey.Decode(coseKey);

        // Act
        var result = credentialPublicKey.Verify(data, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CredentialPublicKey_Verify_WithInvalidSignature_ReturnsFalse()
    {
        // Arrange
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var publicKeyParams = ecdsa.ExportParameters(false);
        var coseKey = CreateES256CoseKeyFromParams(publicKeyParams);

        var data = new byte[64];
        System.Security.Cryptography.RandomNumberGenerator.Fill(data);

        // Create an invalid signature
        var invalidSignature = new byte[70];
        System.Security.Cryptography.RandomNumberGenerator.Fill(invalidSignature);

        var credentialPublicKey = CredentialPublicKey.Decode(coseKey);

        // Act
        var result = credentialPublicKey.Verify(data, invalidSignature);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a minimal CBOR-encoded attestation object with "none" format.
    /// </summary>
    private static byte[] CreateMinimalAttestationObjectCbor()
    {
        // CBOR map with 3 items:
        // - "fmt" -> "none"
        // - "authData" -> empty byte string
        // - "attStmt" -> empty map
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

    /// <summary>
    /// Creates a CBOR-encoded attestation object with a specific format.
    /// </summary>
    private static byte[] CreateAttestationObjectCborWithFormat(string format)
    {
        var formatBytes = System.Text.Encoding.UTF8.GetBytes(format);
        var result = new List<byte>
        {
            0xA3,                                       // Map with 3 items
            0x63, 0x66, 0x6D, 0x74,                     // Text string "fmt"
            (byte)(0x60 | formatBytes.Length)           // Text string header for format
        };
        result.AddRange(formatBytes);
        result.AddRange([
            0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61, // Text string "authData"
            0x40,                                       // Empty byte string
            0x67, 0x61, 0x74, 0x74, 0x53, 0x74, 0x6D, 0x74, // Text string "attStmt"
            0xA0                                        // Empty map
        ]);
        return [.. result];
    }

    /// <summary>
    /// Creates a CBOR-encoded attestation object with authData.
    /// </summary>
    private static byte[] CreateAttestationObjectWithAuthData(byte[] authData)
    {
        var result = new List<byte>
        {
            0xA3,                                       // Map with 3 items
            0x63, 0x66, 0x6D, 0x74,                     // Text string "fmt"
            0x64, 0x6E, 0x6F, 0x6E, 0x65,               // Text string "none"
            0x68, 0x61, 0x75, 0x74, 0x68, 0x44, 0x61, 0x74, 0x61 // Text string "authData"
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

        result.AddRange([
            0x67, 0x61, 0x74, 0x74, 0x53, 0x74, 0x6D, 0x74, // Text string "attStmt"
            0xA0                                        // Empty map
        ]);
        return [.. result];
    }

    /// <summary>
    /// Creates a CBOR-encoded COSE key for ES256 (ECDSA with P-256) using a real key pair.
    /// </summary>
    private static byte[] CreateES256CoseKeyCbor()
    {
        // Generate a real EC key pair to get valid coordinates
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var ecParams = ecdsa.ExportParameters(false);
        return CreateES256CoseKeyFromParams(ecParams);
    }

    /// <summary>
    /// Creates a CBOR-encoded COSE key for RS256 (RSA with SHA-256).
    /// </summary>
    private static byte[] CreateRS256CoseKeyCbor()
    {
        // COSE Key for RS256:
        // { 1: 3 (kty=RSA), 3: -257 (alg=RS256), -1: n (modulus), -2: e (exponent) }
        var n = new byte[256]; // Dummy modulus (2048 bits)
        var e = new byte[] { 0x01, 0x00, 0x01 }; // Common exponent 65537
        Array.Fill(n, (byte)0xAB);

        var result = new List<byte>
        {
            0xA4,                               // Map with 4 items
            0x01, 0x03,                         // 1: 3 (kty = RSA)
            0x03, 0x39, 0x01, 0x00,             // 3: -257 (alg = RS256, encoded as -1-256 = negative, 2 byte length)
            0x20, 0x59, 0x01, 0x00              // -1: byte string (256 bytes) for n
        };
        result.AddRange(n);
        result.AddRange([0x21, 0x43]); // -2: byte string (3 bytes) for e
        result.AddRange(e);

        return [.. result];
    }

    /// <summary>
    /// Creates a CBOR-encoded COSE key for ES256 from ECParameters.
    /// </summary>
    private static byte[] CreateES256CoseKeyFromParams(System.Security.Cryptography.ECParameters ecParams)
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

    /// <summary>
    /// Creates a CBOR-encoded COSE key for RS256 from RSAParameters.
    /// </summary>
    private static byte[] CreateRS256CoseKeyFromParams(System.Security.Cryptography.RSAParameters rsaParams)
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
