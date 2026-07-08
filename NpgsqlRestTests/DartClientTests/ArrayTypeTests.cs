namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientArrayTypeTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Simple type for 2D composite array tests (defined here to ensure order)
create type dartclient_test.dart_simple_item as (
    id int,
    name text
);

-- 1D array (works correctly)
create function dartclient_test.get_simple_int_array()
returns table(
    numbers int[]
)
language sql as
$$
select array[1,2,3];
$$;
comment on function dartclient_test.get_simple_int_array() is '
dartclient_module=dart_array_types
';

-- 2D array of integers (limitation: typed as List<int> instead of List<List<int>>)
create function dartclient_test.get_2d_int_array()
returns table(
    matrix int[][]
)
language sql as
$$
select array[[1,2,3],[4,5,6]];
$$;
comment on function dartclient_test.get_2d_int_array() is '
dartclient_module=dart_array_types
';

-- 3D array of integers (limitation: typed as List<int> instead of List<List<List<int>>>)
create function dartclient_test.get_3d_int_array()
returns table(
    cube int[][][]
)
language sql as
$$
select array[[[1,2],[3,4]],[[5,6],[7,8]]];
$$;
comment on function dartclient_test.get_3d_int_array() is '
dartclient_module=dart_array_types
';

-- 2D array of text (limitation: typed as List<String> instead of List<List<String>>)
create function dartclient_test.get_2d_text_array()
returns table(
    matrix text[][]
)
language sql as
$$
select array[['a','b'],['c','d']];
$$;
comment on function dartclient_test.get_2d_text_array() is '
dartclient_module=dart_array_types
';

-- 2D array of composite types
create function dartclient_test.get_2d_composite_array()
returns table(
    matrix dartclient_test.dart_simple_item[][]
)
language sql as
$$
select array[
    [row(1,'a')::dartclient_test.dart_simple_item, row(2,'b')::dartclient_test.dart_simple_item],
    [row(3,'c')::dartclient_test.dart_simple_item, row(4,'d')::dartclient_test.dart_simple_item]
];
$$;
comment on function dartclient_test.get_2d_composite_array() is '
dartclient_module=dart_array_types
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class ArrayTypeTests
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

class Matrix {
  final int? id;
  final String? name;

  const Matrix({
    this.id,
    this.name,
  });

  factory Matrix.fromJson(Map<String, dynamic> json) {
    return Matrix(
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

class DartclientTestGet2dCompositeArrayResponse {
  final List<Matrix>? matrix;

  const DartclientTestGet2dCompositeArrayResponse({
    this.matrix,
  });

  factory DartclientTestGet2dCompositeArrayResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGet2dCompositeArrayResponse(
      matrix: (json['matrix'] as List?)?.map((e) => Matrix.fromJson(e as Map<String, dynamic>)).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'matrix': matrix?.map((e) => e.toJson()).toList(),
    };
  }
}

class DartclientTestGet2dIntArrayResponse {
  final List<int>? matrix;

  const DartclientTestGet2dIntArrayResponse({
    this.matrix,
  });

  factory DartclientTestGet2dIntArrayResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGet2dIntArrayResponse(
      matrix: (json['matrix'] as List?)?.map((e) => (e as num).toInt()).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'matrix': matrix,
    };
  }
}

class DartclientTestGet2dTextArrayResponse {
  final List<String>? matrix;

  const DartclientTestGet2dTextArrayResponse({
    this.matrix,
  });

  factory DartclientTestGet2dTextArrayResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGet2dTextArrayResponse(
      matrix: (json['matrix'] as List?)?.map((e) => e as String).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'matrix': matrix,
    };
  }
}

class DartclientTestGet3dIntArrayResponse {
  final List<int>? cube;

  const DartclientTestGet3dIntArrayResponse({
    this.cube,
  });

  factory DartclientTestGet3dIntArrayResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGet3dIntArrayResponse(
      cube: (json['cube'] as List?)?.map((e) => (e as num).toInt()).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'cube': cube,
    };
  }
}

class DartclientTestGetSimpleIntArrayResponse {
  final List<int>? numbers;

  const DartclientTestGetSimpleIntArrayResponse({
    this.numbers,
  });

  factory DartclientTestGetSimpleIntArrayResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestGetSimpleIntArrayResponse(
      numbers: (json['numbers'] as List?)?.map((e) => (e as num).toInt()).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'numbers': numbers,
    };
  }
}

/// function dartclient_test.get_2d_composite_array()
/// returns table(
///     matrix dartclient_test.dart_simple_item[]
/// )
///
/// comment on function dartclient_test.get_2d_composite_array is 'dartclient_module=dart_array_types';
///
/// Returns `List<DartclientTestGet2dCompositeArrayResponse>`.
///
/// See FUNCTION dartclient_test.get_2d_composite_array
Future<List<DartclientTestGet2dCompositeArrayResponse>> dartclientTestGet2dCompositeArray() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-2d-composite-array');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGet2dCompositeArrayResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// function dartclient_test.get_2d_int_array()
/// returns table(
///     matrix integer[]
/// )
///
/// comment on function dartclient_test.get_2d_int_array is 'dartclient_module=dart_array_types';
///
/// Returns `List<DartclientTestGet2dIntArrayResponse>`.
///
/// See FUNCTION dartclient_test.get_2d_int_array
Future<List<DartclientTestGet2dIntArrayResponse>> dartclientTestGet2dIntArray() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-2d-int-array');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGet2dIntArrayResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// function dartclient_test.get_2d_text_array()
/// returns table(
///     matrix text[]
/// )
///
/// comment on function dartclient_test.get_2d_text_array is 'dartclient_module=dart_array_types';
///
/// Returns `List<DartclientTestGet2dTextArrayResponse>`.
///
/// See FUNCTION dartclient_test.get_2d_text_array
Future<List<DartclientTestGet2dTextArrayResponse>> dartclientTestGet2dTextArray() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-2d-text-array');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGet2dTextArrayResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// function dartclient_test.get_3d_int_array()
/// returns table(
///     cube integer[]
/// )
///
/// comment on function dartclient_test.get_3d_int_array is 'dartclient_module=dart_array_types';
///
/// Returns `List<DartclientTestGet3dIntArrayResponse>`.
///
/// See FUNCTION dartclient_test.get_3d_int_array
Future<List<DartclientTestGet3dIntArrayResponse>> dartclientTestGet3dIntArray() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-3d-int-array');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGet3dIntArrayResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

/// function dartclient_test.get_simple_int_array()
/// returns table(
///     numbers integer[]
/// )
///
/// comment on function dartclient_test.get_simple_int_array is 'dartclient_module=dart_array_types';
///
/// Returns `List<DartclientTestGetSimpleIntArrayResponse>`.
///
/// See FUNCTION dartclient_test.get_simple_int_array
Future<List<DartclientTestGetSimpleIntArrayResponse>> dartclientTestGetSimpleIntArray() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/get-simple-int-array');
  final response = await _send(
    'GET',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestGetSimpleIntArrayResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_ArrayTypes_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_array_types.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
