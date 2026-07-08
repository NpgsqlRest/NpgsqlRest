namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientGreetTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.greet(_name text)
returns text
language sql
as $$
select 'Hello, ' || _name || '!';
$$;
comment on function dartclient_test.greet(text) is '
dartclient_module=dart_greet
';

create function dartclient_test.greet_status(_name text)
returns text
language sql
as $$
select 'Hello, ' || _name || '!';
$$;
comment on function dartclient_test.greet_status(text) is '
dartclient_module=dart_greet_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class GreetTests
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

class DartclientTestGreetRequest {
  final String? name;

  const DartclientTestGreetRequest({
    this.name,
  });

  factory DartclientTestGreetRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestGreetRequest(
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'name': name,
    };
  }
}

/// function dartclient_test.greet(
///     _name text
/// )
/// returns text
///
/// comment on function dartclient_test.greet is 'dartclient_module=dart_greet';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.greet
Future<String> dartclientTestGreet(DartclientTestGreetRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/greet');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_Greet_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_greet.dart");
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

class DartclientTestGreetStatusRequest {
  final String? name;

  const DartclientTestGreetStatusRequest({
    this.name,
  });

  factory DartclientTestGreetStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestGreetStatusRequest(
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'name': name,
    };
  }
}

/// function dartclient_test.greet_status(
///     _name text
/// )
/// returns text
///
/// comment on function dartclient_test.greet_status is 'dartclient_module=dart_greet_status
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<String>`.
///
/// See FUNCTION dartclient_test.greet_status
Future<ApiResult<String>> dartclientTestGreetStatus(DartclientTestGreetStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/greet-status');
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
        public void Test_GreetStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_greet_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
