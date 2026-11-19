namespace NpgsqlRestTests.StaticFilesTests;

/// <summary>
/// Security regressions for the static file middleware:
/// - AuthorizePaths must apply to every request path that resolves to the protected file ("//protected/..." used to
///   bypass the "/protected/*" rule because patterns were matched against the raw request path);
/// - files inside dot-directories (.git, .svn, ...) must not be served.
/// </summary>
[Collection("StaticFilesTestFixture")]
public class StaticFilesPathNormalizationTests(StaticFilesTestFixture test)
{
    private HttpClient AnonymousNoRedirect() =>
        new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });

    private Uri Url(string rawPath) => new(test.ServerAddress.TrimEnd('/') + rawPath);

    [Fact]
    public async Task Protected_file_redirects_anonymous_user()
    {
        using var client = AnonymousNoRedirect();
        using var response = await client.GetAsync(Url("/protected/secret.html"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/login.html?return_to=%2Fprotected%2Fsecret.html");
    }

    [Theory]
    [InlineData("//protected/secret.html")]
    [InlineData("///protected/secret.html")]
    [InlineData("/protected//secret.html")]
    public async Task Path_variants_of_a_protected_file_are_protected_too(string rawPath)
    {
        using var client = AnonymousNoRedirect();
        using var response = await client.GetAsync(Url(rawPath));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/login.html?return_to=");
    }

    [Fact]
    public async Task Files_in_dot_directories_are_not_served()
    {
        var gitDir = Path.Combine(test.WebRootPath, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), "[remote \"origin\"]\n\turl = https://token@example.com/repo.git\n");

        using var client = AnonymousNoRedirect();
        using var response = await client.GetAsync(Url("/.git/config"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Well_known_directory_is_still_served()
    {
        var wellKnown = Path.Combine(test.WebRootPath, ".well-known");
        Directory.CreateDirectory(wellKnown);
        File.WriteAllText(Path.Combine(wellKnown, "security.txt"), "Contact: mailto:security@example.com\n");

        using var client = AnonymousNoRedirect();
        using var response = await client.GetAsync(Url("/.well-known/security.txt"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Contact: mailto:security@example.com\n");
    }
}
