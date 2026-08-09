using System.Text.Json;
using CodePrintManager.Application.Services;
using CodePrintManager.Domain.Events;

namespace CodePrintManager.TestHost.Endpoints;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapGet("/api/events", async (HttpContext ctx, JobEventBus eventBus, CancellationToken ct) =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var tcs = new TaskCompletionSource();
            ct.Register(() => tcs.TrySetResult());

            void OnProgress(object? sender, JobProgressChangedEvent e)
            {
                var data = JsonSerializer.Serialize(new { Type = "progress", e.JobId, e.Confirmed, e.Total });
                ctx.Response.WriteAsync($"data: {data}\n\n", ct).GetAwaiter().GetResult();
                ctx.Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
            }

            void OnCompleted(object? sender, JobCompletedEvent e)
            {
                var data = JsonSerializer.Serialize(new { Type = "completed", e.JobId, Status = e.FinalStatus.ToString() });
                ctx.Response.WriteAsync($"data: {data}\n\n", ct).GetAwaiter().GetResult();
                ctx.Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
            }

            eventBus.ProgressChanged += OnProgress;
            eventBus.Completed += OnCompleted;

            try
            {
                await tcs.Task;
            }
            finally
            {
                eventBus.ProgressChanged -= OnProgress;
                eventBus.Completed -= OnCompleted;
            }
        });
    }
}
