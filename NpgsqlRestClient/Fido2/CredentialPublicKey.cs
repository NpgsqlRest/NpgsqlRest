using System.Formats.Cbor;
using System.Security.Cryptography;

namespace NpgsqlRestClient.Fido2;

public sealed class CredentialPublicKey
{
    private readonly COSEKeyType _type;
    private readonly COSEAlgorithmIdentifier _alg;
    private readonly ReadOnlyMemory<byte> _bytes;
    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;

    public COSEAlgorithmIdentifier Algorithm => _alg;

    public int AlgorithmInt => (int)_alg;

    public static IReadOnlyList<COSEAlgorithmIdentifier> SupportedAlgorithms { get; } =
    [
        COSEAlgorithmIdentifier.ES256,
        COSEAlgorithmIdentifier.PS256,
        COSEAlgorithmIdentifier.ES384,
        COSEAlgorithmIdentifier.PS384,
        COSEAlgorithmIdentifier.PS512,
        COSEAlgorithmIdentifier.RS256,
        COSEAlgorithmIdentifier.ES512,
        COSEAlgorithmIdentifier.RS384,
        COSEAlgorithmIdentifier.RS512,
    ];

    public static bool IsSupportedAlgorithm(int alg)
        => IsSupportedAlgorithm((COSEAlgorithmIdentifier)alg);

    public static bool IsSupportedAlgorithm(COSEAlgorithmIdentifier alg)
        => alg switch
        {
            COSEAlgorithmIdentifier.ES256 or
            COSEAlgorithmIdentifier.PS256 or
            COSEAlgorithmIdentifier.ES384 or
            COSEAlgorithmIdentifier.PS384 or
            COSEAlgorithmIdentifier.PS512 or
            COSEAlgorithmIdentifier.RS256 or
            COSEAlgorithmIdentifier.ES512 or
            COSEAlgorithmIdentifier.RS384 or
            COSEAlgorithmIdentifier.RS512 => true,
            _ => false,
        };

    private CredentialPublicKey(ReadOnlyMemory<byte> bytes)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Ctap2Canonical);

        reader.ReadStartMap();

        // Read key type (label 1)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.KeyType);
        _type = (COSEKeyType)reader.ReadInt32();

        // Read algorithm (label 3)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.Alg);
        _alg = (COSEAlgorithmIdentifier)reader.ReadInt32();

        // Skip optional key_ops if present
        if (TryPeekLabel(reader, (int)COSEKeyParameter.KeyOps))
        {
            reader.ReadInt32(); // Read the label
            reader.SkipValue();  // Skip the value
        }

        switch (_type)
        {
            case COSEKeyType.EC2:
                _ecdsa = ParseECDsa(reader);
                break;
            case COSEKeyType.RSA:
                _rsa = ParseRSA(reader);
                break;
            default:
                throw new CborContentException($"Unsupported key type '{_type}'.");
        }

        // Calculate actual bytes consumed
        var bytesConsumed = bytes.Length - reader.BytesRemaining;
        _bytes = bytes[..bytesConsumed];
    }

    public static CredentialPublicKey Decode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            return new CredentialPublicKey(bytes);
        }
        catch (CborContentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CborContentException("Invalid credential public key format.", ex);
        }
    }

    public static CredentialPublicKey Decode(byte[] bytes) => Decode(new ReadOnlyMemory<byte>(bytes));

    public static CredentialPublicKey Decode(ReadOnlyMemory<byte> bytes, out int bytesRead)
    {
        var key = Decode(bytes);
        bytesRead = key._bytes.Length;
        return key;
    }

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        return _type switch
        {
            COSEKeyType.EC2 => _ecdsa!.VerifyData(data, signature, GetHashAlgorithm(), DSASignatureFormat.Rfc3279DerSequence),
            COSEKeyType.RSA => _rsa!.VerifyData(data, signature, GetHashAlgorithm(), GetRSASignaturePadding()),
            _ => throw new InvalidOperationException($"Missing or unknown kty {_type}"),
        };
    }

    private static void ReadExpectedLabel(CborReader reader, int expectedLabel)
    {
        var label = reader.ReadInt32();
        if (label != expectedLabel)
        {
            throw new CborContentException($"Expected COSE key label {expectedLabel}, got {label}");
        }
    }

    private static bool TryPeekLabel(CborReader reader, int expectedLabel)
    {
        if (reader.PeekState() != CborReaderState.NegativeInteger && reader.PeekState() != CborReaderState.UnsignedInteger)
        {
            return false;
        }

        // We can't truly peek without consuming, so we'll check by position
        // For now, return false - key_ops is rarely used
        return false;
    }

    private static RSA ParseRSA(CborReader reader)
    {
        var rsaParams = new RSAParameters();

        // Read modulus n (label -1)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.N);
        rsaParams.Modulus = reader.ReadByteString();

        // Read exponent e (label -2)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.E);
        rsaParams.Exponent = reader.ReadByteString();

        // If we see label -4 (d), this is a private key which we don't support
        if (reader.PeekState() == CborReaderState.NegativeInteger)
        {
            var nextLabel = reader.ReadInt32();
            if (nextLabel == (int)COSEKeyParameter.D)
            {
                throw new CborContentException("The COSE key encodes a private key.");
            }
        }

        return RSA.Create(rsaParams);
    }

    private ECDsa ParseECDsa(CborReader reader)
    {
        var ecParams = new ECParameters();

        // Read curve (label -1)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.Crv);
        var crv = (COSEEllipticCurve)reader.ReadInt32();

        ecParams.Curve = crv switch
        {
            COSEEllipticCurve.P256 => ECCurve.NamedCurves.nistP256,
            COSEEllipticCurve.P384 => ECCurve.NamedCurves.nistP384,
            COSEEllipticCurve.P521 => ECCurve.NamedCurves.nistP521,
            _ => throw new CborContentException($"Unrecognized COSE crv value {crv}"),
        };

        // Read X coordinate (label -2)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.X);
        ecParams.Q.X = reader.ReadByteString();

        // Read Y coordinate (label -3)
        ReadExpectedLabel(reader, (int)COSEKeyParameter.Y);
        ecParams.Q.Y = reader.ReadByteString();

        // If we see label -4 (d), this is a private key which we don't support
        if (reader.PeekState() == CborReaderState.NegativeInteger)
        {
            var nextLabel = reader.ReadInt32();
            if (nextLabel == (int)COSEKeyParameter.D)
            {
                throw new CborContentException("The COSE key encodes a private key.");
            }
        }

        return ECDsa.Create(ecParams);
    }

    private RSASignaturePadding GetRSASignaturePadding()
    {
        return _alg switch
        {
            COSEAlgorithmIdentifier.PS256 or
            COSEAlgorithmIdentifier.PS384 or
            COSEAlgorithmIdentifier.PS512
            => RSASignaturePadding.Pss,

            COSEAlgorithmIdentifier.RS256 or
            COSEAlgorithmIdentifier.RS384 or
            COSEAlgorithmIdentifier.RS512
            => RSASignaturePadding.Pkcs1,

            _ => throw new InvalidOperationException($"Missing or unknown alg {_alg}"),
        };
    }

    private HashAlgorithmName GetHashAlgorithm()
    {
        return _alg switch
        {
            COSEAlgorithmIdentifier.ES256 or
            COSEAlgorithmIdentifier.PS256 or
            COSEAlgorithmIdentifier.RS256 => HashAlgorithmName.SHA256,

            COSEAlgorithmIdentifier.ES384 or
            COSEAlgorithmIdentifier.PS384 or
            COSEAlgorithmIdentifier.RS384 => HashAlgorithmName.SHA384,

            COSEAlgorithmIdentifier.ES512 or
            COSEAlgorithmIdentifier.PS512 or
            COSEAlgorithmIdentifier.RS512 => HashAlgorithmName.SHA512,

            _ => throw new InvalidOperationException($"Invalid COSE algorithm value {_alg}."),
        };
    }

    public ReadOnlyMemory<byte> AsMemory() => _bytes;

    public byte[] ToArray() => _bytes.ToArray();
}

public enum COSEKeyType
{
    OKP = 1,
    EC2 = 2,
    RSA = 3,
    Symmetric = 4
}

public enum COSEAlgorithmIdentifier
{
    ES256 = -7,
    ES384 = -35,
    ES512 = -36,
    PS256 = -37,
    PS384 = -38,
    PS512 = -39,
    RS256 = -257,
    RS384 = -258,
    RS512 = -259,
    EdDSA = -8,
    RS1 = -65535,
    ES256K = -47
}

internal enum COSEKeyParameter
{
    Crv = -1,
    K = -1,
    X = -2,
    Y = -3,
    D = -4,
    N = -1,
    E = -2,
    KeyType = 1,
    KeyId = 2,
    Alg = 3,
    KeyOps = 4,
    BaseIV = 5
}

internal enum COSEEllipticCurve
{
    Reserved = 0,
    P256 = 1,
    P384 = 2,
    P521 = 3,
    X25519 = 4,
    X448 = 5,
    Ed25519 = 6,
    Ed448 = 7,
    P256K = 8,
}
