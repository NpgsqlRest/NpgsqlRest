namespace NpgsqlRestTests
{
    public static partial class Database
    {
        public static void DartClientUploadTests()
        {
            script.Append("""
create schema if not exists dartclient_test;
create function dartclient_test.upload_file(_meta json = null)
returns json
language plpgsql
as $$
begin
    return _meta;
end;
$$;
comment on function dartclient_test.upload_file(json) is '
dartclient_module=dart_upload_file
upload for file_system
param _meta is upload metadata
';
""");
        }
    }
}

namespace NpgsqlRestTests.DartClientTests
{
    [Collection("TestFixture")]
    public class UploadTests
    {
        private const string Expected = """
import 'dart:convert';
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

class DartclientTestUploadFileRequest {
  final dynamic meta;

  const DartclientTestUploadFileRequest({
    this.meta,
  });

  factory DartclientTestUploadFileRequest.fromJson(Map<String, dynamic> json) {
    return DartclientTestUploadFileRequest(
      meta: json['meta'],
    );
  }

  Map<String, dynamic> toJson() {
    return {
      if (meta != null) 'meta': meta,
    };
  }
}

class DartclientTestUploadFileResponse {
  final String? type;
  final String? fileName;
  final String? contentType;
  final int? size;
  final bool? success;
  final String? status;
  final Map<String, dynamic> raw;

  const DartclientTestUploadFileResponse({
    this.type,
    this.fileName,
    this.contentType,
    this.size,
    this.success,
    this.status,
    this.raw = const {},
  });

  factory DartclientTestUploadFileResponse.fromJson(Map<String, dynamic> json) {
    return DartclientTestUploadFileResponse(
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

/// function dartclient_test.upload_file(
///     _meta json DEFAULT NULL::json
/// )
/// returns json
///
/// comment on function dartclient_test.upload_file is 'dartclient_module=dart_upload_file
/// upload for file_system
/// param _meta is upload metadata';
///
/// [files] Multipart files to upload, sent as form field "file".
/// [request] Carries the endpoint parameters.
/// [progress] Optional callback reporting upload progress in bytes.
/// Returns `List<DartclientTestUploadFileResponse>`.
///
/// See FUNCTION dartclient_test.upload_file
Future<List<DartclientTestUploadFileResponse>> dartclientTestUploadFile(
  List<http.MultipartFile> files,
  DartclientTestUploadFileRequest request, {
  void Function(int loaded, int total)? progress,
}) async {
  final uri = Uri.parse('$baseUrl/api/dartclient-test/upload-file' + _query({
    if (request.meta != null) 'meta': request.meta,
  }));
  final multipart = http.MultipartRequest('POST', uri);
  multipart.files.addAll(files);
  final response = await _sendMultipart(multipart, progress: progress);
  if (response.statusCode < 200 || response.statusCode >= 300) {
    throw http.ClientException(utf8.decode(response.bodyBytes), uri);
  }
  return (jsonDecode(utf8.decode(response.bodyBytes)) as List)
      .map((e) => DartclientTestUploadFileResponse.fromJson(e as Map<String, dynamic>))
      .toList();
}

""";

        [Fact]
        public void Test_UploadFile_GeneratedFile()
        {
            var filePath = Path.Combine(Setup.Program.DartClientOutputPath, "dart_upload_file.dart");
            File.Exists(filePath).Should().BeTrue($"Expected file at {filePath}");

            var content = File.ReadAllText(filePath);
            content.Should().Be(Expected);
        }
    }
}
