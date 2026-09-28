// SPDX-License-Identifier: MIT

namespace SmartProbe.Tests;

public class CommandLineTests
{
    private static ProbeOptions Parse(params string[] args)
    {
        Assert.True(CommandLine.TryParse(args, out var options, out var error), error);
        return options;
    }

    private static string Reject(params string[] args)
    {
        Assert.False(CommandLine.TryParse(args, out _, out var error));
        Assert.NotNull(error);
        return error;
    }

    [Fact]
    public void No_arguments_gives_defaults()
    {
        var o = Parse();

        Assert.Equal(5199, o.Port);
        Assert.Equal("loopback", o.Bind);
        Assert.Equal("SMARTProbe", o.ServiceName);
        Assert.False(o.Once);
        Assert.False(o.Install);
        Assert.False(o.Uninstall);
        Assert.False(o.Help);
        Assert.False(o.Version);
    }

    [Theory]
    [InlineData("--port", "8080")]
    [InlineData("-p", "8080")]
    [InlineData("--port=8080")]
    public void Port_accepts_every_spelling(params string[] args) =>
        Assert.Equal(8080, Parse(args).Port);

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("80.0")]
    public void Port_rejects_out_of_range_and_non_numeric(string port) =>
        Assert.Contains("valid port", Reject("--port", port));

    [Fact]
    public void Negative_port_reads_as_a_missing_value()
    {
        // "-5" looks like a flag, so the parser reports the missing value rather than guessing.
        Assert.Contains("requires a value", Reject("--port", "-5"));
        Assert.Contains("valid port", Reject("--port=-5"));
    }

    [Fact]
    public void Bind_is_case_insensitive_and_restricted()
    {
        Assert.Equal("any", Parse("--bind", "ANY").Bind);
        Assert.Equal("loopback", Parse("-b", "Loopback").Bind);
        Assert.Contains("--bind must be", Reject("--bind", "0.0.0.0"));
    }

    [Fact]
    public void Flags_and_short_forms()
    {
        Assert.True(Parse("--once").Once);
        Assert.True(Parse("-1").Once);
        Assert.True(Parse("--install").Install);
        Assert.True(Parse("--uninstall").Uninstall);
        Assert.True(Parse("--help").Help);
        Assert.True(Parse("-h").Help);
        Assert.True(Parse("-?").Help);
        Assert.True(Parse("--version").Version);
    }

    [Fact]
    public void Install_and_uninstall_are_mutually_exclusive() =>
        Assert.Contains("mutually exclusive", Reject("--install", "--uninstall"));

    [Fact]
    public void Service_name_is_carried_through()
    {
        Assert.Equal("Probe2", Parse("--service-name", "Probe2").ServiceName);
        Assert.Equal("Probe2", Parse("--service-name=Probe2").ServiceName);
        Assert.Contains("cannot be empty", Reject("--service-name="));
    }

    [Fact]
    public void Missing_value_is_an_error_not_a_swallowed_flag()
    {
        Assert.Contains("requires a value", Reject("--port"));
        Assert.Contains("requires a value", Reject("--port", "--once"));
    }

    [Fact]
    public void Unknown_option_is_rejected() =>
        Assert.Contains("Unknown option", Reject("--verbose"));

    [Fact]
    public void Boolean_flags_do_not_take_values() =>
        Assert.Contains("does not take a value", Reject("--once=true"));

    [Fact]
    public void Install_arguments_compose()
    {
        var o = Parse("--install", "--port", "6000", "--bind", "any", "--service-name", "Probe6000");

        Assert.True(o.Install);
        Assert.Equal(6000, o.Port);
        Assert.Equal("any", o.Bind);
        Assert.Equal("Probe6000", o.ServiceName);
    }
}
