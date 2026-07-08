namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientAddNumbersTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.add_numbers(_a int, _b int)
returns int
language sql
as $$
select _a + _b;
$$;
comment on function dartclient_test.add_numbers(int, int) is '
dartclient_module=dart_add_numbers
';

create function dartclient_test.add_numbers_status(_a int, _b int)
returns int
language sql
as $$
select _a + _b;
$$;
comment on function dartclient_test.add_numbers_status(int, int) is '
dartclient_module=dart_add_numbers_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class AddNumbersTests
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

class DartclientTestAddNumbersRequest {
  final int? a;
  final int? b;

  const DartclientTestAddNumbersRequest({
    this.a,
    this.b,
  });

  factory DartclientTestAddNumbersRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestAddNumbersRequest(
      a: (json['a'] as num?)?.toInt(),
      b: (json['b'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'a': a,
      'b': b,
    };
  }
}

/// function dartclient_test.add_numbers(
///     _a integer,
///     _b integer
/// )
/// returns integer
///
/// comment on function dartclient_test.add_numbers is 'dartclient_module=dart_add_numbers';
///
/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.add_numbers
Future<int> dartclientTestAddNumbers(DartclientTestAddNumbersRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/add-numbers');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_AddNumbers_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_add_numbers.dart");
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

class DartclientTestAddNumbersStatusRequest {
  final int? a;
  final int? b;

  const DartclientTestAddNumbersStatusRequest({
    this.a,
    this.b,
  });

  factory DartclientTestAddNumbersStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestAddNumbersStatusRequest(
      a: (json['a'] as num?)?.toInt(),
      b: (json['b'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'a': a,
      'b': b,
    };
  }
}

/// function dartclient_test.add_numbers_status(
///     _a integer,
///     _b integer
/// )
/// returns integer
///
/// comment on function dartclient_test.add_numbers_status is 'dartclient_module=dart_add_numbers_status
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<int>`.
///
/// See FUNCTION dartclient_test.add_numbers_status
Future<ApiResult<int>> dartclientTestAddNumbersStatus(DartclientTestAddNumbersStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/add-numbers-status');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<int>(
    status: response.statusCode,
    response: ok ? int.parse(utf8.decode(response.bodyBytes)) : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_AddNumbersStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_add_numbers_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
