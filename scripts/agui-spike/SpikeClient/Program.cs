using System.Text.Json;

namespace SpikeClient;

/// <summary>
/// Console client that connects to the AG-UI SSE spike endpoint
/// and verifies that events arrive incrementally (not as one blob).
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        var url = args.Length > 0 ? args[0] : "http://localhost:5000/api/agui-spike/hello";
        
        Console.WriteLine($"Connecting to {url} ...");

        using var httpClient = new HttpClient();
        
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"ERROR: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return 1;
        }

        Console.WriteLine($"Status: {(int)response.StatusCode} {response.ReasonPhrase}");
        Console.WriteLine($"Content-Type: {response.Content.Headers.ContentType}");
        Console.WriteLine("---");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var eventCount = 0;
        var lastEventTime = DateTime.UtcNow;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            
            if (line is null) break;

            // Skip empty lines (SSE frame separators)
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Skip comment lines (heartbeat pings)
            if (line.StartsWith(':')) 
            {
                Console.WriteLine("[heartbeat]");
                continue;
            }

            // Parse data lines
            if (line.StartsWith("data: "))
            {
                var json = line["data: ".Length..];
                var now = DateTime.UtcNow;
                var elapsed = (now - lastEventTime).TotalMilliseconds;
                lastEventTime = now;

                eventCount++;
                
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var type = doc.RootElement.GetProperty("type").GetString();
                    Console.WriteLine($"Event #{eventCount,2} ({elapsed,6:F0}ms): {type}");
                    
                    // Pretty-print the full JSON for verification
                    Console.WriteLine($"  {json}");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Event #{eventCount}: PARSE ERROR: {ex.Message}");
                    Console.WriteLine($"  Raw: {json}");
                }
            }
            else
            {
                Console.WriteLine($"Unexpected line: {line}");
            }
        }

        Console.WriteLine("---");
        Console.WriteLine($"Total events received: {eventCount}");
        Console.WriteLine(eventCount >= 7 ? "PASS: Received expected number of events" : $"WARN: Expected at least 7 events, got {eventCount}");
        
        return 0;
    }
}