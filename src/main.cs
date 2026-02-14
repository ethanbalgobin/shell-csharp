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

            // Check for output redirection
            string? redirectFile = null;
            int redirectIndex = -1;

            for (int i = 0; i < parsedArgs.Count; i++)
            {
                if (parsedArgs[i] == ">" || parsedArgs[i] == "1>")
                {
                    redirectIndex = i;
                    if (i + 1 < parsedArgs.Count)
                    {
                        redirectFile = parsedArgs[i + 1];
                    }
                    break;
                }
            }

            // Remove redirection operator and filename from args
            List<string> actualArgs = parsedArgs;
            if (redirectIndex >= 0)
            {
                actualArgs = parsedArgs.Take(redirectIndex).ToList();
            }

            if (actualArgs.Count == 0)
            {
                continue;
            }

            string cmd = actualArgs[0].ToLowerInvariant();
            var args = actualArgs.Skip(1).ToList();

            bool isBuiltin = Builtins.Contains(cmd) || cmd == "exit" || cmd == "quit";

            // Set up redirection if needed
            TextWriter? originalOut = null;
            FileStream? fileStream = null;
            StreamWriter? fileWriter = null;

            try
            {
                if (redirectFile != null && isBuiltin)
                {
                    originalOut = Console.Out;
                    fileStream = new FileStream(redirectFile, FileMode.Create, FileAccess.Write);
                    fileWriter = new StreamWriter(fileStream) { AutoFlush = true };
                    Console.SetOut(fileWriter);
                }

                Action action = cmd switch
                {
                    "" => () => { },
                    "echo" => () => HandleEcho(args),
                    "type" => () => HandleType(args),
                    "exit" or "quit" => () => run = false,
                    "pwd" => () => HandlePwd(),
                    "cd" => () => HandleCd(string.Join(" ", args) ?? ""),
                    _ => () => HandleExternalCommand(cmd, args, redirectFile)
                };

                action();
            }
            finally
            {
                // Restore original stdout
                if (originalOut != null)
                {
                    Console.SetOut(originalOut);
                    fileWriter?.Dispose();
                    fileStream?.Dispose();
                }
            }
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

            if (c == '\\' && !inSingleQuote)
            {
                if (i + 1 < input.Length)
                {
                    char nextChar = input[i + 1];
                    
                    if (inDoubleQuote)
                    {
                        if (nextChar == '"' || nextChar == '\\')
                        {
                            i++;
                            currentArg.Append(nextChar);
                        }
                        else
                        {
                            currentArg.Append(c);
                        }
                    }
                    else
                    {
                        i++;
                        currentArg.Append(nextChar);
                    }
                }
                else
                {
                    currentArg.Append(c);
                }
            }
            else if (c == '\'' && !inDoubleQuote)
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

        if (Builtins.Contains(target))
        {
            Console.WriteLine($"{target} is a shell builtin");
            return;
        }

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
        }
        catch (DirectoryNotFoundException)
        {
            Console.WriteLine($"cd: {path}: No such file or directory");
        }
    }

    static void HandleExternalCommand(string command, List<string> args, string? redirectFile = null)
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
                    UseShellExecute = false,
                    RedirectStandardOutput = redirectFile != null
                };

                foreach (var arg in args)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    if (redirectFile != null && process.StandardOutput != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        File.WriteAllText(redirectFile, output);
                    }
                    process.WaitForExit();
                }
            }
            else
            {
                var escapedCommand = EscapeShellArgument(command);
                var escapedPath = EscapeShellArgument(executablePath);
                var escapedArgs = string.Join(" ", args.Select(EscapeShellArgument));
                
                var shellCommand = $"exec -a {escapedCommand} {escapedPath} {escapedArgs}".TrimEnd();
                
                if (redirectFile != null)
                {
                    var escapedRedirect = EscapeShellArgument(redirectFile);
                    shellCommand += $" > {escapedRedirect}";
                }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    ArgumentList = { "-c", shellCommand },
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
            Console.Error.WriteLine($"Error executing {command}: {ex.Message}");
        }
    }

    static string EscapeShellArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "''";
        }

        return "'" + arg.Replace("'", "'\"'\"'") + "'";
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
        if (Path.HasExtension(commandName))
        {
            yield return Path.Combine(dir, commandName);
            yield break;
        }

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