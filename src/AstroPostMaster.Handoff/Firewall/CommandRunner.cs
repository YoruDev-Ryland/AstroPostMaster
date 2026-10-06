using System.ComponentModel;
using System.Diagnostics;

namespace AstroPostMaster.Handoff.Firewall;

public interface ICommandRunner
{
    /// <summary>Runs a program and captures its output. Returns null when the program doesn't exist.</summary>
    Task<CommandResult?> RunAsync(string fileName, IReadOnlyList<string> args, bool elevated = false, CancellationToken cancellationToken = default);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED: the user declined the UAC prompt

    public async Task<CommandResult?> RunAsync(string fileName, IReadOnlyList<string> args, bool elevated = false, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo(fileName) { CreateNoWindow = true };
        if (elevated)
        {
            // Elevation needs the shell; output can't be captured, only the exit code.
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.Arguments = string.Join(' ', args.Select(Quote));
        }
        else
        {
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            foreach (var arg in args) psi.ArgumentList.Add(arg);
        }

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return new CommandResult(-1, "", "The administrator prompt was declined.", Cancelled: true);
        }
        catch (Win32Exception)
        {
            return null; // not installed
        }
        if (process is null) return null;

        using (process)
        {
            var stdout = elevated ? Task.FromResult("") : process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = elevated ? Task.FromResult("") : process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new CommandResult(process.ExitCode, await stdout, await stderr);
        }
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && arg.IndexOfAny([' ', '"', '\t']) < 0 ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";
}
