// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting.WindowsServices;
using SmartProbe;

// The probe holds no state and owns no storage. It exists so a caller that lacks privilege can ask
// a privileged process a question. Scheduling, history and persistence belong to whoever calls it —
// losing the probe costs nothing but the answer.

if (!CommandLine.TryParse(args, out var options, out var error))
{
    await Console.Error.WriteLineAsync($"SMARTProbe: {error}");
    await Console.Error.WriteLineAsync("Run 'SMARTProbe --help' for usage.");
    return 1;
}

if (options.Help)
{
    Console.WriteLine(CommandLine.Usage);
    return 0;
}

if (options.Version)
{
    Console.WriteLine(Program.Version);
    return 0;
}

if (options.Install)
{
    return ServiceControl.Install(options.ServiceName, options.Port, options.Bind);
}

if (options.Uninstall)
{
    return ServiceControl.Uninstall(options.ServiceName);
}

// No self-elevation. A UAC re-launch would run in a new console whose output the caller never
// sees, which breaks `--once | jq` and hides every error. Instead the report says honestly
// which readings needed privilege, and the service install path exists for the long-lived case.
if (!DriveSmartReader.IsElevated())
{
    await Console.Error.WriteLineAsync(
        "warning: not running elevated; SMART counters will be reported as unavailable.");
}

if (options.Once)
{
    Console.WriteLine(JsonSerializer.Serialize(DriveSmartReader.Read(), ProbeJsonContext.Default.SmartReport));
    return 0;
}

// Single instance per port: a second start on the same port is a no-op that defers to the one
// already answering. Global\ so it holds across sessions and elevation levels — which is the
// whole point when the service owns it and someone launches a second copy by hand. Scoped by
// port so a developer can run a copy on another port beside the installed service.
using var mutex = new Mutex(true, $@"Global\SMARTProbe:{options.Port}", out var acquired);
if (!acquired)
{
    Console.WriteLine($"SMARTProbe is already running on port {options.Port}; deferring to it.");
    return 0;
}

await RunServiceAsync(options.Port, options.Bind);
return 0;

static async Task RunServiceAsync(int port, string bind)
{
    // Slim builder: Kestrel over HTTP with nothing the probe does not use (no IIS, no HTTPS
    // defaults, no static files). It is also the AOT-native path.
    var builder = WebApplication.CreateSlimBuilder();

    // Lets the same binary run interactively or under the SCM without a separate entry point.
    builder.Host.UseWindowsService();

    // Loopback by default: the probe hands out hardware detail, so exposing it to the network is
    // an explicit choice, never a default.
    var address = string.Equals(bind, "any", StringComparison.OrdinalIgnoreCase)
        ? $"http://*:{port}"
        : $"http://127.0.0.1:{port}";

    builder.WebHost.UseUrls(address);
    builder.Logging.ClearProviders();

    if (WindowsServiceHelpers.IsWindowsService())
    {
        builder.Logging.AddEventLog();
    }
    else
    {
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
    }

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        // A developer opening the bare origin gets a page to exercise the API from.
        app.MapGet("/", () => Results.Content(DevPage.Html, "text/html; charset=utf-8"));
    }
    else
    {
        // Anyone else hitting the bare origin gets a map of what is here, not a blank 404.
        app.MapGet("/", () => Results.Json(
            new IndexReport("SMARTProbe", Program.Version, Program.Endpoints),
            ProbeJsonContext.Default.IndexReport));
    }

    app.MapGet("/probe/health", () => Results.Json(
        new HealthReport(
            Status: "ok",
            Machine: Environment.MachineName,
            Elevated: DriveSmartReader.IsElevated(),
            Service: WindowsServiceHelpers.IsWindowsService(),
            Version: Program.Version,
            TimestampUtc: DateTime.UtcNow),
        ProbeJsonContext.Default.HealthReport));

    app.MapGet("/probe/smart", () => Results.Json(DriveSmartReader.Read(), ProbeJsonContext.Default.SmartReport));

    app.Logger.LogInformation("SMARTProbe {Version} listening on {Address} (elevated: {Elevated}, service: {Service})",
        Program.Version, address, DriveSmartReader.IsElevated(), WindowsServiceHelpers.IsWindowsService());

    await app.RunAsync();
}

public partial class Program
{
    /// <summary>The routes the probe answers on, for the index served at "/".</summary>
    internal static readonly string[] Endpoints = ["/probe/health", "/probe/smart"];

    /// <summary>Informational version as stamped by the build (e.g. "1.2.3+abc1234").</summary>
    internal static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(Program).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
