using System;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SandBoxSim.ConsoleApp.Online;

/// <summary>BCL-only loopback HTTP transport. No arbitrary code, paths or remote binds.</summary>
public static class HttpService
{
    public static int Run(int port) => RunAsync(port).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(int port)
    {
        if (port is < 1024 or > 65535) { throw new ArgumentException("port must be 1024-65535"); }
        string token = System.Environment.GetEnvironmentVariable("SANDBOXSIM_API_TOKEN") ?? "";
        if (token.Length < 16) { throw new ArgumentException("Set SANDBOXSIM_API_TOKEN to at least 16 characters"); }
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var api = new StepApi(); var gate = new object();
        using var slots = new SemaphoreSlim(8);
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); listener.Stop(); };
        System.Console.CancelKeyPress += cancel;
        string assembly = typeof(HttpService).Assembly.Location;
        string core = typeof(Core.Simulation).Assembly.Location;
        var identity = new { schemaVersion = 1, protocol = "sandboxsim-step-v1", engine = "core-fixed-tick",
            assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))).ToLowerInvariant(),
            coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core))).ToLowerInvariant(),
            machineId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Environment.MachineName))).ToLowerInvariant(),
            os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(), framework = RuntimeInformation.FrameworkDescription,
            processorCount = System.Environment.ProcessorCount, maxSessions = StepApi.MaxSessions, maxTicksPerCall = StepApi.MaxTicks,
            actions = new[] { "terrain", "resource", "fertility" }, visualObservation = false };
        System.Console.WriteLine(JsonSerializer.Serialize(new { listening = $"http://127.0.0.1:{port}/", protocol = "sandboxsim-step-v1" }));
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync();
                if (!slots.Wait(0)) { await Reply(context, 429, new { error = new { code = "busy", message = "Request limit reached" } }); continue; }
                _ = Handle(context);
            }
        }
        catch (HttpListenerException) when (stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        finally { System.Console.CancelKeyPress -= cancel; }
        return 0;

        async Task Handle(HttpListenerContext context)
        {
            try
            {
                byte[] expected = Encoding.UTF8.GetBytes("Bearer " + token);
                byte[] actual = Encoding.UTF8.GetBytes(context.Request.Headers["Authorization"] ?? "");
                if (!CryptographicOperations.FixedTimeEquals(expected, actual)) { throw new ApiError(401, "Bearer token required"); }
                string method = context.Request.HttpMethod, path = context.Request.Url!.AbsolutePath;
                // Reject browser-origin requests; this endpoint is for explicit model/controller clients.
                if (context.Request.Headers["Origin"] != null) { throw new ApiError(403, "Browser origins are not supported"); }
                object result;
                if (method == "GET" && (path == "/v1/health" || path == "/v1/capabilities")) { result = identity; }
                else
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var input = new MemoryStream(); var buffer = new byte[4096];
                    if (context.Request.ContentLength64 > 32768) { throw new ApiError(413, "Body exceeds 32 KiB"); }
                    int count;
                    while ((count = await context.Request.InputStream.ReadAsync(buffer, timeout.Token)) != 0)
                    {
                        input.Write(buffer, 0, count);
                        if (input.Length > 32768) { throw new ApiError(413, "Body exceeds 32 KiB"); }
                    }
                    using var json = JsonDocument.Parse(input.Length == 0 ? "{}" : Encoding.UTF8.GetString(input.ToArray()), new JsonDocumentOptions { MaxDepth = 16 });
                    lock (gate) { result = api.Dispatch(method, path, json.RootElement); }
                }
                await Reply(context, 200, result);
            }
            catch (ApiError e) { await Error(e.Status, e.Message); }
            catch (JsonException) { await Error(400, "Malformed JSON"); }
            catch (OperationCanceledException) { await Error(408, "Request read timed out"); }
            catch (Exception e) { System.Console.Error.WriteLine(e); await Error(500, "Engine error; discard or reset the session"); }
            finally { context.Response.Close(); slots.Release(); }

            async Task Error(int status, string message)
            {
                try { await Reply(context, status, new { schemaVersion = 1, error = new { code = "http_" + status, message } }); }
                catch (Exception e) when (e is IOException || e is HttpListenerException || e is ObjectDisposedException) { }
            }
        }
    }
    private static async Task Reply(HttpListenerContext context, int status, object body)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }
}
