namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientCrudTableTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Table exposed through the CRUD source: each endpoint gets a -{method} suffix in the generated
-- function name (dartDartItemsGet, dartDartItemsPost, ...).
create table dartclient_test.dart_items (
    id int not null,
    name text
);
comment on table dartclient_test.dart_items is '
dartclient_module=dart_crud_items
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class CrudTableTests
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

class DartclientTestDartItemsGetRequest {
  final int? id;
  final String? name;

  const DartclientTestDartItemsGetRequest({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsGetRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsGetRequest(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (id != null) 'id': id,
      if (name != null) 'name': name,
    };
  }
}

class DartclientTestDartItemsGetResponse {
  final int? id;
  final String? name;

  const DartclientTestDartItemsGetResponse({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsGetResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsGetResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
    };
  }
}

class DartclientTestDartItemsDeleteRequest {
  final int? id;
  final String? name;

  const DartclientTestDartItemsDeleteRequest({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsDeleteRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsDeleteRequest(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (id != null) 'id': id,
      if (name != null) 'name': name,
    };
  }
}

class DartclientTestDartItemsReturningDeleteRequest {
  final int? id;
  final String? name;

  const DartclientTestDartItemsReturningDeleteRequest({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsReturningDeleteRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsReturningDeleteRequest(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (id != null) 'id': id,
      if (name != null) 'name': name,
    };
  }
}

class DartclientTestDartItemsReturningDeleteResponse {
  final int? id;
  final String? name;

  const DartclientTestDartItemsReturningDeleteResponse({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsReturningDeleteResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsReturningDeleteResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
    };
  }
}

class DartclientTestDartItemsPutRequest {
  final int? id;
  final String? name;

  const DartclientTestDartItemsPutRequest({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsPutRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsPutRequest(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      if (name != null) 'name': name,
    };
  }
}

class DartclientTestDartItemsReturningPutRequest {
  final int? id;
  final String? name;

  const DartclientTestDartItemsReturningPutRequest({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsReturningPutRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsReturningPutRequest(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (id != null) 'id': id,
      if (name != null) 'name': name,
    };
  }
}

class DartclientTestDartItemsReturningPutResponse {
  final int? id;
  final String? name;

  const DartclientTestDartItemsReturningPutResponse({
    this.id,
    this.name,
  });

  factory DartclientTestDartItemsReturningPutResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestDartItemsReturningPutResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
    };
  }
}

/// select dartclient_test.dart_items
///
/// comment on table dartclient_test.dart_items is 'dartclient_module=dart_crud_items';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartclientTestDartItemsGetResponse>`.
///
/// See TABLE dartclient_test.dart_items
Future<List<DartclientTestDartItemsGetResponse>> dartclientTestDartItemsGet(DartclientTestDartItemsGetRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-items' + _query({
    if (request.id != null) 'id': request.id,
    if (request.name != null) 'name': request.name,
  }));
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestDartItemsGetResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// delete from dartclient_test.dart_items
///
/// comment on table dartclient_test.dart_items is 'dartclient_module=dart_crud_items';
///
/// [request] Carries the endpoint parameters.
///
/// See TABLE dartclient_test.dart_items
Future<void> dartclientTestDartItemsDelete(DartclientTestDartItemsDeleteRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-items' + _query({
    if (request.id != null) 'id': request.id,
    if (request.name != null) 'name': request.name,
  }));
  await _send('DELETE', uri);
}

/// delete from dartclient_test.dart_items
/// returning
///     id, name
///
/// comment on table dartclient_test.dart_items is 'dartclient_module=dart_crud_items';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartclientTestDartItemsReturningDeleteResponse>`.
///
/// See TABLE dartclient_test.dart_items
Future<List<DartclientTestDartItemsReturningDeleteResponse>> dartclientTestDartItemsReturningDelete(DartclientTestDartItemsReturningDeleteRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-items/returning' + _query({
    if (request.id != null) 'id': request.id,
    if (request.name != null) 'name': request.name,
  }));
  final response = await _send(
    'DELETE',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestDartItemsReturningDeleteResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// insert into dartclient_test.dart_items
///
/// comment on table dartclient_test.dart_items is 'dartclient_module=dart_crud_items';
///
/// [request] Carries the endpoint parameters.
///
/// See TABLE dartclient_test.dart_items
Future<void> dartclientTestDartItemsPut(DartclientTestDartItemsPutRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-items');
  await _send(
    'PUT',
    uri,
    body: jsonEncode(request.toJson()),
  );
}

/// insert into dartclient_test.dart_items
/// returning
///     id, name
///
/// comment on table dartclient_test.dart_items is 'dartclient_module=dart_crud_items';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartclientTestDartItemsReturningPutResponse>`.
///
/// See TABLE dartclient_test.dart_items
Future<List<DartclientTestDartItemsReturningPutResponse>> dartclientTestDartItemsReturningPut(DartclientTestDartItemsReturningPutRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/dart-items/returning');
  final response = await _send(
    'PUT',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
    body: jsonEncode(request.toJson()),
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestDartItemsReturningPutResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_CrudItems_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_crud_items.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
