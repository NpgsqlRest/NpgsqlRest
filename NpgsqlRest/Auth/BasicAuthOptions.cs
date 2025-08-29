namespace NpgsqlRest.Auth;

public class EndpointBasicAuthOptions
{
    public bool Enabled { get; set; } = false;
    public string Realm { get; set; } = BasicAuthOptions.DefaultRealm;
    public Dictionary<string, string> Users { get; set; } = new();
    public string? ChallengeCommand { get; set; } = null;
}

public class BasicAuthOptions : EndpointBasicAuthOptions
{
    public const string DefaultRealm = "NpgsqlRest";
    public bool UseDefaultPasswordHasher { get; set; } = true;
    public SslRequirement SslRequirement { get; set; } = SslRequirement.Required;
}