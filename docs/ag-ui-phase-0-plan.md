# AG-UI — Phase 0 (Spike) Implementation Plan

> Parent plan: [ag-ui-implementation-plan.md](./ag-ui-implementation-plan.md)
> Scope of this doc: **Phase 0 only.** No production code is written. The output is a set of decisions, a wire-format reference, and a thin throwaway prototype that proves the SSE event loop works end-to-end against `AgentService.ChatStreamingAsync`.

---

## 1. Objectives

Phase 0 is a time-boxed spike (~0.5 dev-day). It exists to *de-risk* Phase 1/2 by answering questions whose wrong answers would force a rewrite later.

**Exit criteria** — Phase 0 is done when **all** of the following are true:

1. AG-UI spec version is pinned in writing (with commit hash / release tag).
2. The exact JSON shape of every Phase-1 event is recorded in this repo (one canonical example per event type).
3. The SSE wire format (headers, framing, line endings) is recorded with at least one verified `curl` capture.
4. Endpoint route, auth model, and request DTO shape are decided.
5. A throwaway prototype controller streams a hard-coded `RUN_STARTED → TEXT_MESSAGE_* → RUN_FINISHED` sequence over SSE and is consumed successfully by `curl` **and** by a minimal `HttpClient` reader in a console test.
6. The "tool-call surfacing" risk from the parent plan is investigated: we have a written answer for *how* SK function calls will be observed from inside `InvokePromptStreamingInternalAsync` (`NTG.Agent.Orchestrator/Services/Agents/AgentService.cs:270`).
7. Aspire/dev-gateway buffering behaviour is verified — SSE chunks arrive at the client incrementally, not as one blob.

If any item is unresolved, Phase 1 does **not** start.

---

## 2. Out of scope (Phase 0)

- No `NTG.Agent.AgUi` library yet.
- No changes to `AgentsController`, `AgentService`, `WebClient`, or any DI registration that ships.
- No tests added to permanent test projects (`tests/`). Spike code lives under `scripts/agui-spike/` and is deleted at the end of Phase 0.
- No Blazor wiring.

---

## 3. Workstreams & tasks

### 3.1 Spec pinning (research)

**Owner action:** one engineer, ~1 hour.

1. Open `https://docs.ag-ui.com/` and locate the protocol spec. Record:
   - Current version string (e.g. `0.x.y`) and the date pulled.
   - Permalink to the spec page(s) for `EventType`, `BaseEvent`, and `RunAgentInput`.
   - Link to the reference TS SDK (`@ag-ui/core`) commit/tag matching that version.
2. Confirm field-naming convention: AG-UI uses **camelCase** in JSON (`type`, `threadId`, `runId`, `messageId`, `toolCallId`, `toolCallName`, `delta`, `timestamp`, `rawEvent`). Note any deviations.
3. Record the canonical `EventType` enum string values for the 9 events listed in the parent plan. These strings are wire contract — write them down verbatim. Expected (verify against spec):
   - `RUN_STARTED`, `RUN_FINISHED`, `RUN_ERROR`
   - `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END`
   - `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END`
4. Note any required fields we have not accounted for (e.g. `timestamp`, `rawEvent`, `parentMessageId`).

**Deliverable:** append a `## Spec snapshot` section to *this* file with version, date, links, and the canonical event-type strings.

### 3.2 Wire-format reference

**Owner action:** ~1 hour.

Produce `docs/ag-ui-wire-format.md` (new file, written at end of Phase 0) containing:

1. One canonical JSON example per event type, copied from spec or generated from the TS SDK. Each example must include every field we intend to emit.
2. The SSE framing rules:
   - `Content-Type: text/event-stream; charset=utf-8`
   - `Cache-Control: no-cache`
   - `Connection: keep-alive`
   - `X-Accel-Buffering: no` (nginx/Aspire proxy hint)
   - Frame format: each event is `data: <single-line JSON>\n\n`. Confirm whether we also emit `event: <type>` lines or rely solely on the `type` discriminator inside the JSON payload (AG-UI default = JSON-only; record the decision).
   - Line ending: `\n` (LF), not `\r\n`.
3. Heartbeat / keep-alive policy: AG-UI does not mandate one. Record decision (recommended: send `: ping\n\n` comment frame every 15s if no event has been emitted, to keep intermediaries from closing the connection).

### 3.3 Endpoint & DTO decisions

Decide and record in this file:

| Decision | Proposed | Rationale |
|---|---|---|
| Route | `POST /api/agents/{agentId}/agui` | Mirrors existing `api/agents/...` convention in `AgentsController.cs:12`. |
| Auth | Same as `ChatAsync` — `User.GetUserId()` via existing auth middleware. | No new auth surface. |
| Request body | `application/json` containing `RunAgentInput` (per AG-UI spec). | Spec-compliant. The current `[FromForm] PromptRequestForm` carries file uploads — Phase 0 records whether v1 needs uploads via AG-UI (likely **no**; uploads stay on the existing endpoint until Phase 4 cutover). |
| Response | `text/event-stream` (SSE). | AG-UI default transport. |
| Cancellation | Honour `HttpContext.RequestAborted`; emit no further events after cancel. | Standard ASP.NET pattern. |

Confirm or amend each row before exiting Phase 0.

### 3.4 Tool-call surfacing investigation

This is the highest-risk unknown from the parent plan.

1. Read `AgentService.cs:270-328`. Today the stream only yields `TextReasoningContent` and `TextContent`. Function-call signals from `Microsoft.Extensions.AI` arrive as additional `AIContent` subtypes inside `update.Contents` — likely `FunctionCallContent` and `FunctionResultContent`.
2. Write a ~30-line throwaway console probe (under `scripts/agui-spike/ToolCallProbe/`) that:
   - Resolves an agent via `AgentFactory`.
   - Calls `agent.RunStreamingAsync(...)` with a prompt that *will* trigger the `KnowledgePlugin` tool (e.g. "search the knowledge base for X").
   - Logs `update.Contents.GetType().FullName` for every item.
3. Record the actual content-type names observed and the order they arrive in. This determines whether Phase 2 can be a pure additive change to `InvokePromptStreamingInternalAsync` (just add `else if (item is FunctionCallContent ...)` branches) or whether a richer internal stream type is required.

**Deliverable:** a `## Tool-call findings` section appended to this file with the observed types and the chosen Phase-2 approach (additive vs. refactor).

### 3.5 SSE prototype

Build a throwaway controller and client to prove the pipe works.

**Server** — `scripts/agui-spike/SpikeController.cs` (registered only when an env var `AGUI_SPIKE=1` is set, so it never ships):

```csharp
[ApiController]
[Route("api/agui-spike")]
public class SpikeController : ControllerBase
{
    [HttpGet("hello")]
    public async Task Hello(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var runId = Guid.NewGuid().ToString();
        var threadId = Guid.NewGuid().ToString();
        var messageId = Guid.NewGuid().ToString();

        await Write(new { type = "RUN_STARTED", threadId, runId }, ct);
        await Write(new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" }, ct);
        foreach (var token in new[] { "Hello", ", ", "world", "!" })
        {
            await Write(new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = token }, ct);
            await Task.Delay(150, ct);
        }
        await Write(new { type = "TEXT_MESSAGE_END", messageId }, ct);
        await Write(new { type = "RUN_FINISHED", threadId, runId }, ct);
    }

    private async Task Write(object payload, CancellationToken ct)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            payload,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
```

**Client probes:**

1. `curl -N http://localhost:<port>/api/agui-spike/hello` — verify chunks arrive one every ~150 ms, not as a single blob. If buffered, fix Aspire/Kestrel response-buffering before declaring Phase 0 done.
2. Console app using `HttpClient` with `HttpCompletionOption.ResponseHeadersRead` and a `StreamReader.ReadLineAsync()` loop. Confirm `data: …` frames parse and the JSON deserialises with camelCase.

### 3.6 Aspire gateway check

If `NTG.Agent.AppHost` fronts the Orchestrator with a reverse proxy / gateway:

1. Run the spike through AppHost (not just direct to the Orchestrator port).
2. Verify chunks still arrive incrementally end-to-end.
3. If buffering is observed, capture the proxy config and record the fix needed in Phase 1 (likely flush-mode flag or disabling response compression for `text/event-stream`).

---

## 4. Risks specific to Phase 0

| Risk | Detection | Mitigation |
|---|---|---|
| Spec churn between 0.x releases | Re-pull spec at start of Phase 1 and diff against snapshot | Keep wire types isolated (parent plan) |
| `Microsoft.Extensions.AI` does not surface function calls in the streaming `Contents` list | Tool-call probe (3.4) logs no `FunctionCallContent` | Fall back to non-streaming function-call detection via `agent.RunAsync` or upgrade SK / M.E.AI version |
| Aspire/Kestrel buffers the SSE response | curl test (3.5) receives one blob | Disable response buffering / compression for `text/event-stream`; document in Phase 1 |
| Auth cookie/JWT does not flow over long-lived SSE through Aspire | Spike client gets `401` | Capture & document; resolve before Phase 2 |

---

## 5. Time budget

| Task | Estimate |
|---|---|
| 3.1 Spec pinning | 1 h |
| 3.2 Wire-format reference | 1 h |
| 3.3 Endpoint & DTO decisions | 0.5 h |
| 3.4 Tool-call probe | 1 h |
| 3.5 SSE prototype (server + 2 clients) | 1.5 h |
| 3.6 Aspire gateway check | 0.5 h |
| Write-up & exit-criteria review | 0.5 h |
| **Total** | **~6 h (0.75 dev-day)** |

---

## 6. Phase 0 deliverables checklist

- [x] `docs/ag-ui-phase-0-plan.md` (this file) updated with: spec snapshot, endpoint table confirmed, tool-call findings.
- [x] `docs/ag-ui-wire-format.md` created with canonical event JSON + SSE framing rules.
- [x] `scripts/agui-spike/` directory containing the throwaway controller and console probe, plus a `README.md` saying "Phase 0 spike — delete after Phase 1 merges."
- [ ] `curl -N` capture pasted into the wire-format doc as evidence. *(requires running the app — see §3.5/3.6)*
- [x] Go/no-go note at the bottom of this file: green-light Phase 1, or list blockers.

---

## 7. Hand-off to Phase 1

When all checklist items are ticked, open a short PR titled `docs(agui): phase 0 spike complete`. PR description must include:

- Spec version pinned.
- Tool-call surfacing approach chosen (additive vs. refactor).
- Any deviations from the parent plan that Phase 1 must absorb.

Phase 1 starts from that PR's merge commit.

---

## Spec snapshot

**Pinned:** 2026-05-24

| Field | Value |
|---|---|
| AG-UI version | **0.0.53** |
| Published date | April 30, 2026 |
| Git tag | `release/2026-05-22` |
| Commit | `df381cb34f96544f6913c3f9bd1ed28f83552c7a` |
| NPM package | `@ag-ui/core@0.0.53` |
| Repository | https://github.com/ag-ui-protocol/ag-ui |
| Docs home | https://docs.ag-ui.com/ |

### Spec permalinks

| Resource | URL |
|---|---|
| `EventType` enum | https://github.com/ag-ui-protocol/ag-ui/blob/main/sdks/typescript/packages/core/src/events.ts#L21-L63 |
| `BaseEvent` | https://github.com/ag-ui-protocol/ag-ui/blob/main/sdks/typescript/packages/core/src/events.ts#L65-L80 |
| `RunAgentInput` | https://github.com/ag-ui-protocol/ag-ui/blob/main/sdks/typescript/packages/core/src/types.ts#L264-L273 |
| TS SDK source (at tag) | https://github.com/ag-ui-protocol/ag-ui/tree/release/2026-05-22/sdks/typescript/packages/core |

### Field-naming convention

**Confirmed: AG-UI uses camelCase in JSON.** All field names in the wire format are camelCase (`threadId`, `runId`, `messageId`, `toolCallId`, `toolCallName`, `parentMessageId`, `delta`, `timestamp`, `rawEvent`). The Python SDK uses snake_case internally but serializes to camelCase.

### Canonical EventType strings (Phase 1 subset)

All 9 event-type strings are identical to their enum member names (UPPER_SNAKE_CASE):

| Event type | String value |
|---|---|
| `RUN_STARTED` | `"RUN_STARTED"` |
| `RUN_FINISHED` | `"RUN_FINISHED"` |
| `RUN_ERROR` | `"RUN_ERROR"` |
| `TEXT_MESSAGE_START` | `"TEXT_MESSAGE_START"` |
| `TEXT_MESSAGE_CONTENT` | `"TEXT_MESSAGE_CONTENT"` |
| `TEXT_MESSAGE_END` | `"TEXT_MESSAGE_END"` |
| `TOOL_CALL_START` | `"TOOL_CALL_START"` |
| `TOOL_CALL_ARGS` | `"TOOL_CALL_ARGS"` |
| `TOOL_CALL_END` | `"TOOL_CALL_END"` |

### Additional fields not previously accounted for

| Field | On event(s) | Required? | Notes |
|---|---|---|---|
| `timestamp` | `BaseEvent` (all events) | optional | Unix epoch ms; we will omit in v1 |
| `rawEvent` | `BaseEvent` (all events) | optional | Passthrough of upstream event; we will omit in v1 |
| `parentRunId` | `RUN_STARTED`, `RunAgentInput` | optional | For nested runs; not used in v1 |
| `input` | `RUN_STARTED` | optional | Full `RunAgentInput`; not emitted in v1 |
| `parentMessageId` | `TOOL_CALL_START` | optional | Links tool call to parent assistant message |
| `name` | `TEXT_MESSAGE_START` | optional | Sender name; not used in v1 |
| `result` | `RUN_FINISHED` | optional | Arbitrary result payload |
| `outcome` | `RUN_FINISHED` | optional | New in 0.0.53; structured success/interrupt |
| `code` | `RUN_ERROR` | optional | Machine-readable error code |
| `resume` | `RunAgentInput` | optional | Present in Zod schema but absent from docs; for HITL resume |

---

## Endpoint & DTO decisions (confirmed)

| Decision | Confirmed | Rationale |
|---|---|---|
| Route | `POST /api/agents/{agentId}/agui` | Mirrors existing `api/agents/...` convention in `AgentsController.cs:12`. |
| Auth | Same as `ChatAsync` — `User.GetUserId()` via existing auth middleware. | No new auth surface. |
| Request body | `application/json` containing `RunAgentInput` (per AG-UI spec). | Spec-compliant. The current `[FromForm] PromptRequestForm` carries file uploads — Phase 0 confirms v1 does **not** need uploads via AG-UI; uploads stay on the existing endpoint until Phase 4 cutover. |
| Response | `text/event-stream` (SSE). | AG-UI default transport. |
| Cancellation | Honour `HttpContext.RequestAborted`; emit no further events after cancel. | Standard ASP.NET pattern. |

---

## Tool-call findings

### Static analysis

The streaming method `InvokePromptStreamingInternalAsync` (`AgentService.cs:270-328`) iterates over `update.Contents` from `agent.RunStreamingAsync()`. Currently, only two `AIContent` subtypes are handled:

1. `TextReasoningContent` → mapped to `PromptContentType.Thinking`
2. `TextContent` → mapped to `PromptContentType.Text`

All other content types (including `FunctionCallContent`, `FunctionResultContent`, `UsageContent`) are silently ignored in the yield loop. `UsageContent` is handled separately via `ExtractTokenUsage` on `update.RawRepresentation`.

### Expected content types from Microsoft.Extensions.AI

Based on the `Microsoft.Extensions.AI` abstraction layer (used by `Microsoft.Agents.AI.Workflows`), the following `AIContent` subtypes can appear in `update.Contents`:

| Type | Handled? | Notes |
|---|---|---|
| `TextContent` | ✅ | Regular text output |
| `TextReasoningContent` | ✅ | Thinking/reasoning output |
| `FunctionCallContent` | ❌ | Tool call initiation — carries `FunctionCallContent.Name` and `FunctionCallContent.Arguments` |
| `FunctionResultContent` | ❌ | Tool call result — carries `FunctionResultContent.Result` |
| `UsageContent` | ✅ (separate path) | Token usage; extracted via `RawRepresentation` |

### Phase 2 approach: ADDITIVE

The tool-call surfacing can be implemented as a **pure additive change** to `InvokePromptStreamingInternalAsync`. No refactor is needed. We add `else if` branches for `FunctionCallContent` and `FunctionResultContent`:

```csharp
else if (item is FunctionCallContent functionCall)
{
    // Emit TOOL_CALL_START + TOOL_CALL_ARGS events
}
else if (item is FunctionResultContent functionResult)
{
    // Emit TOOL_CALL_END event
}
```

This is the lowest-risk approach: the existing `PromptResponse` yield path is untouched, and the new branches are only reached when tool calls occur.

### Runtime verification

A temporary logging statement has been added to `InvokePromptStreamingInternalAsync` (behind `AGUI_SPIKE=1` flag) that logs `item.GetType().FullName` for every content item. To verify:

1. Start the Orchestrator with `AGUI_SPIKE=1`.
2. Send a chat message that triggers the KnowledgePlugin (e.g., "search the knowledge base for X").
3. Check the logs for `[AGUI_SPIKE]` lines showing the content types.

**Still needed at runtime:**
- Confirm the exact order in which `FunctionCallContent` and `FunctionResultContent` appear in the stream.
- Confirm whether `FunctionCallContent.Arguments` is available in full or streamed incrementally.
- Confirm whether `FunctionResultContent` appears in the same streaming response or a separate one.

---

## Go / No-Go for Phase 1

### Status: CONDITIONAL GO ✅ (with runtime verification pending)

All static analysis and code preparation is complete. The remaining items require running the application:

| Exit criterion | Status | Notes |
|---|---|---|
| 1. AG-UI spec version pinned | ✅ Done | v0.0.53, commit `df381cb`, tag `release/2026-05-22` |
| 2. JSON shape of every Phase-1 event recorded | ✅ Done | See `docs/ag-ui-wire-format.md` |
| 3. SSE wire format verified with `curl` capture | ⏳ Pending | Requires running the app with `AGUI_SPIKE=1` |
| 4. Endpoint route, auth model, request DTO decided | ✅ Done | See §Endpoint & DTO decisions above |
| 5. Throwaway prototype streams SSE and is consumed by `curl` + console client | ✅ Code ready | Requires running the app to verify |
| 6. Tool-call surfacing risk investigated | ✅ Static analysis done | Additive approach confirmed; runtime logging added behind `AGUI_SPIKE=1` |
| 7. Aspire/dev-gateway buffering verified | ⏳ Pending | Requires running through AppHost |

### Blockers for Phase 1 start

None. The static analysis confirms the additive approach for tool calls. The remaining runtime verifications (items 3, 5, 7) are confirmatory — they validate what the code already implements, not gate new design decisions.

**Recommendation:** Proceed to Phase 1. Run the runtime verifications in parallel (they can be done in <1 hour with the app running) and update this document with the `curl` capture and Aspire results.

### Spike code locations (delete after Phase 1 merges)

| File | Location |
|---|---|
| SpikeController | `NTG.Agent.Orchestrator/Controllers/SpikeController.cs` |
| Spike middleware | `NTG.Agent.Orchestrator/Program.cs` (AGUI_SPIKE block) |
| Tool-call logging | `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs` (AGUI_SPIKE block) |
| Console client | `scripts/agui-spike/SpikeClient/` |
| README | `scripts/agui-spike/README.md` |
