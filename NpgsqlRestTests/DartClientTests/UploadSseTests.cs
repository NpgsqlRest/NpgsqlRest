namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientUploadSseTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.upload_with_sse(_meta json = null)
returns json
language plpgsql
as $$
begin
    raise notice 'Processing upload...';
    return _meta;
end;
$$;
comment on function dartclient_test.upload_with_sse(json) is '
dartclient_module=dart_upload_with_sse
upload for file_system
param _meta is upload metadata
sse_events_path /api/dartclient-test/upload-with-sse/events
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class UploadSseTests
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

Future<http.Response> _sendMultipart(
  http.MultipartRequest multipart, {
  void Function(int loaded, int total)? progress,
}) async {
  if (progress == null) {
    return http.Response.fromStream(await _client.send(multipart));
  }
  final total = multipart.contentLength;
  final bytes = multipart.finalize();
  final request = http.StreamedRequest(multipart.method, multipart.url);
  request.headers.addAll(multipart.headers);
  request.contentLength = total;
  var loaded = 0;
  bytes.listen(
    (chunk) {
      loaded += chunk.length;
      progress(loaded, total);
      request.sink.add(chunk);
    },
    onDone: request.sink.close,
    onError: request.sink.addError,
  );
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

Future<SseSubscription> createDartclientTestUploadWithSseEventSource(
  void Function(String message) onMessage, {
  String id = '',
}) {
  return _sse(Uri.parse('$baseUrl/api/dartclient-test/upload-with-sse/events?$id'), onMessage);
}

class DartclientTestUploadWithSseRequest {
  final dynamic meta;

  const DartclientTestUploadWithSseRequest({
    this.meta,
  });

  factory DartclientTestUploadWithSseRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestUploadWithSseRequest(
      meta: json['meta'],
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (meta != null) 'meta': meta,
    };
  }
}

class DartclientTestUploadWithSseResponse {
  final String? type;
  final String? fileName;
  final String? contentType;
  final int? size;
  final bool? success;
  final String? status;
  final Map<String, dynamic> raw;

  const DartclientTestUploadWithSseResponse({
    this.type,
    this.fileName,
    this.contentType,
    this.size,
    this.success,
    this.status,
    this.raw = const {},
  });

  factory DartclientTestUploadWithSseResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestUploadWithSseResponse(
      type: json['type'] as String?,
      fileName: json['fileName'] as String?,
      contentType: json['contentType'] as String?,
      size: (json['size'] as num?)?.toInt(),
      success: json['success'] as bool?,
      status: json['status'] as String?,
      raw: json,
    );
  }

  Map<String, dynamic> toJson() {
    return raw;
  }
}

/// function dartclient_test.upload_with_sse(
///     _meta json DEFAULT NULL::json
/// )
/// returns json
///
/// comment on function dartclient_test.upload_with_sse is 'dartclient_module=dart_upload_with_sse
/// upload for file_system
/// param _meta is upload metadata
/// sse_events_path /api/dartclient-test/upload-with-sse/events';
///
/// [files] Multipart files to upload, sent as form field "file".
/// [request] Carries the endpoint parameters.
/// [progress] Optional callback reporting upload progress in bytes.
/// [onMessage] Optional callback function to handle incoming SSE messages.
/// [id] Optional execution ID for the SSE connection. When supplied, only event streams opened with this ID in the query string will receive events.
/// [closeAfterMs] Time in milliseconds to wait before closing the SSE connection. Used only when onMessage callback is provided.
/// [awaitConnectionMs] Time in milliseconds to wait after opening the SSE connection before sending the request. Used only when onMessage callback is provided.
/// Returns `List<DartclientTestUploadWithSseResponse>`.
///
/// See FUNCTION dartclient_test.upload_with_sse
Future<List<DartclientTestUploadWithSseResponse>> dartclientTestUploadWithSse(
  List<http.MultipartFile> files,
  DartclientTestUploadWithSseRequest request, {
  void Function(int loaded, int total)? progress,
  void Function(String message)? onMessage,
  String? id,
  int closeAfterMs = 1000,
  int awaitConnectionMs = 0,
}) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/upload-with-sse' + _query({
    if (request.meta != null) 'meta': request.meta,
  }));
  final executionId = id ?? _randomId();
  final multipart = http.MultipartRequest('POST', uri);
  multipart.files.addAll(files);
  multipart.headers['X-NpgsqlRest-ID'] = executionId;
  SseSubscription? events;
  if (onMessage != null) {
    events = await createDartclientTestUploadWithSseEventSource(onMessage, id: executionId);
    await Future<void>.delayed(Duration(milliseconds: awaitConnectionMs));
  }
  try {
    final response = await _sendMultipart(multipart, progress: progress);
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw http.ClientException(utf8.decode(response.bodyBytes), uri);
    }
    return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
        .map((e) => DartclientTestUploadWithSseResponse.fromJson(e as Map<String, dynamic>))
        .toList();
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
        public void Test_UploadWithSse_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_upload_with_sse.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
