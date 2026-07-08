namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientIsActiveTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.is_active()
returns boolean
language sql
as $$
select true;
$$;
comment on function dartclient_test.is_active() is '
dartclient_module=dart_is_active
';

create function dartclient_test.is_active_status()
returns boolean
language sql
as $$
select true;
$$;
comment on function dartclient_test.is_active_status() is '
dartclient_module=dart_is_active_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class IsActiveTests
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

/// function dartclient_test.is_active()
/// returns boolean
///
/// comment on function dartclient_test.is_active is 'dartclient_module=dart_is_active';
///
/// Returns `bool`.
///
/// See FUNCTION dartclient_test.is_active
Future<bool> dartclientTestIsActive() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/is-active');
  final response = await _send('POST', uri);
  return utf8.decode(response.bodyBytes) == 't';
}

""";

        [Fact]
        public void Test_IsActive_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_is_active.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedStatus = """
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

ApiError? _error(http.Response response) {
  if (response.statusCode >= 200 && response.statusCode < 300) {
    return null;
  }
  if (response.headers['content-length'] == '0') {
    return null;
  }
  return ApiError.fromJson(
    jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>,
  );
}

class ApiError {
  final int? status;
  final String? title;
  final String? detail;

  const ApiError({this.status, this.title, this.detail});

  factory ApiError.fromJson(Map<String, dynamic> json) {
    return ApiError(
      status: (json['status'] as num?)?.toInt(),
      title: json['title'] as String?,
      detail: json['detail'] as String?,
    );
  }
}

class ApiResult<T> {
  final int status;
  final T? response;
  final ApiError? error;

  const ApiResult({required this.status, this.response, this.error});

  bool get ok => status >= 200 && status < 300;
}

/// function dartclient_test.is_active_status()
/// returns boolean
///
/// comment on function dartclient_test.is_active_status is 'dartclient_module=dart_is_active_status
/// dartclient_status_code=true';
///
/// Returns `ApiResult<bool>`.
///
/// See FUNCTION dartclient_test.is_active_status
Future<ApiResult<bool>> dartclientTestIsActiveStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/is-active-status');
  final response = await _send('POST', uri);
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<bool>(
    status: response.statusCode,
    response: ok ? utf8.decode(response.bodyBytes) == 't' : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_IsActiveStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_is_active_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
