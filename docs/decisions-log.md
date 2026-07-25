# Decisions log

> Tactical, day-to-day decisions during implementation. Anything that justifies
> *why* a particular library, format, version, or approach was chosen at a
> given moment. Architectural decisions go in `docs/adr/`; the long-term plan
> goes in `docs/roadmap.md`; the current state goes in `docs/status.md`.
>
> One entry per decision. Newest at the top. Format:
>
> ```
> ## YYYY-MM-DD — <short title>
> Context: <why we had to decide>
> Decision: <what we chose>
> Alternatives considered: <short list>
> Consequences: <what this enables / blocks>
> Follow-up: <optional — ADR link, ticket, etc.>
> ```

---

## 2026-07-25 — Adopt roadmap-driven development

Context: the project will be built over many sessions, possibly with different
LLMs / context windows. Without a written plan and a current-state file, each
new session reinvents context.

Decision: keep three living docs — `docs/roadmap.md` (the plan, phase by
phase), `docs/status.md` (where we are right now), and this decisions log
(tactical twists). Root `AGENTS.md` instructs every agent to read them
before touching code.

Alternatives considered: keep everything in commit messages; put state in a
single `TODO.md`. Both lost context too easily across sessions.

Consequences: every new agent starts with full context in three files. Cost
is the discipline of keeping `status.md` updated at the end of each session.

Follow-up: none.

---

## 2026-07-25 — Target .NET 10 instead of .NET 8

Context: initial draft of the ADRs and AGENTS files referenced .NET 8. The
user wants .NET 10 (current preview/stable line at project start).

Decision: replace all references from `.NET 8` to `.NET 10` and `net8.0` to
`net10.0` across ADRs, README, and AGENTS files.

Alternatives considered: stay on .NET 8 (LTS, more stable); use .NET 9.
.NET 10 was chosen to keep the portfolio visually current.

Consequences: any future `dotnet new` must use `--framework net10.0`. If
.NET 10 is still preview at install time, document the preview SDK in
`docs/runbook/dev-setup.md` (to be written in Phase 1 or 2).

Follow-up: when the .NET 10 SDK is installed locally, run `dotnet --version`
and pin it in `docs/status.md`.

---

<!-- Template for next entries:

## YYYY-MM-DD — <short title>

Context:
Decision:
Alternatives considered:
Consequences:
Follow-up:

-->