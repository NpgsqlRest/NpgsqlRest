namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientLoginLogoutTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Login endpoints always produce a token string response in the generated client, regardless of
-- the routine's declared return shape; logout endpoints are forced to void.
create function dartclient_test.dart_login(_username text, _password text)
returns table (
    name_identifier text,
    name text
)
language sql
as $$
select _username, _username;
$$;
comment on function dartclient_test.dart_login(text, text) is '
login
dartclient_module=dart_login
';

create function dartclient_test.dart_logout()
returns text
language sql
as $$
select 'bye';
$$;
comment on function dartclient_test.dart_logout() is '
logout
dartclient_module=dart_logout
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class LoginLogoutTests
    {
        private const string ExpectedLogin = """
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

class DartclientTestDartLoginRequest {
  final String? username;
  final String? password;

  const DartclientTestDartLoginRequest({
    this.username,
    this.password,
  });

  factory DartclientTestDartLoginRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartLoginRequest(
      username: json['username'] as String?,
      password: json['password'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'username': username,
      'password': password,
    };
  }
}

/// function dartclient_test.dart_login(
///     _username text,
///     _password text
/// )
/// returns table(
///     name_identifier text,
///     name text
/// )
///
/// comment on function dartclient_test.dart_login is 'login
/// dartclient_module=dart_login';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.dart_login
Future<String> dartclientTestDartLogin(DartclientTestDartLoginRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-login');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_Login_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_login.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedLogin);
        }

        private const string ExpectedLogout = """
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

/// function dartclient_test.dart_logout()
/// returns text
///
/// comment on function dartclient_test.dart_logout is 'logout
/// dartclient_module=dart_logout';
///
/// See FUNCTION dartclient_test.dart_logout
Future<void> dartclientTestDartLogout() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-logout');
  await _send('POST', uri);
}

""";

        [Fact]
        public void Test_Logout_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_logout.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedLogout);
        }
    }
}
