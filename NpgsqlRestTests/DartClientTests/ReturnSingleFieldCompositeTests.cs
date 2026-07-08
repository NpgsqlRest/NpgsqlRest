namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientReturnSingleFieldCompositeTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Single field composite types
create type dartclient_test.dart_single_status as (
    status boolean
);

create type dartclient_test.dart_single_count as (
    count integer
);

-- Functions returning single-field composite types
create function dartclient_test.get_single_status()
returns dartclient_test.dart_single_status
language sql as
$$
select row(true)::dartclient_test.dart_single_status;
$$;
comment on function dartclient_test.get_single_status() is '
dartclient_module=dart_single_field_composite
';

create function dartclient_test.get_single_count()
returns dartclient_test.dart_single_count
language sql as
$$
select row(42)::dartclient_test.dart_single_count;
$$;
comment on function dartclient_test.get_single_count() is '
dartclient_module=dart_single_field_composite
';

-- Multi-field composite type for comparison
create type dartclient_test.dart_user_status as (
    is_active boolean,
    user_count integer
);

create function dartclient_test.get_user_status()
returns dartclient_test.dart_user_status
language sql as
$$
select row(true, 100)::dartclient_test.dart_user_status;
$$;
comment on function dartclient_test.get_user_status() is '
dartclient_module=dart_single_field_composite
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class ReturnSingleFieldCompositeTests
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

class DartclientTestGetSingleCountResponse {
  final int? count;

  const DartclientTestGetSingleCountResponse({
    this.count,
  });

  factory DartclientTestGetSingleCountResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetSingleCountResponse(
      count: (json['count'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'count': count,
    };
  }
}

class DartclientTestGetSingleStatusResponse {
  final bool? status;

  const DartclientTestGetSingleStatusResponse({
    this.status,
  });

  factory DartclientTestGetSingleStatusResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetSingleStatusResponse(
      status: json['status'] as bool?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'status': status,
    };
  }
}

class DartclientTestGetUserStatusResponse {
  final bool? isActive;
  final int? userCount;

  const DartclientTestGetUserStatusResponse({
    this.isActive,
    this.userCount,
  });

  factory DartclientTestGetUserStatusResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetUserStatusResponse(
      isActive: json['isActive'] as bool?,
      userCount: (json['userCount'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'isActive': isActive,
      'userCount': userCount,
    };
  }
}

/// function dartclient_test.get_single_count()
/// returns record
///
/// comment on function dartclient_test.get_single_count is 'dartclient_module=dart_single_field_composite';
///
/// Returns `DartclientTestGetSingleCountResponse`.
///
/// See FUNCTION dartclient_test.get_single_count
Future<DartclientTestGetSingleCountResponse> dartclientTestGetSingleCount() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-single-count');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return DartclientTestGetSingleCountResponse.fromJson(jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>);
}

/// function dartclient_test.get_single_status()
/// returns record
///
/// comment on function dartclient_test.get_single_status is 'dartclient_module=dart_single_field_composite';
///
/// Returns `DartclientTestGetSingleStatusResponse`.
///
/// See FUNCTION dartclient_test.get_single_status
Future<DartclientTestGetSingleStatusResponse> dartclientTestGetSingleStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-single-status');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return DartclientTestGetSingleStatusResponse.fromJson(jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>);
}

/// function dartclient_test.get_user_status()
/// returns record
///
/// comment on function dartclient_test.get_user_status is 'dartclient_module=dart_single_field_composite';
///
/// Returns `DartclientTestGetUserStatusResponse`.
///
/// See FUNCTION dartclient_test.get_user_status
Future<DartclientTestGetUserStatusResponse> dartclientTestGetUserStatus() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-user-status');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return DartclientTestGetUserStatusResponse.fromJson(jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>);
}

""";

        [Fact]
        public void Test_SingleFieldComposite_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_single_field_composite.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
