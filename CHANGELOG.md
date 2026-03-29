# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
