# AG-UI Implementation Plan

## Goal

Add support for the [AG-UI](https://docs.ag-ui.com/) (Agent-User Interaction) protocol to ntg-agent so the Blazor WebClient consumes a standard, spec-compliant event stream from the Orchestrator instead of the current bespoke `PromptResponse` shape.

## Scope (locked)

- **Backend:** `NTG.Agent.Orchestrator` only. No changes to `NTG.Agent.MCP.Server`.
- **Frontend:** Blazor (`NTG.Agent.WebClient`) only. No React/CopilotKit surface.
- **Protocol features:** text streaming + tool calls. No bidirectional state sync, no human-in-the-loop gating, no generative UI in v1.
- **Transport:** SSE (`text/event-stream`), the AG-UI default.
- **Strategy:** add AG-UI as a *parallel* endpoint first, keep the existing `ChatAsync` working, cut over once stable.

## Current state (relevant files)

- `NTG.Agent.Orchestrator/Controllers/AgentsController.cs:33` — streams `IAsyncEnumerable<PromptResponse>` (custom shape).
- `NTG.Agent.Orchestrator/Services/Agents/AgentService.cs:58` — `ChatStreamingAsync`, the source of text chunks and tool invocations from Semantic Kernel / `Microsoft.Extensions.AI`.
- `NTG.Agent.WebClient` — Blazor Server + WASM client that consumes the current stream.
- No official AG-UI C# SDK exists (TS/Python/Go/Rust/Kotlin only). We implement the protocol directly.

## AG-UI events we will emit (v1 subset)

| Event | When |
|---|---|
| `RUN_STARTED` | Start of each request |
| `TEXT_MESSAGE_START` | Before first assistant token |
| `TEXT_MESSAGE_CONTENT` | Per streamed token/chunk |
| `TEXT_MESSAGE_END` | After last assistant token |
| `TOOL_CALL_START` | SK function-call begins |
| `TOOL_CALL_ARGS` | Streamed tool arguments (if available) |
| `TOOL_CALL_END` | Tool result received |
| `RUN_FINISHED` | End of successful run |
| `RUN_ERROR` | Unhandled error during run |

Out of scope for v1: `STATE_SNAPSHOT`, `STATE_DELTA`, `MESSAGES_SNAPSHOT`, custom events, HITL approval.

## Phases

### Phase 0 — Spike (0.5d)

- Pin AG-UI spec version we target (current 0.x).
- Confirm SSE framing format (`event:` + `data:` lines, JSON payload per event with `type` discriminator).
- Decide naming: new endpoint `POST /agents/{agentId}/agui`.

Detailed plan: [ag-ui-phase-0-plan.md](./ag-ui-phase-0-plan.md).

### Phase 1 — Protocol layer (1–2d)

Create `NTG.Agent.AgUi` class library under the solution root.

Contents:
- Event records (immutable, `record` types) — `BaseEvent`, `RunStartedEvent`, `RunFinishedEvent`, `RunErrorEvent`, `TextMessageStartEvent`, `TextMessageContentEvent`, `TextMessageEndEvent`, `ToolCallStartEvent`, `ToolCallArgsEvent`, `ToolCallEndEvent`.
- `System.Text.Json` polymorphic serialization with `type` discriminator matching the spec exactly (snake/camelCase per spec).
- `RunAgentInput` DTO: `threadId`, `runId`, `messages[]`, `tools[]`, `context`, `forwardedProps`.
- SSE writer helper: extension on `HttpResponse` that takes `IAsyncEnumerable<BaseEvent>` and writes the event-stream wire format with correct headers (`Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`).
- Unit tests in `tests/NTG.Agent.AgUi.Tests` for serialization round-trip and SSE framing.

### Phase 2 — Adapter over `AgentService` (1–2d)

In `NTG.Agent.Orchestrator`:

- New `Controllers/AgUiController.cs` with `POST /agents/{agentId}/agui`.
  - Accepts `RunAgentInput`.
  - Resolves `userId` the same way `AgentsController` does.
  - Returns SSE stream via the Phase 1 helper.
- New `Services/AgUi/AgUiAdapter.cs`:
  - Maps `RunAgentInput` → existing `PromptRequestForm`.
  - Wraps `AgentService.ChatStreamingAsync` and translates output:
    - First chunk → emit `RUN_STARTED` + `TEXT_MESSAGE_START` (with generated `messageId`).
    - Each text chunk → `TEXT_MESSAGE_CONTENT`.
    - SK function-call signal → `TOOL_CALL_START` (`toolCallId`, `toolName`) → `TOOL_CALL_ARGS` (if streamed) → `TOOL_CALL_END`.
    - Stream complete → `TEXT_MESSAGE_END` + `RUN_FINISHED`.
    - Exception → `RUN_ERROR` with `code`/`message`.
  - Reuses existing persistence in `AgentService` (no duplication of `ChatMessages` writes).
- Unit tests for adapter using a fake `IAgentService` that emits scripted chunks + tool calls.

Open implementation question (resolve in Phase 2): the current `ChatStreamingAsync` returns `PromptResponse` (text-only?). We may need to surface tool-call signals from inside `InvokePromptStreamingInternalAsync` (`AgentService.cs:270`) by extending `PromptResponse` with an optional `ToolCall` payload, or by exposing a richer internal stream type the adapter consumes directly.

### Phase 3 — Blazor client integration (2–3d)

In `NTG.Agent.WebClient`:

- Add `Services/AgUiClient.cs`:
  - `HttpClient`-based SSE consumer (`HttpCompletionOption.ResponseHeadersRead`, line-by-line reader on the response stream).
  - Parses `event:`/`data:` frames, deserializes JSON to typed events using the Phase 1 records (reference `NTG.Agent.AgUi` from the WebClient project).
  - Exposes `IAsyncEnumerable<BaseEvent> RunAsync(RunAgentInput, CancellationToken)`.
- Wire into existing chat component(s):
  - Replace direct call to the old streaming endpoint with `AgUiClient.RunAsync`.
  - Render handlers per event type: append text on `TEXT_MESSAGE_CONTENT`, show tool-call indicator on `TOOL_CALL_*`, mark message complete on `RUN_FINISHED`, surface error on `RUN_ERROR`.
- Keep the old code path behind a feature flag (`appsettings`/config) for one release so we can flip back if needed.

### Phase 4 — Cutover & cleanup

- Remove the feature flag and old `ChatAsync` endpoint + `PromptResponse` once AG-UI is stable in production.
- Update `README.md` and `docs/` with the new endpoint contract.

## Deliverables

1. `NTG.Agent.AgUi` library + tests.
2. `AgUiController` + `AgUiAdapter` in Orchestrator + tests.
3. `AgUiClient` in WebClient + updated chat UI.
4. Docs: this plan, plus a short "AG-UI endpoint contract" page after Phase 2.

## Risks

- **Spec drift.** AG-UI is pre-1.0; field names may change. Mitigation: isolate all wire types in `NTG.Agent.AgUi` so updates are one-file changes.
- **Tool-call surfacing.** Current `AgentService` stream doesn't clearly expose SK function-call events; Phase 2 may require a small refactor of `InvokePromptStreamingInternalAsync`.
- **SSE through proxies.** Aspire/dev gateway must not buffer; set `X-Accel-Buffering: no` and verify in `AppHost`.

## Estimate

~5–8 dev-days end to end, single engineer.
