namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientGetNamesTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.get_names()
returns setof text
language sql
as $$
select unnest(array['Alice', 'Bob', 'Charlie']);
$$;
comment on function dartclient_test.get_names() is '
dartclient_module=dart_get_names
';

create function dartclient_test.get_names_status()
returns setof text
language sql
as $$
select unnest(array['Alice', 'Bob', 'Charlie']);
$$;
comment on function dartclient_test.get_names_status() is '
dartclient_module=dart_get_names_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class GetNamesTests
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

/// function dartclient_test.get_names()
/// returns setof text
///
/// comment on function dartclient_test.get_names is 'dartclient_module=dart_get_names';
///
/// Returns `List<String>`.
///
/// See FUNCTION dartclient_test.get_names
Future<List<String>> dartclientTestGetNames() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-names');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => e as String)
      .toList();
}

""";

        [Fact]
        public void Test_GetNames_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_names.dart");
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

/// function dartclient_test.get_names_status()
/// returns setof text
///
/// comment on function dartclient_test.get_names_status is 'dartclient_module=dart_get_names_status
/// dartclient_status_code=true';
///
/// Returns `ApiResult<List<String>>`.
///
/// See FUNCTION dartclient_test.get_names_status
Future<ApiResult<List<String>>> dartclientTestGetNamesStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-names-status');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<List<String>>(
    status: response.statusCode,
    response: ok
        ? (jsonDecode(utf8.decode(response.bodyBytes)) as List)
            .map((e) => e as String)
            .toList()
        : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_GetNamesStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_names_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
