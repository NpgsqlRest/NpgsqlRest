namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientReservedWordTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

-- Dart reserved words as parameter and column names: the generated field names get a trailing
-- underscore (class_, default_, ...) while the JSON keys keep the raw converted names.
create function dartclient_test.reserved_words(
    _class text,
    _default int,
    _is boolean,
    _in text,
    _required text,
    _new text
)
returns table (
    "class" text,
    "default" int,
    "is" boolean,
    "in" text,
    "required" text,
    "new" text,
    "void" text
)
language sql
as $$
select _class, _default, _is, _in, _required, _new, 'v';
$$;
comment on function dartclient_test.reserved_words(text, int, boolean, text, text, text) is '
dartclient_module=dart_reserved_words
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class ReservedWordTests
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

class DartclientTestReservedWordsRequest {
  final String? class_;
  final int? default_;
  final bool? is_;
  final String? in_;
  final String? required_;
  final String? new_;

  const DartclientTestReservedWordsRequest({
    this.class_,
    this.default_,
    this.is_,
    this.in_,
    this.required_,
    this.new_,
  });

  factory DartclientTestReservedWordsRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestReservedWordsRequest(
      class_: json['class'] as String?,
      default_: (json['default'] as num?)?.toInt(),
      is_: json['is'] as bool?,
      in_: json['in'] as String?,
      required_: json['required'] as String?,
      new_: json['new'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'class': class_,
      'default': default_,
      'is': is_,
      'in': in_,
      'required': required_,
      'new': new_,
    };
  }
}

class DartclientTestReservedWordsResponse {
  final String? class_;
  final int? default_;
  final bool? is_;
  final String? in_;
  final String? required_;
  final String? new_;
  final String? void_;

  const DartclientTestReservedWordsResponse({
    this.class_,
    this.default_,
    this.is_,
    this.in_,
    this.required_,
    this.new_,
    this.void_,
  });

  factory DartclientTestReservedWordsResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestReservedWordsResponse(
      class_: json['class'] as String?,
      default_: (json['default'] as num?)?.toInt(),
      is_: json['is'] as bool?,
      in_: json['in'] as String?,
      required_: json['required'] as String?,
      new_: json['new'] as String?,
      void_: json['void'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'class': class_,
      'default': default_,
      'is': is_,
      'in': in_,
      'required': required_,
      'new': new_,
      'void': void_,
    };
  }
}

/// function dartclient_test.reserved_words(
///     _class text,
///     _default integer,
///     _is boolean,
///     _in text,
///     _required text,
///     _new text
/// )
/// returns table(
///     class text,
///     "default" integer,
///     "is" boolean,
///     "in" text,
///     required text,
///     new text,
///     void text
/// )
///
/// comment on function dartclient_test.reserved_words is 'dartclient_module=dart_reserved_words';
///
/// [request] Carries the endpoint parameters.
/// Returns `List<DartclientTestReservedWordsResponse>`.
///
/// See FUNCTION dartclient_test.reserved_words
Future<List<DartclientTestReservedWordsResponse>> dartclientTestReservedWords(DartclientTestReservedWordsRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/reserved-words');
  final response = await _send(
    'POST',
    uri,
    headers: {
      'Content-Type': 'application/json',
    },
    body: jsonEncode(request.toJson()),
  );
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestReservedWordsResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_ReservedWords_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_reserved_words.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
