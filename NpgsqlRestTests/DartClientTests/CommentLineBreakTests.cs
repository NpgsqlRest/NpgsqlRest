namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientCommentLineBreakTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.comment_line_break()
returns void
language sql
as $$
select null;
$$;
comment on function dartclient_test.comment_line_break() is E'\ndartclient_module=dart_comment_line_break\nFirst line\rfinal injected = 1;\n';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    /// <summary>
    /// Every physical line of a generated Dart doc comment starts with "///". A lone carriage return in a routine
    /// comment (Dart ends a line there too) used to leave the rest of that line outside the comment, as Dart code.
    /// </summary>
    [Collection("TestFixture")]
    public class CommentLineBreakTests
    {
        private const string Expected = """
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

/// function dartclient_test.comment_line_break()
/// returns void
///
/// comment on function dartclient_test.comment_line_break is 'dartclient_module=dart_comment_line_break
/// First line
/// final injected = 1;';
///
/// See FUNCTION dartclient_test.comment_line_break
Future<void> dartclientTestCommentLineBreak() async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/comment-line-break');
  await _send('POST', uri);
}

""";

        [Fact]
        public void Test_CommentLineBreak_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_comment_line_break.dart");
            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
