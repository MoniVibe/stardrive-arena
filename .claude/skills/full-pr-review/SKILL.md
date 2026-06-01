---
name: full-pr-review
description: Before opening a PR, launch a review agent over the FULL PR diff (all commits on the branch vs the base), apply the deep-review checklist, address findings, and record a HEAD marker so the gh-pr-create gate passes.
---

# Full PR review (before opening a PR)

Run this before `gh pr create`. It reviews the whole branch — every commit since the
base — not just the last commit, and records a marker the PR-create hook checks.

## Steps

1. Determine the base and gather the full PR diff. In parallel:
   - `git rev-parse --abbrev-ref HEAD` — current branch.
   - Base branch is normally `main` (confirm if the branch description says otherwise).
   - `git log --oneline <base>..HEAD` — every commit that will be in the PR.
   - `git diff <base>...HEAD --stat` then `git diff <base>...HEAD` — the cumulative diff
     (three-dot: diff vs the merge-base, i.e. exactly what the PR introduces).

   If the diff is empty, stop and tell the user there is nothing to review.

2. Launch a review agent (`Agent`, `subagent_type: general-purpose`). The agent has none
   of this conversation's context — hand it a self-contained prompt containing:
   - The base branch, the list of commits, and the changed files with paths.
   - 1–3 sentences (synthesized by you, not delegated) on what the PR is trying to achieve.
   - Instruction to run `git diff <base>...HEAD -- <paths>` itself for the exact diff.
   - **The deep-review checklist (required — this is the point of the skill):**
     1. **Correctness / security / concurrency** — the usual.
     2. **Caller-impact pass** — for every method whose signature OR behavior changed,
        enumerate ALL callers (`grep`/`Grep` the repo) and verify the new behavior against
        each caller's intent. Diff-local reading misses regressions that only surface at a
        call site outside the diff (this is the class of bug that has slipped through before).
     3. **Project-convention pass** — check conventions that live OUTSIDE CLAUDE.md too:
        `.csproj` global usings / type aliases (e.g. `Range` aliased to `SDGraphics.Range`,
        so `System.Range` slice syntax `x[..n]` is off-convention), analyzer rules, and the
        defer-remove / for-loops-over-LINQ patterns this codebase prefers in sim hot paths.
     4. **Exception-path pass** — for new IO / external-API / callback code, ask what can
        throw and whether failure is swallowed where it must be (e.g. attachments and
        telemetry callbacks must be best-effort and never derail the primary operation).
     5. **Sensitive-subsystem mode** — if the diff touches telemetry/logging, save/load,
        threading, or serialization, do a DEEPER pass and do not cap brevity. For these the
        bar is "would an external reviewer (Copilot) find zero valid issues?"
   - Ask for findings flagged by severity (blocker / nit), skip praise. Allow more than
     300 words when sensitive-subsystem mode applies; otherwise keep it tight.

3. Triage the report: **blockers** (fix before PR), **nits** (surface to the user), **false
   positives** (note why dismissed). Fix blockers; commit the fixes.

4. Present a short summary to the user. If blockers were found, propose/apply fixes and get
   confirmation. Only proceed once the branch is clean or findings are addressed.

5. Push and `gh pr create` as usual.

   OPTIONAL hard gate: if you've enabled the `gh pr create` PreToolUse hook in your own
   `.claude/settings.local.json` (see Notes), record the review marker LAST — after every fix
   is committed, so it captures the final HEAD:
   `git rev-parse HEAD > .claude/pr-review-passed`
   The hook blocks PR creation unless this marker equals the current HEAD, so any commit made
   after the review re-arms the gate and forces a re-review. Without the hook, this file is
   just a harmless local note.

## Notes

- The review agent runs in the foreground — you need its findings before opening the PR.
- This complements [[self-review-before-commit]] (per-commit) — it does not replace it. The
  per-commit skill should also apply the caller-impact / convention / exception-path passes.
- If the user explicitly says "skip review" or "just open the PR," honor it: record the marker
  (`git rev-parse HEAD > .claude/pr-review-passed`) so the gate doesn't block them, and note
  that review was skipped at their request.
- OPTIONAL: to make this review a hard gate on `gh pr create` (mirroring a test-suite gate),
  add a `PreToolUse` / `Bash` hook to your own `.claude/settings.local.json` (gitignored,
  per-developer) that blocks unless the marker matches HEAD:
  `head=$(git rev-parse HEAD 2>/dev/null); marker=$(cat .claude/pr-review-passed 2>/dev/null); if [ -z "$marker" ] || [ "$head" != "$marker" ]; then echo "Blocking: run the full-pr-review skill first." >&2; exit 2; fi`
  guarded by `printf '%s' "$(cat)" | grep -qE 'gh(\.exe)?[^"]* pr create' || exit 0`.
