using ConsoleAppFramework;
using MkSidecar.Extensions;
using MkSidecar.Metadata.Combinators;
using MkSidecar.Metadata.Parsers;
using MkSidecar.Xmp;
using MkSidecar.Xmp.Fragments;
using Spectre.Console;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

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
    public async Task<int> GenerateAsync(
        [Argument] string root = ".",
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

        ImmutableArray<string> prefixes = allowedPrefixes is [_, ..]
            ? [.. allowedPrefixes]
            : [];

        AndCombinator parseTree = new(
            new ExtensionParser(".jpg", ".jpeg", ".png", ".mp4"),
            new OrCombinator(
                new TimestampFormatParser("yyyy-MM-dd_HH-mm-ss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyy-MM-dd HH-mm-ss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyyMMdd_HHmmss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyyMMdd-HHmmss", stripTrailing: true, prefixes),
                new TimestampFormatParser("yyyy-MM-dd", stripTrailing: false, allowedPrefixes: prefixes),
                new InteractiveTimestampParser(enabled: interactive)));

        RunStatistics statistics = new();
        List<IXmpFragment> fragments = [];

        RenderHeader(root, timeZone, dryRun, overwrite, verbose, interactive, prefixes);

        bool useLiveStatus = !verbose && !dryRun && !interactive;

        if (useLiveStatus)
        {
            await AnsiConsole
                .Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("green"))
                .StartAsync("Scanning media files...", async context => await ProcessFilesAsync(context));
        }
        else
        {
            await ProcessFilesAsync(statusContext: null);
        }

        RenderSummary(statistics);

        return statistics.Failed == 0 ? 0 : 1;

        async Task ProcessFilesAsync(StatusContext? statusContext)
        {
            await foreach (FileInfo fileInfo in Directory.EnumerateFilesSafelyAsync(root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (fileInfo.Extension.Equals(".xmp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                statistics.Scanned++;
                fragments.Clear();

                statusContext?.Status(CreateStatusText(statistics, fileInfo));

                bool parsed;
                try
                {
                    parsed = parseTree.TryParse(new MetadataParserContext(fileInfo, timeZone), fragments);
                }
                catch (Exception ex)
                {
                    statistics.Failed++;
                    await Console.Error.WriteLineAsync($"FAIL parse {fileInfo.FullName}: {ex.Message}");
                    continue;
                }

                if (!parsed)
                {
                    statistics.Skipped++;

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[yellow]unknown:[/] {Markup.Escape(fileInfo.FullName)}");
                    }

                    continue;
                }

                statistics.Matched++;

                string sidecarPath = $"{fileInfo.FullName}.xmp";
                FileInfo sidecarInfo = new(sidecarPath);

                if (sidecarInfo.Exists && !overwrite)
                {
                    statistics.UpToDate++;

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[grey]up-to-date:[/] {Markup.Escape(sidecarInfo.FullName)}");
                    }
                    continue;
                }

                if (dryRun)
                {
                    statistics.WouldWrite++;
                    AnsiConsole.MarkupLine($"[blue]write (dry run)[/] {Markup.Escape(sidecarInfo.FullName)} [grey]->[/] {Markup.Escape(FormatFragments(fragments))}");
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

                    statistics.Written++;

                    if (verbose)
                    {
                        AnsiConsole.MarkupLine($"[green]write[/] {Markup.Escape(sidecarPath)} [grey]->[/] {Markup.Escape(FormatFragments(fragments))}");
                    }
                }
                catch (Exception ex)
                {
                    statistics.Failed++;
                    await Console.Error.WriteLineAsync($"FAIL {sidecarPath}: {ex.Message}");
                }
            }
        }
    }

    private static void RenderHeader(
        string root,
        TimeZoneInfo timeZone,
        bool dryRun,
        bool overwrite,
        bool verbose,
        bool interactive,
        ImmutableArray<string> prefixes)
    {
        Grid grid = new();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[grey]Root[/]", Markup.Escape(Path.GetFullPath(root)));
        grid.AddRow("[grey]Timezone[/]", Markup.Escape(timeZone.Id));
        grid.AddRow("[grey]Mode[/]", dryRun ? "[blue]dry-run[/]" : "[green]write[/]");
        grid.AddRow("[grey]Overwrite[/]", overwrite ? "[yellow]yes[/]" : "no");
        grid.AddRow("[grey]Verbose[/]", verbose ? "yes" : "no");
        grid.AddRow("[grey]Interactive[/]", interactive ? "[yellow]yes[/]" : "no");

        if (!prefixes.IsDefaultOrEmpty)
        {
            grid.AddRow(
                "[grey]Allowed prefixes[/]",
                Markup.Escape(string.Join(", ", prefixes)));
        }

        Panel panel = new Panel(grid)
            .Header("mksidecar")
            .RoundedBorder()
            .BorderColor(Color.Grey);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string CreateStatusText(RunStatistics statistics, FileInfo currentFile)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Scanned [blue]{statistics.Scanned}[/] | Matched [green]{statistics.Matched}[/] | Written [green]{statistics.Written}[/] | Skipped [yellow]{statistics.Skipped}[/] | Failed [red]{statistics.Failed}[/] | [grey]{Markup.Escape(currentFile.Name)}[/]");
    }

    private static void RenderSummary(RunStatistics statistics)
    {
        AnsiConsole.WriteLine();

        Table table = new Table()
            .RoundedBorder()
            .BorderColor(statistics.Failed == 0 ? Color.Green : Color.Red)
            .Title("Summary");

        table.AddColumn("Metric");
        table.AddColumn("Count");

        table.AddRow("Scanned", statistics.Scanned.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Matched", statistics.Matched.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Written", statistics.Written.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Would write", statistics.WouldWrite.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Skipped", statistics.Skipped.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Up-to-date", statistics.UpToDate.ToString(CultureInfo.InvariantCulture));
        table.AddRow("Failed", statistics.Failed == 0
            ? "[green]0[/]"
            : $"[red]{statistics.Failed.ToString(CultureInfo.InvariantCulture)}[/]");

        AnsiConsole.Write(table);

        if (statistics.Failed == 0)
        {
            AnsiConsole.MarkupLine("[green]Done.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]Completed with failures.[/]");
        }
    }

    private static string FormatFragments(IEnumerable<IXmpFragment> fragments) => "[" + string.Join(", ", fragments) + "]";

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

internal sealed class RunStatistics
{
    public int Scanned { get; set; }

    public int Matched { get; set; }

    public int Written { get; set; }

    public int WouldWrite { get; set; }

    public int Skipped { get; set; }

    public int UpToDate { get; set; }

    public int Failed { get; set; }
}
