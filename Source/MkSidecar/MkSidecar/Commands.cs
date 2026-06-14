using ConsoleAppFramework;
using MkSidecar.Extensions;
using MkSidecar.Metadata.Combinators;
using MkSidecar.Metadata.Parsers;
using MkSidecar.Xmp;
using MkSidecar.Xmp.Fragments;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace MkSidecar;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance methods are required by ConsoleAppFramework.")]
internal sealed partial class Commands
{
    private static readonly ImmutableArray<string> s_allowedPrefixes = ["IMG_", "VID_"];

    private static AndCombinator ParseTree => field ??= new(
        new ExtensionParser(".jpg", ".jpeg", ".png", ".mp4"),
        new OrCombinator(
            new TimestampFormatParser("yyyy-MM-dd_HH-mm-ss", stripTrailing: true, s_allowedPrefixes),
            new TimestampFormatParser("yyyy-MM-dd HH-mm-ss", stripTrailing: true, s_allowedPrefixes),
            new TimestampFormatParser("yyyyMMdd_HHmmss", stripTrailing: true, s_allowedPrefixes),
            new TimestampFormatParser("yyyyMMdd-HHmmss", stripTrailing: true, s_allowedPrefixes),
            new TimestampFormatParser("yyyy-MM-dd", stripTrailing: false, allowedPrefixes: s_allowedPrefixes)
            ));

    /// <summary>
    /// Generates XMP sidecars with timezone-aware dates derived from strict media filenames.
    /// </summary>
    /// <param name="root">Root directory to scan recursively.</param>
    /// <param name="tz">Timezone ID used to resolve local timestamps.</param>
    /// <param name="dryRun">Print what would be written without creating files.</param>
    /// <param name="overwrite">Overwrite existing .xmp sidecars.</param>
    [Command("")]
    public async Task<int> GenerateAsync([Argument] string root = ".", string tz = "Europe/Berlin", bool dryRun = false, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root))
        {
            await Console.Error.WriteLineAsync($"Directory does not exist: {root}");
            return 2;
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(tz);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            await Console.Error.WriteLineAsync($"Timezone not found or invalid: {tz}");
            await Console.Error.WriteLineAsync(ex.Message);
            return 2;
        }

        int scanned = 0;
        int matched = 0;
        int written = 0;
        int wouldWrite = 0;
        int skipped = 0;
        int failed = 0;

        List<IXmpFragment> fragments = [];
        await foreach (FileInfo fileInfo in Directory.EnumerateFilesSafelyAsync(root, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            scanned++;
            fragments.Clear();
            if (!ParseTree.TryParse(new MetadataParserContext(fileInfo, timeZone), fragments))
            {
                skipped++;
                Console.WriteLine($"Cannot parse: {fileInfo.FullName}");
                continue;
            }

            matched++;

            string sidecarPath = $"{fileInfo.FullName}.xmp";
            FileInfo sidecarInfo = new(sidecarPath);

            if (sidecarInfo.Exists && !overwrite)
            {
                Console.WriteLine($"SKIP existing sidecar: {sidecarInfo.FullName}");
                skipped++;
                continue;
            }

            if (dryRun)
            {
                Console.WriteLine($"WOULD WRITE {sidecarInfo.FullName} -> [{string.Join(", ", fragments)}]");
                wouldWrite++;
                continue;
            }
            XmpSidecar sidecar = new(requiredFragments: XmpTimestampFragment.Id);
            foreach (IXmpFragment fragment in fragments)
            {
                sidecar.Add(fragment);
            }
            try
            {
                await sidecar.SaveAsync(sidecarPath, overwrite, cancellationToken: cancellationToken);

                Console.WriteLine($"WRITE {sidecarPath} -> [{string.Join(", ", fragments)}]");
                written++;
            }
            catch (Exception ex)
            {
                failed++;
                await Console.Error.WriteLineAsync($"FAIL {sidecarPath}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Scanned:     {scanned}");
        Console.WriteLine($"Matched:     {matched}");
        Console.WriteLine($"Written:     {written}");
        Console.WriteLine($"Would write: {wouldWrite}");
        Console.WriteLine($"Skipped:     {skipped}");
        Console.WriteLine($"Failed:      {failed}");

        return failed == 0 ? 0 : 1;
    }
}
