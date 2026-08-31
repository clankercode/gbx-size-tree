using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GbxSizeTree.Abstractions;
using GbxSizeTree.Model;
using GbxSizeTree.Model.Json;

namespace GbxSizeTree.Cli.Output;

/// <summary>
/// Writes the stable command-line JSON envelope around the map model described in
/// docs/FORMAT-NOTES.md, using only the source-generated model metadata.
/// </summary>
public static class JsonReportWriter
{
    private const string AbsoluteSourceLabelPattern =
        "\"(?<key>sourceLabel|SourceLabel)\"(?<separator>\\s*:\\s*)\"(?<path>(?:[A-Za-z]:[\\\\/]|/|(?:\\\\){4})[^\"]*)\"";

    public static void Write(
        TextWriter stdout,
        MapAnalysis analysis,
        RecommendationReport? recommendations,
        Model.OptimizationSummary? optimization = null)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(analysis);

        WriteJson(stdout, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WritePropertyName("analysis");
            JsonSerializer.Serialize(writer, analysis, AnalysisJsonContext.Default.MapAnalysis);

            if (recommendations is not null)
            {
                writer.WritePropertyName("recommendations");
                JsonSerializer.Serialize(
                    writer,
                    recommendations,
                    AnalysisJsonContext.Default.RecommendationReport);
            }

            if (optimization is not null)
            {
                writer.WritePropertyName("optimization");
                JsonSerializer.Serialize(
                    writer,
                    optimization,
                    AnalysisJsonContext.Default.OptimizationSummary);
            }

            writer.WriteEndObject();
        });
    }

    public static void WriteUnknownChunks(TextWriter stdout, Model.UnknownChunksReport report)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(report);

        WriteJson(stdout, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WritePropertyName("unknownChunksReport");
            JsonSerializer.Serialize(writer, report, AnalysisJsonContext.Default.UnknownChunksReport);
            writer.WriteEndObject();
        });
    }

    public static void WriteError(TextWriter stdout, int code, string message)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(message);

        WriteJson(
            stdout,
            writer => JsonSerializer.Serialize(
                writer,
                new JsonErrorEnvelope(new JsonError(code, message)),
                AnalysisJsonContext.Default.JsonErrorEnvelope));
    }

    public static string Normalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var normalizedNewlines = json.Replace("\r\n", "\n", StringComparison.Ordinal);
        return Regex.Replace(
            normalizedNewlines,
            AbsoluteSourceLabelPattern,
            match => $"\"{match.Groups["key"].Value}\"{match.Groups["separator"].Value}\"<path>\"",
            RegexOptions.CultureInvariant);
    }

    private static void WriteJson(TextWriter stdout, Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            write(writer);
        }

        stdout.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        stdout.WriteLine();
    }
}
