namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientSearchItemsTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.search_items(_query text, _page int, _limit int default 10)
returns table (
    id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (1, 'Item A', 10.99),
    (2, 'Item B', 20.50)
) as t(id, name, price);
$$;
comment on function dartclient_test.search_items(text, int, int) is '
dartclient_module=dart_search_items
HTTP GET
request_param_type query_string
';

create function dartclient_test.search_items_status(_query text, _page int, _limit int default 10)
returns table (
    id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (1, 'Item A', 10.99),
    (2, 'Item B', 20.50)
) as t(id, name, price);
$$;
comment on function dartclient_test.search_items_status(text, int, int) is '
dartclient_module=dart_search_items_status
HTTP GET
request_param_type query_string
dartclient_status_code=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class SearchItemsTests
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

class DartclientTestSearchItemsRequest {
  final String? query;
  final int? page;
  final int? limit;

  const DartclientTestSearchItemsRequest({
    this.query,
    this.page,
    this.limit,
  });

  factory DartclientTestSearchItemsRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestSearchItemsRequest(
      query: json['query'] as String?,
      page: (json['page'] as num?)?.toInt(),
      limit: (json['limit'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'query': query,
      'page': page,
      if (limit != null) 'limit': limit,
    };
  }
}

class DartclientTestSearchItemsResponse {
  final int? id;
  final String? name;
  final double? price;

  const DartclientTestSearchItemsResponse({
    this.id,
    this.name,
    this.price,
  });

  factory DartclientTestSearchItemsResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestSearchItemsResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
      price: (json['price'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'price': price,
    };
  }
}

/// function dartclient_test.search_items(
///     _query text,
///     _page integer,
///     _limit integer DEFAULT 10
/// )
/// returns table(
///     id integer,
///     name text,
///     price numeric
/// )
///
/// comment on function dartclient_test.search_items is 'dartclient_module=dart_search_items
/// HTTP GET
/// request_param_type query_string';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartclientTestSearchItemsResponse>`.
///
/// See FUNCTION dartclient_test.search_items
Future<List<DartclientTestSearchItemsResponse>> dartclientTestSearchItems(DartclientTestSearchItemsRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/search-items' + _query({
    'query': request.query,
    'page': request.page,
    if (request.limit != null) 'limit': request.limit,
  }));
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestSearchItemsResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_SearchItems_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_search_items.dart");
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

class DartclientTestSearchItemsStatusRequest {
  final String? query;
  final int? page;
  final int? limit;

  const DartclientTestSearchItemsStatusRequest({
    this.query,
    this.page,
    this.limit,
  });

  factory DartclientTestSearchItemsStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestSearchItemsStatusRequest(
      query: json['query'] as String?,
      page: (json['page'] as num?)?.toInt(),
      limit: (json['limit'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'query': query,
      'page': page,
      if (limit != null) 'limit': limit,
    };
  }
}

class DartclientTestSearchItemsStatusResponse {
  final int? id;
  final String? name;
  final double? price;

  const DartclientTestSearchItemsStatusResponse({
    this.id,
    this.name,
    this.price,
  });

  factory DartclientTestSearchItemsStatusResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestSearchItemsStatusResponse(
      id: (json['id'] as num?)?.toInt(),
      name: json['name'] as String?,
      price: (json['price'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'price': price,
    };
  }
}

/// function dartclient_test.search_items_status(
///     _query text,
///     _page integer,
///     _limit integer DEFAULT 10
/// )
/// returns table(
///     id integer,
///     name text,
///     price numeric
/// )
///
/// comment on function dartclient_test.search_items_status is 'dartclient_module=dart_search_items_status
/// HTTP GET
/// request_param_type query_string
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<List<DartclientTestSearchItemsStatusResponse>>`.
///
/// See FUNCTION dartclient_test.search_items_status
Future<ApiResult<List<DartclientTestSearchItemsStatusResponse>>> dartclientTestSearchItemsStatus(DartclientTestSearchItemsStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/search-items-status' + _query({
    'query': request.query,
    'page': request.page,
    if (request.limit != null) 'limit': request.limit,
  }));
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<List<DartclientTestSearchItemsStatusResponse>>(
    status: response.statusCode,
    response: ok
        ? (jsonDecode(utf8.decode(response.bodyBytes)) as List)
            .map((e) => DartclientTestSearchItemsStatusResponse.fromJson(e as Map<String, dynamic>))
            .toList()
        : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_SearchItemsStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_search_items_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }
    }
}
