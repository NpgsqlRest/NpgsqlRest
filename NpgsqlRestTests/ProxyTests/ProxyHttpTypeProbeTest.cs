using WireMock.Server;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock;
using WireMock.Types;
using WireMock.Util;

namespace NpgsqlRestTests;

public static partial class Database
{
    public static void ProxyHttpTypeProbeTest()
    {
        // Functions that have BOTH an HTTP Custom Type parameter AND a (passthrough) @proxy.
        // The HTTP type points at the HttpClientType WireMock (port 50953); the @proxy (no host)
        // forwards to ProxyOptions.Host = http://localhost:50954, preserving the incoming path.
        // The function bodies raise, to prove they are NOT executed (passthrough).
        script.Append($@"
        create type proxy_httptype_probe as (
            body text,
            status_code int,
            success boolean,
            error_message text
        );
        comment on type proxy_httptype_probe is 'GET http://localhost:{WireMockFixture.Port}/httptype-source';

        -- GET endpoint: server-filled HTTP-type fields are forwarded on the proxy QUERY STRING.
        create function proxy_with_httptype(
            _response proxy_httptype_probe default null
        )
        returns void
        language plpgsql
        as
        $$
        begin
            raise exception 'passthrough proxy must NOT execute the function';
        end;
        $$;
        comment on function proxy_with_httptype(proxy_httptype_probe) is 'HTTP GET
allow_anonymous
proxy';

        -- GET endpoint, but @proxy POST + @body_parameter_name redirects ONE expanded HTTP-type field
        -- (targeted by its converted/API name 'responseBody') into the upstream POST BODY, while the
        -- remaining small fields still go to the proxy QUERY STRING. Proves case-insensitive matching
        -- of the body-param name against the converted name of a custom-type-expanded field.
        create function proxy_bodyparam_with_httptype(
            _response proxy_httptype_probe default null
        )
        returns void
        language plpgsql
        as
        $$
        begin
            raise exception 'passthrough proxy must NOT execute the function';
        end;
        $$;
        comment on function proxy_bodyparam_with_httptype(proxy_httptype_probe) is 'HTTP GET
allow_anonymous
proxy POST
body_parameter_name responseBody';

        -- Same as above, but the body param is targeted by the per-field name shown in the generated
        -- signature / .http file ('_response_body'). That name is the composite base + field, and is
        -- matched via the ExpandedName alias (ActualName stays the shared base '_response').
        create function proxy_bodyparam_expanded_with_httptype(
            _response proxy_httptype_probe default null
        )
        returns void
        language plpgsql
        as
        $$
        begin
            raise exception 'passthrough proxy must NOT execute the function';
        end;
        $$;
        comment on function proxy_bodyparam_expanded_with_httptype(proxy_httptype_probe) is 'HTTP GET
allow_anonymous
proxy POST
body_parameter_name _response_body';

        -- POST endpoint: server-filled HTTP-type fields are merged into the proxy JSON BODY,
        -- alongside the client's original body fields.
        create function proxy_post_with_httptype(
            _payload text default null,
            _response proxy_httptype_probe default null
        )
        returns void
        language plpgsql
        as
        $$
        begin
            raise exception 'passthrough proxy must NOT execute the function';
        end;
        $$;
        comment on function proxy_post_with_httptype(text, proxy_httptype_probe) is 'HTTP POST
allow_anonymous
proxy';

        -- POST endpoint, but params come from the QUERY STRING (param_type query). Placement must
        -- follow RequestParamType, not the verb: the HTTP-type fields go to the proxy QUERY even
        -- though the method is POST and a JSON body is sent.
        create function proxy_post_query_with_httptype(
            _response proxy_httptype_probe default null
        )
        returns void
        language plpgsql
        as
        $$
        begin
            raise exception 'passthrough proxy must NOT execute the function';
        end;
        $$;
        comment on function proxy_post_query_with_httptype(proxy_httptype_probe) is 'HTTP POST
allow_anonymous
param_type query
proxy';

        -- Resolved-parameter expression: _token is filled server-side from a DB lookup. It is an
        -- automatic parameter and must be forwarded to the proxy just like claims / IP / HTTP-type.
        create table proxy_resolved_tokens (user_name text primary key, api_token text not null);
        insert into proxy_resolved_tokens values ('alice', 'TOKEN-ALICE-123');

        create function proxy_resolved_param(
            _name text,
            _token text default null
        )
        returns void
        language plpgsql
        as
        $$
        begin
            raise exception 'passthrough proxy must NOT execute the function';
        end;
        $$;
        comment on function proxy_resolved_param(text, text) is 'HTTP GET
allow_anonymous
_token = select api_token from proxy_resolved_tokens where user_name = {{_name}}
proxy';
");
    }
}

[Collection("TestFixture")]
public class ProxyHttpTypeProbeTest : IClassFixture<WireMockFixture>, IClassFixture<ProxyWireMockFixture>
{
    private readonly TestFixture _test;
    private readonly WireMockServer _httpTypeServer;  // 50953 — the HTTP Custom Type target
    private readonly WireMockServer _proxyServer;     // 50954 — the @proxy target

    public ProxyHttpTypeProbeTest(TestFixture test, WireMockFixture httpType, ProxyWireMockFixture proxy)
    {
        _test = test;
        _httpTypeServer = httpType.Server;
        _proxyServer = proxy.Server;
        _httpTypeServer.Reset();
        _proxyServer.Reset();
    }

    // GET proxy: the auto-filled HTTP-type fields are forwarded on the proxy's QUERY STRING,
    // alongside the client's original query — "same signature, sent upstream".
    [Fact]
    public async Task HttpType_fields_are_forwarded_on_proxy_query_for_get()
    {
        int httpTypeCalls = 0;
        _httpTypeServer
            .Given(Request.Create().WithPath("/httptype-source").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref httpTypeCalls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "FETCHED-FROM-HTTPTYPE", DetectedBodyType = BodyType.String }
                };
            }));

        // Proxy target echoes the exact URL (incl. query) it received.
        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-with-httptype/").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"PROXY-URL:[{req.Url}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.GetAsync("/api/proxy-with-httptype/?clientParam=XYZ");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);   // proxy response (function never ran)
        content.Should().StartWith("PROXY-URL:[");
        httpTypeCalls.Should().Be(1);                          // HTTP-type call fired

        // The proxy received the client's original query …
        content.Should().Contain("clientParam=XYZ");
        // … AND the auto-filled HTTP-type fields:
        content.Should().Contain("responseBody=FETCHED-FROM-HTTPTYPE");
        content.Should().Contain("responseStatusCode=200");
    }

    // POST proxy: the auto-filled HTTP-type fields are merged into the proxy JSON BODY, alongside the
    // client's original body fields — and a large body field never ends up in the URL.
    [Fact]
    public async Task HttpType_fields_are_merged_into_proxy_json_body_for_post()
    {
        int httpTypeCalls = 0;
        _httpTypeServer
            .Given(Request.Create().WithPath("/httptype-source").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref httpTypeCalls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "FETCHED-FROM-HTTPTYPE", DetectedBodyType = BodyType.String }
                };
            }));

        // Proxy target echoes the exact body it received.
        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-post-with-httptype/").UsingPost())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"PROXY-BODY:[{req.Body}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.PostAsync(
            "/api/proxy-post-with-httptype/",
            new StringContent("{\"payload\": \"CLIENT\"}", System.Text.Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        httpTypeCalls.Should().Be(1);

        // The client's original body field is preserved …
        content.Should().Contain("\"payload\":\"CLIENT\"");
        // … and the auto-filled HTTP-type fields are merged in, typed (string / number / bool):
        content.Should().Contain("\"responseBody\":\"FETCHED-FROM-HTTPTYPE\"");
        content.Should().Contain("\"responseStatusCode\":200");
        content.Should().Contain("\"responseSuccess\":true");
    }

    // Placement follows RequestParamType, NOT the HTTP verb. This POST endpoint is configured with
    // `param_type query`, so even though the method is POST and the client sends a JSON body, the
    // HTTP-type fields must go to the proxy QUERY STRING — not merged into the body.
    [Fact]
    public async Task HttpType_fields_follow_request_param_type_not_verb()
    {
        int httpTypeCalls = 0;
        _httpTypeServer
            .Given(Request.Create().WithPath("/httptype-source").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref httpTypeCalls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "FETCHED-FROM-HTTPTYPE", DetectedBodyType = BodyType.String }
                };
            }));

        // Proxy target echoes both the URL and the body it received.
        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-post-query-with-httptype/").UsingPost())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"URL:[{req.Url}] BODY:[{req.Body}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.PostAsync(
            "/api/proxy-post-query-with-httptype/",
            new StringContent("{\"ignored\": \"CLIENTBODY\"}", System.Text.Encoding.UTF8, "application/json"));
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        httpTypeCalls.Should().Be(1);

        // HTTP-type fields forwarded on the QUERY (param_type query), in query form …
        content.Should().Contain("responseBody=FETCHED-FROM-HTTPTYPE");
        // … and NOT merged into the JSON body (which is forwarded verbatim).
        content.Should().NotContain("\"responseBody\":");
        content.Should().Contain("CLIENTBODY");
    }

    // Consistency: a resolved-parameter expression is an automatic parameter too, so it is forwarded
    // to the proxy exactly like claims / IP / HTTP-type fields (here: a GET → query string).
    [Fact]
    public async Task Resolved_param_is_forwarded_to_proxy_consistently()
    {
        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-resolved-param/").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"PROXY-URL:[{req.Url}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.GetAsync("/api/proxy-resolved-param/?name=alice");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("name=alice");             // client query forwarded verbatim
        content.Should().Contain("token=TOKEN-ALICE-123");  // resolved (automatic) param forwarded
    }

    // @body_parameter_name targets an expanded HTTP-type field by its converted (API) name. The match
    // is case-insensitive, so "responseBody" resolves to the body field even though its database
    // (actual) name is the shared composite base "_response". The field becomes the upstream POST body
    // (not the query), while the remaining small fields are still forwarded on the query string.
    [Fact]
    public async Task BodyParam_redirects_httptype_field_to_proxy_body_by_converted_name()
    {
        int httpTypeCalls = 0;
        _httpTypeServer
            .Given(Request.Create().WithPath("/httptype-source").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref httpTypeCalls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "FETCHED-FROM-HTTPTYPE", DetectedBodyType = BodyType.String }
                };
            }));

        // @proxy POST: the upstream call is POST even though the client called GET. Echo URL + body.
        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-bodyparam-with-httptype/").UsingPost())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"URL:[{req.Url}] BODY:[{req.Body}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.GetAsync("/api/proxy-bodyparam-with-httptype/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        httpTypeCalls.Should().Be(1);

        // The body field is the raw POST body …
        content.Should().Contain("BODY:[FETCHED-FROM-HTTPTYPE]");
        // … and is NOT in the query string (it was redirected to the body).
        content.Should().NotContain("responseBody=");
        // The remaining small fields still go to the query.
        content.Should().Contain("responseStatusCode=200");
    }

    // Same redirect, but the body param targets the per-field name from the generated signature
    // ('_response_body'). ActualName is the shared composite base '_response'; this resolves via the
    // ExpandedName alias, and is unique per field (so it targets the body field unambiguously).
    [Fact]
    public async Task BodyParam_redirects_httptype_field_to_proxy_body_by_expanded_name()
    {
        int httpTypeCalls = 0;
        _httpTypeServer
            .Given(Request.Create().WithPath("/httptype-source").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
            {
                Interlocked.Increment(ref httpTypeCalls);
                return new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = "FETCHED-FROM-HTTPTYPE", DetectedBodyType = BodyType.String }
                };
            }));

        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-bodyparam-expanded-with-httptype/").UsingPost())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"URL:[{req.Url}] BODY:[{req.Body}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.GetAsync("/api/proxy-bodyparam-expanded-with-httptype/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        httpTypeCalls.Should().Be(1);

        // The body field (targeted by its expanded signature name) is the raw POST body, not the query.
        content.Should().Contain("BODY:[FETCHED-FROM-HTTPTYPE]");
        content.Should().NotContain("responseBody=");
        content.Should().Contain("responseStatusCode=200");
    }

    // Guard: an oversized automatic value (here the HTTP-type body field) cannot be carried in a query
    // string. It is skipped (with a warning) instead of producing an unusable request line, while the
    // small fields are still forwarded. Default ProxyOptions.MaxForwardedQueryParamLength is 2048.
    [Fact]
    public async Task Oversized_httptype_field_is_skipped_from_proxy_query()
    {
        var largeBody = new string('X', 3000); // exceeds the default MaxForwardedQueryParamLength (2048)
        _httpTypeServer
            .Given(Request.Create().WithPath("/httptype-source").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = largeBody, DetectedBodyType = BodyType.String }
                }));

        _proxyServer
            .Given(Request.Create().WithPath("/api/proxy-with-httptype/").UsingGet())
            .RespondWith(Response.Create().WithCallback(req =>
                new ResponseMessage
                {
                    StatusCode = 200,
                    BodyData = new BodyData { BodyAsString = $"PROXY-URL:[{req.Url}]", DetectedBodyType = BodyType.String }
                }));

        using var response = await _test.Client.GetAsync("/api/proxy-with-httptype/");
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The oversized body field is skipped from the query (guard) …
        content.Should().NotContain("responseBody=");
        // … while the small fields are still forwarded.
        content.Should().Contain("responseStatusCode=200");
    }
}
