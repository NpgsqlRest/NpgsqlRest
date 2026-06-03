# Static-content env-var injection for `ParseContentOptions`

> **Status**: design doc — not implemented. Carved out by a downstream
> consumer ([mathmodule2](https://github.com/vbilopav/mathmodule2)) who
> needs to surface a handful of K8s pod env vars to the SPA at boot time
> without rebuilding the bundle per environment. NpgsqlRest's existing
> `ParseContentOptions` already templates per-request user claims into
> static files via the same `{TOKEN}` substitution syntax; this proposes
> extending the same machinery to also feed *app-wide, request-independent*
> env-var values.

## The downstream use case

A Single-Page App bundled at build time can't read K8s env vars at
runtime. Standard solutions:

1. **Server-rendered config injection** — server templates a small
   `window.__appConfig = {...}` block into `index.html` per request,
   reading from env. SPA reads `window.__appConfig` before mounting.
   One round trip, app-wide values available before any code runs.
2. **Runtime `/config.json` endpoint** — SPA fetches config before
   mounting. Two round trips, slightly more flexible.

NpgsqlRest already implements **(1)** for per-user claims via
[`DefaultResponseParser.cs:13-78`](NpgsqlRestClient/DefaultResponseParser.cs#L13-L78)
and the `AvailableClaims` config key. So downstream consumers can do this
today for claim data:

```html
<!-- index.html -->
<script>
  window.__appConfig = {
    userId: {user_id},
    userName: {user_name}
  };
</script>
```

```jsonc
"ParseContentOptions": {
  "Enabled": true,
  "FilePaths": [ "/index.html" ],
  "AvailableClaims": ["user_id", "user_name"]
}
```

That works for per-user state but not for environment-level values like
`DEMO_FLAG`, `BUILD_LABEL`, analytics IDs, or feature-flag toggles —
NpgsqlRest's response parser today reads only from
`context.User.Claims`.

The mechanism (token substitution via `Formatter.FormatString`) is
already wired; what's missing is a parallel feed for static, env-sourced
values.

> **Relationship to the existing config-level env mechanism.** NpgsqlRest
> *already* injects env vars at the **configuration** layer:
> `Config:ParseEnvironmentVariables: true` (default) builds an `EnvDict`
> from `Environment.GetEnvironmentVariables()` and substitutes `{ENV}`
> tokens inside `appsettings.json` values (see
> [`Config.cs:102-110`](NpgsqlRestClient/Config.cs#L102-L110)). That feed
> is **server-side only** — the substituted values never leave the
> process. This proposal is a **different trust boundary**: it injects
> env values into bytes sent to *every browser client*. The two paths
> look similar in code (both call `Formatter.FormatString`) and must be
> kept distinct — see the security section. In particular, the
> config-level path can safely read the *whole* env dictionary; this
> client-facing path must **never** do that — it is allowlist-only.

## What changes

Add `AvailableEnvVars` under `StaticFiles.ParseContentOptions`. It accepts
**two forms**:

- **Array form** — `["NAME1", "NAME2"]`: list the env-var names. A missing
  env var resolves to the empty string `""`.
- **Object form** — `{ "NAME1": "default1", "NAME2": "" }`: list each name
  with an explicit fallback value used when the env var is **absent** from
  the process environment.

Each name is resolved **once at parser construction** via
`Environment.GetEnvironmentVariable` and injected into the same
`replacements` dictionary the parser already hands to
`Formatter.FormatString`. The same `{NAME1}` placeholder syntax the
existing claim path uses now works for env vars too.

**Values are JSON-escaped, exactly like claims.** The existing claim path
runs every value through `PgConverters.SerializeString` before
substitution, so `{user_id}` becomes the *quoted, escaped* JSON literal
`"123"` (or unquoted `null` when absent). Env vars follow the **same
rule**: the engine substitutes a fully-formed JSON literal, so the
template places the **bare** `{TOKEN}` with no surrounding quotes. This
is what makes the feature injection-safe — an env value containing `"`
or `</script>` is escaped and cannot break out of the `<script>` context.

`AvailableClaims` gains the **same object form** for symmetry — list a
claim with an explicit default used when the claim is absent or the user
is unauthenticated (array form keeps the historical `NULL` default).

Example config:

```jsonc
"StaticFiles": {
  "Enabled": true,
  "ParseContentOptions": {
    "Enabled": true,
    "FilePaths": [ "/index.html" ],
    "AvailableClaims": ["user_id", "user_name"],
    "AvailableEnvVars": {
      "BUILD_LABEL": "local",
      "DEMO_FLAG": "false",
      "TRACKING_ID": ""
    }
  }
}
```

Example `index.html` — note the **bare** tokens (no quotes); the
substituted value is already a complete JSON literal:

```html
<script>
  window.__appConfig = {
    userId: {user_id},          // → 123   or   null
    userName: {user_name},      // → "alice"   or   null
    buildLabel: {BUILD_LABEL},  // → "demo"   or   "local" (default)
    demoMode: {DEMO_FLAG} === "true",  // → "true" === "true" → true
    trackingId: {TRACKING_ID}   // → "GA-…"   or   "" (default)
  };
</script>
```

K8s sets `BUILD_LABEL=demo`, `DEMO_FLAG=true`, `TRACKING_ID=GA-…` on the
pod; NpgsqlRest substitutes them into every served `index.html` without
the SPA bundle needing to know about them at build time.

## Implementation

Three files touched, one new behaviour, no breaking change.

### 1. [`NpgsqlRestClient/App.cs`](NpgsqlRestClient/App.cs) (~5 lines)

In `ConfigureStaticFiles`, add a read for the new key right next to the
existing `availableClaims`:

```csharp
var availableClaims = _config.GetConfigEnumerable("AvailableClaims", parseCfg)?.ToArray();
var availableEnvVars = _config.GetConfigEnumerable("AvailableEnvVars", parseCfg)?.ToArray();
```

Thread `availableEnvVars` into the call to
`AppStaticFileMiddleware.ConfigureStaticFileMiddleware(...)` alongside
`availableClaims`.

### 2. [`NpgsqlRestClient/AppStaticFileMiddleware.cs`](NpgsqlRestClient/AppStaticFileMiddleware.cs) (~3 lines)

Add an `availableEnvVars` parameter to
`ConfigureStaticFileMiddleware` and thread it through into the
`DefaultResponseParser` constructor at the point that currently
passes `availableClaimTypes`.

### 3. [`NpgsqlRestClient/DefaultResponseParser.cs`](NpgsqlRestClient/DefaultResponseParser.cs)

Change the claim parameter to the name→default map and add the env-var
map. (`Dictionary<string, string?>`: value `null` = "no explicit default",
falling back to the per-feed default — `Consts.Null` for claims, `""` for
env vars.)

```csharp
public class DefaultResponseParser(
    NpgsqlRestAuthenticationOptions options,
    string? antiforgeryFieldNameTag,
    string? antiforgeryTokenTag,
    Dictionary<string, string?>? availableClaims,
    Dictionary<string, string?>? availableEnvVars)
{
    // Resolve env vars ONCE at construction. They don't change at
    // runtime within the pod's lifetime, so re-reading per request
    // would just add System.Environment overhead with no payoff. K8s
    // pod restarts re-instantiate the middleware → re-read.
    private readonly Dictionary<string, string> _envVarReplacements = ResolveEnvVars(availableEnvVars);

    private static Dictionary<string, string> ResolveEnvVars(Dictionary<string, string?>? envVars)
    {
        if (envVars is null || envVars.Count == 0) return [];
        var result = new Dictionary<string, string>(envVars.Count);
        foreach (var (name, def) in envVars)
        {
            // present env wins → configured default → empty string.
            var raw = Environment.GetEnvironmentVariable(name) ?? def ?? string.Empty;
            // JSON-escape, exactly like the claim path. The substituted
            // value is a complete JSON literal (a quoted string), so it
            // is safe inside a <script> block and the template uses a
            // bare {NAME} token, no surrounding quotes.
            result[name] = PgConverters.SerializeString(raw);
        }
        return result;
    }

    public ReadOnlySpan<char> Parse(...)
    {
        Dictionary<string, string> replacements = [...]; // existing claim work

        // … existing claim + antiforgery blocks …

        // Listed-but-absent claims fall back to their configured default,
        // or Consts.Null (the historical behaviour) when none was given.
        if (availableClaims is not null)
        {
            foreach (var (name, def) in availableClaims)
            {
                replacements.TryAdd(name, def ?? Consts.Null);
            }
        }

        // Feed env vars into the same replacements dictionary. Claim
        // values added above WIN — per-request user data takes precedence
        // over static config if the names ever collide.
        foreach (var (name, value) in _envVarReplacements)
        {
            replacements.TryAdd(name, value);
        }

        return Formatter.FormatString(input, replacements);
    }
}
```

That's the core of the feature.

## Design decisions to lock in before coding

### 1. Resolution timing: cache at construction vs. re-read per request

**Recommendation: cache at construction.**

- Pro: zero per-request overhead, deterministic — every request in the
  pod's lifetime sees the same value.
- Con: an `Environment.SetEnvironmentVariable` call inside the
  process wouldn't propagate. But that's a non-use-case — K8s sets
  env at pod start, not at runtime, and runtime mutation by app code
  is a code-smell.

### 2. Missing env var → configured default → empty string (never exception)

**Recommendation: per-name default, falling back to empty string.**

- Object form lets each name carry an explicit fallback used when the
  env var is absent: `{ "DEMO_FLAG": "false" }`. After JSON-escaping the
  template sees `"false"`.
- Array form (no defaults) falls back to the empty string `""` — a clean
  no-op token rather than the literal text `null`.
- Exception at startup is too aggressive; an unset feature-flag is
  legitimately empty in some environments (dev, local, etc.).

Because every value is JSON-escaped, an absent env var with no default
renders as the empty JSON string `""`, not the bare text `null` and not
broken HTML.

### 2b. Escaping: env values are JSON-serialized, like claims

**Recommendation: run env values through `PgConverters.SerializeString`,
identical to the claim path.**

- The claim path already escapes (`DefaultResponseParser.cs:35`). Env
  values inherited the *unescaped* treatment in the first draft of this
  spec, which is an injection bug: an env value of `"; alert(1); //`
  would break out of the JS string literal. Escaping it to a JSON string
  closes that — `buildLabel: {BUILD_LABEL}` with a quote-containing value
  stays a single well-formed string.
- **Precise guarantee (don't overclaim).** `PgConverters.SerializeString`
  uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, which escapes `"`,
  `\`, and control characters — but **not** `<` / `>` / `&`. So a value
  containing `</script>` is **not** neutralised and could still break out
  of the surrounding `<script>` element. This is acceptable here for the
  same reason it is for claims: env-var values are **operator-controlled**
  (whoever sets pod env already controls the whole deployment), so the
  realistic threat is an *accidental* quote or backslash, which the JSON
  escaping fully handles. It is **not** a defence against a hostile value.
  If a consumer templates genuinely untrusted data, they must encode it
  themselves. We deliberately keep parity with the claim path rather than
  introducing a second, stricter encoder.
- Consequence for templates: because the substituted value is a complete
  JSON literal (quoted string), templates use the **bare** `{NAME}` token
  with **no** surrounding quotes — `buildLabel: {BUILD_LABEL}`, not
  `buildLabel: "{BUILD_LABEL}"`. This matches how claims already work.

### 2c. Claim defaults (symmetry)

`AvailableClaims` gains the same object form. A claim listed with a
default uses that default when the claim is absent or the user is
anonymous; the array form keeps the historical `NULL` default
(`Consts.Null`). This directly answers the "same goes for claim values"
question — one mechanism covers both feeds.

### 3. Claim vs. env-var name collision

**Recommendation: claims win.**

- Per-request user data takes precedence over static config.
- Implemented by adding env vars AFTER the claim work with
  `replacements.TryAdd` — claims, already present, are not overwritten.
- Document this clearly so consumers don't accidentally rely on env
  vars being authoritative when a claim of the same name exists.

### 4. Casing / naming convention

Existing claims use lowercase with underscores (`{user_id}`,
`{user_name}`). Env vars conventionally use SCREAMING_SNAKE_CASE
(`{BUILD_LABEL}`). Keep them as-is — the substituter is
case-sensitive and matches whatever names the user lists. No
transformation. Consumers control the convention by choosing names.

### 5. Should there be a fallback chain?

Some frameworks let env vars override an `appsettings` default value.
Out of scope for this patch — that's a configuration-layer concern.
`AvailableEnvVars` is specifically about templating the *current
environment's* env-var values into static content. (The per-name default
in the object form is a *missing-value* fallback, not a config-override
chain.)

## Security guardrails (must enforce, not just document)

1. **Allowlist-only — never read the whole environment.** Resolution is
   `Environment.GetEnvironmentVariable(name)` over the explicit
   `AvailableEnvVars` list. It must **never** become a
   `Environment.GetEnvironmentVariables()` wholesale dump — that would
   leak `DB_PASSWORD`, `JWT_KEY`, connection strings, etc. into
   `index.html`. This is the key difference from the config-level
   `EnvDict` ([`Config.cs:102-110`](NpgsqlRestClient/Config.cs#L102-L110)),
   which *can* read the whole environment because it stays server-side.
   The two paths look similar; do not "unify" them. State this invariant
   in a code comment.
2. **Public templating.** Anything named in `AvailableEnvVars` is
   templated into static content served to **any** client. Never put a
   secret in the list — see the prominent doc guardrail below.
3. **Escaping.** Values are JSON-escaped (decision 2b) so an *accidental*
   quote/backslash cannot break the JS string. Note the relaxed encoder
   does **not** escape `<`/`>`; this matches the claim path and is fine
   because env values are operator-controlled, not untrusted input.

## Backward compatibility

- The new config key is **optional**. Missing or empty → no behaviour
  change. Existing configs continue to work bit-for-bit.
- `DefaultResponseParser`'s constructor gains an optional parameter
  (`string[]? availableEnvVars`) defaulting to `null`. No call site
  outside the codebase needs to change.
- No public API surface gains or loses members; the parser is internal
  to `NpgsqlRestClient`.

## Tests

`NpgsqlRestTests/ConfigTests/ResponseParserEnvVarTests.cs`:

1. **Empty `AvailableEnvVars`** → no substitution happens, content
   passes through untouched.
2. **Single env var present** → bare `{NAME}` becomes the env var's
   value as a JSON-escaped, quoted literal (`"value"`).
3. **Env var missing, no default (array form)** → `{NAME}` becomes the
   empty JSON string `""`, not literal `null`, not exception.
4. **Env var missing, object-form default** → `{NAME}` becomes the
   JSON-escaped default value.
5. **Escaping** → an env value containing `"` and `\` is JSON-escaped
   (`\"`, `\\`) so it stays a single well-formed JS string literal.
6. **Multiple env vars** → all substituted correctly in one pass.
7. **Claim-env collision** → claim value wins; env var ignored when
   the name is also in `context.User.Claims`.
8. **Anonymous request (no claims) + env vars** → env-var values
   substituted normally.
9. **Claim object-form default** → an absent claim listed with a default
   uses that default instead of `null`; array form keeps `null`.
10. **Cache-at-construction**: change `Environment.SetEnvironmentVariable`
    mid-test between two requests, verify the parser still returns the
    captured value (proves the value is cached at the parser, not
    re-read).
11. **Round-trip via the full middleware**: HTTP GET `/index.html`,
    verify the env-templated values appear in the response body.

`NpgsqlRestTests/ConfigTests/ConfigValidationTests.cs`:

9. **Unknown key under `ParseContentOptions`** still trips the existing
   `ValidateConfigKeys: "Error"` validator — verify `AvailableEnvVars`
   is now in the allowlist and configs using it don't fail validation.

## Documentation updates

- **`changelog/v3.16.3.md`** (or whichever next patch) — short note
  about the feature.
- **`npgsqlrest-docs/docs/config/static-files.md`** (or wherever
  `ParseContentOptions` is documented today) — add an
  `AvailableEnvVars` row to the settings reference table, a small
  example block showing env-var + claim co-use in one `index.html`,
  and the four design decisions (resolution timing, missing-value
  behaviour, claim precedence, casing) as explicit guarantees.

The doc must include the critical guardrail prominently:

> **Anything you name in `AvailableEnvVars` is publicly templated
> into static content served to any client.** Templating a secret
> (DB password, API key, signing token) into `index.html` will leak
> it to every signed-in user via DevTools. Treat the list as a
> public allowlist, never as a "make this accessible to the app"
> shortcut. Secrets stay in code paths that read env directly on
> the server side.

## Out of scope

- **Per-environment overrides via `appsettings.{env}.json`** — that's
  the standard ASP.NET Core config layer and doesn't interact with
  `ParseContentOptions`. Consumers can already use it for
  server-side config; the env-var feature is for client-side reach.
- **Dynamic env var refresh** — see design decision #1; explicitly
  not supported.
- **Type coercion** — `{DEMO_FLAG}` always substitutes as a raw
  string. If the SPA wants a bool, it does `"true" === envValue` in
  JS. The server has no idea what type the consumer wants the value
  rendered as.
- **Templated config substitution** — i.e. allowing env vars in
  `appsettings.json` itself. That mechanism already exists via the
  config's `ParseEnvironmentVariables: true` flag and `{ENV}` syntax
  in connection strings, etc. This is unrelated; it covers static
  content served to the browser.
- **Endpoint-level env templating** — a *separate* substitution path
  exists at runtime in
  [`NpgsqlRestEndpoint.cs:1637-1685`](NpgsqlRest/NpgsqlRestEndpoint.cs#L1637-L1685),
  where the same `Formatter.FormatString` engine templates parameter and
  claim values into **custom parameters, response content type, and
  response headers**. Feeding env vars there would let an operator flip
  *server-side* behaviour (headers, PG-function arguments) via an env
  var — arguably a more powerful lever for the "control features via env
  in K8s" goal than static HTML. It is **deliberately deferred** to a
  separate patch and must carry the **same allowlist discipline** (never
  auto-merge the whole environment into the endpoint lookup). Tracked as
  a follow-up, not part of this change.

## File layout

Files touched:

- `NpgsqlRestClient/Config.cs` (new `GetConfigNameDefaults` reader that
  accepts both array and object form)
- `NpgsqlRestClient/App.cs`
- `NpgsqlRestClient/AppStaticFileMiddleware.cs`
- `NpgsqlRestClient/DefaultResponseParser.cs`
- `NpgsqlRestClient/ConfigDefaults.cs`, `NpgsqlRestClient/ConfigTemplate.cs`,
  `NpgsqlRestClient/ConfigSchemaGenerator.cs` (register the new key in the
  defaults allowlist, the `--config` template, and the JSON-schema docs)
- `changelog/v3.16.3.md` (new)
- `npgsqlrest-docs/docs/config/static-files.md` (or equivalent) — add
  the new key, the guardrail, the four design decisions

New files:

- `NpgsqlRestTests/ConfigTests/ResponseParserEnvVarTests.cs`

## Implementation order

1. Read the new config key in `App.cs`, thread parameter through the
   middleware and parser ctors. No behaviour yet — both the new
   parameter and the new dictionary lookup remain a no-op until the
   config key is set.
2. Add the resolution logic + `Parse()` integration in
   `DefaultResponseParser`. With the config still empty, behaviour is
   unchanged.
3. Add tests 1-8 against the parser unit + tests 9 against config
   validation.
4. Update `ValidateConfigKeys` allowlist for the new key.
5. Document the feature + the security guardrail.

---

Document author context: this spec was drafted by an external agent
session helping a downstream consumer
([mathmodule2](https://github.com/vbilopav/mathmodule2)) work through a
K8s SPA deployment pattern. The downstream consumer plans to take it
over in a separate session to implement, test, and ship as a patch
release alongside the maintainer.
