using System.Buffers.Binary;

namespace NpgsqlRestClient.Fido2;

public class AuthenticatorData
{
    public byte[] RpIdHash { get; set; } = [];

    public bool UserPresent { get; set; }

    public bool UserVerified { get; set; }

    public bool BackupEligible { get; set; }

    public bool BackedUp { get; set; }

    public bool HasAttestedCredentialData { get; set; }

    public bool HasExtensions { get; set; }

    public uint SignCount { get; set; }

    public AttestedCredentialData? AttestedCredentialData { get; set; }

    public byte Flags { get; set; }

    public static AuthenticatorData? Parse(byte[] data)
    {
        if (data == null || data.Length < 37)
            return null;

        try
        {
            var rpIdHash = data[..32];
            var flags = data[32];
            var signCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(33, 4));

            var authData = new AuthenticatorData
            {
                RpIdHash = rpIdHash,
                Flags = flags,
                UserPresent = (flags & AuthenticatorDataFlags.UP) != 0,
                UserVerified = (flags & AuthenticatorDataFlags.UV) != 0,
                BackupEligible = (flags & AuthenticatorDataFlags.BE) != 0,
                BackedUp = (flags & AuthenticatorDataFlags.BS) != 0,
                HasAttestedCredentialData = (flags & AuthenticatorDataFlags.AT) != 0,
                HasExtensions = (flags & AuthenticatorDataFlags.ED) != 0,
                SignCount = signCount
            };

            // Parse attested credential data if present
            if (authData.HasAttestedCredentialData && data.Length > 37)
            {
                authData.AttestedCredentialData = AttestedCredentialData.Parse(data.AsSpan(37));
            }

            return authData;
        }
        catch
        {
            return null;
        }
    }
}

public static class AuthenticatorDataFlags
{
    public const byte UP = 0x01;
    public const byte UV = 0x04;
    public const byte BE = 0x08;
    public const byte BS = 0x10;
    public const byte AT = 0x40;
    public const byte ED = 0x80;
}

public class AttestedCredentialData
{
    public byte[] Aaguid { get; set; } = [];

    public byte[] CredentialId { get; set; } = [];

    public byte[] PublicKeyBytes { get; set; } = [];

    public CredentialPublicKey? PublicKey { get; set; }

    public int Algorithm => PublicKey?.AlgorithmInt ?? 0;

    public static AttestedCredentialData? Parse(ReadOnlySpan<byte> data)
    {
        // Minimum: 16 (aaguid) + 2 (length) = 18 bytes
        if (data.Length < 18)
            return null;

        try
        {
            var aaguid = data[..16].ToArray();
            var credentialIdLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(16, 2));

            if (data.Length < 18 + credentialIdLength)
                return null;

            var credentialId = data.Slice(18, credentialIdLength).ToArray();

            // The rest is the COSE public key
            var publicKeyData = data[(18 + credentialIdLength)..];
            var publicKey = CredentialPublicKey.Decode(publicKeyData.ToArray(), out int bytesRead);
            var publicKeyBytes = publicKeyData[..bytesRead].ToArray();

            return new AttestedCredentialData
            {
                Aaguid = aaguid,
                CredentialId = credentialId,
                PublicKeyBytes = publicKeyBytes,
                PublicKey = publicKey
            };
        }
        catch
        {
            return null;
        }
    }
}
