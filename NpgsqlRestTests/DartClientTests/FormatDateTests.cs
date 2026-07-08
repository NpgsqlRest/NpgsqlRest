namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientFormatDateTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.format_date(_dt timestamp)
returns text
language sql
as $$
select to_char(_dt, 'YYYY-MM-DD');
$$;
comment on function dartclient_test.format_date(timestamp) is '
dartclient_module=dart_format_date
';

create function dartclient_test.format_date_status(_dt timestamp)
returns text
language sql
as $$
select to_char(_dt, 'YYYY-MM-DD');
$$;
comment on function dartclient_test.format_date_status(timestamp) is '
dartclient_module=dart_format_date_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class FormatDateTests
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

class DartclientTestFormatDateRequest {
  final DateTime? dt;

  const DartclientTestFormatDateRequest({
    this.dt,
  });

  factory DartclientTestFormatDateRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestFormatDateRequest(
      dt: json['dt'] == null ? null : DateTime.parse(json['dt'] as String),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'dt': dt?.toIso8601String(),
    };
  }
}

/// function dartclient_test.format_date(
///     _dt timestamp without time zone
/// )
/// returns text
///
/// comment on function dartclient_test.format_date is 'dartclient_module=dart_format_date';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.format_date
Future<String> dartclientTestFormatDate(DartclientTestFormatDateRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/format-date');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_FormatDate_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_format_date.dart");
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

class DartclientTestFormatDateStatusRequest {
  final DateTime? dt;

  const DartclientTestFormatDateStatusRequest({
    this.dt,
  });

  factory DartclientTestFormatDateStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestFormatDateStatusRequest(
      dt: json['dt'] == null ? null : DateTime.parse(json['dt'] as String),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'dt': dt?.toIso8601String(),
    };
  }
}

/// function dartclient_test.format_date_status(
///     _dt timestamp without time zone
/// )
/// returns text
///
/// comment on function dartclient_test.format_date_status is 'dartclient_module=dart_format_date_status
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<String>`.
///
/// See FUNCTION dartclient_test.format_date_status
Future<ApiResult<String>> dartclientTestFormatDateStatus(DartclientTestFormatDateStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/format-date-status');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<String>(
    status: response.statusCode,
    response: ok ? utf8.decode(response.bodyBytes) : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_FormatDateStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_format_date_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
