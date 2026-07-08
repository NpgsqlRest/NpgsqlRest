// SQL setup for these endpoints lives in BodyParamGetTests.cs (bodyparam_expanded, bodyparam_mixed).
// This class asserts the OmitAutomaticParameters=true instance output: the four HTTP Custom Type
// fields are dropped from the request class, query string and body, while normal client
// parameters stay.

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class OmitAutomaticParamsTests
    {
        private const string ExpectedExpanded = """
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

/// function dartclient_test.bodyparam_expanded(
///     _response_body text DEFAULT NULL::dartclient_test.dartc_http_probe,
///     _response_status_code integer,
///     _response_success boolean,
///     _response_error_message text
/// )
/// returns text
///
/// comment on function dartclient_test.bodyparam_expanded is 'HTTP POST
/// dartclient_module=dart_bodyparam_expanded
/// body_parameter_name _response_body';
///
/// Returns `String`.
///
/// See FUNCTION dartclient_test.bodyparam_expanded
Future<String> dartclientTestBodyparamExpanded() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/bodyparam-expanded');
  final response = await _send('POST', uri);
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_BodyParamExpanded_Omitted_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOmitOutputPath, "dart_bodyparam_expanded.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedExpanded);
        }

        private const string ExpectedMixed = """
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

class DartclientTestBodyparamMixedRequest {
  final String? keyword;

  const DartclientTestBodyparamMixedRequest({
    this.keyword,
  });

  factory DartclientTestBodyparamMixedRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestBodyparamMixedRequest(
      keyword: json['keyword'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'keyword': keyword,
    };
  }
}

/// function dartclient_test.bodyparam_mixed(
///     _keyword text,
///     _response_body text DEFAULT NULL::dartclient_test.dartc_http_probe,
///     _response_status_code integer,
///     _response_success boolean,
///     _response_error_message text
/// )
/// returns text
///
/// comment on function dartclient_test.bodyparam_mixed is 'HTTP GET
/// dartclient_module=dart_bodyparam_mixed';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.bodyparam_mixed
Future<String> dartclientTestBodyparamMixed(DartclientTestBodyparamMixedRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/bodyparam-mixed' + _query({
    'keyword': request.keyword,
  }));
  final response = await _send('GET', uri);
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_BodyParamMixed_Omitted_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOmitOutputPath, "dart_bodyparam_mixed.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedMixed);
        }
    }
}
