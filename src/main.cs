using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

            var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd  = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string args = parts.Length == 2 ? parts[1] : "";

            Action action = cmd switch
            {
                "" => () => { }
                ,
                "echo" => () => HandleEcho(args),
                "type" => () => HandleType(args),
                "exit" or "quit" => () => run = false,
                "pwd" => () => HandlePwd(),
                "cd" => () => HandleCd(args),
                _ => () => HandleExternalCommand(cmd, args)
            };

            action();
        }
    }

    static void HandleEcho(string statement)
    {
        Console.WriteLine(statement);
    }

    static void HandleType(string command)
    {
        var target = command.Trim();
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

    static void HandleExternalCommand(string command, string arguments)
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
                    Arguments = arguments,
                    UseShellExecute = false
                };

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    process.WaitForExit();
                }
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"exec -a '{command}' '{executablePath}' {arguments}\"",
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
