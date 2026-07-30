using System.Security.Cryptography;
using System.Text;

namespace ReferenceAverageHistoryCorrectionGraphPreview;

internal sealed class DeterministicCsvOutput
{
    private readonly string directory;
    private readonly List<OutputEvidence> evidence = [];

    public DeterministicCsvOutput(string directory)
    {
        this.directory = directory;
    }

    public IReadOnlyList<OutputEvidence> Evidence => evidence;

    public async Task WriteAsync(
        string fileName,
        IReadOnlyList<string> header,
        IEnumerable<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        var partialPath = Path.Combine(directory, fileName + ".partial");
        var finalPath = Path.Combine(directory, fileName);
        long rowCount = 0;
        await using (var stream = new FileStream(
                         partialPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            await writer.WriteLineAsync(string.Join(',', header.Select(Escape)));
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Count != header.Count)
                {
                    throw new InvalidDataException(
                        $"Output row for {fileName} has {row.Count} values; expected {header.Count}.");
                }

                await writer.WriteLineAsync(string.Join(',', row.Select(Escape)));
                rowCount++;
            }
        }

        File.Move(partialPath, finalPath);
        await using var hashStream = File.OpenRead(finalPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream, cancellationToken));
        evidence.Add(new OutputEvidence(fileName, rowCount, hash));
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
