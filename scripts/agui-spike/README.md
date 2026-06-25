# AG-UI Phase 0 Spike

Phase 0 spike — **delete after Phase 1 merges.**

## What's here

| Path | Purpose |
|------|---------|
| `SpikeClient/` | Console app that connects to the SSE spike endpoint and verifies incremental event delivery. |
| `SpikeController.cs` | Lives in `NTG.Agent.Orchestrator/Controllers/SpikeController.cs` — hardcoded SSE event loop, registered only when `AGUI_SPIKE=1`. |

## How to run

### 1. Start the Orchestrator with the spike enabled

```bash
# Set the environment variable to enable the spike endpoint
export AGUI_SPIKE=1

# Run the Orchestrator (via AppHost or directly)
dotnet run --project NTG.Agent.Orchestrator
```

### 2. Test with curl

```bash
# Replace <port> with the actual Orchestrator port
curl -N http://localhost:<port>/api/agui-spike/hello
```

You should see events arriving one every ~150 ms, not as a single blob.

### 3. Test with the console client

```bash
dotnet run --project scripts/agui-spike/SpikeClient -- http://localhost:<port>/api/agui-spike/hello
```

### 4. Test through Aspire gateway (§3.6)

```bash
# Start AppHost
dotnet run --project NTG.Agent.AppHost

# Test through the gateway port (check Aspire dashboard for the port)
curl -N http://localhost:<gateway-port>/api/agui-spike/hello
```

Verify that events still arrive incrementally through the Aspire reverse proxy.

## Tool-call probe (§3.4)

When `AGUI_SPIKE=1`, the Orchestrator logs every `AIContent` type observed in
`InvokePromptStreamingInternalAsync`. To trigger a tool call:

1. Start the Orchestrator with `AGUI_SPIKE=1`.
2. Send a chat message that triggers the KnowledgePlugin (e.g., "search the knowledge base for X").
3. Check the logs for lines prefixed with `[AGUI_SPIKE]` showing the content types.

Expected output:
```
[AGUI_SPIKE] Content type: TextContent (Microsoft.Extensions.AI.TextContent)
[AGUI_SPIKE] Content type: FunctionCallContent (Microsoft.Extensions.AI.FunctionCallContent)
[AGUI_SPIKE] Content type: FunctionResultContent (Microsoft.Extensions.AI.FunctionResultContent)
```

## Cleanup

After Phase 1 merges, delete:

- `NTG.Agent.Orchestrator/Controllers/SpikeController.cs`
- `scripts/agui-spike/` directory
- The `AGUI_SPIKE` middleware block in `Program.cs`
- The `[AGUI_SPIKE]` logging block in `AgentService.cs`