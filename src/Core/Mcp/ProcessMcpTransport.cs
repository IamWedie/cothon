using System.Text;
using System.Diagnostics;

namespace Cothon.Core.Mcp;

/// <summary>
/// Speaks line-JSON over the stdio of one spawned MCP server process.
/// The process dies with us; StopAsync closes stdin and, after a brief
/// grace, kills it so no zombie lingers.
/// </summary>
public sealed class ProcessMcpTransport(ProcessStartInfo psi) : IMcpTransport
{
    private Process? _process;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public ProcessMcpTransport(string command, string args)
        : this(BuildPsi(command, args)) { }

    private static ProcessStartInfo BuildPsi(string command, string args)
        => new(command, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

    private Process Start()
    {
        var p = Process.Start(psi)
            ?? throw new IOException($"MCP server process for '{psi.FileName}' could not start.");
        p.ErrorDataReceived += (_, e) => { /* server stderr is its own log; never parsed */ };
        return p;
    }

    public Task StartAsync()
    {
        _process = Start();
        _process.BeginErrorReadLine();
        _reader = _process.StandardOutput;
        _writer = _process.StandardInput;
        return Task.CompletedTask;
    }

    public void Send(string line) => _writer?.WriteLine(line);

    public async Task<string?> ReadLineAsync()
    {
        if (_reader is null)
            return null;
        return await _reader.ReadLineAsync().ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        try { _writer?.Dispose(); } catch { }
        if (_process is { HasExited: false })
        {
            if (!_process.WaitForExit(2000))
            {
                try { _process.Kill(true); } catch { }
            }
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
