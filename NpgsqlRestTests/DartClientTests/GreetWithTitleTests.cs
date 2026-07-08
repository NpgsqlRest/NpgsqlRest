namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientGreetWithTitleTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.greet_with_title(_name text, _title text default 'Mr.')
returns text
language sql
as $$
select 'Hello, ' || _title || ' ' || _name || '!';
$$;
comment on function dartclient_test.greet_with_title(text, text) is '
dartclient_module=dart_greet_with_title
';

create function dartclient_test.greet_with_title_status(_name text, _title text default 'Mr.')
returns text
language sql
as $$
select 'Hello, ' || _title || ' ' || _name || '!';
$$;
comment on function dartclient_test.greet_with_title_status(text, text) is '
dartclient_module=dart_greet_with_title_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class GreetWithTitleTests
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

class DartclientTestGreetWithTitleRequest {
  final String? name;
  final String? title;

  const DartclientTestGreetWithTitleRequest({
    this.name,
    this.title,
  });

  factory DartclientTestGreetWithTitleRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestGreetWithTitleRequest(
      name: json['name'] as String?,
      title: json['title'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'name': name,
      if (title != null) 'title': title,
    };
  }
}

/// function dartclient_test.greet_with_title(
///     _name text,
///     _title text DEFAULT 'Mr.'::text
/// )
/// returns text
///
/// comment on function dartclient_test.greet_with_title is 'dartclient_module=dart_greet_with_title';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.greet_with_title
Future<String> dartclientTestGreetWithTitle(DartclientTestGreetWithTitleRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/greet-with-title');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_GreetWithTitle_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_greet_with_title.dart");
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

class DartclientTestGreetWithTitleStatusRequest {
  final String? name;
  final String? title;

  const DartclientTestGreetWithTitleStatusRequest({
    this.name,
    this.title,
  });

  factory DartclientTestGreetWithTitleStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestGreetWithTitleStatusRequest(
      name: json['name'] as String?,
      title: json['title'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'name': name,
      if (title != null) 'title': title,
    };
  }
}

/// function dartclient_test.greet_with_title_status(
///     _name text,
///     _title text DEFAULT 'Mr.'::text
/// )
/// returns text
///
/// comment on function dartclient_test.greet_with_title_status is 'dartclient_module=dart_greet_with_title_status
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<String>`.
///
/// See FUNCTION dartclient_test.greet_with_title_status
Future<ApiResult<String>> dartclientTestGreetWithTitleStatus(DartclientTestGreetWithTitleStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/greet-with-title-status');
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
        public void Test_GreetWithTitleStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_greet_with_title_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
