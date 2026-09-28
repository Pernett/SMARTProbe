// SPDX-License-Identifier: MIT

using System.Globalization;

namespace SmartProbe;

/// <summary>Everything the command line can say.</summary>
internal sealed record ProbeOptions
{
    public int Port { get; init; } = CommandLine.DefaultPort;
    public string Bind { get; init; } = "loopback";
    public bool Once { get; init; }
    public bool Install { get; init; }
    public bool Uninstall { get; init; }
    public string ServiceName { get; init; } = ServiceControl.DefaultServiceName;
    public bool Help { get; init; }
    public bool Version { get; init; }
}

/// <summary>
/// Hand-rolled parser for six flags. Accepts <c>--name value</c>, <c>--name=value</c> and the
/// short forms; rejects anything it does not know rather than guessing.
/// </summary>
internal static class CommandLine
{
    public const int DefaultPort = 5199;

    public const string Usage = """
        SMARTProbe — a stateless, privileged local probe for drive SMART and temperature.

        Usage:
          SMARTProbe [options]

        Options:
          -p, --port <port>              TCP port for the web service. [default: 5199]
          -b, --bind <loopback|any>      Interface to bind: 'loopback' (default, local callers only) or 'any'.
          -1, --once                     Print one SMART report as JSON to stdout and exit.
          --install                      Install as a LocalSystem Windows service and start it. Requires elevation.
          --uninstall                    Stop and remove the Windows service. Requires elevation.
          --service-name <name>          Service name to install or remove. [default: SMARTProbe]
          -h, --help                     Show this help.
          --version                      Show the version.
        """;

    public static bool TryParse(string[] args, out ProbeOptions options, out string? error)
    {
        options = new ProbeOptions();
        error = null;

        int port = DefaultPort;
        string bind = "loopback", serviceName = ServiceControl.DefaultServiceName;
        bool once = false, install = false, uninstall = false, help = false, version = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string name = arg, inlineValue = null!;
            var hasInline = false;

            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (arg.StartsWith("--", StringComparison.Ordinal) && eq > 0)
            {
                name = arg[..eq];
                inlineValue = arg[(eq + 1)..];
                hasInline = true;
            }

            switch (name)
            {
                case "--port" or "-p":
                    if (!TakeValue(args, ref i, hasInline, inlineValue, name, out var portText, out error)
                        || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                        || port is < 1 or > 65535)
                    {
                        error ??= $"'{portText}' is not a valid port (1-65535).";
                        return false;
                    }
                    break;

                case "--bind" or "-b":
                    if (!TakeValue(args, ref i, hasInline, inlineValue, name, out bind!, out error))
                    {
                        return false;
                    }
                    bind = bind.ToLowerInvariant();
                    if (bind is not ("loopback" or "any"))
                    {
                        error = $"--bind must be 'loopback' or 'any', not '{bind}'.";
                        return false;
                    }
                    break;

                case "--service-name":
                    if (!TakeValue(args, ref i, hasInline, inlineValue, name, out serviceName!, out error))
                    {
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(serviceName))
                    {
                        error = "--service-name cannot be empty.";
                        return false;
                    }
                    break;

                case "--once" or "-1": once = true; break;
                case "--install": install = true; break;
                case "--uninstall": uninstall = true; break;
                case "--help" or "-h" or "-?" or "/?": help = true; break;
                case "--version": version = true; break;

                default:
                    error = $"Unknown option '{arg}'.";
                    return false;
            }

            if (hasInline && name is "--once" or "--install" or "--uninstall" or "--help" or "--version")
            {
                error = $"'{name}' does not take a value.";
                return false;
            }
        }

        if (install && uninstall)
        {
            error = "--install and --uninstall are mutually exclusive.";
            return false;
        }

        options = new ProbeOptions
        {
            Port = port,
            Bind = bind,
            Once = once,
            Install = install,
            Uninstall = uninstall,
            ServiceName = serviceName,
            Help = help,
            Version = version
        };
        return true;
    }

    private static bool TakeValue(string[] args, ref int i, bool hasInline, string inlineValue, string name,
        out string value, out string? error)
    {
        error = null;
        if (hasInline)
        {
            value = inlineValue;
            return true;
        }

        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            value = args[++i];
            return true;
        }

        value = string.Empty;
        error = $"'{name}' requires a value.";
        return false;
    }
}
