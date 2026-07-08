namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientGetUserDataTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.get_user_data()
returns table (
    id int,
    name text,
    email text,
    is_active bool
)
language sql
as $$
select * from (
    values
    (1, 'Alice', 'alice@example.com', true),
    (2, 'Bob', 'bob@example.com', false)
) as t(id, name, email, is_active);
$$;
comment on function dartclient_test.get_user_data() is '
dartclient_module=dart_get_user_data
';

create function dartclient_test.get_user_data_status()
returns table (
    id int,
    name text,
    email text,
    is_active bool
)
language sql
as $$
select * from (
    values
    (1, 'Alice', 'alice@example.com', true),
    (2, 'Bob', 'bob@example.com', false)
) as t(id, name, email, is_active);
$$;
comment on function dartclient_test.get_user_data_status() is '
dartclient_module=dart_get_user_data_status
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class GetUserDataTests
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

class DartclientTestGetUserDataResponse {
  final int? id;
  final String? name;
  final String? email;
  final bool? isActive;

  const DartclientTestGetUserDataResponse({
    this.id,
    this.name,
    this.email,
    this.isActive,
  });

  factory DartclientTestGetUserDataResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetUserDataResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
      email: json['email'] as String?,
      isActive: json['isActive'] as bool?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'email': email,
      'isActive': isActive,
    };
  }
}

/// function dartclient_test.get_user_data()
/// returns table(
///     id integer,
///     name text,
///     email text,
///     is_active boolean
/// )
///
/// comment on function dartclient_test.get_user_data is 'dartclient_module=dart_get_user_data';
///
/// Returns `List<DartclientTestGetUserDataResponse>`.
///
/// See FUNCTION dartclient_test.get_user_data
Future<List<DartclientTestGetUserDataResponse>> dartclientTestGetUserData() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-user-data');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGetUserDataResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_GetUserData_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_user_data.dart");
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

class DartclientTestGetUserDataStatusResponse {
  final int? id;
  final String? name;
  final String? email;
  final bool? isActive;

  const DartclientTestGetUserDataStatusResponse({
    this.id,
    this.name,
    this.email,
    this.isActive,
  });

  factory DartclientTestGetUserDataStatusResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetUserDataStatusResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
      email: json['email'] as String?,
      isActive: json['isActive'] as bool?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'email': email,
      'isActive': isActive,
    };
  }
}

/// function dartclient_test.get_user_data_status()
/// returns table(
///     id integer,
///     name text,
///     email text,
///     is_active boolean
/// )
///
/// comment on function dartclient_test.get_user_data_status is 'dartclient_module=dart_get_user_data_status
/// dartclient_status_code=true';
///
/// Returns `ApiResult<List<DartclientTestGetUserDataStatusResponse>>`.
///
/// See FUNCTION dartclient_test.get_user_data_status
Future<ApiResult<List<DartclientTestGetUserDataStatusResponse>>> dartclientTestGetUserDataStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-user-data-status');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<List<DartclientTestGetUserDataStatusResponse>>(
    status: response.statusCode,
    response: ok
        ? (jsonDecode(utf8.decode(response.bodyBytes)) as List)
            .map((e) => DartclientTestGetUserDataStatusResponse.fromJson(e as Map<String, dynamic>))
            .toList()
        : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_GetUserDataStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_user_data_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
