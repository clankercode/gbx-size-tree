using System.IO.Compression;
using GBX.NET.Serialization;
using GbxSizeTree.Measure;

namespace GbxSizeTree.Tests.Measure;

public sealed class EmbeddedFileContributionProgressTests
{
    [Fact]
    public void Measure_ReportsEachBoundedTrialBeforeAndAfter()
    {
        var file = BuildFile(BuildZip(("a", new byte[32]), ("b", new byte[48])));
        var seen = new List<EmbeddedFileContributionProgress>();
        var result = new EmbeddedFileContributionMeasurer(body => body.LongLength).Measure(
            file, ["a", "missing", "b"], new() { MaxTrials = 1 },
            TestContext.Current.CancellationToken, seen.Add);

        Assert.Equal(
        [
            new("a", 0, 1),
            new("a", 1, 1),
        ], seen);
        Assert.NotNull(result.Entries[0].MarginalCompressedBodyBytes);
        Assert.Contains("present", result.Entries[1].UnavailableReason!);
        Assert.Contains("budget", result.Entries[2].UnavailableReason!);
    }

    [Fact]
    public void Measure_ObserverFailurePropagatesBeforeTrial()
    {
        var file = BuildFile(BuildZip(("asset", new byte[16])));
        var calls = 0;
        var measurer = new EmbeddedFileContributionMeasurer(body => { calls++; return body.LongLength; });

        var error = Assert.Throws<InvalidOperationException>(() => measurer.Measure(
            file, ["asset"], cancellationToken: TestContext.Current.CancellationToken,
            progress: _ => throw new InvalidOperationException("observer failed")));

        Assert.Equal("observer failed", error.Message);
        Assert.Equal(0, calls);
    }

    private static byte[] BuildZip(params (string Path, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var output = archive.CreateEntry(entry.Path, CompressionLevel.NoCompression).Open();
                output.Write(entry.Bytes);
            }
        }
        return stream.ToArray();
    }

    private static byte[] BuildFile(byte[] zip)
    {
        using var payload = new MemoryStream();
        using (var writer = new GbxWriter(payload))
        {
            writer.Write(1);
            writer.WriteEncapsulated(inner =>
            {
                inner.WriteList(new List<GBX.NET.Ident>());
                inner.WriteData(zip);
                inner.WriteList(new List<string>());
            });
        }
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x03043011U);
            writer.Write(0x534B4950U);
            writer.Write(8);
            writer.Write(new byte[8]);
            writer.Write(0x03043054U);
            writer.Write(0x534B4950U);
            writer.Write((int)payload.Length);
            writer.Write(payload.ToArray());
            writer.Write(0x03043012U);
            writer.Write(0x534B4950U);
            writer.Write(8);
            writer.Write(new byte[8]);
            writer.Write(0xFACADE01U);
        }
        return WrapBody(body.ToArray());
    }

    private static byte[] WrapBody(byte[] body)
    {
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write("GBX"u8);
        writer.Write((short)6);
        writer.Write("BUUR"u8);
        writer.Write(0x03043000U);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(body);
        return file.ToArray();
    }
}
