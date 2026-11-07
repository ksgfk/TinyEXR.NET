using System.Globalization;
using System.Text.Json;

namespace TinyEXR.Benchmark;

public sealed record ComparisonRow(string Implementation, string Operation, string Compression,
    string Status, double? MeanMilliseconds, long EncodedBytes, double? AllocatedBytes, string Reason = "");

public static class ComparisonReport
{
    public static IEnumerable<(string Implementation, string Operation, string Compression)> Keys =>
        from implementation in new[] { "TinyEXR.NET", "TinyEXR", "OpenEXR" }
        from compression in Enum.GetValues<Compression>().Where(c => (int)c <= 11)
        where implementation == "OpenEXR" || FileComparisonData.SharedCompressions.Contains(compression)
        from operation in new[] { "Encode", "Decode" }
        select (implementation, operation, compression.ToString());

    public static bool IsUnsupported(string implementation, string operation, string compression) =>
        implementation == "TinyEXR" && operation == "Decode" && compression is "HTJ2K32" or "HTJ2K256";

    public static void Validate(IReadOnlyList<ComparisonRow> rows)
    {
        var expected = Keys.ToHashSet();
        foreach (var row in rows)
        {
            if (!expected.Remove((row.Implementation, row.Operation, row.Compression)))
                throw new InvalidDataException("Duplicate or unexpected result: " + row);
            bool unsupported = IsUnsupported(row.Implementation, row.Operation, row.Compression);
            if (row.EncodedBytes <= 0 ||
                (unsupported ? row.Status != "Unsupported" || row.MeanMilliseconds != null ||
                               !row.Reason.StartsWith("Unsupported: TinyEXR EXR_ERROR_UNSUPPORTED (-3)", StringComparison.Ordinal)
                             : row.Status != "Measured" || row.MeanMilliseconds is not > 0 ||
                               !double.IsFinite(row.MeanMilliseconds.Value)))
                throw new InvalidDataException("Failed or invalid measurement: " + row);
            if (row.Implementation != "TinyEXR.NET" && row.AllocatedBytes != null)
                throw new InvalidDataException("Native allocations are not measured.");
        }
        if (expected.Count != 0) throw new InvalidDataException("Missing results: " + string.Join(", ", expected));
    }

    public static void Write(string managedJson, string tinyJson, string officialJson, string output)
    {
        List<ComparisonRow> rows = [];
        using (var document = JsonDocument.Parse(File.ReadAllText(managedJson)))
        {
            foreach (var item in document.RootElement.GetProperty("Benchmarks").EnumerateArray())
            {
                string operation = item.GetProperty("Method").GetString()!;
                string parameter = item.GetProperty("Parameters").GetString()!;
                if (!parameter.StartsWith("Compression=", StringComparison.Ordinal)) throw new InvalidDataException("Unknown managed parameter.");
                string compression = parameter["Compression=".Length..];
                double mean = item.GetProperty("Statistics").GetProperty("Mean").GetDouble() / 1e6;
                long size = operation == "Decode" ? FileComparisonData.LoadVerified(Enum.Parse<Compression>(compression)).LongLength :
                    new FileInfo(V3CompressionBenchmarkData.GetFixturePath(Enum.Parse<Compression>(compression))).Length;
                rows.Add(new("TinyEXR.NET", operation, compression, "Measured", mean, size,
                    item.GetProperty("Memory").GetProperty("BytesAllocatedPerOperation").GetDouble()));
            }
        }
        ReadNative(tinyJson, "TinyEXR", rows);
        ReadNative(officialJson, "OpenEXR", rows);
        Validate(rows);
        using StreamWriter writer = new(output);
        writer.WriteLine("Implementation,Operation,Compression,Status,RawMiBPerSecond,ImagesPerSecond,MeanMilliseconds,EncodedBytes,CompressionRatio,AllocatedBytes,InputSource,Baseline,Reason");
        foreach (var row in rows.OrderBy(r => r.Operation).ThenBy(r => r.Compression).ThenBy(r => r.Implementation))
        {
            double? fps = row.MeanMilliseconds.HasValue ? 1000 / row.MeanMilliseconds.Value : null;
            object?[] cells = [row.Implementation, row.Operation, row.Compression, row.Status,
                fps * FileComparisonData.RawBytes / 1048576, fps, row.MeanMilliseconds, row.EncodedBytes,
                (double)FileComparisonData.RawBytes / row.EncodedBytes, row.AllocatedBytes,
                row.Operation == "Decode" ? "OpenEXR 3.5.3" : "Shared RGBA HALF samples",
                row.Compression == "None" ? "Uncompressed" : "", row.Reason];
            writer.WriteLine(string.Join(",", cells.Select(c => "\"" + Convert.ToString(c, CultureInfo.InvariantCulture)?.Replace("\"", "\"\"") + "\"")));
        }
    }

    private static void ReadNative(string path, string implementation, List<ComparisonRow> rows)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var groups = document.RootElement.GetProperty("benchmarks").EnumerateArray()
            .GroupBy(item => item.TryGetProperty("run_name", out var name) ? name.GetString()! :
                item.GetProperty("name").GetString()!);
        foreach (var group in groups)
        {
            string[] key = group.Key.Split('/');
            if (key.Length < 3 || key[0] != implementation) throw new InvalidDataException("Unknown native key: " + group.Key);
            string operation = key[1], compression = key[2];
            var iterations = group.Where(i => i.GetProperty("run_type").GetString() == "iteration").ToArray();
            if (iterations.Select(i => i.GetProperty("repetition_index").GetInt32()).Distinct().Count() != iterations.Length)
                throw new InvalidDataException("Duplicate native repetition: " + group.Key);
            var errors = group.Where(i => i.TryGetProperty("error_occurred", out var error) && error.GetBoolean()).ToArray();
            if (errors.Length > 0)
            {
                if (errors.Length != group.Count()) throw new InvalidDataException("Mixed success and failure: " + group.Key);
                foreach (var error in errors)
                    if (!error.GetProperty("error_message").GetString()!.StartsWith("Unsupported: TinyEXR EXR_ERROR_UNSUPPORTED (-3)", StringComparison.Ordinal))
                        throw new InvalidDataException("Native failure: " + error);
                rows.Add(new(implementation, operation, compression, "Unsupported", null,
                    FileComparisonData.LoadVerified(Enum.Parse<Compression>(compression)).Length, null,
                    errors[0].GetProperty("error_message").GetString()!));
                continue;
            }
            var means = group.Where(i => i.TryGetProperty("aggregate_name", out var a) && a.GetString() == "mean").ToArray();
            var selected = means.Length > 0 ? means : iterations;
            if (selected.Length != 1) throw new InvalidDataException("Missing/duplicate native mean: " + group.Key);
            var item = selected[0];
            double milliseconds = item.GetProperty("real_time").GetDouble() * (item.GetProperty("time_unit").GetString() switch
            { "ns" => 1e-6, "us" => 1e-3, "ms" => 1, "s" => 1000, _ => throw new InvalidDataException("Unknown time unit.") });
            rows.Add(new(implementation, operation, compression, "Measured", milliseconds,
                checked((long)item.GetProperty("EncodedBytes").GetDouble()), null));
        }
    }
}
