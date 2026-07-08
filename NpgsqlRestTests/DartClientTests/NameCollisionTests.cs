namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientNameCollisionTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Two endpoints on the same path (GET and POST) camel-case to the same function name: the second
-- one gets a numeric suffix (dartCollide, dartCollide1). Their request shapes are identical, so on
-- the UniqueModels instance both functions share one request class.
create function dartclient_test.collide_get(_x int)
returns int
language sql
as $$
select _x;
$$;
comment on function dartclient_test.collide_get(int) is '
dartclient_module=dart_name_collision
HTTP GET
path /api/dart/collide
';

create function dartclient_test.collide_post(_x int)
returns int
language sql
as $$
select _x;
$$;
comment on function dartclient_test.collide_post(int) is '
dartclient_module=dart_name_collision
HTTP POST
path /api/dart/collide
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class NameCollisionTests
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

String _query(Map<String, Object?> query) {
  final parts = <String>[];
  query.forEach((key, value) {
    if (value is List) {
      for (final v in value) {
        parts.add(v == null ? '$key=' : '$key=${Uri.encodeQueryComponent(_str(v))}');
      }
    } else if (value == null) {
      parts.add('$key=');
    } else {
      parts.add('$key=${Uri.encodeQueryComponent(_str(value))}');
    }
  });
  return '?${parts.join('&')}';
}

String _str(Object value) =>
    value is DateTime ? value.toIso8601String() : value.toString();

class DartCollideRequest {
  final int? x;

  const DartCollideRequest({
    this.x,
  });

  factory DartCollideRequest.fromJson(Map<String, dynamic> json) {
    return DartCollideRequest(
      x: (json['x'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'x': x,
    };
  }
}

class DartCollide1Request {
  final int? x;

  const DartCollide1Request({
    this.x,
  });

  factory DartCollide1Request.fromJson(Map<String, dynamic> json) {
    return DartCollide1Request(
      x: (json['x'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'x': x,
    };
  }
}

/// function dartclient_test.collide_get(
///     _x integer
/// )
/// returns integer
///
/// comment on function dartclient_test.collide_get is 'dartclient_module=dart_name_collision
/// HTTP GET
/// path /api/dart/collide';
///
/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.collide_get
Future<int> dartCollide(DartCollideRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dart/collide' + _query({
    'x': request.x,
  }));
  final response = await _send('GET', uri);
  return int.parse(utf8.decode(response.bodyBytes));
}

/// function dartclient_test.collide_post(
///     _x integer
/// )
/// returns integer
///
/// comment on function dartclient_test.collide_post is 'dartclient_module=dart_name_collision
/// HTTP POST
/// path /api/dart/collide';
///
/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.collide_post
Future<int> dartCollide1(DartCollide1Request request) async {
  final uri = Uri.parse('$baseUrl/api/dart/collide');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_NameCollision_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_name_collision.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedUniqueModels = """
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'dart_name_collision_models.dart';
export 'dart_name_collision_models.dart';

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

String _query(Map<String, Object?> query) {
  final parts = <String>[];
  query.forEach((key, value) {
    if (value is List) {
      for (final v in value) {
        parts.add(v == null ? '$key=' : '$key=${Uri.encodeQueryComponent(_str(v))}');
      }
    } else if (value == null) {
      parts.add('$key=');
    } else {
      parts.add('$key=${Uri.encodeQueryComponent(_str(value))}');
    }
  });
  return '?${parts.join('&')}';
}

String _str(Object value) =>
    value is DateTime ? value.toIso8601String() : value.toString();

/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.collide_get
Future<int> dartCollide(DartCollideRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dart/collide' + _query({
    'x': request.x,
  }));
  final response = await _send('GET', uri);
  return int.parse(utf8.decode(response.bodyBytes));
}

/// [request] Carries the endpoint parameters.
/// Returns `int`.
///
/// See FUNCTION dartclient_test.collide_post
Future<int> dartCollide1(DartCollideRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dart/collide');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_NameCollision_UniqueModels_GeneratedFile()
        {
            // On the UniqueModels instance the two identical request shapes merge into one class.
            var filePath = Path.Combine(Setup.Program.DartClientModelsOutputPath, "dart_name_collision.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedUniqueModels);
        }
    }
}
