namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientQueryStringConversionTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Query-string value conversion: Dart URIs need string values, so bool/num/DateTime/array values
-- go through the generated _query/_str helpers (DateTime as ISO 8601, lists as repeated keys).
-- The `_when` parameter also covers a Dart reserved word in a query map (field name `when_`,
-- wire key `when`).
create function dartclient_test.query_conversions(
    _flag boolean,
    _amount numeric,
    _when timestamp,
    _tags text[],
    _note text default null
)
returns int
language sql
as $$
select 1;
$$;
comment on function dartclient_test.query_conversions(boolean, numeric, timestamp, text[], text) is '
dartclient_module=dart_query_conversions
HTTP GET
request_param_type query_string
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class QueryStringConversionTests
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

String _query(Map<String, Object?> query) {
  final parts = <String>[];
  query.forEach((key, value) {
    if (value is List) {
      for (final v in value) {
        parts.add(v == null ? '$key=' : '$key=${Uri.encodeQueryComponent(_str(v))}');
      }
    } else if (value == null) {
      parts.add('$key=');
    } else {
      parts.add('$key=${Uri.encodeQueryComponent(_str(value))}');
    }
  });
  return '?${parts.join('&')}';
}

String _str(Object value) =>
    value is DateTime ? value.toIso8601String() : value.toString();

class DartclientTestQueryConversionsRequest {
  final bool? flag;
  final double? amount;
  final DateTime? when_;
  final List<String>? tags;
  final String? note;

  const DartclientTestQueryConversionsRequest({
    this.flag,
    this.amount,
    this.when_,
    this.tags,
    this.note,
  });

  factory DartclientTestQueryConversionsRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestQueryConversionsRequest(
      flag: json['flag'] as bool?,
      amount: (json['amount'] as num?)?.toDouble(),
      when_: json['when'] == null ? null : DateTime.parse(json['when'] as String),
      tags: (json['tags'] as List?)?.map((e) => e as String).toList(),
      note: json['note'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'flag': flag,
      'amount': amount,
      'when': when_?.toIso8601String(),
      'tags': tags,
      if (note != null) 'note': note,
    };
  }
}

/// function dartclient_test.query_conversions(
///     _flag boolean,
///     _amount numeric,
///     _when timestamp without time zone,
///     _tags text[],
///     _note text DEFAULT NULL::text
/// )
/// returns integer
///
/// comment on function dartclient_test.query_conversions is 'dartclient_module=dart_query_conversions
/// HTTP GET
/// request_param_type query_string';
///
/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.query_conversions
Future<int> dartclientTestQueryConversions(DartclientTestQueryConversionsRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/query-conversions' + _query({
    'flag': request.flag,
    'amount': request.amount,
    'when': request.when_,
    'tags': request.tags,
    if (request.note != null) 'note': request.note,
  }));
  final response = await _send('GET', uri);
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_QueryConversions_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_query_conversions.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
