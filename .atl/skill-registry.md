# Skill Registry

**Delegator use only.** Any agent that launches sub-agents reads this registry to resolve compact rules, then injects them directly into sub-agent prompts. Sub-agents do NOT read this registry or individual SKILL.md files.

See `_shared/skill-resolver.md` for the full resolution protocol.

## User Skills

| Trigger | Skill | Path |
|---------|-------|------|
| Creating a pull request, opening a PR, preparing changes for review | branch-pr | C:\Users\luis_\.config\opencode\skills\branch-pr\SKILL.md |
| PR would exceed 400 changed lines; planning chained/stacked PRs or reviewable slices | gentle-ai-chained-pr | C:\Users\luis_\.config\opencode\skills\chained-pr\SKILL.md |
| Writing guides, READMEs, RFCs, onboarding docs, architecture docs, review-facing docs | cognitive-doc-design | C:\Users\luis_\.config\opencode\skills\cognitive-doc-design\SKILL.md |
| Drafting or posting feedback, review comments, maintainer replies, Slack messages, GitHub comments | comment-writer | C:\Users\luis_\.config\opencode\skills\comment-writer\SKILL.md |
| Helping users discover and install agent skills ("how do I do X", "find a skill for X") | find-skills | C:\Users\luis_\.agents\skills\find-skills\SKILL.md |
| Writing Go tests, using teatest, adding test coverage | go-testing | C:\Users\luis_\.config\opencode\skills\go-testing\SKILL.md |
| Creating a GitHub issue, reporting a bug, requesting a feature | issue-creation | C:\Users\luis_\.config\opencode\skills\issue-creation\SKILL.md |
| "judgment day", "judgment-day", adversarial/dual review, "juzgar", "que lo juzguen" | judgment-day | C:\Users\luis_\.config\opencode\skills\judgment-day\SKILL.md |
| Creating a new skill, adding agent instructions, documenting patterns for AI | skill-creator | C:\Users\luis_\.config\opencode\skills\skill-creator\SKILL.md |
| Implementing a change, preparing commits, splitting PRs, planning chained/stacked PRs | work-unit-commits | C:\Users\luis_\.config\opencode\skills\work-unit-commits\SKILL.md |
| Planning/proposing/implementing gameplay features in this Unity game (prototipado, game jam, core loop, "que el juego sea divertido") | unity-rapid-prototyping | D:\Unity\ToporV3\.agents\skills\unity-rapid-prototyping\SKILL.md |

## Compact Rules

Pre-digested rules per skill. Delegators copy matching blocks into sub-agent prompts as `## Project Standards (auto-resolved)`.

### branch-pr
- Every PR MUST link an approved issue (`status:approved` label) — no exceptions
- Every PR MUST have exactly one `type:*` label; blank PRs without issue linkage are blocked by CI
- Branch naming MUST match `^(feat|fix|chore|docs|style|refactor|perf|test|build|ci|revert)\/[a-z0-9._-]+$` — lowercase, no spaces
- PR body MUST contain a link keyword: `Closes #N`, `Fixes #N`, or `Resolves #N`
- Implement with conventional commits; run shellcheck on modified scripts; wait for automated checks to pass

### gentle-ai-chained-pr
- MUST split when a PR exceeds 400 changed lines (`additions + deletions`) unless maintainer-approved `size:exception`
- Design each PR for approximately ≤60-minute human review; one deliverable work unit per PR
- Every chained PR MUST state where it starts/ends, what came before, and what comes next, plus a dependency diagram marking the current PR
- Each PR must be CI green, autonomous, verifiable alone, with reasonable rollback
- Honor SDD `delivery_strategy`: ask on risk, auto-chain, or require/record `size:exception`
- Once the user picks a chain strategy (stacked-to-main vs feature-branch-chain with tracker), follow it for the entire chain

### cognitive-doc-design
- Lead with the answer: decision/action/outcome first, context after
- Progressive disclosure: happy path first, then details, edge cases, references
- Chunk related information into small sections; keep flat lists short
- Signpost with headings, labels, callouts, and summaries
- Recognition over recall: tables, checklists, examples, templates over prose
- Design for review empathy — reviewers verify intent without reconstructing the story

### comment-writer
- Start with the actionable point; do not recap the whole PR before feedback
- Be warm and direct like a thoughtful teammate; keep to 1–3 short paragraphs or a tight bullet list
- Explain WHY when requesting a change
- Avoid pile-ons — comment on the highest-value issue only
- Match the thread language; in Spanish use Rioplatense voseo (podés, tenés, fijate, dale)
- No em dashes — use commas, periods, or parentheses

### find-skills
- Discovery skill: when the user asks "how do I do X" / "find a skill for X", search installable skill sources
- Use for capability discovery only — do not apply to project code

### go-testing
- Use table-driven tests for multiple cases (name, input, expected, wantErr)
- Test Bubbletea models via direct `Update()` transitions with `tea.KeyMsg`
- Use teatest for TUI integration; golden file testing for snapshots
- Note: this project is Unity C# — apply only if Go code appears

### issue-creation
- Blank issues are disabled — MUST use the bug report or feature request template
- Every issue gets `status:needs-review` automatically; a maintainer MUST add `status:approved` before any PR can be opened
- Search existing issues for duplicates first; fill ALL required fields and pre-flight checkboxes
- Questions go to Discussions, not issues

### judgment-day
- Run the Skill Resolver Protocol BEFORE launching judges (registry → compact rules → inject as `## Project Standards (auto-resolved)`)
- Launch TWO blind judge sub-agents via `delegate` in parallel; neither knows about the other; never review yourself as orchestrator
- Synthesize verdicts: confirmed (both) → fix immediately; single-suspect → triage; contradiction → flag for manual decision
- Classify warnings: real (fix required) vs theoretical (report as INFO, do NOT fix, no re-judgment trigger)
- Fix via a separate Fix Agent, then re-judge with both judges in parallel; escalate after 2 iterations

### skill-creator
- Create skills for repeated patterns, project-specific conventions, or complex workflows
- Structure: `skills/{name}/SKILL.md` plus optional `assets/` and `references/`
- SKILL.md frontmatter MUST have: name, description (with `Trigger:`), license, metadata (author, version)
- Body sections: When to Use, Critical Patterns, plus rules and code examples
- Do NOT create when documentation already exists, the pattern is trivial, or it is a one-off task

### work-unit-commits
- Commit by deliverable work unit, NOT by file type (avoid `add models` / `add services` / `add tests` batches)
- Keep tests in the same commit as the behavior they verify; docs with the user-visible change they explain
- Use Conventional Commit messages that explain the outcome, not the file list
- Each commit should be PR-ready / a candidate chained PR
- SDD workload guard: if the forecast exceeds 400 lines, group commits into chained PR slices before implementation

### unity-rapid-prototyping (project-specific — this Unity game)
- Run the Decision Gate BEFORE proposing any code: core loop? <1h? 10x simpler version? asset over custom? raises finish odds? Fail → don't build now
- Gameplay-first: validate the fun in play mode BEFORE the architecture
- Minimal architecture: NO DI containers, NO event buses, NO extensible systems — plain MonoBehaviours, direct references, one scene
- Placeholders are features: cubes, capsules, white sprites are acceptable until the loop is fun
- Keep game rules in pure C# classes OUTSIDE MonoBehaviours so EditMode tests can validate them
- After creating/modifying scripts, run `read_console` to catch compile errors; validate logic with `run_tests` (EditMode/PlayMode); validate feel with play mode + screenshots
- Kill-or-simplify: if a mechanic isn't fun after one iteration, delete it — prefer deleting over refactoring
- Validate MCP prerequisites first: Unity editor connected, `com.coplaydev.unity-mcp` + `com.unity.test-framework` installed

## Project Conventions

| File | Path | Notes |
|------|------|-------|
| (none found) | — | No AGENTS.md / CLAUDE.md / .cursorrules / GEMINI.md / copilot-instructions.md at project root |

Read the convention files listed above for project-specific patterns and rules. All referenced paths have been extracted — no need to read index files to discover more.

Working rules for this project live in the project skill `.agents/skills/unity-rapid-prototyping/SKILL.md` (captured in Compact Rules above).
