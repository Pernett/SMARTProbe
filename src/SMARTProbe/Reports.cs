// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace SmartProbe;

/// <summary>Answer to <c>GET /probe/health</c>.</summary>
public sealed record HealthReport(
    string Status,
    string Machine,
    bool Elevated,
    bool Service,
    string Version,
    DateTime TimestampUtc);

/// <summary>Answer to <c>GET /</c> outside Development: what is here.</summary>
public sealed record IndexReport(string Name, string Version, IReadOnlyList<string> Endpoints);

/// <summary>
/// Source-generated JSON contracts — the payload is read by both C# and eyeballs, so camelCase,
/// nulls omitted, indented. Reflection-free, which Native AOT requires.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(SmartReport))]
[JsonSerializable(typeof(HealthReport))]
[JsonSerializable(typeof(IndexReport))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
