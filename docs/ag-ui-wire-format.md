# AG-UI Wire Format Reference

> Pinned spec version: **0.0.53** (Apr 30 2026) · Git tag: `release/2026-05-22` · Commit: `df381cb`
> See [ag-ui-phase-0-plan.md](./ag-ui-phase-0-plan.md) § Spec snapshot for provenance.

---

## 1. SSE Framing Rules

### Response Headers

```
Content-Type: text/event-stream; charset=utf-8
Cache-Control: no-cache
Connection: keep-alive
X-Accel-Buffering: no
```

- `X-Accel-Buffering: no` — instructs nginx/Aspire reverse proxy to flush immediately, preventing buffering of SSE chunks.
- `Connection: keep-alive` — ensures the TCP connection stays open for the duration of the stream.

### Frame Format

Each event is a single `data:` line followed by two newlines:

```
data: <single-line JSON>\n\n
```

- **No `event:` line.** The event type discriminator lives inside the JSON payload as the `type` field. This matches the AG-UI default (JSON-only; no SSE `event:` field).
- **No multi-line `data:`.** The JSON payload is serialized to a single line (no pretty-printing, no embedded newlines). If a `delta` value contains a literal newline, it MUST be escaped as `\n` in the JSON string.
- **Line ending:** `\n` (LF), not `\r\n`.

### Heartbeat / Keep-alive

AG-UI does not mandate a heartbeat. **Decision:** send an SSE comment frame every 15 seconds if no event has been emitted, to prevent intermediary proxies from closing the connection:

```
: ping\n\n
```

SSE comment frames start with `:` and are ignored by compliant SSE parsers.

---

## 2. JSON Naming Convention

All JSON field names use **camelCase**, per the AG-UI TypeScript SDK and spec:

| Field                | JSON key            |
|----------------------|---------------------|
| Thread ID            | `threadId`          |
| Run ID               | `runId`             |
| Parent Run ID        | `parentRunId`       |
| Message ID           | `messageId`         |
| Tool Call ID         | `toolCallId`        |
| Tool Call Name       | `toolCallName`      |
| Parent Message ID    | `parentMessageId`   |
| Delta (text chunk)   | `delta`             |
| Timestamp            | `timestamp`         |
| Raw Event            | `rawEvent`          |
| Role                 | `role`              |
| Name                 | `name`              |
| Message              | `message`           |
| Code                 | `code`              |
| Result               | `result`            |
| Outcome              | `outcome`           |

---

## 3. Canonical Event Examples (Phase 1 Subset)

Each example includes every field we intend to emit. Optional fields that we omit are noted.

### 3.1 RUN_STARTED

```json
{
  "type": "RUN_STARTED",
  "threadId": "thread_abc123",
  "runId": "run_def456"
}
```

| Field       | Required | Notes                        |
|-------------|----------|------------------------------|
| `type`      | ✅       | Always `"RUN_STARTED"`       |
| `threadId`  | ✅       | Conversation ID              |
| `runId`     | ✅       | Unique per request           |
| `parentRunId` | ❌     | Not used in v1               |
| `input`     | ❌       | Not emitted in v1            |

### 3.2 TEXT_MESSAGE_START

```json
{
  "type": "TEXT_MESSAGE_START",
  "messageId": "msg_ghi789",
  "role": "assistant"
}
```

| Field        | Required | Notes                          |
|--------------|----------|--------------------------------|
| `type`       | ✅       | Always `"TEXT_MESSAGE_START"`  |
| `messageId`  | ✅       | Unique per assistant message   |
| `role`       | ❌       | Defaults to `"assistant"`      |
| `name`       | ❌       | Not used in v1                 |

### 3.3 TEXT_MESSAGE_CONTENT

```json
{
  "type": "TEXT_MESSAGE_CONTENT",
  "messageId": "msg_ghi789",
  "delta": "Hello"
}
```

| Field        | Required | Notes                            |
|--------------|----------|----------------------------------|
| `type`       | ✅       | Always `"TEXT_MESSAGE_CONTENT"` |
| `messageId`  | ✅       | Matches the `TEXT_MESSAGE_START`  |
| `delta`      | ✅       | Non-empty text chunk             |

### 3.4 TEXT_MESSAGE_END

```json
{
  "type": "TEXT_MESSAGE_END",
  "messageId": "msg_ghi789"
}
```

| Field        | Required | Notes                        |
|--------------|----------|------------------------------|
| `type`       | ✅       | Always `"TEXT_MESSAGE_END"`  |
| `messageId`  | ✅       | Matches the `TEXT_MESSAGE_START` |

### 3.5 TOOL_CALL_START

```json
{
  "type": "TOOL_CALL_START",
  "toolCallId": "call_jkl012",
  "toolCallName": "knowledge_search"
}
```

| Field              | Required | Notes                          |
|--------------------|----------|--------------------------------|
| `type`             | ✅       | Always `"TOOL_CALL_START"`     |
| `toolCallId`       | ✅       | Unique per tool invocation      |
| `toolCallName`     | ✅       | Function/plugin name            |
| `parentMessageId`  | ❌       | Not used in v1                 |

### 3.6 TOOL_CALL_ARGS

```json
{
  "type": "TOOL_CALL_ARGS",
  "toolCallId": "call_jkl012",
  "delta": "{\"query\": \"search terms\"}"
}
```

| Field        | Required | Notes                        |
|--------------|----------|------------------------------|
| `type`       | ✅       | Always `"TOOL_CALL_ARGS"`    |
| `toolCallId` | ✅       | Matches `TOOL_CALL_START`     |
| `delta`      | ✅       | JSON fragment of arguments    |

**Note:** `delta` is a JSON string fragment. If the arguments are streamed incrementally, each `delta` is a partial JSON string that the client concatenates. If the full arguments are available at once, a single `TOOL_CALL_ARGS` event contains the complete JSON string.

### 3.7 TOOL_CALL_END

```json
{
  "type": "TOOL_CALL_END",
  "toolCallId": "call_jkl012"
}
```

| Field        | Required | Notes                        |
|--------------|----------|------------------------------|
| `type`       | ✅       | Always `"TOOL_CALL_END"`     |
| `toolCallId` | ✅       | Matches `TOOL_CALL_START`    |

### 3.8 RUN_FINISHED

```json
{
  "type": "RUN_FINISHED",
  "threadId": "thread_abc123",
  "runId": "run_def456"
}
```

| Field       | Required | Notes                          |
|-------------|----------|--------------------------------|
| `type`      | ✅       | Always `"RUN_FINISHED"`        |
| `threadId`  | ✅       | Matches `RUN_STARTED`          |
| `runId`     | ✅       | Matches `RUN_STARTED`          |
| `result`    | ❌       | Not used in v1                 |
| `outcome`   | ❌       | New in 0.0.53; not used in v1  |

### 3.9 RUN_ERROR

```json
{
  "type": "RUN_ERROR",
  "message": "An internal error occurred.",
  "code": "INTERNAL_ERROR"
}
```

| Field      | Required | Notes                        |
|------------|----------|------------------------------|
| `type`     | ✅       | Always `"RUN_ERROR"`         |
| `message`  | ✅       | Human-readable error message |
| `code`     | ❌       | Machine-readable error code  |

---

## 4. Event Sequence

A typical successful run emits events in this order:

```
RUN_STARTED
  TEXT_MESSAGE_START
    TEXT_MESSAGE_CONTENT  (repeated, one per token/chunk)
    TEXT_MESSAGE_CONTENT
    …
  TEXT_MESSAGE_END
  [optional: TOOL_CALL_START → TOOL_CALL_ARGS → TOOL_CALL_END  (repeated per tool call)]
RUN_FINISHED
```

An error run emits:

```
RUN_STARTED
  … (zero or more events before the error)
RUN_ERROR
```

**Important:** `RUN_ERROR` replaces `RUN_FINISHED` — a run ends with exactly one of these two events, never both.

---

## 5. curl Verification Capture

> **Placeholder** — to be filled after running the SSE prototype (§3.5 of phase-0-plan.md).
>
> Expected command:
> ```bash
> curl -N http://localhost:<port>/api/agui-spike/hello
> ```
>
> Expected output (one `data:` line per event, arriving ~150 ms apart):
> ```
> data: {"type":"RUN_STARTED","threadId":"...","runId":"..."}
>
> data: {"type":"TEXT_MESSAGE_START","messageId":"...","role":"assistant"}
>
> data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":"Hello"}
>
> data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":", "}
>
> data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":"world"}
>
> data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"...","delta":"!"}
>
> data: {"type":"TEXT_MESSAGE_END","messageId":"..."}
>
> data: {"type":"RUN_FINISHED","threadId":"...","runId":"..."}
>
> ```