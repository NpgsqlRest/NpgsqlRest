namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientInternalEndpointExclusionTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- internal-only endpoint: has no public HTTP route, so it must NOT appear in the generated
-- Dart client (a function for it would 404).
create function dartclient_test.internal_widget(_x int) returns int language sql as 'select _x';
comment on function dartclient_test.internal_widget(int) is '
HTTP GET
internal
dartclient_module=dart_internal_widget';

-- visible sibling: anchors that generation ran for this schema (its artifacts are present).
create function dartclient_test.visible_widget(_x int) returns int language sql as 'select _x';
comment on function dartclient_test.visible_widget(int) is '
HTTP GET
dartclient_module=dart_visible_widget';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class InternalEndpointExclusionTests
    {
        [Fact]
        public void Test_InternalWidget_IsExcluded()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_internal_widget.dart");
            File.Exists(filePath).Should().BeFalse($"internal endpoint must be excluded, but found {filePath}");
        }

        private const string ExpectedVisible = """
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

class DartclientTestVisibleWidgetRequest {
  final int? x;

  const DartclientTestVisibleWidgetRequest({
    this.x,
  });

  factory DartclientTestVisibleWidgetRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestVisibleWidgetRequest(
      x: (json['x'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'x': x,
    };
  }
}

/// function dartclient_test.visible_widget(
///     _x integer
/// )
/// returns integer
///
/// comment on function dartclient_test.visible_widget is 'HTTP GET
/// dartclient_module=dart_visible_widget';
///
/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.visible_widget
Future<int> dartclientTestVisibleWidget(DartclientTestVisibleWidgetRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/visible-widget' + _query({
    'x': request.x,
  }));
  final response = await _send('GET', uri);
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_VisibleWidget_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_visible_widget.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedVisible);
        }
    }
}
