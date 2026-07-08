namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientSseTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.sse_endpoint(_message text)
returns text
language plpgsql
as $$
begin
    raise notice '%', _message;
    return 'done';
end;
$$;
comment on function dartclient_test.sse_endpoint(text) is '
dartclient_module=dart_sse_endpoint
HTTP POST
sse_events_path /api/dartclient-test/sse-endpoint/events
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class SseTests
    {
        private const string Expected = """
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

Future<SseSubscription> createDartclientTestSseEndpointEventSource(
  void Function(String message) onMessage, {
  String id = '',
}) {
  return _sse(Uri.parse('$baseUrl/api/dartclient-test/sse-endpoint/events?$id'), onMessage);
}

class DartclientTestSseEndpointRequest {
  final String? message;

  const DartclientTestSseEndpointRequest({
    this.message,
  });

  factory DartclientTestSseEndpointRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestSseEndpointRequest(
      message: json['message'] as String?,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'message': message,
    };
  }
}

/// function dartclient_test.sse_endpoint(
///     _message text
/// )
/// returns text
///
/// comment on function dartclient_test.sse_endpoint is 'dartclient_module=dart_sse_endpoint
/// HTTP POST
/// sse_events_path /api/dartclient-test/sse-endpoint/events';
///
/// [request] Carries the endpoint parameters.
/// [onMessage] Optional callback function to handle incoming SSE messages.
/// [id] Optional execution ID for the SSE connection. When supplied, only event streams opened with this ID in the query string will receive events.
/// [closeAfterMs] Time in milliseconds to wait before closing the SSE connection. Used only when onMessage callback is provided.
/// [awaitConnectionMs] Time in milliseconds to wait after opening the SSE connection before sending the request. Used only when onMessage callback is provided.
/// Returns `String`.
///
/// See FUNCTION dartclient_test.sse_endpoint
Future<String> dartclientTestSseEndpoint(
  DartclientTestSseEndpointRequest request, {
  void Function(String message)? onMessage,
  String? id,
  int closeAfterMs = 1000,
  int awaitConnectionMs = 0,
}) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/sse-endpoint');
  final executionId = id ?? _randomId();
  SseSubscription? events;
  if (onMessage != null) {
    events = await createDartclientTestSseEndpointEventSource(onMessage, id: executionId);
    await Future<void>.delayed(Duration(milliseconds: awaitConnectionMs));
  }
  try {
    final response = await _send(
      'POST',
      uri,
      headers: {
        'X-NpgsqlRest-ID': executionId,
      },
      body: jsonEncode(request.toJson()),
    );
    return utf8.decode(response.bodyBytes);
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
        public void Test_SseEndpoint_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_sse_endpoint.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
