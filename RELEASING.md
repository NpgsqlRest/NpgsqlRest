# Releasing NpgsqlRest

The complete release procedure. Written so that someone who has never cut a release can do it.

## Version model

- **One product version** for: core library (NuGet `NpgsqlRest`), the `NpgsqlRestClient` binary, release binaries/Docker tags, and the `npgsqlrest` npm package.
- **Single source of truth: [`version.txt`](version.txt)** in the repo root. Everything derives from it:
  - `Directory.Build.props` reads it into `$(NpgsqlRestProductVersion)` → core + client `.csproj` versions
  - `.github/workflows/build-test-publish.yml` reads it per job (`RELEASE_VERSION=v$(cat version.txt)`) → release tag, release name, changelog file lookup, Docker tags
  - the `npm-publish` job syncs `npm/package.json` from it before publishing
  - `npm/postinstall.js` downloads the binary from the release tag **matching its own package version** (`v${package.json version}`) — no hardcoded version anywhere in the npm package
- **Plugins (`plugins/*`) version independently** — bump a plugin's `.csproj` version only when that plugin changes. NuGet publish uses `--skip-duplicate`, so unchanged plugin versions are skipped automatically.

## Release checklist

1. **Finish the changelog**: `changelog/v<X.Y.Z>.md` must exist. The GitHub release description is built from it: its opening paragraphs (everything before the first `### ` section), followed by links to the full changelog and the install commands. The whole changelog is not copied into the release (GitHub limits a description to 125,000 characters, and the text would be there twice). So the paragraphs at the top should say what the release is. Cover in the file: headline, new features, breaking changes (⚠️), fixes, tests.
2. **Bump the version**: edit `version.txt` (e.g. `3.18.0`). Nothing else needs editing. Sanity check locally:
   ```sh
   dotnet pack NpgsqlRest/NpgsqlRest.csproj -c Release -o /tmp/packcheck   # expect NpgsqlRest.<X.Y.Z>.nupkg
   ```
3. **Bump plugin versions** in `plugins/*/​*.csproj` for any plugin that changed since its last published version.
4. **Full test suite green** locally (`dotnet test`, needs PostgreSQL on `localhost:5432`, `postgres`/`postgres`).
5. **Merge/push to `master`.** That's the trigger. The workflow then automatically:
   - builds + runs the full test suite (PostgreSQL 17 service container),
   - publishes all `.nupkg` to NuGet (`--skip-duplicate`),
   - creates the GitHub release `v<X.Y.Z>` with a short description (the changelog's opening paragraphs, links, install commands),
   - builds AOT binaries: win-x64, linux-x64 (+ **smoke test**: `--version` + `--validate` against a live PostgreSQL), linux-arm64, osx-arm64 — and uploads them as release assets together with `appsettings.json`,
   - builds + pushes Docker images: `vbilopav/npgsqlrest:{v,latest}{,-aot,-jit,-arm,-bun}`,
   - publishes the npm package (version synced from `version.txt`, OIDC trusted publishing).
6. **After the release**: verify the GitHub release page, `docker pull vbilopav/npgsqlrest:latest`, `npm view npgsqlrest version`, and NuGet listing.
7. **Docs**: update the docs site (separate repo, `npgsqlrest-docs`):
   - create `docs/guide/changelog/v<X.Y.Z>.md` from this repo's `changelog/v<X.Y.Z>.md`,
   - add it to the sidebar in `docs/.vitepress/config.ts` and move the "(Latest)" marker,
   - update `docs/guide/changelog/index.md` ("Version X.Y (Latest)" heading),
   - regenerate `docs/config/latest.md` from the released `NpgsqlRestClient/appsettings.json` (update its version references and download links),
   - update any feature pages affected by the release.

## Required repository secrets

| Secret | Used by |
|---|---|
| (NuGet: trusted publishing — no key; a policy on nuget.org for owner `vbilopav` trusts repository `NpgsqlRest/NpgsqlRest`, workflow file `build-test-publish.yml`) | NuGet publish |
| `DOCKER_HUB_USERNAME` / `DOCKER_HUB_TOKEN` | Docker Hub push |
| (npm: OIDC trusted publishing — no token; configured on npmjs.com for the `npgsqlrest` package) | npm publish |

## CI overview

- **`test.yml`** — runs on every push/PR to non-master branches: build + full suite on a **PostgreSQL 15/16/17 matrix**.
- **`build-test-publish.yml`** — runs on push to `master` (and when started by hand with `workflow_dispatch`): the full release pipeline above. It publishes: do not start it by hand from a branch that is not ready to release.

## Hotfix procedure

1. Branch from the release tag, fix, add `changelog/v<X.Y.Z+1>.md`, bump `version.txt`, test, merge to `master`. The pipeline does the rest.
