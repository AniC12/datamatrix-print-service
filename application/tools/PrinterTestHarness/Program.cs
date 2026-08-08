using CodePrintManager.Printer.Savema;
using CodePrintManager.Domain.Interfaces;
using Microsoft.Extensions.Logging;

Console.WriteLine("=== Printer Test Harness ===");
Console.WriteLine("Interactive tool for testing printer adapters against real hardware.");
Console.WriteLine("Type 'help' for available commands.\n");

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
var factory = new SavemaAdapterFactory(loggerFactory);
IPrinterAdapter? adapter = null;

while (true)
{
    Console.Write(adapter != null ? "[connected]> " : "> ");
    var input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input)) continue;

    var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var command = parts[0].ToLowerInvariant();

    try
    {
        switch (command)
        {
            case "help":
                PrintHelp();
                break;

            case "connect":
                if (parts.Length < 2) { Console.WriteLine("Usage: connect <ip> [port] [adapter_type]"); break; }
                var ip = parts[1];
                var port = parts.Length > 2 ? int.Parse(parts[2]) : 9100;
                var type = parts.Length > 3 ? parts[3] : "savema_tto";

                adapter?.Dispose();
                adapter = factory.Create(type);
                var connected = await adapter.ConnectAsync(ip, port, CancellationToken.None);
                Console.WriteLine(connected ? $"Connected to {ip}:{port}" : "Connection FAILED");
                if (!connected) { adapter.Dispose(); adapter = null; }
                break;

            case "disconnect":
                if (adapter == null) { Console.WriteLine("Not connected."); break; }
                await adapter.DisconnectAsync();
                adapter.Dispose();
                adapter = null;
                Console.WriteLine("Disconnected.");
                break;

            case "status":
                RequireConnection(adapter);
                var status = await adapter!.GetStatusAsync(CancellationToken.None);
                Console.WriteLine($"Printer status: {status}");
                break;

            case "counter":
            case "counters":
                RequireConnection(adapter);
                var current = await adapter!.GetCurrentCounterAsync(CancellationToken.None);
                var total = await adapter!.GetTotalCounterAsync(CancellationToken.None);
                Console.WriteLine($"Current counter (SPGGCP): {current}");
                Console.WriteLine($"Lifetime counter (SPGGTP): {total}");
                break;

            case "upload-csv":
                RequireConnection(adapter);
                if (parts.Length < 3) { Console.WriteLine("Usage: upload-csv <filename> <code1,code2,...>"); break; }
                var csvName = parts[1];
                var codes = parts[2].Split(',');
                var uploaded = await adapter!.UploadCsvAsync(csvName, codes, CancellationToken.None);
                Console.WriteLine(uploaded ? $"Uploaded {csvName} ({codes.Length} codes)" : "Upload FAILED");
                break;

            case "list-csv":
                RequireConnection(adapter);
                var csvFiles = await adapter!.ListCsvFilesAsync(CancellationToken.None);
                Console.WriteLine($"CSV files on printer ({csvFiles.Count}):");
                foreach (var f in csvFiles) Console.WriteLine($"  {f}");
                break;

            case "delete-csv":
                RequireConnection(adapter);
                if (parts.Length < 2) { Console.WriteLine("Usage: delete-csv <filename>"); break; }
                var deletedCsv = await adapter!.DeleteCsvAsync(parts[1], CancellationToken.None);
                Console.WriteLine(deletedCsv ? "Deleted" : "Delete FAILED");
                break;

            case "list-templates":
                RequireConnection(adapter);
                var templates = await adapter!.ListTemplatesAsync(CancellationToken.None);
                Console.WriteLine($"Templates on printer ({templates.Count}):");
                foreach (var t in templates) Console.WriteLine($"  {t}");
                break;

            case "activate-template":
                RequireConnection(adapter);
                if (parts.Length < 2) { Console.WriteLine("Usage: activate-template <name>"); break; }
                var activated = await adapter!.ActivateTemplateAsync(parts[1], CancellationToken.None);
                Console.WriteLine(activated ? "Template activated" : "Activate FAILED");
                break;

            case "set-qty":
                RequireConnection(adapter);
                if (parts.Length < 2) { Console.WriteLine("Usage: set-qty <quantity>"); break; }
                var qty = int.Parse(parts[1]);
                var setOk = await adapter!.SetPrintQuantityAsync(qty, CancellationToken.None);
                Console.WriteLine(setOk ? $"Quantity set to {qty}" : "Set quantity FAILED");
                break;

            case "start":
                RequireConnection(adapter);
                var started = await adapter!.StartPrintAsync(CancellationToken.None);
                Console.WriteLine(started ? "Print started" : "Start FAILED");
                break;

            case "stop":
                RequireConnection(adapter);
                var stopped = await adapter!.StopPrintAsync(CancellationToken.None);
                Console.WriteLine(stopped ? "Print stopped" : "Stop FAILED");
                break;

            case "poll":
                RequireConnection(adapter);
                var interval = parts.Length > 1 ? int.Parse(parts[1]) : 500;
                Console.WriteLine($"Polling every {interval}ms. Press any key to stop...");
                while (!Console.KeyAvailable)
                {
                    var c = await adapter!.GetCurrentCounterAsync(CancellationToken.None);
                    Console.Write($"\rCounter: {c}    ");
                    await Task.Delay(interval);
                }
                Console.ReadKey(true);
                Console.WriteLine();
                break;

            case "exit":
            case "quit":
                adapter?.Dispose();
                Console.WriteLine("Bye.");
                return;

            default:
                Console.WriteLine($"Unknown command: {command}. Type 'help' for list.");
                break;
        }
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("Not connected"))
    {
        Console.WriteLine(ex.Message);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
    }
}

static void RequireConnection(IPrinterAdapter? adapter)
{
    if (adapter == null) throw new InvalidOperationException("Not connected. Use 'connect <ip>' first.");
}

static void PrintHelp()
{
    Console.WriteLine("""
    Commands:
      connect <ip> [port] [type]          Connect to printer (default port 9100, type savema_tto)
      disconnect                          Disconnect from printer
      status                              Get printer status
      counter(s)                          Read current + lifetime counters
      upload-csv <name> <c1,c2,...>        Upload CSV data to printer
      list-csv                            List CSV files on printer
      delete-csv <name>                   Delete a CSV file from printer
      list-templates                      List templates on printer
      activate-template <name>            Activate a template
      set-qty <quantity>                  Set print quantity
      start                               Start printing
      stop                                Stop printing
      poll [interval_ms]                  Continuously poll counter (default 500ms)
      help                                Show this help
      exit / quit                         Exit
    """);
}
