using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        IncludeFields = true
    };

    private static readonly StableDiffusionCppSidecarEngine Engine = new StableDiffusionCppSidecarEngine();

    public static async Task<int> Main(string[] args)
    {
        string listenIp = "127.0.0.1";
        int listenPort = 0;
        int parentProcessId = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.Equals(arg, "--listen-ip", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                listenIp = args[++i];
            }
            else if (string.Equals(arg, "--listen-port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                     int.TryParse(args[++i], out int parsedPort))
            {
                listenPort = parsedPort;
            }
            else if (string.Equals(arg, "--parent-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                     int.TryParse(args[++i], out int parsedPid))
            {
                parentProcessId = parsedPid;
            }
        }

        if (listenPort <= 0)
        {
            Console.Error.WriteLine("[SidecarWorker] --listen-port must be a positive integer.");
            return 1;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://{listenIp}:{listenPort}/");

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SidecarWorker] Failed to start HTTP listener: {ex}");
            return 1;
        }

        using var shutdownCts = new CancellationTokenSource();
        _ = MonitorParentProcessAsync(parentProcessId, listener, shutdownCts.Token);

        Console.WriteLine($"[SidecarWorker] Listening on http://{listenIp}:{listenPort}/");

        try
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                _ = Task.Run(() => HandleRequestAsync(listener, context));
            }
        }
        finally
        {
            shutdownCts.Cancel();
            Engine.ReleaseContext();
            try
            {
                listener.Close();
            }
            catch
            {
                // Ignore shutdown cleanup failures.
            }
        }

        return 0;
    }

    private static async Task HandleRequestAsync(HttpListener listener, HttpListenerContext context)
    {
        string path = context.Request.Url?.AbsolutePath ?? "/";
        if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, 200, new StableDiffusionCppWorkerHealthResponse
            {
                ok = true,
                processId = Environment.ProcessId,
                hasLoadedContext = Engine.HasLoadedContext,
                isBusy = Engine.IsBusy
            });
            return;
        }

        if (string.Equals(path, "/shutdown", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, 200, new StableDiffusionCppWorkerGenerateResponse
            {
                success = true
            });

            try
            {
                listener.Stop();
            }
            catch
            {
                // Ignore stop failures.
            }

            return;
        }

        if (string.Equals(path, "/progress", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, 200, Engine.GetProgressSnapshot());
            return;
        }

        if (string.Equals(path, "/release", StringComparison.OrdinalIgnoreCase))
        {
            Engine.ReleaseContext();
            await WriteJsonAsync(context, 200, new StableDiffusionCppWorkerGenerateResponse
            {
                success = true
            });
            return;
        }

        if (!string.Equals(path, "/generate", StringComparison.OrdinalIgnoreCase))
        {
            await WriteJsonAsync(context, 404, new StableDiffusionCppWorkerGenerateResponse
            {
                success = false,
                errorMessage = $"Unknown endpoint: {path}"
            });
            return;
        }

        StableDiffusionCppWorkerGenerateRequest request = null;
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
            string body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                request = JsonSerializer.Deserialize<StableDiffusionCppWorkerGenerateRequest>(body, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context, 400, new StableDiffusionCppWorkerGenerateResponse
            {
                success = false,
                errorMessage = $"Failed to parse request JSON: {ex.Message}"
            });
            return;
        }

        if (request == null)
        {
            await WriteJsonAsync(context, 400, new StableDiffusionCppWorkerGenerateResponse
            {
                success = false,
                errorMessage = "Request body is empty."
            });
            return;
        }

        StableDiffusionCppWorkerGenerateResponse response = Engine.Generate(request);
        await WriteJsonAsync(context, response.success ? 200 : 500, response);

        if (Engine.ShouldRecycleAfterResponse)
        {
            try
            {
                listener.Stop();
            }
            catch
            {
                // Ignore stop failures.
            }
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object responseObject)
    {
        string json = JsonSerializer.Serialize(responseObject, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentEncoding = Encoding.UTF8;
        context.Response.ContentLength64 = bytes.LongLength;

        await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    private static async Task MonitorParentProcessAsync(
        int parentProcessId,
        HttpListener listener,
        CancellationToken cancellationToken)
    {
        if (parentProcessId <= 0)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Process parent = Process.GetProcessById(parentProcessId);
                if (parent.HasExited)
                {
                    break;
                }

                parent.Dispose();
            }
            catch
            {
                break;
            }

            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        try
        {
            listener.Stop();
        }
        catch
        {
            // Ignore stop failures.
        }
    }
}
