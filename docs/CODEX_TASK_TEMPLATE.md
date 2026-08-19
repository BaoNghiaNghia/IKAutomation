# Codex Task Template

`AGENTS.md` contains the repository-wide rules. This prompt describes only the
current change.

## Objective

<Describe the desired behavior in 2–5 sentences.>

## Scope

Inspect and modify only:

- <service/model/test/options file or area>
- <related dependency-registration file if required>

Do not inspect unrelated workflows unless a dependency requires it.

## Required behavior

- <new or changed rule>
- <important business outcome>
- <what must not happen>

## Validation

- Build the affected project(s).
- Run the affected test class(es) or filtered tests.
- Do not run the full suite unless shared interfaces or infrastructure changed.
- Review `git diff` and `git status`.

## Commit

Commit message:

`<type>: <short description>`

Push the current branch according to `AGENTS.md`.

Final report: maximum 10 lines.
