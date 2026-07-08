namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientBodyParamGetTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.bodyparam_get(_keyword text, _payload text default null)
returns text
language sql
as $$
select coalesce(_keyword, '') || coalesce(_payload, '');
$$;
comment on function dartclient_test.bodyparam_get(text, text) is '
HTTP GET
dartclient_module=dart_bodyparam_get
body_parameter_name payload';

-- POST endpoint with an HTTP Custom Type whose body field is targeted by @body_parameter_name using
-- its EXPANDED signature name (_response_body). The generator must match that name (via ExpandedName)
-- the same way the server does, emit the body with the bare converted name (request.responseBody),
-- and exclude it from the query string. The HTTP type points at an unused port; it never fires
-- during code generation.
create type dartclient_test.dartc_http_probe as (
    body text,
    status_code int,
    success boolean,
    error_message text
);
comment on type dartclient_test.dartc_http_probe is 'GET http://localhost:1/dartc-dummy';

create function dartclient_test.bodyparam_expanded(_response dartclient_test.dartc_http_probe default null)
returns text
language plpgsql
as $$ begin return ''; end; $$;
comment on function dartclient_test.bodyparam_expanded(dartclient_test.dartc_http_probe) is '
HTTP POST
dartclient_module=dart_bodyparam_expanded
body_parameter_name _response_body';

-- Mixed: a normal client parameter (_keyword) alongside an HTTP Custom Type. With
-- OmitAutomaticParameters enabled, the four HTTP-type fields are dropped but _keyword stays in the
-- request and on the query string.
create function dartclient_test.bodyparam_mixed(_keyword text, _response dartclient_test.dartc_http_probe default null)
returns text
language plpgsql
as $$ begin return _keyword; end; $$;
comment on function dartclient_test.bodyparam_mixed(text, dartclient_test.dartc_http_probe) is '
HTTP GET
dartclient_module=dart_bodyparam_mixed';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class BodyParamGetTests
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

class DartclientTestBodyparamGetRequest {
  final String? keyword;
  final String? payload;

  const DartclientTestBodyparamGetRequest({
    this.keyword,
    this.payload,
  });

  factory DartclientTestBodyparamGetRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestBodyparamGetRequest(
      keyword: json['keyword'] as String?,
      payload: json['payload'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'keyword': keyword,
      if (payload != null) 'payload': payload,
    };
  }
}

/// function dartclient_test.bodyparam_get(
///     _keyword text,
///     _payload text DEFAULT NULL::text
/// )
/// returns text
///
/// comment on function dartclient_test.bodyparam_get is 'HTTP GET
/// dartclient_module=dart_bodyparam_get
/// body_parameter_name payload';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.bodyparam_get
Future<String> dartclientTestBodyparamGet(DartclientTestBodyparamGetRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/bodyparam-get' + _query({
    'keyword': request.keyword,
  }));
  final response = await _send('GET', uri);
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_BodyParamGet_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_bodyparam_get.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

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

class DartclientTestBodyparamExpandedRequest {
  final String? responseBody;
  final int? responseStatusCode;
  final bool? responseSuccess;
  final String? responseErrorMessage;

  const DartclientTestBodyparamExpandedRequest({
    this.responseBody,
    this.responseStatusCode,
    this.responseSuccess,
    this.responseErrorMessage,
  });

  factory DartclientTestBodyparamExpandedRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestBodyparamExpandedRequest(
      responseBody: json['responseBody'] as String?,
      responseStatusCode: (json['responseStatusCode'] as num?)?.toInt(),
      responseSuccess: json['responseSuccess'] as bool?,
      responseErrorMessage: json['responseErrorMessage'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (responseBody != null) 'responseBody': responseBody,
      if (responseStatusCode != null) 'responseStatusCode': responseStatusCode,
      if (responseSuccess != null) 'responseSuccess': responseSuccess,
      if (responseErrorMessage != null) 'responseErrorMessage': responseErrorMessage,
    };
  }
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
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.bodyparam_expanded
Future<String> dartclientTestBodyparamExpanded(DartclientTestBodyparamExpandedRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/bodyparam-expanded' + _query({
    if (request.responseStatusCode != null) 'responseStatusCode': request.responseStatusCode,
    if (request.responseSuccess != null) 'responseSuccess': request.responseSuccess,
    if (request.responseErrorMessage != null) 'responseErrorMessage': request.responseErrorMessage,
  }));
  final response = await _send(
    'POST',
    uri,
    body: request.responseBody,
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_BodyParamExpanded_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_bodyparam_expanded.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedExpanded);
        }
    }
}
