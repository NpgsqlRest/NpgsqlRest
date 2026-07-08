namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientProxyPassthroughResponseTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.proxy_passthrough()
returns void
language sql
as $$
select null;
$$;
comment on function dartclient_test.proxy_passthrough() is '
HTTP GET
proxy
dartclient_module=dart_proxy_passthrough_response
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class ProxyPassthroughResponseTests
    {
        private const string Expected = """
import 'package:http/http.dart' as http;

String baseUrl = '';

/// Override to inject a custom [http.Client] (e.g. MockClient in tests).
http.Client? httpClient;

http.Client get _client => httpClient ??= http.Client();

Future<http.Response> _send(
  String method,
  Uri uri, {
  Map<String, String>? headers,
  String? body,
}) async {
  final request = http.Request(method, uri);
  if (headers != null) {
    request.headers.addAll(headers);
  }
  if (body != null) {
    request.body = body;
  }
  return http.Response.fromStream(await _client.send(request));
}

/// function dartclient_test.proxy_passthrough()
/// returns void
///
/// comment on function dartclient_test.proxy_passthrough is 'HTTP GET
/// proxy
/// dartclient_module=dart_proxy_passthrough_response';
///
/// Returns `http.Response`.
///
/// See FUNCTION dartclient_test.proxy_passthrough
Future<http.Response> dartclientTestProxyPassthrough() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/proxy-passthrough');
  final response = await _send('GET', uri);
  return response;
}

""";

        [Fact]
        public void Test_ProxyPassthrough_GeneratedFile()
        {
            // Void proxy pass-through endpoints return the raw upstream response - the generated
            // function returns http.Response directly.
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_proxy_passthrough_response.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
