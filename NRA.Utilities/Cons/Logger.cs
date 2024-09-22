using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace NRA.Utilities.Cons;

public static class Logger
{
    public enum Prefixes
    {
        Battle = 10,
        Cmd = 13,
        Debug = 1,
        Error = 7,
        Info = 2,
        Load = 4,
        Lobby = 9,
        Receive = 17,
        Restart = 12,
        Return = 14,
        Returned = 16,
        Send = 15,
        Start = 20,
        Stop = 3,
        Tcp = 5,
        Udp = 6,
        Unload = 11,
        Warn = 8
    }

    public static Dictionary<Prefixes, int> PrefixesLvlImportance = new()
    {
        // Hypersensitive
        { Prefixes.Battle, 100 },
        { Prefixes.Lobby, 100 },

        // MediumSensitive
        { Prefixes.Receive, 80 },
        { Prefixes.Send, 80 },
        { Prefixes.Return, 80 },
        { Prefixes.Returned, 80 },

        // AlmostSensitive
        { Prefixes.Tcp, 40 },
        { Prefixes.Udp, 40 },

        // AlmostInsensitive
        { Prefixes.Debug, 30 },
        { Prefixes.Info, 30 },

        // Insensitive
        { Prefixes.Warn, 20 },

        // FatalInsensitive
        { Prefixes.Cmd, 10 },

        { Prefixes.Load, 10 },
        { Prefixes.Unload, 10 },

        { Prefixes.Start, 10 },
        { Prefixes.Restart, 10 },
        { Prefixes.Stop, 10 },

        // CrazyNoneLogLevel
        { Prefixes.Error, 1 }
    };

    private static readonly Dictionary<Prefixes, ConsoleColor> PrefixesPalette = new()
    {
        { Prefixes.Battle, ConsoleColor.DarkMagenta },
        { Prefixes.Cmd, ConsoleColor.DarkRed },
        { Prefixes.Debug, ConsoleColor.White },
        { Prefixes.Error, ConsoleColor.Red },
        { Prefixes.Info, ConsoleColor.Green },
        { Prefixes.Lobby, ConsoleColor.DarkMagenta },
        { Prefixes.Load, ConsoleColor.Black },
        { Prefixes.Receive, ConsoleColor.DarkGray },
        { Prefixes.Restart, ConsoleColor.DarkGreen },
        { Prefixes.Return, ConsoleColor.DarkCyan },
        { Prefixes.Returned, ConsoleColor.DarkCyan },
        { Prefixes.Send, ConsoleColor.Cyan },
        { Prefixes.Start, ConsoleColor.Blue },
        { Prefixes.Stop, ConsoleColor.DarkBlue },
        { Prefixes.Tcp, ConsoleColor.Gray },
        { Prefixes.Udp, ConsoleColor.Gray },
        { Prefixes.Unload, ConsoleColor.Black },
        { Prefixes.Warn, ConsoleColor.Yellow }
    };

    private static readonly char[] EndSymbols =
    [
        '.',
        '!',
        '?',
        '@',
        '#',
        '$',
        ';',
        '^',
        '%',
        ')',
        '&',
        '-',
        '_',
        '*',
        '/',
        '<',
        '>',
        '|',
        '~',
        '`'
    ];

    private static readonly ConcurrentDictionary<Prefixes, long> Dictionary = new();

    private static readonly
        ConcurrentQueue<(StackTrace stackTrace, Prefixes prefix, object? element, int lvl, int lineNumber, bool centring
            , bool duplicateToMem)> Queue = new();

    private static bool _spec;

    static Logger()
    {
        foreach (var l in Enum.GetValues(typeof(Prefixes)).Cast<Prefixes>())
            Dictionary.TryAdd(l, 0);

        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(1000 / 60);

                try
                {
                    while (Queue.TryDequeue(out var data))
                    {
                        while (_spec) ;

                        Dictionary[data.prefix] += Math.Clamp(data.lvl, 1, 10);

                        var sb = new StringBuilder("@ST: ");
                        {
                            for (var i = Math.Clamp(data.stackTrace.FrameCount - 1, 0, 3); i > 0; i--)
                            {
                                var method = data.stackTrace.GetFrame(i)!.GetMethod()!;

                                sb.Append($"({i}: [{method.DeclaringType!.Name}::{method.Name}" +
                                          (i == 1 ? $", {data.lineNumber}])" : "]) -> "));
                            }
                        }

                        var logMessage =
                            $"[{data.prefix.ToString().ToUpper()}~({Dictionary[data.prefix]}_{data.lvl})]-" +
                            $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()} ? " + sb +
                            "  =>  $DATA: " + (data.element == null! ? "NULL" : data.element);

                        if (data.centring && logMessage.Length < Console.WindowWidth)
                            Console.SetCursorPosition((Console.WindowWidth - logMessage.Length) / 2, Console.CursorTop);

                        if (data.duplicateToMem)
                            MemoryLogger.AppendLine(logMessage);

                        if (PrefixesLvlImportance[data.prefix] > AppConfig.LogSensitive)
                            return;

                        Console.ForegroundColor = PrefixesPalette[data.prefix];
                        Console.WriteLine((data.duplicateToMem ? "_DUPLICATED_  " : "") + logMessage);
                        Console.ResetColor();
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }).Start();
    }

    public static StringBuilder MemoryLogger { get; } = new();

    public static void Log(Prefixes prefix, object? element, int lvl = 1, bool centring = false,
        bool duplicateToMem = false,
        [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            if (element is ICollection d)
            {
                var se = d.Cast<object?>()
                    .Aggregate<object?, object?>(string.Empty, (current, v) => current + (v + ", ")) as string;
                element = element + " => [" + se!.Remove(se.Length - 2) + "]";
            }
        }
        catch
        {
            // not is array.
        }

        if (prefix is Prefixes.Start or Prefixes.Stop or Prefixes.Restart or Prefixes.Warn or Prefixes.Error)
            duplicateToMem = true;

        Queue.Enqueue((new StackTrace(), prefix, element, lvl, lineNumber, centring, duplicateToMem));
    }

    public static void Test(object text, [CallerLineNumber] int lineNumber = 0)
    {
        _spec = true;

        var stackTrace = new StackTrace();

        var sb = new StringBuilder("@ST: ");
        {
            for (var i = Math.Clamp(stackTrace.FrameCount - 1, 0, 5); i > 0; i--)
            {
                var method = stackTrace.GetFrame(i)!.GetMethod()!;

                sb.Append($"({i}: [{method.DeclaringType!.Name}::{method.Name}" +
                          (i == 1 ? $", {lineNumber}])" : "]) -> "));
            }
        }

        var logMessage = $"[TEST]-" +
                         $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()} ? " + sb + " | " +
                         "=> $DATA: " + text;

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(Environment.NewLine + logMessage + Environment.NewLine);
        Console.ResetColor();

        _spec = false;
    }

    public static void Saw(object text, [CallerLineNumber] int lineNumber = 0)
    {
        _spec = true;

        var stackTrace = new StackTrace();

        var sb = new StringBuilder("@ST: ");
        {
            for (var i = Math.Clamp(stackTrace.FrameCount - 1, 0, 20); i > 0; i--)
            {
                var method = stackTrace.GetFrame(i)!.GetMethod()!;

                sb.Append($"({i}: [{method.DeclaringType!.Name}::{method.Name}" +
                          (i == 1 ? $", {lineNumber}])" : "]) -> "));
            }
        }

        var logMessage = $"[SAW]-" +
                         $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()} ? " + sb + " | " +
                         "=> $DATA: " + text;

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(Environment.NewLine + logMessage + Environment.NewLine);
        Console.ResetColor();

        _spec = false;
    }

    public static void FixedPrint(object? text)
    {
        try
        {
            var data = text?.ToString()?.ToCharArray()!;

            if (EndSymbols.Contains(data[text!.ToString()!.Length - 1]))
                Console.WriteLine($"{text}");
            else if (text != null!)
                Console.WriteLine($"{text}.");
            else Console.WriteLine(".unfinished-log.");
        }
        catch
        {
            // ignored.
        }
    }
}