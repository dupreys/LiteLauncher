using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace LiteLauncher.App;

/// <summary>Runs <c>netsh.exe</c> without a console window and returns its text output.</summary>
internal static class NetshRunner
{
    private static readonly Encoding OutputEncoding = PickEncoding();

    public static async Task<(int ExitCode, string Output)> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("netsh.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = OutputEncoding,
                    StandardErrorEncoding = OutputEncoding,
                },
            };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return (process.ExitCode, output);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return (-1, "");
        }
    }

    /// <summary>netsh writes in the console (OEM) code page; without this, accented network names come out garbled.</summary>
    private static Encoding PickEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }
}
