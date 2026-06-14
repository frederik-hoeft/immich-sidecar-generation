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
    /// <summary>
    /// Generates XMP sidecars with timezone-aware dates derived from strict media filenames.
    /// </summary>
    /// <param name="root">Root directory to scan recursively.</param>
    /// <param name="tz">Timezone ID used to resolve local timestamps.</param>
    /// <param name="dryRun">Print what would be written without creating files.</param>
    /// <param name="overwrite">Overwrite existing .xmp sidecars.</param>
    /// <param name="verbose">-v|Enable verbose output.</param>
    /// <param name="interactive">-i|Enable interactive timestamp parsing for filenames that don't match strict formats.</param>
    /// <param name="allowedPrefixes">Additional allowed prefixes that may appear before timestamps in filenames, e.g. "IMG_", "VID_".</param>
    [Command("")]
    public async Task<int> GenerateAsync([Argument] string root = ".",
        string tz = "Europe/Berlin",
        bool dryRun = false,
        bool overwrite = false,
        bool verbose = false,
        bool interactive = false,
        string[]? allowedPrefixes = null,
        CancellationToken cancellationToken = default)
    {
        (int status, TimeZoneInfo? timeZone) = ValidateParameters(root, tz);
        if (status != 0 || timeZone == null)
        {
            return status;
        }
        ImmutableArray<string> prefixes = allowedPrefixes is [_, ..] ? [.. allowedPrefixes] : [];

        AndCombinator parseTree = new(
            new ExtensionParser(".jpg", ".jpeg", ".png", ".mp4"),
            new OrCombinator(
                new TimestampFormatParser("yyyy-MM-dd_HH-mm-ss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyy-MM-dd HH-mm-ss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyyMMdd_HHmmss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyyMMdd-HHmmss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyy-MM-dd", stripTrailing: false, allowedPrefixes: prefixes),
                new InteractiveTimestampParser(enabled: interactive)
                ));

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
            if (!parseTree.TryParse(new MetadataParserContext(fileInfo, timeZone), fragments))
            {
                skipped++;
                if (verbose)
                {
                    Console.WriteLine($"Cannot parse: {fileInfo.FullName}");
                }
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
            try
            {
                XmpSidecar sidecar = new(requiredFragments: XmpTimestampFragment.Id);
                foreach (IXmpFragment fragment in fragments)
                {
                    sidecar.Add(fragment);
                }
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

    private static (int status, TimeZoneInfo? timeZoneInfo) ValidateParameters(string root, string tz)
    {
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Directory does not exist: {root}");
            return (status: 2, timeZoneInfo: null);
        }
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(tz);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            Console.Error.WriteLine($"Timezone not found or invalid: {tz}, Error: '{ex.Message}'");
            return (status: 2, timeZoneInfo: null);
        }

        return (status: 0, timeZoneInfo: timeZone);
    }
}
