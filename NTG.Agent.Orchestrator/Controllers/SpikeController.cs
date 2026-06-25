using Microsoft.AspNetCore.Mvc;

namespace NTG.Agent.Orchestrator.Controllers;

/// <summary>
/// SPIKE — Phase 0 AG-UI SSE prototype. DELETE AFTER PHASE 1 MERGES.
/// Only accessible when AGUI_SPIKE=1 environment variable is set.
/// </summary>
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