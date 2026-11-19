using Microsoft.Extensions.Configuration;
using NpgsqlRestClient;

namespace NpgsqlRestTests.AuthTests;

/// <summary>
/// Regression: Auth:LogoutPath (a URL path, like Auth:LoginPath) was compared with the routine *name* and marked the
/// matching endpoint as a *login* endpoint. A URL never matched (logout did nothing), and the routine name turned the
/// logout routine into an anonymous login endpoint.
/// </summary>
public class AuthLoginLogoutPathTests
{
    private static Action<RoutineEndpoint?> Handler(string authJson)
    {
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, $$"""{ "Auth": {{authJson}} }""");
        var config = new Config();
        try
        {
            config.Build([tempFile], []);
        }
        finally
        {
            File.Delete(tempFile);
        }
        var app = new App(config, new Builder(config));
        return app.CreateEndpointCreatedHandler(config.Cfg.GetSection("Auth"))!;
    }

    private static RoutineEndpoint Endpoint(string name, string path) => new(
        routine: new Routine
        {
            Type = RoutineType.Function, Schema = "public", Name = name, Comment = null, IsStrict = false,
            CrudType = CrudType.Select, ReturnsRecordType = false, ReturnsSet = false, ColumnCount = 0,
            OriginalColumnNames = [], ColumnNames = [], JsonColumnNames = [], ColumnsTypeDescriptor = [],
            ReturnsUnnamedSet = false, IsVoid = true, ParamCount = 0, Parameters = [], ParamsHash = [],
            OriginalParamsHash = [], Expression = string.Empty, FullDefinition = string.Empty,
            SimpleDefinition = string.Empty, FormatUrlPattern = null, Tags = null, EndpointHandler = null, Metadata = null,
        },
        path: path,
        method: Method.POST,
        requestParamType: RequestParamType.BodyJson,
        requiresAuthorization: false,
        responseContentType: "application/json",
        responseHeaders: [],
        requestHeadersMode: RequestHeadersMode.Ignore,
        requestHeadersParameterName: string.Empty,
        bodyParameterName: null,
        textResponseNullHandling: TextResponseNullHandling.EmptyString,
        queryStringNullHandling: QueryStringNullHandling.EmptyString);

    [Fact]
    public void LogoutPath_marks_the_endpoint_with_that_path_as_logout()
    {
        var handler = Handler("""{ "LoginPath": "/api/login", "LogoutPath": "/api/logout" }""");
        var login = Endpoint("login", "/api/login");
        var logout = Endpoint("logout", "/api/logout");

        handler(login);
        handler(logout);

        login.Login.Should().BeTrue();
        login.Logout.Should().BeFalse();
        logout.Logout.Should().BeTrue();
        logout.Login.Should().BeFalse();
    }

    [Fact]
    public void LogoutPath_does_not_match_by_routine_name()
    {
        var handler = Handler("""{ "LogoutPath": "logout" }""");
        var endpoint = Endpoint("logout", "/api/logout");

        handler(endpoint);

        endpoint.Login.Should().BeFalse();
        endpoint.Logout.Should().BeFalse();
    }
}
