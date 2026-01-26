namespace NpgsqlRestClient.Fido2;

public static class CoseAlgorithm
{
    public const int ES256 = -7;

    public const int ES384 = -35;

    public const int ES512 = -36;

    public const int RS256 = -257;

    public const int RS384 = -258;

    public const int RS512 = -259;

    public const int PS256 = -37;

    public const int PS384 = -38;

    public const int PS512 = -39;
}

public static class CoseKeyType
{
    public const int OKP = 1;

    public const int EC2 = 2;

    public const int RSA = 3;
}

public static class CoseCurve
{
    public const int P256 = 1;

    public const int P384 = 2;

    public const int P521 = 3;
}
