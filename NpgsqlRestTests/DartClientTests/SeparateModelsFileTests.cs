// SQL setup for search_items lives in SearchItemsTests.cs.
// This class asserts the SeparateModelsFile=true instance output: model classes move to a
// {name}_models.dart file which the client file imports and re-exports.

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class SeparateModelsFileTests
    {
        private const string ExpectedClient = """
import 'dart:convert';
import 'package:http/http.dart' as http;
import 'dart_search_items_models.dart';
export 'dart_search_items_models.dart';

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
        public void Test_SeparateModels_ClientFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientModelsOutputPath, "dart_search_items.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedClient);
        }

        private const string ExpectedModels = """
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

""";

        [Fact]
        public void Test_SeparateModels_ModelsFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientModelsOutputPath, "dart_search_items_models.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedModels);
        }
    }
}
