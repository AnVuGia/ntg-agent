# Session snapshot — agui-phase-0-implementation — 2026-05-24

## Context
Executing Phase 0 of the AG-UI implementation plan. The spike document (`docs/ag-ui-phase-0-plan.md`) was already written in a prior session. This session implemented all static-analysis and code deliverables: spec pinning, wire-format reference, endpoint/DTO decisions, tool-call investigation, SSE prototype controller + client, and the go/no-go assessment. Runtime verifications (curl capture, Aspire buffering, tool-call content-type logging) are pending app startup.

## What is done
- **3.1 Spec pinning:** AG-UI spec version **0.0.53** pinned (commit `df381cb`, tag `release/2026-05-22`, npm `@ag-ui/core@0.0.53`). All 9 Phase-1 event-type strings verified as UPPER_SNAKE_CASE identical to enum names. camelCase JSON naming confirmed. Additional fields documented (`timestamp`, `rawEvent`, `parentMessageId`, `resume`, `outcome`, `code`). Findings appended to `docs/ag-ui-phase-0-plan.md` under `## Spec snapshot`.
- **3.2 Wire-format reference:** Created `docs/ag-ui-wire-format.md` with canonical JSON examples for all 9 event types, SSE framing rules (headers, `data:` line format, LF endings, no `event:` line — JSON-only discriminator), heartbeat policy (15 s `: ping\n\n` comment frames), and event sequence documentation. `curl` capture placeholder present; needs runtime fill.
- **3.3 Endpoint & DTO decisions:** Confirmed in `docs/ag-ui-phase-0-plan.md` — `POST /api/agents/{agentId}/agui`, same auth as `ChatAsync`, JSON `RunAgentInput` body, SSE response, `RequestAborted` cancellation. File uploads stay on legacy endpoint until Phase 4.
- **3.4 Tool-call surfacing investigation:** Static analysis of `AgentService.cs:270-328` confirms `FunctionCallContent` and `FunctionResultContent` from `Microsoft.Extensions.AI` are available but currently ignored. **Phase 2 approach: ADDITIVE** — just add `else if` branches, no refactor needed. Temporary `[AGUI_SPIKE]` logging added to `InvokePromptStreamingInternalAsync` for runtime verification.
- **3.5 SSE prototype:** Created `SpikeController.cs` in `NTG.Agent.Orchestrator/Controllers/` — hardcoded `RUN_STARTED → TEXT_MESSAGE_* → RUN_FINISHED` SSE sequence, gated behind `AGUI_SPIKE=1` env var middleware in `Program.cs`. Created `scripts/agui-spike/SpikeClient/` console app using `HttpClient` + `ResponseHeadersRead` + `StreamReader.ReadLineAsync()` to parse SSE frames. Both projects compile (0 errors). README with run instructions at `scripts/agui-spike/README.md`.
- **Go/no-go note:** Written at bottom of `phase-0-plan.md`. **CONDITIONAL GO** for Phase 1 — all static analysis complete, no blockers. Runtime verifications are confirmatory, not gating.

## What is in progress
- **3.6 Aspire gateway check:** Requires running the app through `NTG.Agent.AppHost` and verifying SSE chunks arrive incrementally through the Aspire reverse proxy. Not yet done.
- **3.5 curl capture:** The `curl -N` verification and console-client test require the Orchestrator running with `AGUI_SPIKE=1`. Not yet done.
- **3.4 Runtime tool-call logging:** The `[AGUI_SPIKE]` logging is in place but needs a live run with a KnowledgePlugin-triggering prompt to confirm `FunctionCallContent`/`FunctionResultContent` appear in `update.Contents`.

## What to do next
1. Start the Orchestrator with `AGUI_SPIKE=1` and run `curl -N http://localhost:<port>/api/agui-spike/hello`. Paste the capture into `docs/ag-ui-wire-format.md` §5.
2. Run the SpikeClient console app and verify it parses all 8 events correctly.
3. Run through `NTG.Agent.AppHost` and verify SSE chunks arrive incrementally (not as one blob). If buffered, add `X-Accel-Buffering: no` / disable response compression for `text/event-stream` and document the fix.
4. Send a chat message that triggers KnowledgePlugin and check `[AGUI_SPIKE]` logs for `FunctionCallContent` / `FunctionResultContent` content types. Record findings in `docs/ag-ui-phase-0-plan.md` §Tool-call findings.
5. Once all runtime checks pass, tick the remaining checklist items in `phase-0-plan.md` §6 and open PR `docs(agui): phase 0 spike complete`.

## Pitfalls and gotchas
- **SpikeController `JsonSerializerOptions` allocation on every write.** CA1869 warning — acceptable for a throwaway spike, but Phase 1 must cache a static `JsonSerializerOptions` instance.
- **`AGUI_SPIKE=1` middleware is a simple path prefix check.** It blocks all `/api/agui-spike/*` routes when the env var is unset. This is intentionally crude — delete after Phase 1.
- **No `event:` SSE line.** The AG-UI spec uses JSON-only discrimination (`type` field). If a future client expects `event: TEXT_MESSAGE_CONTENT\n` lines, we'd need to add them. Current decision: JSON-only, recorded in wire-format doc.
- **`FunctionCallContent.Arguments` may be a complete JSON string or streamed incrementally.** This affects whether `TOOL_CALL_ARGS` emits one event or many. Must be confirmed at runtime.
- **Auth over long-lived SSE through Aspire is unverified.** The spike endpoint is `[HttpGet]` with no auth — Phase 2 must test auth cookie/JWT flow on the real `POST /api/agents/{agentId}/agui` endpoint.

## Open questions
- **Resolved:** AG-UI spec version → 0.0.53. JSON-only discriminator (no `event:` line). Heartbeat → 15 s `: ping\n\n`. Uploads → stay on legacy endpoint until Phase 4.
- **Pending runtime:** Exact order and granularity of `FunctionCallContent`/`FunctionResultContent` in the stream. Whether `Arguments` is full or incremental.
- **Pending runtime:** Aspire proxy buffering behaviour for `text/event-stream`.

## References
- `docs/ag-ui-phase-0-plan.md` — Phase 0 spike document (updated this session with spec snapshot, endpoint decisions, tool-call findings, go/no-go).
- `docs/ag-ui-wire-format.md` — New file: canonical event JSON + SSE framing rules.
- `NTG.Agent.Orchestrator/Controllers/SpikeController.cs` — New file: SSE prototype controller (delete after Phase 1).
- `NTG.Agent.Orchestrator/Program.cs` — Updated: `AGUI_SPIKE` middleware block (delete after Phase 1).
- `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs` — Updated: `[AGUI_SPIKE]` logging in `InvokePromptStreamingInternalAsync` (delete after Phase 1).
- `scripts/agui-spike/SpikeClient/` — New: console SSE client (delete after Phase 1).
- `scripts/agui-spike/README.md` — New: spike usage instructions (delete after Phase 1).
- `docs/ag-ui-implementation-plan.md` — Parent multi-phase plan (unchanged this session).
- AG-UI spec: `@ag-ui/core@0.0.53`, tag `release/2026-05-22`, commit `df381cb`.
- Branch: `feat/open_api_compatible_config` (all changes are uncommitted working-tree modifications).