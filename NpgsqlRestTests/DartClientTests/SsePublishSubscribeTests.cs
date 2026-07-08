namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientSsePublishSubscribeTests()
        {
            script.Append("""
create schema if not exists dartclient_test;

create function dartclient_test.sse_publish_only(_message text)
returns text
language plpgsql
as $$
begin
    raise info '%', _message;
    return 'published';
end;
$$;
comment on function dartclient_test.sse_publish_only(text) is '
dartclient_module=dart_sse_publish_only
HTTP POST
sse_publish
';

create function dartclient_test.sse_subscribe_only()
returns void
language plpgsql immutable
as $$ begin perform 1; end $$;
comment on function dartclient_test.sse_subscribe_only() is '
dartclient_module=dart_sse_subscribe_only
HTTP GET
sse_subscribe /api/dartclient-test/sse-subscribe-only/events
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class SsePublishSubscribeTests
    {
        private const string ExpectedPublish = """
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

class DartclientTestSsePublishOnlyRequest {
  final String? message;

  const DartclientTestSsePublishOnlyRequest({
    this.message,
  });

  factory DartclientTestSsePublishOnlyRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestSsePublishOnlyRequest(
      message: json['message'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'message': message,
    };
  }
}

/// function dartclient_test.sse_publish_only(
///     _message text
/// )
/// returns text
///
/// comment on function dartclient_test.sse_publish_only is 'dartclient_module=dart_sse_publish_only
/// HTTP POST
/// sse_publish';
///
/// [request] Carries the endpoint parameters.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.sse_publish_only
Future<String> dartclientTestSsePublishOnly(DartclientTestSsePublishOnlyRequest request) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/sse-publish-only');
  final response = await _send(
    'POST',
    uri,
    body: jsonEncode(request.toJson()),
  );
  return utf8.decode(response.bodyBytes);
}

""";

        [Fact]
        public void Test_SsePublishOnly_GeneratesNormalFunction()
        {
            // Publish-only endpoints have no SSE events path - they are plain POST endpoints and
            // must generate a normal function.
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_sse_publish_only.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedPublish);
        }

        private const string ExpectedSubscribe = """
import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;
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

String _randomId() {
  final random = math.Random();
  return List.generate(32, (_) => random.nextInt(16).toRadixString(16)).join();
}

/// Active server-sent events subscription. Call [close] to stop listening.
class SseSubscription {
  final http.Client _sseClient;
  final StreamSubscription<String> _lines;

  SseSubscription._(this._sseClient, this._lines);

  Future<void> close() async {
    await _lines.cancel();
    _sseClient.close();
  }
}

Future<SseSubscription> _sse(
  Uri uri,
  void Function(String message) onMessage,
) async {
  final client = http.Client();
  final request = http.Request('GET', uri);
  request.headers['Accept'] = 'text/event-stream';
  final response = await client.send(request);
  final data = StringBuffer();
  final lines = response.stream
      .transform(utf8.decoder)
      .transform(const LineSplitter())
      .listen((line) {
    if (line.startsWith('data:')) {
      if (data.isNotEmpty) {
        data.write('\n');
      }
      data.write(line.startsWith('data: ') ? line.substring(6) : line.substring(5));
    } else if (line.isEmpty && data.isNotEmpty) {
      onMessage(data.toString());
      data.clear();
    }
  });
  return SseSubscription._(client, lines);
}

Future<SseSubscription> createDartclientTestSseSubscribeOnlyEventSource(
  void Function(String message) onMessage, {
  String id = '',
}) {
  return _sse(Uri.parse('$baseUrl/api/dartclient-test/sse-subscribe-only/events?$id'), onMessage);
}

/// function dartclient_test.sse_subscribe_only()
/// returns void
///
/// comment on function dartclient_test.sse_subscribe_only is 'dartclient_module=dart_sse_subscribe_only
/// HTTP GET
/// sse_subscribe /api/dartclient-test/sse-subscribe-only/events';
///
/// [onMessage] Optional callback function to handle incoming SSE messages.
/// [id] Optional execution ID for the SSE connection. When supplied, only event streams opened with this ID in the query string will receive events.
/// [closeAfterMs] Time in milliseconds to wait before closing the SSE connection. Used only when onMessage callback is provided.
/// [awaitConnectionMs] Time in milliseconds to wait after opening the SSE connection before sending the request. Used only when onMessage callback is provided.
///
/// See FUNCTION dartclient_test.sse_subscribe_only
Future<void> dartclientTestSseSubscribeOnly({
  void Function(String message)? onMessage,
  String? id,
  int closeAfterMs = 1000,
  int awaitConnectionMs = 0,
}) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/sse-subscribe-only');
  final executionId = id ?? _randomId();
  SseSubscription? events;
  if (onMessage != null) {
    events = await createDartclientTestSseSubscribeOnlyEventSource(onMessage, id: executionId);
    await Future<void>.delayed(Duration(milliseconds: awaitConnectionMs));
  }
  try {
    await _send(
      'GET',
      uri,
      headers: {
        'X-NpgsqlRest-ID': executionId,
      },
    );
  } finally {
    if (events != null) {
      final subscription = events;
      unawaited(
        Future<void>.delayed(Duration(milliseconds: closeAfterMs), subscription.close),
      );
    }
  }
}

""";

        [Fact]
        public void Test_SseSubscribeOnly_GeneratedFile()
        {
            // Subscribe endpoints stream SSE - the generated function accepts onMessage/id/
            // closeAfterMs/awaitConnectionMs and an event source factory is emitted.
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_sse_subscribe_only.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(ExpectedSubscribe);
        }
    }
}
