using System.Text.Json.Serialization;
using GbxSizeTree.Abstractions;

namespace GbxSizeTree.Model.Json;

/// <summary>
/// Source-generated JSON (trim/AOT-safe, no reflection). MapAnalysis is the --json root;
/// key names and SizeNode ids are frozen contract (docs/CONTRACTS.md).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MapAnalysis))]
[JsonSerializable(typeof(RecommendationReport))]
[JsonSerializable(typeof(JsonErrorEnvelope))]
public sealed partial class AnalysisJsonContext : JsonSerializerContext;

/// <summary>Emitted on stdout in --json mode when the run fails, so scripts always get JSON.</summary>
public sealed record JsonErrorEnvelope(JsonError Error);

public sealed record JsonError(int Code, string Message);
