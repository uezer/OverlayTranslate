using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Text;

namespace OverlayTranslate.Services;

public sealed class SidecarManager : IDisposable
{
    private static readonly TimeSpan PythonProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SidecarReadyTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _httpClient;
    private readonly StringBuilder _outputBuffer = new();
    private Process? _process;
    private Uri _baseUri = new("http://127.0.0.1:0/"); // placeholder; updated when sidecar starts

    /// <summary>The actual base URI of the running sidecar (valid only after EnsureAvailableAsync succeeds).</summary>
    public Uri BaseUri => _baseUri;

    public SidecarManager()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        AppLogger.Info("SidecarManager.EnsureAvailableAsync entered.");

        // If process is still alive and the endpoint responds, we're done.
        if (_process is { HasExited: false } && await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
        {
            AppLogger.Info("Local sidecar already healthy.");
            return;
        }

        await ValidatePythonEnvironmentAsync(cancellationToken).ConfigureAwait(false);

        // Stop any previously tracked (now unhealthy) process.
        StopProcess();

        await StartAndWaitForReadyAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        AppLogger.Info("SidecarManager dispose.");
        _httpClient.Dispose();
        StopProcess();
    }

    /// <summary>
    /// Starts the sidecar with port=0 (OS picks a free port), then reads the
    /// "LISTENING:{port}" line from stdout to learn the actual port.
    /// </summary>
    private async Task StartAndWaitForReadyAsync(CancellationToken cancellationToken)
    {
        string baseDirectory = AppContext.BaseDirectory;
        string scriptPath = Path.Combine(baseDirectory, "Sidecar", "translator_sidecar.py");
        if (!File.Exists(scriptPath))
        {
            AppLogger.Error($"Translator sidecar script was not found at {scriptPath}.");
            throw new FileNotFoundException("Translator sidecar script was not found.", scriptPath);
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = "python",
            Arguments = $"\"{scriptPath}\" --host 127.0.0.1 --port 0",
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? baseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _outputBuffer.Clear();
        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the local translation sidecar.");

        // Capture stderr asynchronously for diagnostics.
        _process.ErrorDataReceived += (_, args) => AppendProcessOutput("stderr", args.Data);
        _process.BeginErrorReadLine();

        AppLogger.Info($"Sidecar process started. PID={_process.Id}, Script={scriptPath}. Waiting for LISTENING signal…");

        // Read the first stdout line ("LISTENING:{port}") with a timeout.
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(SidecarReadyTimeout);

        string? readyLine;
        try
        {
            readyLine = await _process.StandardOutput.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            string buffered = GetBufferedOutput();
            AppLogger.Warn($"Sidecar did not send LISTENING signal within {SidecarReadyTimeout.TotalSeconds}s. Stderr: {buffered}");
            StopProcess();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(buffered)
                    ? "本地翻译 Sidecar 启动超时，未收到就绪信号。"
                    : $"本地翻译 Sidecar 启动超时。输出：{buffered}");
        }

        const string listeningPrefix = "LISTENING:";
        if (readyLine is null || !readyLine.StartsWith(listeningPrefix, StringComparison.Ordinal))
        {
            string buffered = GetBufferedOutput();
            StopProcess();
            throw new InvalidOperationException($"Sidecar startup failed. Unexpected output: '{readyLine}'. Stderr: {buffered}");
        }

        if (!int.TryParse(readyLine.AsSpan(listeningPrefix.Length), out int port) || port <= 0)
        {
            StopProcess();
            throw new InvalidOperationException($"Sidecar reported an invalid port in: '{readyLine}'");
        }

        _baseUri = new Uri($"http://127.0.0.1:{port}/");
        AppLogger.Info($"Sidecar is ready. Port={port}, URI={_baseUri}.");

        // Continue capturing stdout using the same line-reader mode to avoid mixing APIs.
        _ = DrainStdoutAsync(_process, cancellationToken);
    }

    private async Task DrainStdoutAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                AppendProcessOutput("stdout", line);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
            // Process stream may be closed while shutting down.
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed while draining sidecar stdout: {exception.Message}");
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            HealthResponse? payload = await _httpClient.GetFromJsonAsync<HealthResponse>(
                new Uri(_baseUri, "health"), timeoutCts.Token).ConfigureAwait(false);
            return payload?.Status?.Equals("ok", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception exception)
        {
            AppLogger.Info($"Sidecar health check: {exception.Message}");
            return false;
        }
    }

    private async Task ValidatePythonEnvironmentAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "python",
            Arguments = "-c \"import argostranslate; print('ARGOS_OK')\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start Python to validate the translation environment.");

        using CancellationTokenRegistration _ = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task waitTask = process.WaitForExitAsync(cancellationToken);
        Task timeoutTask = Task.Delay(PythonProbeTimeout, cancellationToken);
        Task completedTask = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false);
        if (completedTask == timeoutTask)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            AppLogger.Warn("Python environment validation timed out.");
            throw new InvalidOperationException("Python 翻译环境检测超时。");
        }

        string stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        string stderr = (await stderrTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            string reason = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            AppLogger.Warn($"Python environment validation failed. ExitCode={process.ExitCode}, Output={reason}");
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(reason)
                    ? "本地翻译环境不可用：未能加载 argostranslate。"
                    : $"本地翻译环境不可用：{reason}");
        }

        AppLogger.Info($"Python environment validation passed. Output={stdout}");
    }

    private void AppendProcessOutput(string streamName, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        lock (_outputBuffer)
        {
            if (_outputBuffer.Length > 4000)
            {
                _outputBuffer.Remove(0, _outputBuffer.Length - 4000);
            }

            _outputBuffer.Append('[');
            _outputBuffer.Append(streamName);
            _outputBuffer.Append("] ");
            _outputBuffer.AppendLine(data);
        }

        AppLogger.Info($"Sidecar {streamName}: {data}");
    }

    private string GetBufferedOutput()
    {
        lock (_outputBuffer)
        {
            return _outputBuffer.ToString().Trim();
        }
    }

    private void StopProcess()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Failed to stop sidecar process cleanly: {exception.Message}");
        }
        finally
        {
            try
            {
                _process.Dispose();
            }
            catch
            {
            }

            _process = null;
        }
    }

    private sealed class HealthResponse
    {
        public string? Status { get; set; }
    }
}
