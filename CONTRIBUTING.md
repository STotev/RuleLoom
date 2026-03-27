# Contributing to RuleForge

Thank you for your interest in contributing!

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md).
Report unacceptable behavior via the contact in [SECURITY.md](SECURITY.md).

---

## Ways to contribute

- **Report a bug** — open a bug report issue
- **Request a feature** — open a feature request issue before writing code so we can align on design
- **Fix a bug or implement a feature** — comment on the issue to claim it, then open a PR
- **Improve documentation** — README, XML doc comments, code examples

---

## Development setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git

### Build and test

```bash
git clone https://github.com/STotev/RuleForge.git
cd RuleForge
dotnet build
dotnet test
```

---

## Submitting changes

1. **Open or find an issue** — all non-trivial changes should start with an issue
2. **Fork and create a branch** — `feat/my-feature`, `fix/my-bug`, `docs/update-readme`
3. **Make your changes** — keep them focused; one logical change per PR
4. **Write or update tests** for any behavior change
5. **Update `CHANGELOG.md`** under `## [Unreleased]` for user-facing changes
6. **Open a pull request** targeting `main`, fill in the template, and link the related issue

### Commit messages

We follow [Conventional Commits](https://www.conventionalcommits.org/) — this is encouraged
but not enforced. Common prefixes: `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `ci:`, `chore:`.
Use `feat!:` or a `BREAKING CHANGE:` footer for breaking API changes.

### Review process

- A maintainer will review within a reasonable time
- Address feedback by pushing new commits — do not force-push during review
- Approved PRs are squash-merged

---

## Project structure

```
src/RuleForge.Abstractions/          # Zero-dependency contracts — no external NuGet references allowed
src/RuleForge.Core/                  # Default implementations, built-in operators and actions
src/RuleForge.Extensions.DependencyInjection/  # Optional DI helpers
tests/RuleForge.Tests/               # Unit tests
tests/RuleForge.Tests.Integration/   # Integration tests
samples/RuleForge.Samples.Console/   # Usage examples
```

---

## Reporting security issues

**Please do not open public issues for security vulnerabilities.**
See [SECURITY.md](SECURITY.md) for the private disclosure process.
