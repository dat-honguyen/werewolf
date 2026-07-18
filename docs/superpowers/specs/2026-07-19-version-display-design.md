# Design: version number + display for BE and FE

**Goal:** let a user (or us, debugging a deploy) see which build of the backend and frontend
they're currently talking to, matching the git release tag used for that deploy (e.g. `v1.0.1`).

## Backend (werewolf)

- New `GetVersion/GetVersionEndpoint.cs` (static-reference endpoint, same shape as
  `GetRoles`/`GetRules`): `GET /api/v1/version` → `{ "version": "v1.0.1" }`.
  Reads `Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev"`.
- `Dockerfile`: add `ARG APP_VERSION=dev` and `ENV APP_VERSION=$APP_VERSION` in the runtime
  stage, so the version is baked into the image itself (not dependent on the deploy script
  remembering to pass an env var at `docker run` time).
- `.github/workflows/deploy.yml`: new step computes `VERSION`:
  - if `github.event_name == 'release'`: `github.event.release.tag_name` (e.g. `v1.0.1`)
  - else (manual `workflow_dispatch` / non-release build): `dev-<short-sha>`
  Passed to `docker build --build-arg APP_VERSION="$VERSION" ...`.
- Regenerate Wolverine codegen (`dotnet run -- codegen write`) after adding the new endpoint,
  per this repo's standing instruction for any new handler/endpoint.

## Frontend (werewolf-frontend)

- New `src/environments/version.ts`, committed with a dev default:
  `export const APP_VERSION = 'dev';`
- `.github/workflows/deploy.yml`: new step, right before `npm run build -- --configuration
  production`, overwrites that file with the computed version (same tag-or-short-sha logic as
  the backend, sourced from the same GitHub Actions release event).
- Settings modal (`settings-modal.ts` / `.html`): a small footer line reading
  `FE v1.0.1 · BE v1.0.1`.
  - FE version: imported directly from `version.ts` (no network call, always available).
  - BE version: fetched via a new `getVersion()` call (added alongside the existing
    `LobbyApiService`-style services) hitting `GET /api/v1/version` when the modal opens.
    Shows `…` while loading, `unknown` if the fetch fails — never blocks the rest of the modal.

## Out of scope

- No version display on the home/landing screen (settings modal only, confirmed with user).
- No commit SHA or build-time metadata in the API response — just the version string
  (confirmed with user).
- No mismatch warning/highlighting if FE and BE versions differ — just display both side by
  side; a human reads them.
