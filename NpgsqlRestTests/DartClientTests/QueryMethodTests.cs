namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientQueryMethodTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- HTTP QUERY method: parameters default to the JSON body; the generated Dart function
-- sends method QUERY with a jsonEncode body.
create function dartclient_test.query_search(_q text, _top int default 3)
returns text
language sql
as $$
select _q || '/' || _top::text;
$$;
comment on function dartclient_test.query_search(text, int) is '
HTTP QUERY
dartclient_module=dart_query_search
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class QueryMethodTests
    {
        private const string Expected = """
import 'dart:convert';
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

class DartclientTestQuerySearchRequest {
  final String? q;
  final int? top;

  const DartclientTestQuerySearchRequest({
    this.q,
    this.top,
  });

  factory DartclientTestQuerySearchRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestQuerySearchRequest(
      q: json['q'] as String?,
      top: (json['top'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'q': q,
      if (top != null) 'top': top,
    };
  }
}

/// function dartclient_test.query_search(
///     _q text,
///     _top integer DEFAULT 3
/// )
/// returns text
///
/// comment on function dartclient_test.query_search is 'HTTP QUERY
/// dartclient_module=dart_query_search';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.query_search
Future<String> dartclientTestQuerySearch(DartclientTestQuerySearchRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/query-search');
  final response = await _send(
    'QUERY',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_QueryMethod_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_query_search.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
            content.Should().Contain("'QUERY',");
            content.Should().Contain("body: jsonEncode(request.toJson())");
        }
    }
}
