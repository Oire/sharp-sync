<!--
Sync Impact Report
==================
Version change: 1.0.0 → 1.1.0 (added platform safety rule)
Modified principles: N/A
Added sections:
  - Quality & Safety Standards: "No Null Device Writes" rule
Removed sections: N/A
Templates requiring updates:
  - .specify/templates/plan-template.md — ✅ compatible
  - .specify/templates/spec-template.md — ✅ compatible
  - .specify/templates/tasks-template.md — ✅ compatible
  - .specify/templates/checklist-template.md — ✅ compatible
  - .specify/templates/agent-file-template.md — ✅ compatible
Follow-up TODOs: None
-->

# SharpSync Constitution

## Core Principles

### I. Library-First, UI-Agnostic

SharpSync is a **pure .NET library** with zero UI dependencies. Every
feature MUST be consumable by any .NET application—console, desktop,
mobile, or server—without requiring a specific UI framework. No feature
may introduce a hard dependency on a UI toolkit, platform-specific
windowing API, or interactive prompt. Hooks (callbacks, events,
delegates) MUST be provided so that callers supply their own UI when
user interaction is needed (e.g., OAuth2 browser flow, conflict
resolution dialogs).

**Rationale**: SharpSync exists to power applications like Nimbus
(Windows desktop client) while remaining usable in headless CI
pipelines, Linux daemons, and cross-platform mobile apps. Coupling to
any UI would break this contract.

### II. Interface-Driven Design

All major components MUST be defined by interfaces (`ISyncEngine`,
`ISyncStorage`, `ISyncDatabase`, `IConflictResolver`, `ISyncFilter`,
`IOAuth2Provider`). Concrete implementations MUST accept dependencies
via constructor injection. New storage backends, conflict strategies,
and database providers MUST be addable without modifying existing code.

**Rationale**: Interface-based design enables testability (mocking),
extensibility (new backends), and inversion of control for host
applications that use dependency injection containers.

### III. Async-First, Thread-Safe Where Documented

All I/O-bound operations MUST use `async`/`await` with
`CancellationToken` support. Public APIs that are safe to call from
any thread (state properties, notification methods, pause/resume)
MUST be explicitly documented as thread-safe. Only one sync operation
may run at a time per `SyncEngine` instance—this invariant MUST be
enforced, not merely documented.

**Rationale**: Modern .NET consumers expect async APIs. Clear
thread-safety contracts prevent data races in desktop and server
applications that call SharpSync from multiple threads.

### IV. Test Discipline

Unit tests MUST accompany new functionality. Integration tests MUST
exist for every storage backend and MUST run in CI via Docker services
on Ubuntu. Tests MUST auto-skip gracefully (using `Skip.If()` or
equivalent) on platforms where required services are unavailable.
The test suite MUST pass on all CI matrix platforms (Ubuntu, Windows,
macOS) before a PR may merge. Code coverage MUST be reported via
Codecov.

**Rationale**: SharpSync targets multiple OS platforms and multiple
remote storage protocols. Automated, cross-platform testing is the
primary defense against regressions.

### V. Simplicity & YAGNI

Features MUST solve a current, demonstrated need—not a hypothetical
future one. Abstractions MUST be introduced only when two or more
concrete consumers exist. Complexity (e.g., additional projects,
repository patterns, plugin systems) MUST be justified in a
Complexity Tracking table during planning. If a simpler alternative
is sufficient, it MUST be preferred.

**Rationale**: A synchronization library carries inherent complexity
in conflict resolution, concurrency, and multi-protocol support.
Unnecessary abstractions compound this complexity and slow both
contributors and consumers.

## Quality & Safety Standards

- **Warnings as Errors**: The project MUST compile with
  `TreatWarningsAsErrors` enabled and .NET analyzers at the latest
  analysis level.
- **Nullable Reference Types**: All projects MUST enable `<Nullable>
  enable</Nullable>`. Nullable warnings MUST be resolved, not
  suppressed.
- **XML Documentation**: All public APIs MUST have XML doc comments.
  The `GenerateDocumentationFile` MSBuild property MUST remain enabled.
- **No Native Dependencies**: SharpSync MUST remain a pure managed
  .NET library. Native/P-Invoke dependencies are prohibited in the
  core library (consumers may use native code in their own layers).
- **Security**: Code MUST NOT introduce OWASP Top 10 vulnerabilities.
  Credentials, tokens, and secrets MUST NOT be logged or committed.
  Sensitive files (`.env`, credentials) MUST be excluded from source
  control.
- **No Null Device Writes**: Code MUST NEVER copy or write to the
  Windows null device (`NUL`), `/dev/null`, or `Stream.Null` as a
  file operation target. This includes build scripts, tests, and
  CI pipelines. Discarding data via null device copies can mask
  errors and cause silent data loss on Windows.
- **Licensing**: All dependencies MUST have licenses compatible with
  Apache-2.0.

## Development Workflow

- **Branching**: Feature work MUST occur on a named branch. Pull
  requests target `master`.
- **CI Gate**: The GitHub Actions matrix (Ubuntu, Windows, macOS) MUST
  pass—build, format check, and all applicable tests—before merge.
- **Integration Tests**: Run via Docker Compose on Ubuntu CI. Locally,
  developers use the provided scripts (`scripts/run-integration-tests
  .sh` / `.ps1`). Integration tests MUST NOT fail the build on
  platforms where Docker services are unavailable; they MUST skip.
- **Code Review**: All PRs MUST be reviewed before merge. The reviewer
  MUST verify compliance with this constitution's principles.
- **Commit Hygiene**: Commits MUST have concise, descriptive messages.
  Avoid mixing unrelated changes in a single commit.

## Governance

This constitution is the authoritative source of project principles
and standards. In cases of conflict between this document and other
project documentation, this constitution takes precedence.

### Amendment Procedure

1. Propose changes via a pull request modifying this file.
2. The PR description MUST state the version bump type (MAJOR, MINOR,
   PATCH) with rationale.
3. At least one maintainer MUST approve the amendment PR.
4. Upon merge, update `CONSTITUTION_VERSION` and `LAST_AMENDED_DATE`.

### Versioning Policy

- **MAJOR**: Removal or incompatible redefinition of an existing
  principle.
- **MINOR**: Addition of a new principle or material expansion of
  existing guidance.
- **PATCH**: Clarifications, wording improvements, typo fixes.

### Compliance Review

Every pull request review SHOULD include a check that the proposed
changes do not violate the principles above. The plan template's
"Constitution Check" section MUST be filled before design work begins
on any new feature.

**Version**: 1.1.0 | **Ratified**: 2026-01-30 | **Last Amended**: 2026-01-30
