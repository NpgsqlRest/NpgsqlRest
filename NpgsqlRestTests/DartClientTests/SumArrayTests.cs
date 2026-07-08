namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientSumArrayTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.sum_array(_numbers int[])
returns int
language sql
as $$
select coalesce(sum(n), 0)::int from unnest(_numbers) as n;
$$;
comment on function dartclient_test.sum_array(int[]) is '
dartclient_module=dart_sum_array
';

create function dartclient_test.sum_array_status(_numbers int[])
returns int
language sql
as $$
select coalesce(sum(n), 0)::int from unnest(_numbers) as n;
$$;
comment on function dartclient_test.sum_array_status(int[]) is '
dartclient_module=dart_sum_array_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class SumArrayTests
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

class DartclientTestSumArrayRequest {
  final List<int>? numbers;

  const DartclientTestSumArrayRequest({
    this.numbers,
  });

  factory DartclientTestSumArrayRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestSumArrayRequest(
      numbers: (json['numbers'] as List?)?.map((e) => (e as num).toInt()).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'numbers': numbers,
    };
  }
}

/// function dartclient_test.sum_array(
///     _numbers integer[]
/// )
/// returns integer
///
/// comment on function dartclient_test.sum_array is 'dartclient_module=dart_sum_array';
///
/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.sum_array
Future<int> dartclientTestSumArray(DartclientTestSumArrayRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/sum-array');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_SumArray_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_sum_array.dart");
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

class DartclientTestSumArrayStatusRequest {
  final List<int>? numbers;

  const DartclientTestSumArrayStatusRequest({
    this.numbers,
  });

  factory DartclientTestSumArrayStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestSumArrayStatusRequest(
      numbers: (json['numbers'] as List?)?.map((e) => (e as num).toInt()).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'numbers': numbers,
    };
  }
}

/// function dartclient_test.sum_array_status(
///     _numbers integer[]
/// )
/// returns integer
///
/// comment on function dartclient_test.sum_array_status is 'dartclient_module=dart_sum_array_status
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<int>`.
///
/// See FUNCTION dartclient_test.sum_array_status
Future<ApiResult<int>> dartclientTestSumArrayStatus(DartclientTestSumArrayStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/sum-array-status');
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
        public void Test_SumArrayStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_sum_array_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
