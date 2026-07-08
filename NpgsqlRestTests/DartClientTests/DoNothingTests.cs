namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientDoNothingTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.do_nothing()
returns void
language sql
as $$
select null;
$$;
comment on function dartclient_test.do_nothing() is '
dartclient_module=dart_do_nothing
';

create function dartclient_test.do_nothing_status()
returns void
language sql
as $$
select null;
$$;
comment on function dartclient_test.do_nothing_status() is '
dartclient_module=dart_do_nothing_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class DoNothingTests
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

/// function dartclient_test.do_nothing()
/// returns void
///
/// comment on function dartclient_test.do_nothing is 'dartclient_module=dart_do_nothing';
///
/// See FUNCTION dartclient_test.do_nothing
Future<void> dartclientTestDoNothing() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/do-nothing');
  await _send('POST', uri);
}

""";

        [Fact]
        public void Test_DoNothing_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_do_nothing.dart");
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

/// function dartclient_test.do_nothing_status()
/// returns void
///
/// comment on function dartclient_test.do_nothing_status is 'dartclient_module=dart_do_nothing_status
/// dartclient_status_code=true';
///
/// Returns `ApiResult<void>`.
///
/// See FUNCTION dartclient_test.do_nothing_status
Future<ApiResult<void>> dartclientTestDoNothingStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/do-nothing-status');
  final response = await _send('POST', uri);
  return ApiResult<void>(
    status: response.statusCode,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_DoNothingStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_do_nothing_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
