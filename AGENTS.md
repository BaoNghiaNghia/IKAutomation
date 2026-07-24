# IKAutomation Codex Guidelines

## Scope

These instructions apply to the entire repository unless a nested `AGENTS.md`
provides more specific guidance.

## Project

- This is a C#/.NET WPF automation project for Infinity Kingdom on LDPlayer.
- The solution is `IKAutomation.sln`; the main WPF project is under `ADB/`,
  and focused test projects are under `Tests/`.
- Follow the existing architecture, naming, dependency-injection, options,
  logging, and testing conventions.
- Prefer the smallest change that satisfies the task.
- Do not rewrite unrelated legacy workflows.
- Do not upgrade the target framework or replace existing image libraries
  unless the task explicitly requires it.

## Architecture

- Core workflow and domain models must not depend on WPF controls.
- UI event handlers must remain short: create a request, call a service, and
  render the result.
- Use existing abstractions such as:
  - `ILdPlayerClient`
  - `IGameStateDetector`
  - `ITemplateRegistry`
  - `IImageMatcher`
  - shared device operation lock/workflow lease
- Do not call Auto_LDPlayer, KAutoHelper, MainWindow, Dispatcher, or
  DeviceHelper directly from core services.
- Direct library calls belong only in adapters or existing implementations.
- Reuse existing services instead of copying matching, polling, retry, or
  navigation logic into higher-level workflows.
- Avoid nested device-lock deadlocks. Use the existing operation context,
  workflow lease, or internal no-lock/core methods.

## Async and cancellation

- Pass `CancellationToken` through all async operations.
- Do not use `CancellationToken.None` in new code.
- Every `Task.Delay` must receive the active `CancellationToken`.
- Do not create unbounded polling or retry loops.
- Validate timeout, polling, retry, and ROI options.
- Release locks and leases in `finally` blocks.
- Cancellation while waiting for a lock or lease must be respected.

## Image matching and input

- Runtime templates belong under `Data/InfinityKingdom/1280x720/vi/` within
  the main project.
- Do not create fake runtime templates.
- Do not use full-screen screenshots as runtime templates.
- Do not use Facebook images as fallback templates.
- Prefer stable UI overlays that are unaffected by map day/night lighting.
- Avoid template regions containing map terrain, trees, buildings,
  coordinates, timers, quantities, power values, or other dynamic text.
- Use configured ROI where available.
- Matching bounds returned from an ROI must use original screenshot
  coordinates.
- Immediately before every production Tap:
  1. capture a fresh screenshot;
  2. rematch the target;
  3. require valid bounds;
  4. tap the center of the latest bounds.
- Do not use hard-coded gameplay coordinates when match bounds exist.
- Do not use `TapByPercent` as an implicit production fallback.
- Never send Back, Tap, or Swipe blindly from Unknown state.
- Do not infer success from an input call alone; verify the resulting state.

## State and workflow behavior

- Specific overlays and dialogs must be detected before underlying WorldMap.
- Treat expected business outcomes separately from technical failures.
- Unknown is not automatically a technical error.
- Do not add OCR, YOLO, or new computer-vision frameworks unless explicitly
  requested.
- Do not start multi-device orchestration, schedulers, or infinite farm loops
  unless explicitly requested.
- A one-shot workflow must stop after one verified successful dispatch unless
  the task explicitly changes that behavior.

## Day/night robustness

- Do not detect states using the overall brightness or color of WorldMap.
- Prefer UI overlay templates and small stable anchors.
- Frame comparison must use a relevant ROI rather than the full screen.
- Prefer grayscale or luminance comparison when region-change detection is
  needed.

## Git safety

Before editing:

- Run `git status`.
- Run `git branch --show-current`.
- Run `git rev-parse HEAD`.
- Never work directly on `main`; create a feature, fix, or chore branch.
- Do not merge into `main`.
- Do not delete, overwrite, stage, or revert unrelated uncommitted changes.
- Do not modify remotes, credentials, or authentication settings.

Never use `git add .` or `git add -A`.

Never stage:

- `bin/`
- `obj/`
- `.vs/`
- `Logs/`
- `Diagnostics/`
- runtime screenshots
- generated XAML or caches
- local configuration
- credentials
- temporary files

Use one focused commit per task. Push with `git push -u origin HEAD`. If push
fails because of authentication or permissions, keep the local commit, do not
modify credentials or remotes, and report the failed command.

## Inspection scope

- Inspect only files relevant to the requested change.
- Do not scan the whole repository when the task names specific services,
  models, tests, options, or templates.
- Start with exact filenames and references.
- Expand the inspection scope only when necessary to resolve dependencies.

## Validation strategy

For small localized changes:

1. Build only affected projects when possible.
2. Run only affected test executables or filtered tests.
3. Inspect `git diff`.
4. Inspect `git status`.

Run a full solution build and all tests only when shared interfaces or
infrastructure changed broadly, the task is an integration milestone, the task
is preparing a pull request for merge, or the user explicitly requests it.

For documentation-only changes, do not build or run tests unless repository
hooks require it. Distinguish failures caused by the new change from existing
package or environment warnings and unavailable local dependencies. Do not
commit code with compile failures caused by the task.

## Testing

- Automated tests must not depend on a real LDPlayer unless explicitly marked
  as manual or integration diagnostics.
- Use existing fake or mock implementations.
- Test new behavior and the most important failure and cancellation paths.
- Prefer focused or parameterized regression tests over repetitive cases.

## Task execution

For every task:

1. Read this `AGENTS.md`.
2. Inspect only the requested scope.
3. State a brief implementation plan.
4. Make the smallest coherent change.
5. Run targeted validation.
6. Review `git diff` and `git status`.
7. Commit and push only when allowed by the task.

Do not repeat these repository-wide rules in each implementation prompt. The
task prompt should describe only the requested delta.

## Final report

Keep the final report concise, normally no more than 10 lines:

- branch
- commit hash
- push result
- files changed
- implementation summary
- targeted build or test result
- existing warnings
- known limitation or manual test instruction

Do not repeat the full task specification in the final report.
