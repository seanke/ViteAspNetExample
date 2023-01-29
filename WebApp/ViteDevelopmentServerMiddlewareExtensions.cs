using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SpaServices;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebVite;

internal static class ViteDevelopmentServerMiddlewareExtensions
{
    private const string LogCategoryName = "Microsoft.AspNetCore.SpaServices";

    private static readonly TimeSpan
        RegexMatchTimeout =
            TimeSpan.FromSeconds(5); // This is a development-time only feature, so a very long timeout is fine

    public static void UseViteDevelopmentServer(
        this ISpaBuilder spaBuilder,
        string npmScript)
    {
        if (spaBuilder == null)
        {
            throw new ArgumentNullException(nameof(spaBuilder));
        }

        var spaOptions = spaBuilder.Options;

        if (string.IsNullOrEmpty(spaOptions.SourcePath))
        {
            throw new InvalidOperationException(
                $"To use {nameof(UseViteDevelopmentServer)}, you must supply a non-empty value for the {nameof(SpaOptions.SourcePath)} property of {nameof(SpaOptions)} when calling {nameof(SpaApplicationBuilderExtensions.UseSpa)}.");
        }

        Attach(spaBuilder, npmScript);
    }

    private static void Attach(
        ISpaBuilder spaBuilder,
        string scriptName)
    {
        var pkgManagerCommand = spaBuilder.Options.PackageManagerCommand;
        var sourcePath = spaBuilder.Options.SourcePath;
        var devServerPort = spaBuilder.Options.DevServerPort;
        if (string.IsNullOrEmpty(sourcePath))
        {
            throw new ArgumentException("Property 'SourcePath' cannot be null or empty", nameof(spaBuilder));
        }

        if (string.IsNullOrEmpty(scriptName))
        {
            throw new ArgumentException("Cannot be null or empty", nameof(scriptName));
        }

        // Start vite and attach to middleware pipeline
        var appBuilder = spaBuilder.ApplicationBuilder;
        var applicationStoppingToken = appBuilder.ApplicationServices.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping;
        var logger = GetOrCreateLogger(appBuilder, LogCategoryName);
        var diagnosticSource = appBuilder.ApplicationServices.GetRequiredService<DiagnosticSource>();
        var portTask = StartViteDevServerAsync(sourcePath, scriptName, pkgManagerCommand, devServerPort, logger,
            diagnosticSource, applicationStoppingToken);

        spaBuilder.UseProxyToSpaDevelopmentServer(async () =>
        {
            // On each request, we create a separate startup task with its own timeout. That way, even if
            // the first request times out, subsequent requests could still work.
            var timeout = spaBuilder.Options.StartupTimeout;
            var port = await portTask.WithTimeout(timeout,
                "The vite server did not start listening for requests " +
                $"within the timeout period of {timeout.TotalSeconds} seconds. " +
                "Check the log output for error information.");

            // Everything we proxy is hardcoded to target http://localhost because:
            // - the requests are always from the local machine (we're not accepting remote
            //   requests that go directly to the vite server)
            // - given that, there's no reason to use https, and we couldn't even if we
            //   wanted to, because in general the vite server has no certificate
            return new UriBuilder("http", "localhost", port).Uri;
        });
    }

    private static ILogger GetOrCreateLogger(
        IApplicationBuilder appBuilder,
        string logCategoryName)
    {
        // If the DI system gives us a logger, use it. Otherwise, set up a default one
        var loggerFactory = appBuilder.ApplicationServices.GetService<ILoggerFactory>();
        var logger = loggerFactory != null
            ? loggerFactory.CreateLogger(logCategoryName)
            : NullLogger.Instance;
        return logger;
    }

    private static async Task<int> StartViteDevServerAsync(
        string sourcePath, string scriptName, string pkgManagerCommand, int portNumber, ILogger logger,
        DiagnosticSource diagnosticSource, CancellationToken applicationStoppingToken)
    {
        if (portNumber == default(int))
        {
            portNumber = FindAvailablePort();
        }

        logger.LogInformation("Starting vite server on port {PortNumber}...", portNumber);

        var envVars = new Dictionary<string, string>
        {
            { "PORT", portNumber.ToString(CultureInfo.InvariantCulture) },
            {
                "BROWSER", "none"
            }, // We don't want vite to open its own extra browser window pointing to the internal dev server port
        };
        var scriptRunner = new NodeScriptRunner(
            sourcePath, scriptName, null, envVars, pkgManagerCommand, diagnosticSource, applicationStoppingToken);
        scriptRunner.AttachToLogger(logger);

        using var stdErrReader = new EventedStreamStringReader(scriptRunner.StdErr);
        try
        {
            // Although the vite dev server may eventually tell us the URL it's listening on,
            // it doesn't do so until it's finished compiling, and even then only if there were
            // no compiler warnings. So instead of waiting for that, consider it ready as soon
            // as it starts listening for requests.
            await scriptRunner.StdOut.WaitForMatch(
                new Regex("ready in", RegexOptions.None, RegexMatchTimeout));
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidOperationException(
                $"The {pkgManagerCommand} script '{scriptName}' exited without indicating that the " +
                "vite server was listening for requests. The error output was: " +
                $"{stdErrReader.ReadAsString()}", ex);
        }

        return portNumber;
    }

    private static async Task<T> WithTimeout<T>(this Task<T> task, TimeSpan timeoutDelay, string message)
    {
        if (task == await Task.WhenAny(task, Task.Delay(timeoutDelay)))
        {
            return task.Result;
        }
        else
        {
            throw new TimeoutException(message);
        }
    }

    private static int FindAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }


    private
        
        sealed class NodeScriptRunner : IDisposable
{
    private Process? _npmProcess;
    public EventedStreamReader StdOut { get; }
    public EventedStreamReader StdErr { get; }

    private static readonly Regex AnsiColorRegex =
        new Regex("\x001b\\[[0-9;]*m", RegexOptions.None, TimeSpan.FromSeconds(1));

    public NodeScriptRunner(string workingDirectory, string scriptName, string? arguments,
        IDictionary<string, string>? envVars, string pkgManagerCommand, DiagnosticSource diagnosticSource,
        CancellationToken applicationStoppingToken)
    {
        if (string.IsNullOrEmpty(workingDirectory))
        {
            throw new ArgumentException("Cannot be null or empty.", nameof(workingDirectory));
        }

        if (string.IsNullOrEmpty(scriptName))
        {
            throw new ArgumentException("Cannot be null or empty.", nameof(scriptName));
        }

        if (string.IsNullOrEmpty(pkgManagerCommand))
        {
            throw new ArgumentException("Cannot be null or empty.", nameof(pkgManagerCommand));
        }

        var exeToRun = pkgManagerCommand;
        var completeArguments = $"run {scriptName} -- {arguments ?? string.Empty}";
        if (OperatingSystem.IsWindows())
        {
            // On Windows, the node executable is a .cmd file, so it can't be executed
            // directly (except with UseShellExecute=true, but that's no good, because
            // it prevents capturing stdio). So we need to invoke it via "cmd /c".
            exeToRun = "cmd";
            completeArguments = $"/c {pkgManagerCommand} {completeArguments}";
        }

        var processStartInfo = new ProcessStartInfo(exeToRun)
        {
            Arguments = completeArguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };

        if (envVars != null)
        {
            foreach (var keyValuePair in envVars)
            {
                processStartInfo.Environment[keyValuePair.Key] = keyValuePair.Value;
            }
        }

        _npmProcess = LaunchNodeProcess(processStartInfo, pkgManagerCommand);
        StdOut = new EventedStreamReader(_npmProcess.StandardOutput);
        StdErr = new EventedStreamReader(_npmProcess.StandardError);

        applicationStoppingToken.Register(((IDisposable)this).Dispose);

        if (diagnosticSource.IsEnabled("Microsoft.AspNetCore.NodeServices.Npm.NpmStarted"))
        {
            WriteDiagnosticEvent(
                diagnosticSource,
                "Microsoft.AspNetCore.NodeServices.Npm.NpmStarted",
                new
                {
                    processStartInfo,
                    process = _npmProcess
                });
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
            Justification =
                "The values being passed into Write have the commonly used properties being preserved with DynamicDependency.")]
        static void WriteDiagnosticEvent<TValue>(DiagnosticSource diagnosticSource, string name, TValue value)
            => diagnosticSource.Write(name, value);
    }

    public void AttachToLogger(ILogger logger)
    {
        // When the node task emits complete lines, pass them through to the real logger
        StdOut.OnReceivedLine += line =>
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                // Node tasks commonly emit ANSI colors, but it wouldn't make sense to forward
                // those to loggers (because a logger isn't necessarily any kind of terminal)
                logger.LogInformation(StripAnsiColors(line));
            }
        };

        StdErr.OnReceivedLine += line =>
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                logger.LogError(StripAnsiColors(line));
            }
        };

        // But when it emits incomplete lines, assume this is progress information and
        // hence just pass it through to StdOut regardless of logger config.
        StdErr.OnReceivedChunk += chunk =>
        {
            Debug.Assert(chunk.Array != null);

            var containsNewline = Array.IndexOf(
                chunk.Array, '\n', chunk.Offset, chunk.Count) >= 0;
            if (!containsNewline)
            {
                Console.Write(chunk.Array, chunk.Offset, chunk.Count);
            }
        };
    }

    private static string StripAnsiColors(string line)
        => AnsiColorRegex.Replace(line, string.Empty);

    private static Process LaunchNodeProcess(ProcessStartInfo startInfo, string commandName)
    {
        try
        {
            var process = Process.Start(startInfo)!;

            // See equivalent comment in OutOfProcessNodeInstance.cs for why
            process.EnableRaisingEvents = true;

            return process;
        }
        catch (Exception ex)
        {
            var message = $"Failed to start '{commandName}'. To resolve this:.\n\n"
                          + $"[1] Ensure that '{commandName}' is installed and can be found in one of the PATH directories.\n"
                          + $"    Current PATH environment variable is: {Environment.GetEnvironmentVariable("PATH")}\n"
                          + "    Make sure the executable is in one of those directories, or update your PATH.\n\n"
                          + "[2] See the InnerException for further details of the cause.";
            throw new InvalidOperationException(message, ex);
        }
    }

    void IDisposable.Dispose()
    {
        if (_npmProcess != null && !_npmProcess.HasExited)
        {
            _npmProcess.Kill(entireProcessTree: true);
            _npmProcess = null;
        }
    }
}

internal sealed class EventedStreamReader
{
    public delegate void OnReceivedChunkHandler(ArraySegment<char> chunk);

    public delegate void OnReceivedLineHandler(string line);

    public delegate void OnStreamClosedHandler();

    public event OnReceivedChunkHandler? OnReceivedChunk;
    public event OnReceivedLineHandler? OnReceivedLine;
    public event OnStreamClosedHandler? OnStreamClosed;

    private readonly StreamReader _streamReader;
    private readonly StringBuilder _linesBuffer;

    public EventedStreamReader(StreamReader streamReader)
    {
        _streamReader = streamReader ?? throw new ArgumentNullException(nameof(streamReader));
        _linesBuffer = new StringBuilder();
        Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
    }

    public Task<Match> WaitForMatch(Regex regex)
    {
        var tcs = new TaskCompletionSource<Match>();
        var completionLock = new object();

        OnReceivedLineHandler? onReceivedLineHandler = null;
        OnStreamClosedHandler? onStreamClosedHandler = null;

        void ResolveIfStillPending(Action applyResolution)
        {
            lock (completionLock)
            {
                if (!tcs.Task.IsCompleted)
                {
                    OnReceivedLine -= onReceivedLineHandler;
                    OnStreamClosed -= onStreamClosedHandler;
                    applyResolution();
                }
            }
        }

        onReceivedLineHandler = line =>
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                ResolveIfStillPending(() => tcs.SetResult(match));
            }
        };

        onStreamClosedHandler = () => { ResolveIfStillPending(() => tcs.SetException(new EndOfStreamException())); };

        OnReceivedLine += onReceivedLineHandler;
        OnStreamClosed += onStreamClosedHandler;

        return tcs.Task;
    }

    private async Task Run()
    {
        var buf = new char[8 * 1024];
        while (true)
        {
            var chunkLength = await _streamReader.ReadAsync(buf, 0, buf.Length);
            if (chunkLength == 0)
            {
                if (_linesBuffer.Length > 0)
                {
                    OnCompleteLine(_linesBuffer.ToString());
                    _linesBuffer.Clear();
                }

                OnClosed();
                break;
            }

            OnChunk(new ArraySegment<char>(buf, 0, chunkLength));

            int lineBreakPos;
            var startPos = 0;

            // get all the newlines
            while ((lineBreakPos = Array.IndexOf(buf, '\n', startPos, chunkLength - startPos)) >= 0 &&
                   startPos < chunkLength)
            {
                var length = (lineBreakPos + 1) - startPos;
                _linesBuffer.Append(buf, startPos, length);
                OnCompleteLine(_linesBuffer.ToString());
                _linesBuffer.Clear();
                startPos = lineBreakPos + 1;
            }

            // get the rest
            if (lineBreakPos < 0 && startPos < chunkLength)
            {
                _linesBuffer.Append(buf, startPos, chunkLength - startPos);
            }
        }
    }

    private void OnChunk(ArraySegment<char> chunk)
    {
        var dlg = OnReceivedChunk;
        dlg?.Invoke(chunk);
    }

    private void OnCompleteLine(string line)
    {
        var dlg = OnReceivedLine;
        dlg?.Invoke(line);
    }

    private void OnClosed()
    {
        var dlg = OnStreamClosed;
        dlg?.Invoke();
    }
}

private sealed class EventedStreamStringReader : IDisposable
{
    private readonly EventedStreamReader _eventedStreamReader;
    private bool _isDisposed;
    private readonly StringBuilder _stringBuilder = new StringBuilder();

    public EventedStreamStringReader(EventedStreamReader eventedStreamReader)
    {
        _eventedStreamReader = eventedStreamReader
                               ?? throw new ArgumentNullException(nameof(eventedStreamReader));
        _eventedStreamReader.OnReceivedLine += OnReceivedLine;
    }

    public string ReadAsString() => _stringBuilder.ToString();

    private void OnReceivedLine(string line) => _stringBuilder.AppendLine(line);

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _eventedStreamReader.OnReceivedLine -= OnReceivedLine;
            _isDisposed = true;
        }
    }
}
}
