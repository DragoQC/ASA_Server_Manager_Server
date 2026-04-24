using asa_server_node_api.Constants;

namespace asa_server_node_api.Services;

public sealed class ClusterClientInstallService
{
    public bool IsInstalling { get; private set; }

    public bool HasWireGuardClientInstall()
    {
        return File.Exists("/usr/bin/wg") &&
               File.Exists("/usr/bin/wg-quick");
    }

    public bool HasNfsClientInstall()
    {
        return (File.Exists("/sbin/mount.nfs") || File.Exists("/usr/sbin/mount.nfs"))
               && IsPackageFullyInstalled("nfs-common")
               && IsPackageFullyInstalled("rpcbind");
    }

    public bool HasClusterClientInstall()
    {
        return HasWireGuardClientInstall() && HasNfsClientInstall();
    }

    public async Task<string> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalling)
        {
            throw new InvalidOperationException("Cluster client install is already running.");
        }

        IsInstalling = true;

        try
        {
            await RunProcessAsync(
                SystemCommandConstants.SudoPath,
                ["-n", InstallStateConstants.PrepareClusterClientScriptPath],
                cancellationToken);

            return "Installed cluster client tools. This node is ready to receive WireGuard and NFS configuration.";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private static bool IsPackageFullyInstalled(string packageName)
    {
        try
        {
            using System.Diagnostics.Process process = new()
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/dpkg-query",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("-W");
            process.StartInfo.ArgumentList.Add("--showformat=${db:Status-Abbrev}");
            process.StartInfo.ArgumentList.Add(packageName);

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            return string.Equals(output, "ii ", StringComparison.Ordinal)
                || string.Equals(output, "ii", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Command failed." : error.Trim());
            }
        }
        finally
        {
            process.Dispose();
        }
    }
}
