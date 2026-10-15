# Contributing to RuleLoom

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
git clone https://github.com/STotev/RuleLoom.git
cd RuleLoom
dotnet build
dotnet test
```

Tests use xUnit v3 on Microsoft.Testing.Platform, so test projects are passed with `--project`:

```bash
dotnet test --project tests/RuleLoom.Tests                 # one project
dotnet test --filter-class "RuleLoom.Tests.ValueComparerTests"
dotnet test --coverage --coverage-output-format cobertura --results-directory ./TestResults
```

---

## Submitting changes

1. **Open or find an issue** — all non-trivial changes should start with an issue
2. **Fork and create a branch** — `feat/my-feature`, `fix/my-bug`, `docs/update-readme`
3. **Make your changes** — keep them focused; one logical change per PR
4. **Write or update tests** for any behavior change
5. **Open a pull request** targeting `main`, fill in the template, and link the related issue

### Commit messages

We follow [Conventional Commits](https://www.conventionalcommits.org/). PRs are squash-merged, so
the **PR title** becomes the commit on `main` and must be a Conventional Commit — release-please
uses it to pick the next version and write the `CHANGELOG.md` entry (don't edit the changelog by
hand). `feat:` and `fix:` appear in the changelog; `docs:`, `test:`, `refactor:`, `ci:`, `chore:`
do not. Use `feat!:` / `fix!:` in the PR title for breaking API changes.

Commits within a PR should follow the same format, but only the PR title reaches the changelog.

### Review process

- A maintainer will review within a reasonable time
- Address feedback by pushing new commits — do not force-push during review
- Approved PRs are squash-merged

---

## Project structure

```
src/RuleLoom.Abstractions/          # Zero-dependency contracts — no external NuGet references allowed
src/RuleLoom/                       # Default implementations, built-in operators and actions
src/RuleLoom.Extensions.DependencyInjection/  # services.AddRuleLoom(...)
tests/RuleLoom.Tests/               # Unit tests
tests/RuleLoom.Tests.Integration/   # End-to-end tests
tests/RuleLoom.Extensions.DependencyInjection.Tests/  # DI registration tests
samples/RuleLoom.Samples.Console/   # Usage examples
samples/RuleLoom.Samples.RulesService/  # Central rules service (ASP.NET Core, Quartz.NET)
docs/                               # Guides, roadmap, vision and design specs
```

---

## Reporting security issues

**Please do not open public issues for security vulnerabilities.**
See [SECURITY.md](SECURITY.md) for the private disclosure process.
