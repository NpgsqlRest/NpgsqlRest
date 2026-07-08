namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientNumericSplitTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Dart distinguishes int (smallint/integer/bigint) from double (real/double precision/numeric/money),
-- unlike TypeScript where every numeric is just `number`.
create function dartclient_test.numeric_split(
    _small smallint,
    _regular int,
    _big bigint,
    _real real,
    _double double precision,
    _numeric numeric,
    _money money
)
returns table (
    small_col smallint,
    regular_col int,
    big_col bigint,
    real_col real,
    double_col double precision,
    numeric_col numeric,
    money_col money
)
language sql
as $$
select _small, _regular, _big, _real, _double, _numeric, _money;
$$;
comment on function dartclient_test.numeric_split(smallint, int, bigint, real, double precision, numeric, money) is '
dartclient_module=dart_numeric_split
';

create function dartclient_test.numeric_split_bigint()
returns bigint
language sql
as $$
select 9007199254740993;
$$;
comment on function dartclient_test.numeric_split_bigint() is '
dartclient_module=dart_numeric_split_bigint
';

create function dartclient_test.numeric_split_real()
returns real
language sql
as $$
select 3.14;
$$;
comment on function dartclient_test.numeric_split_real() is '
dartclient_module=dart_numeric_split_real
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class NumericSplitTests
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

class DartclientTestNumericSplitRequest {
  final int? small;
  final int? regular;
  final int? big;
  final double? real;
  final double? double_;
  final double? numeric;
  final double? money;

  const DartclientTestNumericSplitRequest({
    this.small,
    this.regular,
    this.big,
    this.real,
    this.double_,
    this.numeric,
    this.money,
  });

  factory DartclientTestNumericSplitRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestNumericSplitRequest(
      small: (json['small'] as num?)?.toInt(),
      regular: (json['regular'] as num?)?.toInt(),
      big: (json['big'] as num?)?.toInt(),
      real: (json['real'] as num?)?.toDouble(),
      double_: (json['double'] as num?)?.toDouble(),
      numeric: (json['numeric'] as num?)?.toDouble(),
      money: (json['money'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'small': small,
      'regular': regular,
      'big': big,
      'real': real,
      'double': double_,
      'numeric': numeric,
      'money': money,
    };
  }
}

class DartclientTestNumericSplitResponse {
  final int? smallCol;
  final int? regularCol;
  final int? bigCol;
  final double? realCol;
  final double? doubleCol;
  final double? numericCol;
  final double? moneyCol;

  const DartclientTestNumericSplitResponse({
    this.smallCol,
    this.regularCol,
    this.bigCol,
    this.realCol,
    this.doubleCol,
    this.numericCol,
    this.moneyCol,
  });

  factory DartclientTestNumericSplitResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestNumericSplitResponse(
      smallCol: (json['smallCol'] as num?)?.toInt(),
      regularCol: (json['regularCol'] as num?)?.toInt(),
      bigCol: (json['bigCol'] as num?)?.toInt(),
      realCol: (json['realCol'] as num?)?.toDouble(),
      doubleCol: (json['doubleCol'] as num?)?.toDouble(),
      numericCol: (json['numericCol'] as num?)?.toDouble(),
      moneyCol: (json['moneyCol'] as num?)?.toDouble(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'smallCol': smallCol,
      'regularCol': regularCol,
      'bigCol': bigCol,
      'realCol': realCol,
      'doubleCol': doubleCol,
      'numericCol': numericCol,
      'moneyCol': moneyCol,
    };
  }
}

/// function dartclient_test.numeric_split(
///     _small smallint,
///     _regular integer,
///     _big bigint,
///     _real real,
///     _double double precision,
///     _numeric numeric,
///     _money money
/// )
/// returns table(
///     small_col smallint,
///     regular_col integer,
///     big_col bigint,
///     real_col real,
///     double_col double precision,
///     numeric_col numeric,
///     money_col money
/// )
///
/// comment on function dartclient_test.numeric_split is 'dartclient_module=dart_numeric_split';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartclientTestNumericSplitResponse>`.
///
/// See FUNCTION dartclient_test.numeric_split
Future<List<DartclientTestNumericSplitResponse>> dartclientTestNumericSplit(DartclientTestNumericSplitRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/numeric-split');
  final response = await _send(
    'POST',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
    body: jsonEncode(request.toJson()),
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestNumericSplitResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_NumericSplit_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_numeric_split.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }

        private const string ExpectedBigint = """
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

/// function dartclient_test.numeric_split_bigint()
/// returns bigint
///
/// comment on function dartclient_test.numeric_split_bigint is 'dartclient_module=dart_numeric_split_bigint';
///
/// Returns `int`.
///
/// See FUNCTION dartclient_test.numeric_split_bigint
Future<int> dartclientTestNumericSplitBigint() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/numeric-split-bigint');
  final response = await _send('POST', uri);
  return int.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_NumericSplitBigint_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_numeric_split_bigint.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedBigint);
        }

        private const string ExpectedReal = """
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

/// function dartclient_test.numeric_split_real()
/// returns real
///
/// comment on function dartclient_test.numeric_split_real is 'dartclient_module=dart_numeric_split_real';
///
/// Returns `double`.
///
/// See FUNCTION dartclient_test.numeric_split_real
Future<double> dartclientTestNumericSplitReal() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/numeric-split-real');
  final response = await _send('POST', uri);
  return double.parse(utf8.decode(response.bodyBytes));
}

""";

        [Fact]
        public void Test_NumericSplitReal_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_numeric_split_real.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedReal);
        }
    }
}
