#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property PublishAot=true
#:property PublishTrimmed=true
#:property OptimizationPreference=speed
#:property Nullable=enable
#:property ImplicitUsings=false
#:package ConsoleAppFramework@5.7.13

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var app = ConsoleApp.Create();
app.Add<Commands>();
await app.RunAsync(args);

// ---------------------------------------------------------------------------
// Chain-of-Responsibility: metadata extraction
// ---------------------------------------------------------------------------

/// <summary>
/// Validated, timezone-resolved metadata extracted from a media filename.
/// All TZ-specific validation and offset resolution is centralised here so
/// individual parsers only need to return raw date/time components.
/// </summary>
internal sealed record FileMetadata
{
    public DateTimeOffset Timestamp { get; }

    public string XmpDate =>
        Timestamp.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private FileMetadata(DateTimeOffset timestamp) => Timestamp = timestamp;

    /// <summary>
    /// Validates <paramref name="localDateTime"/> against <paramref name="timeZone"/>
    /// (rejects DST gaps, resolves DST ambiguity to the pre-transition / larger offset),
    /// then wraps the result as a <see cref="FileMetadata"/> instance.
    /// </summary>
    /// <param name="filePath">Used only for diagnostic console output.</param>
    public static bool TryCreate(
        DateTime localDateTime,
        TimeZoneInfo timeZone,
        string filePath,
        [NotNullWhen(true)] out FileMetadata? metadata)
    {
        metadata = null;

        if (timeZone.IsInvalidTime(localDateTime))
        {
            Console.WriteLine($"SKIP invalid local time due to DST gap: {filePath}");
            return false;
        }

        TimeSpan offset;
        if (timeZone.IsAmbiguousTime(localDateTime))
        {
            TimeSpan[] offsets = timeZone.GetAmbiguousTimeOffsets(localDateTime);
            // Prefer the pre-transition (larger / more-positive) offset.
            offset = offsets[0] > offsets[1] ? offsets[0] : offsets[1];
            Console.WriteLine(
                $"WARN ambiguous local time due to DST overlap, using offset {offset}: {filePath}");
        }
        else
        {
            offset = timeZone.GetUtcOffset(localDateTime);
        }

        metadata = new FileMetadata(new DateTimeOffset(localDateTime, offset));
        return true;
    }
}

/// <summary>
/// Extracts <see cref="FileMetadata"/> from a <see cref="FileInfo"/>.
/// Implementations inspect only the filename; timezone-aware validation is
/// delegated to <see cref="FileMetadata.TryCreate"/>.
/// </summary>
internal interface IMetadataParser
{
    bool TryParse(
        FileInfo file,
        TimeZoneInfo timeZone,
        [NotNullWhen(true)] out FileMetadata? data);
}

/// <summary>
/// Tries each registered parser in order and returns the first successful result.
/// </summary>
internal sealed class CompositeMetadataParser : IMetadataParser
{
    private readonly IReadOnlyList<IMetadataParser> _parsers;

    public CompositeMetadataParser(IReadOnlyList<IMetadataParser> parsers) =>
        _parsers = parsers;

    public bool TryParse(
        FileInfo file,
        TimeZoneInfo timeZone,
        [NotNullWhen(true)] out FileMetadata? data)
    {
        foreach (IMetadataParser parser in _parsers)
        {
            if (parser.TryParse(file, timeZone, out data))
                return true;
        }

        data = null;
        return false;
    }
}

/// <summary>
/// Handles filenames of the form <c>YYYYMMDD-HHmmss[_N].ext</c>
/// where ext is jpeg, jpg, mp4, or mov (case-insensitive).
/// </summary>
internal sealed partial class StandardTimestampParser : IMetadataParser
{
    [GeneratedRegex(
        @"^(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})-(?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})(?:_\d+)?\.(?:jpe?g|mp4|mov)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();

    public bool TryParse(
        FileInfo file,
        TimeZoneInfo timeZone,
        [NotNullWhen(true)] out FileMetadata? data)
    {
        data = null;

        Match match = Pattern().Match(file.Name);
        if (!match.Success)
            return false;

        if (!TryParseComponents(match, out DateTime localDateTime))
        {
            Console.WriteLine($"SKIP invalid date/time: {file.FullName}");
            return false;
        }

        return FileMetadata.TryCreate(localDateTime, timeZone, file.FullName, out data);
    }

    private static bool TryParseComponents(Match match, out DateTime localDateTime)
    {
        localDateTime = default;

        if (!(int.TryParse(match.Groups["year"].Value, CultureInfo.InvariantCulture, out int year) &&
              int.TryParse(match.Groups["month"].Value, CultureInfo.InvariantCulture, out int month) &&
              int.TryParse(match.Groups["day"].Value, CultureInfo.InvariantCulture, out int day) &&
              int.TryParse(match.Groups["hour"].Value, CultureInfo.InvariantCulture, out int hour) &&
              int.TryParse(match.Groups["minute"].Value, CultureInfo.InvariantCulture, out int minute) &&
              int.TryParse(match.Groups["second"].Value, CultureInfo.InvariantCulture, out int second)))
        {
            return false;
        }

        try
        {
            localDateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

// ---------------------------------------------------------------------------
// CLI command
// ---------------------------------------------------------------------------

internal sealed partial class Commands
{
    private static readonly IMetadataParser Parser = new CompositeMetadataParser(
    [
        new StandardTimestampParser(),
    ]);

    /// <summary>
    /// Generates XMP sidecars with timezone-aware dates derived from strict media filenames.
    /// </summary>
    /// <param name="root">Root directory to scan recursively.</param>
    /// <param name="tz">Timezone ID used to resolve local timestamps.</param>
    /// <param name="dryRun">Print what would be written without creating files.</param>
    /// <param name="overwrite">Overwrite existing .xmp sidecars.</param>
    [Command("")]
    public Task<int> GenerateAsync(
        [Argument] string root = ".",
        string tz = "Europe/Berlin",
        bool dryRun = false,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Directory does not exist: {root}");
            return Task.FromResult(2);
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = FindTimeZone(tz);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            Console.Error.WriteLine($"Timezone not found or invalid: {tz}");
            Console.Error.WriteLine(ex.Message);
            return Task.FromResult(2);
        }

        int scanned = 0;
        int matched = 0;
        int written = 0;
        int wouldWrite = 0;
        int skipped = 0;
        int failed = 0;

        foreach (string filePath in EnumerateFilesSafely(root))
        {
            cancellationToken.ThrowIfCancellationRequested();

            scanned++;

            FileInfo fileInfo = new FileInfo(filePath);

            if (!Parser.TryParse(fileInfo, timeZone, out FileMetadata? metadata))
            {
                skipped++;
                continue;
            }

            matched++;

            string sidecarPath = filePath + ".xmp";

            if (File.Exists(sidecarPath) && !overwrite)
            {
                Console.WriteLine($"SKIP existing sidecar: {sidecarPath}");
                skipped++;
                continue;
            }

            if (dryRun)
            {
                Console.WriteLine($"WOULD WRITE {sidecarPath} -> {metadata.XmpDate}");
                wouldWrite++;
                continue;
            }

            try
            {
                string content = CreateXmp(metadata.XmpDate);
                WriteSidecarAtomically(sidecarPath, content, overwrite);

                Console.WriteLine($"WRITE {sidecarPath} -> {metadata.XmpDate}");
                written++;
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"FAIL {sidecarPath}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Scanned:     {scanned}");
        Console.WriteLine($"Matched:     {matched}");
        Console.WriteLine($"Written:     {written}");
        Console.WriteLine($"Would write: {wouldWrite}");
        Console.WriteLine($"Skipped:     {skipped}");
        Console.WriteLine($"Failed:      {failed}");

        return Task.FromResult(failed == 0 ? 0 : 1);
    }

    private static TimeZoneInfo FindTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id.Equals("Europe/Berlin", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
        catch (InvalidTimeZoneException) when (id.Equals("Europe/Berlin", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root)
    {
        Stack<string> pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SKIP directory files: {directory}: {ex.Message}");
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(directory);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"SKIP directory children: {directory}: {ex.Message}");
                continue;
            }

            foreach (string subdirectory in subdirectories)
            {
                FileAttributes attributes;

                try
                {
                    attributes = File.GetAttributes(subdirectory);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"SKIP directory attributes: {subdirectory}: {ex.Message}");
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Console.WriteLine($"SKIP reparse point/symlink: {subdirectory}");
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    private static void WriteSidecarAtomically(string sidecarPath, string content, bool overwrite)
    {
        string? directory = Path.GetDirectoryName(sidecarPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = ".";
        }

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            "." + Path.GetFileName(sidecarPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        File.WriteAllText(tempPath, content, Encoding.UTF8);

        try
        {
            File.Move(tempPath, sidecarPath, overwrite);
        }
        catch
        {
            File.Delete(tempPath);
            throw;
        }
    }

    private static string CreateXmp(string xmpDate)
    {
        return $"""
<?xpacket begin="" id="W5M0MpCehiHzreSzNTczkc9d"?>
<x:xmpmeta xmlns:x="adobe:ns:meta/">
  <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
    <rdf:Description rdf:about=""
      xmlns:exif="http://ns.adobe.com/exif/1.0/"
      xmlns:xmp="http://ns.adobe.com/xap/1.0/"
      xmlns:photoshop="http://ns.adobe.com/photoshop/1.0/"
      exif:DateTimeOriginal="{xmpDate}"
      xmp:CreateDate="{xmpDate}"
      xmp:MediaCreateDate="{xmpDate}"
      photoshop:DateCreated="{xmpDate}"/>
  </rdf:RDF>
</x:xmpmeta>
<?xpacket end="w"?>

""";
    }
}