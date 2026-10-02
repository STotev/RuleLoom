# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0](https://github.com/STotev/RuleForge/compare/v0.1.0...v0.2.0) (2026-10-02)


### Features

* action factory pattern & RuleEngine update ([4648872](https://github.com/STotev/RuleForge/commit/464887228906ad31d924b4d2bee9f7ca810472f7))
* add action executor factory abstraction ([d723f96](https://github.com/STotev/RuleForge/commit/d723f96338625fcd938d981a922dd9498b96f068))
* add direct action factory and update RuleEngine execution flow ([db98892](https://github.com/STotev/RuleForge/commit/db9889264c8da8429c3e93909770473efda58b54))
* add schedule calculation engine and action scheduling ([9533fc9](https://github.com/STotev/RuleForge/commit/9533fc981c89c5d1abe5fea47eb8142d24270c88))
* implement phase 1.2A scheduling abstractions ([60fb10e](https://github.com/STotev/RuleForge/commit/60fb10e64ff53fb8d8abd838035883dfea80c4a4))
* implement schedule calculation engine ([9398c05](https://github.com/STotev/RuleForge/commit/9398c0545fde84be7b1d498758f52236cebbfe78))
* scheduling model and action result improvements ([b4001c2](https://github.com/STotev/RuleForge/commit/b4001c2cd17aaf622cc3a43c87b842baf61dd008))


### Bug Fixes

* add CancellationToken to IActionExecutor and propagate to RuleEngine; cache ActionResult.Success(); seal LogAction and RuleEngine\n\nCo-authored-by: Copilot &lt;223556219+Copilot@users.noreply.github.com&gt; ([f2ec235](https://github.com/STotev/RuleForge/commit/f2ec235ee99c23ed864dd099c3fa06ef94da1cfb))

## [Unreleased]

### Added
- Introduced `IActionExecutorFactory` abstraction for action resolution.
- Added `DirectActionExecutorFactory` as the default implementation.
- Refactored `RuleEngine` to use the action factory and return per-rule `RuleExecutionResult` objects.
- Added and updated tests for the action factory and rule engine result model.
- Updated documentation and sample to reflect new extension points and usage.

### Changed
- Added scheduling support to actions via `ScheduleMetadata`, `ScheduleMode`, and `ScheduleUnit` types.
- `ActionDefinition` now supports an optional `Schedule` property for delayed or advanced execution.
- `IActionExecutor.ExecuteAsync` is now an async method returning `Task<ActionResult>` and accepts a `CancellationToken` (breaking change).
- Introduced `ActionResult`, `ActionExecutionStatus`, and updated `RuleExecutionResult` to wrap action results.
- Updated `LogAction` and test actions to new contract and sealed key classes.
- Improved JSON parsing for schedule enums and added/adjusted tests for new types and contract.
- `ActionResult.ScheduledFor` now uses `DateTimeOffset?` for explicit UTC-safe scheduling semantics (breaking change).
- `RuleEngine` now calculates schedule timestamps for non-immediate actions and returns `ActionResult.Scheduled(...)` instead of executing them immediately.
