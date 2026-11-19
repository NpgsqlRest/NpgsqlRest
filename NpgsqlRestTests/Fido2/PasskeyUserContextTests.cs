using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NpgsqlRest.Auth;
using NpgsqlRestClient.Fido2;

namespace NpgsqlRestTests.Fido2;

/// <summary>
/// Security regression: the passkey userContext round trip (options endpoint -> browser -> completion endpoint)
/// must not let a client change the context, e.g. replace the user id to attach its own passkey to another account.
/// </summary>
public class PasskeyUserContextTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddDataProtection().Services.BuildServiceProvider();
    private static readonly NpgsqlRestAuthenticationOptions AuthOptions = new();

    private static HttpContext Context(string? userId)
    {
        var context = new DefaultHttpContext { RequestServices = Services };
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(AuthOptions.DefaultUserIdClaimType, userId)], "test"));
        }
        return context;
    }

    // What the options endpoint sends to the browser as "userContext"
    private static string Issue(HttpContext context, string dbUserContext, string challengeId)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            PasskeyUserContext.Write(writer, context, dbUserContext, "aGFuZGxl", challengeId,
                PasskeyUserContext.GetBoundUserId(context, AuthOptions));
            writer.WriteEndObject();
        }
        return JsonNode.Parse(buffer.ToArray())!["userContext"]!.ToJsonString();
    }

    [Fact]
    public void Untouched_user_context_round_trips_to_the_issued_copy()
    {
        var context = Context("42");
        var returned = Issue(context, "{\"id\":42,\"deviceName\":\"laptop\"}", "1001");

        var issued = PasskeyUserContext.Open(context, returned, "1001", PasskeyUserContext.GetBoundUserId(context, AuthOptions));

        issued.Should().Be("{\"id\":42,\"deviceName\":\"laptop\",\"userHandle\":\"aGFuZGxl\"}");
    }

    [Fact]
    public void Edited_user_id_is_ignored_and_the_issued_copy_is_used()
    {
        var context = Context("42");
        var returned = JsonNode.Parse(Issue(context, "{\"id\":42,\"deviceName\":\"laptop\"}", "1001"))!;
        returned["id"] = 1; // attacker tries to attach the passkey to user 1

        var issued = PasskeyUserContext.Open(context, returned.ToJsonString(), "1001", PasskeyUserContext.GetBoundUserId(context, AuthOptions));

        issued.Should().Be("{\"id\":42,\"deviceName\":\"laptop\",\"userHandle\":\"aGFuZGxl\"}");
    }

    [Fact]
    public void Context_issued_for_another_challenge_is_rejected()
    {
        var context = Context("42");
        var returned = Issue(context, "{\"id\":42}", "1001");

        PasskeyUserContext.Open(context, returned, "1002", PasskeyUserContext.GetBoundUserId(context, AuthOptions)).Should().BeNull();
    }

    [Fact]
    public void Context_issued_to_another_user_is_rejected()
    {
        var victim = Context("1");
        var returned = Issue(victim, "{\"id\":1}", "1001"); // a context sealed for user 1 ...

        var attacker = Context("42"); // ... completed by user 42
        PasskeyUserContext.Open(attacker, returned, "1001", PasskeyUserContext.GetBoundUserId(attacker, AuthOptions)).Should().BeNull();
    }

    [Fact]
    public void Context_without_seal_is_rejected()
    {
        var context = Context("42");
        PasskeyUserContext.Open(context, "{\"id\":1,\"userHandle\":\"aGFuZGxl\"}", "1001", "42").Should().BeNull();
    }

    [Fact]
    public void Context_with_forged_seal_is_rejected()
    {
        var context = Context("42");
        PasskeyUserContext.Open(context, "{\"id\":1,\"_protected\":\"Q2ZESjhBQUFBQUFBQUFBQUFB\"}", "1001", "42").Should().BeNull();
        PasskeyUserContext.Open(context, "{\"id\":1,\"_protected\":12}", "1001", "42").Should().BeNull();
        PasskeyUserContext.Open(context, "not json", "1001", "42").Should().BeNull();
    }

    [Fact]
    public void Registration_context_is_bound_to_the_challenge_only()
    {
        var anonymous = Context(null);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            PasskeyUserContext.Write(writer, anonymous, "{\"email\":\"a@b.c\"}", "aGFuZGxl", "7", boundUserId: null);
            writer.WriteEndObject();
        }
        var returned = JsonNode.Parse(buffer.ToArray())!["userContext"]!;
        returned["email"] = "admin@b.c";

        PasskeyUserContext.Open(anonymous, returned.ToJsonString(), "7", boundUserId: null)
            .Should().Be("{\"email\":\"a@b.c\",\"userHandle\":\"aGFuZGxl\"}");
        PasskeyUserContext.Open(anonymous, returned.ToJsonString(), "8", boundUserId: null).Should().BeNull();
    }
}
