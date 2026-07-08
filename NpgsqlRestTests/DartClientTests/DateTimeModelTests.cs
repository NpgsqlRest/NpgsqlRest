namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientDateTimeModelTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- date/timestamp/timestamptz model fields map to DateTime (DateTime.parse in fromJson,
-- toIso8601String in toJson), including arrays of timestamps.
create function dartclient_test.datetime_models()
returns table (
    d date,
    ts timestamp,
    tstz timestamptz,
    ts_arr timestamp[]
)
language sql
as $$
select
    '2024-01-15'::date,
    '2024-01-15 10:30:00'::timestamp,
    '2024-01-15 10:30:00+02'::timestamptz,
    array['2024-01-15 10:30:00'::timestamp, '2024-02-20 11:00:00'::timestamp];
$$;
comment on function dartclient_test.datetime_models() is '
dartclient_module=dart_datetime_models
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class DateTimeModelTests
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

class DartclientTestDatetimeModelsResponse {
  final DateTime? d;
  final DateTime? ts;
  final DateTime? tstz;
  final List<DateTime>? tsArr;

  const DartclientTestDatetimeModelsResponse({
    this.d,
    this.ts,
    this.tstz,
    this.tsArr,
  });

  factory DartclientTestDatetimeModelsResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestDatetimeModelsResponse(
      d: json['d'] == null ? null : DateTime.parse(json['d'] as String),
      ts: json['ts'] == null ? null : DateTime.parse(json['ts'] as String),
      tstz: json['tstz'] == null ? null : DateTime.parse(json['tstz'] as String),
      tsArr: (json['tsArr'] as List?)?.map((e) => DateTime.parse(e as String)).toList(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'd': d?.toIso8601String(),
      'ts': ts?.toIso8601String(),
      'tstz': tstz?.toIso8601String(),
      'tsArr': tsArr?.map((e) => e.toIso8601String()).toList(),
    };
  }
}

/// function dartclient_test.datetime_models()
/// returns table(
///     d date,
///     ts timestamp without time zone,
///     tstz timestamp with time zone,
///     ts_arr timestamp without time zone[]
/// )
///
/// comment on function dartclient_test.datetime_models is 'dartclient_module=dart_datetime_models';
///
/// Returns `List<DartclientTestDatetimeModelsResponse>`.
///
/// See FUNCTION dartclient_test.datetime_models
Future<List<DartclientTestDatetimeModelsResponse>> dartclientTestDatetimeModels() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/datetime-models');
  final response = await _send(
    'POST',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestDatetimeModelsResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_DateTimeModels_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_datetime_models.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
