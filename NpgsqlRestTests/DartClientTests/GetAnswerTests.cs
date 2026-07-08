namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientGetAnswerTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.get_answer()
returns int
language sql
as $$
select 42;
$$;
comment on function dartclient_test.get_answer() is '
dartclient_module=dart_get_answer
';

create function dartclient_test.get_answer_status()
returns int
language sql
as $$
select 42;
$$;
comment on function dartclient_test.get_answer_status() is '
dartclient_module=dart_get_answer_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class GetAnswerTests
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

/// function dartclient_test.get_answer()
/// returns integer
///
/// comment on function dartclient_test.get_answer is 'dartclient_module=dart_get_answer';
///
/// Returns `int`.
///
/// See FUNCTION dartclient_test.get_answer
Future<int> dartclientTestGetAnswer() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-answer');
  final response = await _send('GET', uri);
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_GetAnswer_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_answer.dart");
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

/// function dartclient_test.get_answer_status()
/// returns integer
///
/// comment on function dartclient_test.get_answer_status is 'dartclient_module=dart_get_answer_status
/// dartclient_status_code=true';
///
/// Returns `ApiResult<int>`.
///
/// See FUNCTION dartclient_test.get_answer_status
Future<ApiResult<int>> dartclientTestGetAnswerStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-answer-status');
  final response = await _send('GET', uri);
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<int>(
    status: response.statusCode,
    response: ok ? int.parse(utf8.decode(response.bodyBytes)) : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_GetAnswerStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_answer_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
