# Session snapshot — agui-phase-0-plan — 2026-05-24

## Context
User is adding AG-UI (Agent-User Interaction protocol) support to ntg-agent so the Blazor WebClient consumes a standard SSE event stream from the Orchestrator instead of the bespoke `PromptResponse` shape. Scope is locked to: Blazor only, text streaming + tool calls only, `NTG.Agent.Orchestrator` only. A parent multi-phase plan already exists; this session refined Phase 0 into its own actionable spike document.

## What is done
- Parent plan reviewed: `docs/ag-ui-implementation-plan.md` (untracked, from prior session).
- New detailed Phase 0 doc written: `docs/ag-ui-phase-0-plan.md`. Sections cover exit criteria, out-of-scope, six workstreams (spec pinning, wire-format reference, endpoint/DTO table, tool-call probe, SSE prototype with sample C# controller, Aspire gateway buffering check), Phase-0-specific risks, ~6 h time budget, deliverables checklist, and Phase 1 hand-off PR contract.
- Parent plan cross-linked: added `Detailed plan: [ag-ui-phase-0-plan.md](./ag-ui-phase-0-plan.md).` under the Phase 0 — Spike section of `docs/ag-ui-implementation-plan.md`.
- Verified current streaming surface at `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs:270-328` only yields `TextReasoningContent` and `TextContent` today — recorded as the open risk Phase 0 must resolve via a tool-call probe.

## What is in progress
_None._ Phase 0 plan is written; no spike code has been executed yet.

## What to do next
1. Execute Phase 0 per `docs/ag-ui-phase-0-plan.md` §3:
   - 3.1 Pin AG-UI spec version from `https://docs.ag-ui.com/` and fill the `## Spec snapshot` section in the Phase 0 doc.
   - 3.4 Build the throwaway tool-call probe under `scripts/agui-spike/ToolCallProbe/` and log `update.Contents` types when `KnowledgePlugin` fires. Record findings in the Phase 0 doc.
   - 3.5 Drop `scripts/agui-spike/SpikeController.cs` (sample code in the plan), gate behind `AGUI_SPIKE=1`, verify with `curl -N` and a `HttpClient` console reader.
   - 3.6 Re-run the curl test through `NTG.Agent.AppHost` to confirm Aspire does not buffer `text/event-stream`.
2. Open PR `docs(agui): phase 0 spike complete` once the §6 checklist is green.

## Pitfalls and gotchas
- **No official AG-UI C# SDK.** TS/Python/Go/Rust/Kotlin only — we implement the wire protocol directly. Treat the TS `@ag-ui/core` types as the reference.
- **Tool-call surfacing is the highest-risk unknown.** Current `InvokePromptStreamingInternalAsync` (`AgentService.cs:270`) does not branch on `FunctionCallContent`/`FunctionResultContent`. If `Microsoft.Extensions.AI` does not emit those in the streaming `Contents` list at our pinned version, Phase 2 grows from additive to refactor — must be answered in Phase 0, not Phase 2.
- **SSE buffering through Aspire.** Must verify chunks arrive incrementally end-to-end, not just direct to Kestrel. Response compression on `text/event-stream` will silently coalesce frames.
- **Auth over long-lived SSE.** Cookie/JWT flow through Aspire on a streaming response is unverified; probe with the spike client before Phase 2.
- **`PromptRequestForm` is `[FromForm]` (file uploads).** AG-UI's `RunAgentInput` is JSON — uploads stay on the legacy `ChatAsync` endpoint until the Phase 4 cutover.

## Open questions
- Final AG-UI spec version to pin (waiting on §3.1 lookup).
- Whether to emit `event: <TYPE>` SSE lines in addition to the `type` field inside the JSON payload (spec default appears to be JSON-only; confirm).
- Heartbeat policy: send `: ping\n\n` every 15 s, or rely on proxy timeouts? Recommendation in the plan is 15 s ping; needs sign-off.
- Whether v1 AG-UI needs to accept file uploads, or uploads stay on the legacy endpoint until Phase 4 cutover.

## References
- `docs/ag-ui-implementation-plan.md` — parent multi-phase plan (untracked).
- `docs/ag-ui-phase-0-plan.md` — this session's primary deliverable (untracked).
- `NTG.Agent.Orchestrator/Controllers/AgentsController.cs:32` — existing `POST chat` endpoint that AG-UI will run parallel to.
- `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs:58` — `ChatStreamingAsync` entry point.
- `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs:270` — `InvokePromptStreamingInternalAsync`; the streaming loop that needs tool-call branches.
- Branch: `feat/open_api_compatible_config` (no new commits this session — only untracked docs added).
- AG-UI docs: `https://docs.ag-ui.com/`.
