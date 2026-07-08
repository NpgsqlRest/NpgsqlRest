# NpgsqlRest.DartClient

Automatic Dart client code generation for [NpgsqlRest](https://github.com/NpgsqlRest/NpgsqlRest), targeting Flutter projects.

Generates plain Dart modules that call NpgsqlRest endpoints using [`package:http`](https://pub.dev/packages/http) — no other dependencies, works on all Flutter targets (mobile, desktop, web).

## Generated code

For every endpoint the generator emits:

- A **request class** with a `toJson()` method (one field per routine parameter, optional-named constructor parameters).
- A **response class** with a `fromJson()` factory (one field per column), or a plain Dart type for scalar responses.
- An **async function** wrapping the HTTP call: query-string, path and JSON-body parameters handled automatically.
- Optional **`ApiResult<T>`/`ApiError`** wrappers carrying the HTTP status code and problem-details errors (`IncludeStatusCode`).
- **Multipart upload functions** for upload endpoints (`http.MultipartRequest`, files sent as form field `file`, optional `progress` callback reporting uploaded bytes, optional XSRF token header).
- **Server-sent events support** for SSE endpoints: a `create{Name}EventSource` factory returning an `SseSubscription` (built on streamed `package:http` responses — no extra dependency), and SSE-aware functions accepting `onMessage`, `id`, `closeAfterMs` and `awaitConnectionMs`, attaching the execution-id header to the triggering request.
- **Raw responses for proxies**: `proxy_out` and void proxy pass-through endpoints return `Future<http.Response>` directly.
- Optional per-call **`parseUrl` / `parseRequest` hooks** (`IncludeParseUrlParam` / `IncludeParseRequestParam`) to rewrite the URI or request before sending.

Testing: generated modules expose a top-level `http.Client? httpClient` variable — assign a `MockClient` from `package:http/testing.dart` to intercept requests in tests.

## Type mapping

| PostgreSQL | Dart |
|---|---|
| smallint, integer, bigint | `int` |
| real, double precision, numeric, money | `double` |
| boolean | `bool` |
| json, jsonb | `dynamic` (configurable via `DefaultJsonType`) |
| date, timestamp, timestamptz | `DateTime` (configurable via `UseDateTimeType`) |
| everything else (text, uuid, time, interval, ranges, ...) | `String` |
| arrays | `List<T>` |

All model fields are nullable (`T?`).

## Known limits

- `numeric`/`money` map to `double` — arbitrary precision beyond IEEE 754 is lost (the JSON wire format already carries a JSON number).
- Dart `int` on the web (dart2js) is limited to 2^53; `bigint` values beyond that lose precision on Flutter Web.
- Multi-dimensional PostgreSQL arrays are typed as one-dimensional `List<T>` (PostgreSQL's type system does not expose array dimensionality).

## Comment annotations

Per-endpoint overrides in routine comments:

- `dartclient off` — exclude the endpoint from generation.
- `dartclient_module=name` — group the endpoint into module file `name` (falls back to `tsclient_module`, so SQL-file directory grouping applies to both generators).
- `dartclient_status_code=true` — wrap this endpoint's response in `ApiResult<T>`.
- `dartclient_events=false` — generate a plain function for an SSE endpoint (no event-source parameters).
- `dartclient_parse_url=true` / `dartclient_parse_request=true` — add the per-call rewrite hooks to this endpoint.
- `dartclient_export_url=true` — emit a `{name}Url()` URL-builder function.
- `dartclient_url_only=true` — emit only the URL-builder function.
