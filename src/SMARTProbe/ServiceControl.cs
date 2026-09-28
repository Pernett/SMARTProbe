// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace SmartProbe;

/// <summary>
/// Registers the probe as a Windows service.
///
/// The point of this is privilege, not convenience. Reading SMART needs a raw disk handle,
/// which is Administrators-only; nothing can be marked on the executable to grant that. A
/// service runs as LocalSystem and is not subject to UAC at all, so installing once (elevated)
/// buys a probe that answers forever without another prompt.
///
/// Shells out to sc.exe rather than P/Invoking CreateService: it keeps the privileged surface to
/// a documented, auditable tool, and every step can be reproduced by hand.
/// </summary>
public static class ServiceControl
{
    public const string DefaultServiceName = "SMARTProbe";
    private const string DisplayName = "SMARTProbe";
    private const string Description =
        "Answers privileged local hardware questions (drive SMART/temperature) over a loopback HTTP API. Stateless.";

    /// <summary>
    /// Creates the service with the port/bind baked into its binPath, sets crash recovery,
    /// and starts it. The arguments are fixed at install time on purpose — a caller can never
    /// talk this service into running something else.
    /// </summary>
    public static int Install(string serviceName, int port, string bind)
    {
        if (!DriveSmartReader.IsElevated())
        {
            Console.Error.WriteLine(
                "--install needs an elevated prompt (creating a service is an administrator action).");
            return 5;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.Error.WriteLine("Could not determine this executable's path.");
            return 1;
        }

        // sc.exe wants "binPath=" and its value as separate tokens — hence the trailing space
        // convention. The inner quotes protect an exe path containing spaces.
        var binPath = $"\"{exePath}\" --port {port} --bind {bind}";

        var create = Sc("create", serviceName,
            "binPath=", binPath,
            "start=", "auto",
            "obj=", "LocalSystem",
            "DisplayName=", DisplayName);

        if (create != 0)
        {
            Console.Error.WriteLine($"sc create failed ({create}). Is '{serviceName}' already installed?");
            return create;
        }

        Sc("description", serviceName, Description);

        // Restart on crash: three attempts a minute apart, counter resets daily.
        Sc("failure", serviceName, "reset=", "86400",
            "actions=", "restart/60000/restart/60000/restart/60000");

        var start = Sc("start", serviceName);
        if (start != 0)
        {
            Console.Error.WriteLine($"Service installed but did not start ({start}).");
            return start;
        }

        var host = string.Equals(bind, "any", StringComparison.OrdinalIgnoreCase) ? "0.0.0.0" : "127.0.0.1";
        Console.WriteLine($"Installed and started '{serviceName}' on http://{host}:{port} (LocalSystem).");
        return 0;
    }

    /// <summary>Stops and removes the service. A missing service is reported, not thrown.</summary>
    public static int Uninstall(string serviceName)
    {
        if (!DriveSmartReader.IsElevated())
        {
            Console.Error.WriteLine(
                "--uninstall needs an elevated prompt (removing a service is an administrator action).");
            return 5;
        }

        // Stop is best-effort: an already-stopped service returns non-zero and that is fine.
        Sc("stop", serviceName);

        var delete = Sc("delete", serviceName);
        if (delete != 0)
        {
            Console.Error.WriteLine($"sc delete failed ({delete}). Is '{serviceName}' installed?");
            return delete;
        }

        Console.WriteLine($"Removed '{serviceName}'.");
        return 0;
    }

    private static int Sc(params string[] arguments)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            return 1;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
        {
            Console.Error.Write(error);
        }
        else if (!string.IsNullOrWhiteSpace(output) && arguments[0] is "create" or "delete")
        {
            Console.Write(output);
        }

        return process.ExitCode;
    }
}
