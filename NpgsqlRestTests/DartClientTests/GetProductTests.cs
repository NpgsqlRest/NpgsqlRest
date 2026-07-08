namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientGetProductTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.get_product(_category_id int, _product_id int)
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
comment on function dartclient_test.get_product(int, int) is '
dartclient_module=dart_get_product
HTTP GET
path /api/dart/categories/{_category_id}/products/{_product_id}
';

create function dartclient_test.get_product_status(_category_id int, _product_id int)
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
comment on function dartclient_test.get_product_status(int, int) is '
dartclient_module=dart_get_product_status
HTTP GET
path /api/dart/categories/{_category_id}/products/{_product_id}/status
dartclient_status_code=true
';

create function dartclient_test.get_product_export_url(_category_id int, _product_id int)
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
comment on function dartclient_test.get_product_export_url(int, int) is '
dartclient_module=dart_get_product_export_url
HTTP GET
path /api/dart/categories/{_category_id}/products/{_product_id}/export-url
dartclient_export_url=true
';

create function dartclient_test.get_product_url_only(_category_id int, _product_id int)
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
comment on function dartclient_test.get_product_url_only(int, int) is '
dartclient_module=dart_get_product_url_only
HTTP GET
path /api/dart/categories/{_category_id}/products/{_product_id}/url-only
dartclient_url_only=true
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class GetProductTests
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

class DartCategoriesCategoryIdProductsProductIdRequest {
  final int? categoryId;
  final int? productId;

  const DartCategoriesCategoryIdProductsProductIdRequest({
    this.categoryId,
    this.productId,
  });

  factory DartCategoriesCategoryIdProductsProductIdRequest.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdRequest(
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

class DartCategoriesCategoryIdProductsProductIdResponse {
  final int? id;
  final int? categoryId;
  final String? name;
  final double? price;

  const DartCategoriesCategoryIdProductsProductIdResponse({
    this.id,
    this.categoryId,
    this.name,
    this.price,
  });

  factory DartCategoriesCategoryIdProductsProductIdResponse.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdResponse(
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

/// function dartclient_test.get_product(
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
/// comment on function dartclient_test.get_product is 'dartclient_module=dart_get_product
/// HTTP GET
/// path /api/dart/categories/{_category_id}/products/{_product_id}';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartCategoriesCategoryIdProductsProductIdResponse>`.
///
/// See FUNCTION dartclient_test.get_product
Future<List<DartCategoriesCategoryIdProductsProductIdResponse>> dartCategoriesCategoryIdProductsProductId(DartCategoriesCategoryIdProductsProductIdRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dart/categories/${request.categoryId}/products/${request.productId}');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartCategoriesCategoryIdProductsProductIdResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_GetProduct_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_product.dart");
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

class DartCategoriesCategoryIdProductsProductIdStatusRequest {
  final int? categoryId;
  final int? productId;

  const DartCategoriesCategoryIdProductsProductIdStatusRequest({
    this.categoryId,
    this.productId,
  });

  factory DartCategoriesCategoryIdProductsProductIdStatusRequest.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdStatusRequest(
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

class DartCategoriesCategoryIdProductsProductIdStatusResponse {
  final int? id;
  final int? categoryId;
  final String? name;
  final double? price;

  const DartCategoriesCategoryIdProductsProductIdStatusResponse({
    this.id,
    this.categoryId,
    this.name,
    this.price,
  });

  factory DartCategoriesCategoryIdProductsProductIdStatusResponse.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdStatusResponse(
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

/// function dartclient_test.get_product_status(
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
/// comment on function dartclient_test.get_product_status is 'dartclient_module=dart_get_product_status
/// HTTP GET
/// path /api/dart/categories/{_category_id}/products/{_product_id}/status
/// dartclient_status_code=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `ApiResult<List<DartCategoriesCategoryIdProductsProductIdStatusResponse>>`.
///
/// See FUNCTION dartclient_test.get_product_status
Future<ApiResult<List<DartCategoriesCategoryIdProductsProductIdStatusResponse>>> dartCategoriesCategoryIdProductsProductIdStatus(DartCategoriesCategoryIdProductsProductIdStatusRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dart/categories/${request.categoryId}/products/${request.productId}/status');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  final ok = response.statusCode >= 200 && response.statusCode < 300;
  return ApiResult<List<DartCategoriesCategoryIdProductsProductIdStatusResponse>>(
    status: response.statusCode,
    response: ok
        ? (jsonDecode(utf8.decode(response.bodyBytes)) as List)
            .map((e) => DartCategoriesCategoryIdProductsProductIdStatusResponse.fromJson(e as Map<String, dynamic>))
            .toList()
        : null,
    error: _error(response),
  );
}

""";

        [Fact]
        public void Test_GetProductStatus_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_product_status.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedStatus);
        }

        private const string ExpectedExportUrl = """
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

String dartCategoriesCategoryIdProductsProductIdExportUrlUrl(DartCategoriesCategoryIdProductsProductIdExportUrlRequest request) => '$baseUrl/api/dart/categories/${request.categoryId}/products/${request.productId}/export-url';

class DartCategoriesCategoryIdProductsProductIdExportUrlRequest {
  final int? categoryId;
  final int? productId;

  const DartCategoriesCategoryIdProductsProductIdExportUrlRequest({
    this.categoryId,
    this.productId,
  });

  factory DartCategoriesCategoryIdProductsProductIdExportUrlRequest.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdExportUrlRequest(
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

class DartCategoriesCategoryIdProductsProductIdExportUrlResponse {
  final int? id;
  final int? categoryId;
  final String? name;
  final double? price;

  const DartCategoriesCategoryIdProductsProductIdExportUrlResponse({
    this.id,
    this.categoryId,
    this.name,
    this.price,
  });

  factory DartCategoriesCategoryIdProductsProductIdExportUrlResponse.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdExportUrlResponse(
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

/// function dartclient_test.get_product_export_url(
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
/// comment on function dartclient_test.get_product_export_url is 'dartclient_module=dart_get_product_export_url
/// HTTP GET
/// path /api/dart/categories/{_category_id}/products/{_product_id}/export-url
/// dartclient_export_url=true';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartCategoriesCategoryIdProductsProductIdExportUrlResponse>`.
///
/// See FUNCTION dartclient_test.get_product_export_url
Future<List<DartCategoriesCategoryIdProductsProductIdExportUrlResponse>> dartCategoriesCategoryIdProductsProductIdExportUrl(DartCategoriesCategoryIdProductsProductIdExportUrlRequest request) async {
  final uri = Uri.parse(dartCategoriesCategoryIdProductsProductIdExportUrlUrl(request));
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartCategoriesCategoryIdProductsProductIdExportUrlResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_GetProductExportUrl_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_product_export_url.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedExportUrl);
        }

        private const string ExpectedUrlOnly = """
String baseUrl = '';

String dartCategoriesCategoryIdProductsProductIdUrlOnlyUrl(DartCategoriesCategoryIdProductsProductIdUrlOnlyRequest request) => '$baseUrl/api/dart/categories/${request.categoryId}/products/${request.productId}/url-only';

class DartCategoriesCategoryIdProductsProductIdUrlOnlyRequest {
  final int? categoryId;
  final int? productId;

  const DartCategoriesCategoryIdProductsProductIdUrlOnlyRequest({
    this.categoryId,
    this.productId,
  });

  factory DartCategoriesCategoryIdProductsProductIdUrlOnlyRequest.fromJson(Map<String, dynamic> json) {
    return DartCategoriesCategoryIdProductsProductIdUrlOnlyRequest(
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

""";

        [Fact]
        public void Test_GetProductUrlOnly_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_get_product_url_only.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedUrlOnly);
        }
    }
}
