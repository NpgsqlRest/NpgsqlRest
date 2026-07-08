namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientParseHooksTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.get_product_parse_url(_category_id int, _product_id int)
returns table (
    id int,
    category_id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99)
) as t(id, category_id, name, price);
$$;
comment on function dartclient_test.get_product_parse_url(int, int) is '
dartclient_module=dart_get_product_parse_url
HTTP GET
path /api/dart/categories/{_category_id}/products/{_product_id}/parse-url
dartclient_parse_url=true
';

create function dartclient_test.get_product_parse_request(_category_id int, _product_id int)
returns table (
    id int,
    category_id int,
    name text,
    price numeric
)
language sql
as $$
select * from (
    values
    (_product_id, _category_id, 'Product Name', 99.99)
) as t(id, category_id, name, price);
$$;
comment on function dartclient_test.get_product_parse_request(int, int) is '
dartclient_module=dart_get_product_parse_request
HTTP GET
path /api/dart/categories/{_category_id}/products/{_product_id}/parse-request
dartclient_parse_request=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class ParseHooksTests
    {
        private const string ExpectedParseUrl = """
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

class DartCategoriesCategoryIdProductsProductIdParseUrlRequest {
  final int? categoryId;
  final int? productId;

  const DartCategoriesCategoryIdProductsProductIdParseUrlRequest({
    this.categoryId,
    this.productId,
  });

  factory DartCategoriesCategoryIdProductsProductIdParseUrlRequest.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdParseUrlRequest(
      categoryId: (json['categoryId'] as num?)?.toInt(),
      productId: (json['productId'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'categoryId': categoryId,
      'productId': productId,
    };
  }
}

class DartCategoriesCategoryIdProductsProductIdParseUrlResponse {
  final int? id;
  final int? categoryId;
  final String? name;
  final double? price;

  const DartCategoriesCategoryIdProductsProductIdParseUrlResponse({
    this.id,
    this.categoryId,
    this.name,
    this.price,
  });

  factory DartCategoriesCategoryIdProductsProductIdParseUrlResponse.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdParseUrlResponse(
      id: (json['id'] as num?)?.toInt(),
      categoryId: (json['categoryId'] as num?)?.toInt(),
      name: json['name'] as String?,
      price: (json['price'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'categoryId': categoryId,
      'name': name,
      'price': price,
    };
  }
}

/// function dartclient_test.get_product_parse_url(
///     _category_id integer,
///     _product_id integer
/// )
/// returns table(
///     id integer,
///     category_id integer,
///     name text,
///     price numeric
/// )
///
/// comment on function dartclient_test.get_product_parse_url is 'dartclient_module=dart_get_product_parse_url
/// HTTP GET
/// path /api/dart/categories/{_category_id}/products/{_product_id}/parse-url
/// dartclient_parse_url=true';
///
/// [request] Carries the endpoint parameters.
/// [parseUrl] Optional function to rewrite the constructed URI before making the request.
/// Returns `List<DartCategoriesCategoryIdProductsProductIdParseUrlResponse>`.
///
/// See FUNCTION dartclient_test.get_product_parse_url
Future<List<DartCategoriesCategoryIdProductsProductIdParseUrlResponse>> dartCategoriesCategoryIdProductsProductIdParseUrl(
  DartCategoriesCategoryIdProductsProductIdParseUrlRequest request, {
  Uri Function(Uri uri)? parseUrl,
}) async {
  var uri = Uri.parse('$baseUrl/api/dart/categories/${request.categoryId}/products/${request.productId}/parse-url');
  if (parseUrl != null) {
    uri = parseUrl(uri);
  }
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartCategoriesCategoryIdProductsProductIdParseUrlResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_ParseUrl_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_product_parse_url.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedParseUrl);
        }

        private const string ExpectedParseRequest = """
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
  http.Request Function(http.Request request)? parseRequest,
}) async {
  var request = http.Request(method, uri);
  if (headers != null) {
    request.headers.addAll(headers);
  }
  if (body != null) {
    request.body = body;
  }
  if (parseRequest != null) {
    request = parseRequest(request);
  }
  return http.Response.fromStream(await _client.send(request));
}

class DartCategoriesCategoryIdProductsProductIdParseRequestRequest {
  final int? categoryId;
  final int? productId;

  const DartCategoriesCategoryIdProductsProductIdParseRequestRequest({
    this.categoryId,
    this.productId,
  });

  factory DartCategoriesCategoryIdProductsProductIdParseRequestRequest.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdParseRequestRequest(
      categoryId: (json['categoryId'] as num?)?.toInt(),
      productId: (json['productId'] as num?)?.toInt(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'categoryId': categoryId,
      'productId': productId,
    };
  }
}

class DartCategoriesCategoryIdProductsProductIdParseRequestResponse {
  final int? id;
  final int? categoryId;
  final String? name;
  final double? price;

  const DartCategoriesCategoryIdProductsProductIdParseRequestResponse({
    this.id,
    this.categoryId,
    this.name,
    this.price,
  });

  factory DartCategoriesCategoryIdProductsProductIdParseRequestResponse.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdParseRequestResponse(
      id: (json['id'] as num?)?.toInt(),
      categoryId: (json['categoryId'] as num?)?.toInt(),
      name: json['name'] as String?,
      price: (json['price'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'categoryId': categoryId,
      'name': name,
      'price': price,
    };
  }
}

/// function dartclient_test.get_product_parse_request(
///     _category_id integer,
///     _product_id integer
/// )
/// returns table(
///     id integer,
///     category_id integer,
///     name text,
///     price numeric
/// )
///
/// comment on function dartclient_test.get_product_parse_request is 'dartclient_module=dart_get_product_parse_request
/// HTTP GET
/// path /api/dart/categories/{_category_id}/products/{_product_id}/parse-request
/// dartclient_parse_request=true';
///
/// [request] Carries the endpoint parameters.
/// [parseRequest] Optional function to rewrite the constructed request before it is sent.
/// Returns `List<DartCategoriesCategoryIdProductsProductIdParseRequestResponse>`.
///
/// See FUNCTION dartclient_test.get_product_parse_request
Future<List<DartCategoriesCategoryIdProductsProductIdParseRequestResponse>> dartCategoriesCategoryIdProductsProductIdParseRequest(
  DartCategoriesCategoryIdProductsProductIdParseRequestRequest request, {
  http.Request Function(http.Request request)? parseRequest,
}) async {
  final uri = Uri.parse('$baseUrl/api/dart/categories/${request.categoryId}/products/${request.productId}/parse-request');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
    parseRequest: parseRequest,
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartCategoriesCategoryIdProductsProductIdParseRequestResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_ParseRequest_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_product_parse_request.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedParseRequest);
        }
    }
}
