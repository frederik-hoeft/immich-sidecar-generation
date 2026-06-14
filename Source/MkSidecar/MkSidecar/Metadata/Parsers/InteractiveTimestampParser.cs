using MkSidecar.Xmp;
using MkSidecar.Xmp.Fragments;
using Spectre.Console;
using System.Globalization;

namespace MkSidecar.Metadata.Parsers;

internal sealed class InteractiveTimestampParser(bool enabled) : IMetadataParser
{
    private const string ENTER_TIMESTAMP = "Enter timestamp";
    private const string REUSE_PREVIOUS = "Reuse previous timestamp";
    private const string SKIP_FILE = "Skip this file";
    private const string DISABLE_INTERACTIVE = "Disable interactive for remaining files";

    private bool _enabled = enabled;
    private DateOnly? _previousDate;
    private TimeOnly? _previousTime;
    private TimeZoneInfo? _previousTimeZone;

    public bool TryParse(MetadataParserContext context, List<IXmpFragment> results)
    {
        if (!_enabled)
        {
            return false;
        }

        while (true)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(
                new Panel(Markup.Escape(context.File.FullName))
                    .Header("Manual timestamp required")
                    .Expand());

            List<string> choices =
            [
                ENTER_TIMESTAMP,
                SKIP_FILE,
                DISABLE_INTERACTIVE,
            ];

            if (_previousDate is not null && _previousTime is not null && _previousTimeZone is not null)
            {
                choices.Insert(1, REUSE_PREVIOUS);
            }

            string action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What do you want to do?")
                    .AddChoices(choices));

            switch (action)
            {
                case ENTER_TIMESTAMP:
                    if (TryPromptTimestamp(context, results))
                    {
                        return true;
                    }

                    break;

                case REUSE_PREVIOUS:
                    if (TryAddTimestamp(
                            context,
                            _previousDate!.Value,
                            _previousTime!.Value,
                            _previousTimeZone!,
                            results))
                    {
                        return true;
                    }

                    break;

                case SKIP_FILE:
                    return false;

                case DISABLE_INTERACTIVE:
                    _enabled = false;
                    return false;
            }
        }
    }

    private bool TryPromptTimestamp(MetadataParserContext context, List<IXmpFragment> results)
    {
        while (true)
        {
            DateOnly date = PromptDate(_previousDate);
            TimeOnly time = PromptTime(_previousTime);
            TimeZoneInfo timeZone = PromptTimeZone(context.TimeZone, _previousTimeZone);

            if (TryAddTimestamp(context, date, time, timeZone, results))
            {
                _previousDate = date;
                _previousTime = time;
                _previousTimeZone = timeZone;
                return true;
            }

            AnsiConsole.MarkupLine("[red]The selected local timestamp is invalid in that timezone, probably because of a DST gap.[/]");

            bool retry = AnsiConsole.Confirm("Try another timestamp?", defaultValue: true);
            if (!retry)
            {
                return false;
            }
        }
    }

    private static bool TryAddTimestamp(
        MetadataParserContext context,
        DateOnly date,
        TimeOnly time,
        TimeZoneInfo timeZone,
        List<IXmpFragment> results)
    {
        DateTime localDateTime = date.ToDateTime(time, DateTimeKind.Unspecified);
        MetadataParserContext adjustedContext = context with { TimeZone = timeZone };

        if (!XmpTimestampFragment.TryCreate(
                adjustedContext,
                localDateTime,
                out XmpTimestampFragment? fragment))
        {
            return false;
        }

        results.Add(fragment);
        return true;
    }

    private static DateOnly PromptDate(DateOnly? defaultDate)
    {
        while (true)
        {
            TextPrompt<string> prompt = new TextPrompt<string>("Date [grey](yyyy-MM-dd or yyyyMMdd)[/]:")
                .PromptStyle("green")
                .Validate(input =>
                    TryParseDate(input, out _)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Expected yyyy-MM-dd or yyyyMMdd.[/]"));

            if (defaultDate is not null)
            {
                prompt.DefaultValue(defaultDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            string input = AnsiConsole.Prompt(prompt);

            if (TryParseDate(input, out DateOnly date))
            {
                return date;
            }
        }
    }

    private static TimeOnly PromptTime(TimeOnly? defaultTime)
    {
        while (true)
        {
            TextPrompt<string> prompt = new TextPrompt<string>("Time [grey](HH:mm:ss, HH:mm, HH-mm-ss, or HHmmss)[/]:")
                .PromptStyle("green")
                .Validate(input =>
                    TryParseTime(input, out _)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Expected HH:mm:ss, HH:mm, HH-mm-ss, HH-mm, HHmmss, or HHmm.[/]"));

            if (defaultTime is not null)
            {
                prompt.DefaultValue(defaultTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            }

            string input = AnsiConsole.Prompt(prompt);

            if (TryParseTime(input, out TimeOnly time))
            {
                return time;
            }
        }
    }

    private static TimeZoneInfo PromptTimeZone(TimeZoneInfo defaultTimeZone, TimeZoneInfo? previousTimeZone)
    {
        TimeZoneInfo proposedDefault = previousTimeZone ?? defaultTimeZone;

        bool useDefault = AnsiConsole.Confirm(
            $"Use timezone [green]{Markup.Escape(proposedDefault.Id)}[/]?",
            defaultValue: true);

        if (useDefault)
        {
            return proposedDefault;
        }

        while (true)
        {
            string input = AnsiConsole.Prompt(
                new TextPrompt<string>("Timezone ID:")
                    .PromptStyle("green")
                    .DefaultValue(proposedDefault.Id)
                    .Validate(value =>
                    {
                        try
                        {
                            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
                            return ValidationResult.Success();
                        }
                        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                        {
                            return ValidationResult.Error("[red]Timezone not found or invalid.[/]");
                        }
                    }));

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(input);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Validation should already have caught this; keep loop robust anyway.
            }
        }
    }

    private static bool TryParseDate(string input, out DateOnly date)
    {
        return DateOnly.TryParseExact(
            input,
            ["yyyy-MM-dd", "yyyyMMdd"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool TryParseTime(string input, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(
            input,
            ["HH:mm:ss", "HH:mm", "HH-mm-ss", "HH-mm", "HHmmss", "HHmm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }
}
