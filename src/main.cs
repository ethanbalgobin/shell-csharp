using System.Diagnostics;
using System.Text;

class Program
{
    static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "echo", "exit", "quit", "type", "pwd", "cd"
    };

    static void Main()
    {
        bool run = true;

        while (run)
        {
            Console.Write("$ ");

            string? input = Console.ReadLine();
            if (input is null) break;

            input = input.Trim();

            var parsedArgs = ParseCommand(input);
            if (parsedArgs.Count == 0)
            {
                continue;
            }

            string cmd = parsedArgs[0].ToLowerInvariant();
            var args = parsedArgs.Skip(1).ToList();

            Action action = cmd switch
            {
                "" => () => { }
                ,
                "echo" => () => HandleEcho(args),
                "type" => () => HandleType(args),
                "exit" or "quit" => () => run = false,
                "pwd" => () => HandlePwd(),
                "cd" => () => HandleCd(string.Join(" ", args) ?? ""),
                _ => () => HandleExternalCommand(cmd, args)
            };

            action();
        }
    }
    
    static List<string> ParseCommand(string input)
    {
        var result = new List<string>();
        var currentArg = new StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }
            else if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (currentArg.Length > 0)
                {
                    result.Add(currentArg.ToString());
                    currentArg.Clear();
                }
            }
            else
            {
                currentArg.Append(c);
            }
        }

        if (currentArg.Length > 0)
        {
            result.Add(currentArg.ToString());
        }

        return result;
    }

    static void HandleEcho(List<string> statement)
    {
        Console.WriteLine(string.Join(" ", statement));
    }

    static void HandleType(List<string> args)
    {
        if (args.Count == 0)
        {
            return;
        }

        var target = args[0];

        if (target.Length == 0)
            return;

        // 1) Builtin check
        if (Builtins.Contains(target))
        {
            Console.WriteLine($"{target} is a shell builtin");
            return;
        }

        // 2) Search PATH for an executable
        string? fullPath = FindExecutableInPath(target);

        if (fullPath is not null)
        {
            Console.WriteLine($"{target} is {fullPath}");
            return;
        }

        Console.WriteLine($"{target}: not found");
    }

    static void HandlePwd()
    {
        Console.WriteLine(Directory.GetCurrentDirectory());
        return;
    }

    static void HandleCd(string path)
    {

        if (path == "~")
        {
            string? homeDirectory = Environment.GetEnvironmentVariable("HOME");
            if (homeDirectory is not null)
            {
                path = homeDirectory;
            }
        }

        try
        {
            Directory.SetCurrentDirectory(path);
            return;
        }
        catch (DirectoryNotFoundException)
        {
            Console.WriteLine($"cd: {path}: No such file or directory");
            return;
        }
    }

    static void HandleExternalCommand(string command, List<string> args)
    {
        string? executablePath = FindExecutableInPath(command);

        if (executablePath is null)
        {
            Console.WriteLine($"{command}: command not found");
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = false
                };

                foreach (var arg in args)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    process.WaitForExit();
                }
            }
            else
            {
                var escapedArgs = args.Select(arg => $"'{arg.Replace("'", "'\\''")}'");
                var allArgs = string.Join(" ", escapedArgs);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"exec -a '{command}' '{executablePath}' {allArgs}\"",
                    UseShellExecute = false
                };

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    process.WaitForExit();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error executing {command}: {ex.Message}");
        }
    }

    static string? FindExecutableInPath(string commandName)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        foreach (var rawDir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = rawDir.Trim();
            if (dir.Length == 0 || !Directory.Exists(dir))
                continue;

            // On Windows, consider PATHEXT when command has no extension
            if (OperatingSystem.IsWindows())
            {
                foreach (var candidate in GetWindowsCandidates(dir, commandName))
                {
                    if (File.Exists(candidate) && HasExecutePermission(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
            else
            {
                var candidate = Path.Combine(dir, commandName);
                if (File.Exists(candidate) && HasExecutePermission(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    static IEnumerable<string> GetWindowsCandidates(string dir, string commandName)
    {
        // If user typed an extension (e.g. "git.exe"), try it directly
        if (Path.HasExtension(commandName))
        {
            yield return Path.Combine(dir, commandName);
            yield break;
        }

        // Otherwise try PATHEXT (e.g. .EXE;.BAT;.CMD;.COM)
        var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM";
        foreach (var extRaw in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = extRaw.Trim();
            if (ext.Length == 0) continue;

            yield return Path.Combine(dir, commandName + ext);
        }
    }

    static bool HasExecutePermission(string filePath)
    {
        try
        {
            var attrs = File.GetAttributes(filePath);
            if (attrs.HasFlag(FileAttributes.Directory))
                return false;
        }
        catch
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(filePath);
            const UnixFileMode execBits =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

            return (mode & execBits) != 0;
        }
        catch
        {
            return false;
        }
    }
}
