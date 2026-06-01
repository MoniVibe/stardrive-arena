---
name: self-review-before-commit
description: Before committing, launch a code-review agent over the staged + unstaged diff and report findings; only proceed to commit after the review (and any fixes it surfaces).
---

# Self-review before commit

When this skill is invoked, do NOT commit yet. Run a self-review first.

## Steps

1. Gather the diff to review. In parallel:
   - `git status` — full list of changed files (including untracked).
   - `git diff HEAD` — combined staged + unstaged changes since the last commit.
   - `git log -1 --format="%H %s"` — base commit for context.

   If the diff is empty, stop and tell the user there is nothing to review.

2. Launch a review agent. Use the `Agent` tool with `subagent_type: general-purpose`
   (or `code-reviewer` if available in this environment). Hand it a self-contained
   prompt — the agent has none of this conversation's context. Include:
   - The exact files changed and their paths.
   - What the user is trying to accomplish in this set of changes (1-2 sentences,
     synthesized from the conversation — do not delegate this understanding).
   - The diff itself, or instruction to `git diff HEAD -- <paths>` themselves.
   - Ask for: correctness bugs, security issues, concurrency/race risks,
     obvious style violations per project conventions in CLAUDE.md, and any
     dead/unused code introduced. Explicitly ask the agent to flag findings
     by severity (blocker / nit) and to skip praise.
   - Also require these three passes (they catch the bugs diff-local reading misses):
     - **Caller-impact** — for any method whose signature OR behavior changed,
       enumerate ALL callers and check the new behavior against each caller's intent.
       A regression often only manifests at a call site outside the diff.
     - **Project conventions beyond CLAUDE.md** — `.csproj` global usings / type
       aliases (e.g. `Range` → `SDGraphics.Range`, so `System.Range` slice syntax is
       off-convention), analyzer rules, and the codebase's preferred patterns.
     - **Exception paths** — for new IO / external-API / callback code, what can throw,
       and is failure swallowed where it must be (telemetry/attachments are best-effort)?
   - For sensitive subsystems (telemetry/logging, save/load, threading, serialization),
     run a deeper pass and lift the word cap. Otherwise cap: "Report under 300 words,
     bullet form."

3. Read the agent's report. Categorize findings:
   - **Blockers** — correctness/security/concurrency issues. Fix before committing.
   - **Nits** — style/clarity. Surface to the user; let them decide.
   - **False positives** — note briefly why dismissed.

4. Present the review to the user in a short summary. If there are blockers,
   propose fixes and ask the user before applying. If there are only nits,
   list them and ask whether to address or commit as-is. If the review is
   clean, say so and proceed to the normal commit flow.

5. After the user confirms (and any fixes are applied), perform the commit
   exactly as the standard "create a git commit" flow does — drafted commit
   message via HEREDOC, `Co-Authored-By` trailer, etc.

## Notes

- The review agent should run in the foreground — you need its findings before
  you can decide whether to commit.
- Do not skip step 2 when the diff is "obviously small." A trivial-looking
  diff is exactly where a fresh pair of eyes catches the missed null guard
  or the accidentally-removed branch.
- If the user explicitly says "skip review" or "just commit," honor that and
  skip directly to the commit flow.
