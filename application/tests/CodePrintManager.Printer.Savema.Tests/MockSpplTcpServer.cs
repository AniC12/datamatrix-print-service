using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CodePrintManager.Printer.Savema.Tests;

/// <summary>
/// A test helper TCP server that speaks the SPPL protocol.
/// Listens on a random port, accepts connections, parses incoming SPPL commands,
/// and returns configurable responses. Supports fault injection at the TCP level.
/// </summary>
public class MockSpplTcpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, string> _responses = new();
    private readonly ConcurrentQueue<Func<string, NetworkStream, Task<bool>>> _faultHandlers = new();
    private Task? _acceptTask;
    private TcpClient? _currentClient;
    private NetworkStream? _currentStream;

    public int Port { get; }

    public MockSpplTcpServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        // Default SPPL responses (payload only — frame wrapper ~SPGRES{CMD:PAYLOAD}^ added later)
        // Query responses: payload is the raw value
        // Command responses: payload is "OK" or "FAIL"
        _responses["SPPSTA"] = "WAITING";       // ParseStatus → state="WAITING" → Idle
        _responses["SPGGCP"] = "0";             // AsInt() → 0
        _responses["SPGGTP"] = "0";             // AsInt() → 0
        _responses["SPPGLQ"] = "0";             // AsInt() → 0
        _responses["SPGGSN"] = "MOCK-TCP-001";  // raw serial string
        _responses["SPLGST"] = "";              // AsList() → empty
        _responses["SPLGAT"] = "";              // active template name
        _responses["SPLGSD"] = "";              // AsList() → empty
        _responses["SPPSAP"] = "OK";            // IsOk → true
        _responses["SPPSTP"] = "OK";
        _responses["SPPSLQ"] = "OK";
        _responses["SPLLTF"] = "OK";
        _responses["SPLRTF"] = "OK";
        _responses["SPLCDF"] = "OK";
        _responses["SPLDTF"] = "OK";
        _responses["SPLDDF"] = "OK";

        _acceptTask = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Set the response payload for a specific SPPL command.
    /// E.g., SetResponse("SPGGCP", "OK<42>") → ~SPGRES<SPGGCP:OK<42>>^
    /// </summary>
    public void SetResponse(string command, string payload)
    {
        _responses[command] = payload;
    }

    /// <summary>
    /// Set counter response values.
    /// </summary>
    public void SetCounters(int current, int lifetime)
    {
        _responses["SPGGCP"] = current.ToString();
        _responses["SPGGTP"] = lifetime.ToString();
    }

    /// <summary>
    /// Set status response.
    /// </summary>
    public void SetStatus(string status)
    {
        _responses["SPPSTA"] = status;
    }

    /// <summary>
    /// Enqueue a fault handler that intercepts the NEXT command.
    /// The handler receives the parsed command name and the NetworkStream.
    /// Return true to indicate the command was handled (skip normal response).
    /// Return false to let normal response processing continue.
    /// </summary>
    public void EnqueueFault(Func<string, NetworkStream, Task<bool>> handler)
    {
        _faultHandlers.Enqueue(handler);
    }

    /// <summary>
    /// Enqueue: accept command, then close the connection (simulate crash/drop).
    /// </summary>
    public void EnqueueDropAfterReceive()
    {
        EnqueueFault((cmd, stream) =>
        {
            // Received the command, now close the connection without responding
            stream.Close();
            return Task.FromResult(true);
        });
    }

    /// <summary>
    /// Enqueue: accept command, send partial response, then close.
    /// </summary>
    public void EnqueuePartialResponse(string command)
    {
        EnqueueFault(async (cmd, stream) =>
        {
            // Send only part of the response frame (no closing })
            var partial = $"~SPGRES{{{command}:OK<WAI";
            var bytes = Encoding.ASCII.GetBytes(partial);
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
            await Task.Delay(50);
            stream.Close();
            return true;
        });
    }

    /// <summary>
    /// Enqueue: accept command, never respond (simulate hung printer).
    /// The handler blocks until the connection is closed by the client (timeout).
    /// </summary>
    public void EnqueueHang()
    {
        EnqueueFault(async (cmd, stream) =>
        {
            // Just wait forever — the client's read timeout will fire
            try { await Task.Delay(Timeout.Infinite, _cts.Token); }
            catch (OperationCanceledException) { }
            return true;
        });
    }

    /// <summary>
    /// Enqueue: send an unsolicited frame BEFORE the actual response.
    /// </summary>
    public void EnqueueUnsolicitedFrame(string unsolicitedCommand, string payload)
    {
        EnqueueFault(async (cmd, stream) =>
        {
            // Send unsolicited frame first (curly-brace wrapped per SPPL spec)
            var unsolicited = $"~SPGRES{{{unsolicitedCommand}:{payload}}}^";
            var bytes1 = Encoding.ASCII.GetBytes(unsolicited);
            await stream.WriteAsync(bytes1);

            // Then send the real response
            var response = _responses.TryGetValue(cmd, out var resp) ? resp : "OK";
            var real = $"~SPGRES{{{cmd}:{response}}}^";
            var bytes2 = Encoding.ASCII.GetBytes(real);
            await stream.WriteAsync(bytes2);
            await stream.FlushAsync();
            return true;
        });
    }

    /// <summary>
    /// Enqueue: TCP RST the connection.
    /// </summary>
    public void EnqueueReset()
    {
        EnqueueFault((cmd, stream) =>
        {
            // Set linger to force RST
            var socket = _currentClient?.Client;
            if (socket != null)
            {
                socket.LingerState = new LingerOption(true, 0);
                socket.Close();
            }
            return Task.FromResult(true);
        });
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _currentClient = client;
                _currentStream = client.GetStream();
                _ = HandleClientAsync(client, _currentStream, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { /* ignore connection errors, keep accepting */ }
        }
    }

    private async Task HandleClientAsync(TcpClient client, NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var receiveBuffer = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && client.Connected)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, ct);
                }
                catch { break; }

                if (bytesRead == 0) break;

                receiveBuffer.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                var raw = receiveBuffer.ToString();

                // Process all complete frames
                while (raw.Contains('^'))
                {
                    var frameEnd = raw.IndexOf('^') + 1;
                    var frame = raw[..frameEnd];
                    receiveBuffer.Remove(0, frameEnd);
                    raw = receiveBuffer.ToString();

                    // Extract command name from ~COMMAND^ or ~COMMAND{data}^
                    var cmdStart = frame.IndexOf('~') + 1;
                    var cmdEnd = frame.IndexOfAny(['{', '^'], cmdStart);
                    var command = cmdEnd > cmdStart ? frame[cmdStart..cmdEnd] : "";

                    // Check for fault handlers
                    if (_faultHandlers.TryDequeue(out var handler))
                    {
                        try
                        {
                            var handled = await handler(command, stream);
                            if (handled) continue;
                        }
                        catch { break; } // Stream may be closed by fault handler
                    }

                    // Send normal response (curly-brace wrapped per SPPL spec)
                    var payload = _responses.TryGetValue(command, out var resp) ? resp : "OK";
                    var response = $"~SPGRES{{{command}:{payload}}}^";
                    var responseBytes = Encoding.ASCII.GetBytes(response);
                    try
                    {
                        await stream.WriteAsync(responseBytes, ct);
                        await stream.FlushAsync(ct);
                    }
                    catch { break; }
                }
            }
        }
        catch { /* connection closed */ }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { _currentClient?.Close(); } catch { }
        try { _acceptTask?.Wait(1000); } catch { }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
