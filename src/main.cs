using System.Diagnostics;
using System.Text;

class Program
{
    static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "echo", "exit", "quit", "type", "pwd", "cd"
    };

    static readonly string[] AutocompleteCommands = { "echo", "exit" };

    static void Main()
    {
        bool run = true;

        while (run)
        {
            Console.Write("$ ");

            string? input = ReadLineWithTabCompletion();
            if (input is null) break;

            input = input.Trim();

            var parsedArgs = ParseCommand(input);
            if (parsedArgs.Count == 0)
            {
                continue;
            }

            string? redirectStdout = null;
            string? redirectStderr = null;
            bool appendStdout = false;
            bool appendStderr = false;
            int stdoutIndex = -1;
            int stderrIndex = -1;

            for (int i = 0; i < parsedArgs.Count; i++)
            {
                if (parsedArgs[i] == ">" || parsedArgs[i] == "1>")
                {
                    stdoutIndex = i;
                    appendStdout = false;
                    if (i + 1 < parsedArgs.Count)
                    {
                        redirectStdout = parsedArgs[i + 1];
                    }
                }
                else if (parsedArgs[i] == ">>" || parsedArgs[i] == "1>>")
                {
                    stdoutIndex = i;
                    appendStdout = true;
                    if (i + 1 < parsedArgs.Count)
                    {
                        redirectStdout = parsedArgs[i + 1];
                    }
                }
                else if (parsedArgs[i] == "2>")
                {
                    stderrIndex = i;
                    appendStderr = false;
                    if (i + 1 < parsedArgs.Count)
                    {
                        redirectStderr = parsedArgs[i + 1];
                    }
                }
                else if (parsedArgs[i] == "2>>")
                {
                    stderrIndex = i;
                    appendStderr = true;
                    if (i + 1 < parsedArgs.Count)
                    {
                        redirectStderr = parsedArgs[i + 1];
                    }
                }
            }

            List<string> actualArgs = new List<string>();
            for (int i = 0; i < parsedArgs.Count; i++)
            {
                if ((i == stdoutIndex || i == stderrIndex) ||
                    (stdoutIndex >= 0 && i == stdoutIndex + 1) ||
                    (stderrIndex >= 0 && i == stderrIndex + 1))
                {
                    continue;
                }
                actualArgs.Add(parsedArgs[i]);
            }

            if (actualArgs.Count == 0)
            {
                continue;
            }

            string cmd = actualArgs[0].ToLowerInvariant();
            var args = actualArgs.Skip(1).ToList();

            bool isBuiltin = Builtins.Contains(cmd) || cmd == "exit" || cmd == "quit";

            TextWriter? originalOut = null;
            TextWriter? originalErr = null;
            FileStream? stdoutStream = null;
            FileStream? stderrStream = null;
            StreamWriter? stdoutWriter = null;
            StreamWriter? stderrWriter = null;

            try
            {
                if (isBuiltin)
                {
                    if (redirectStdout != null)
                    {
                        originalOut = Console.Out;
                        var fileMode = appendStdout ? FileMode.Append : FileMode.Create;
                        stdoutStream = new FileStream(redirectStdout, fileMode, FileAccess.Write);
                        stdoutWriter = new StreamWriter(stdoutStream) { AutoFlush = true };
                        Console.SetOut(stdoutWriter);
                    }

                    if (redirectStderr != null)
                    {
                        originalErr = Console.Error;
                        var fileMode = appendStderr ? FileMode.Append : FileMode.Create;
                        stderrStream = new FileStream(redirectStderr, fileMode, FileAccess.Write);
                        stderrWriter = new StreamWriter(stderrStream) { AutoFlush = true };
                        Console.SetError(stderrWriter);
                    }
                }

                Action action = cmd switch
                {
                    "" => () => { }
                    ,
                    "echo" => () => HandleEcho(args),
                    "type" => () => HandleType(args),
                    "exit" or "quit" => () => run = false,
                    "pwd" => () => HandlePwd(),
                    "cd" => () => HandleCd(string.Join(" ", args) ?? ""),
                    _ => () => HandleExternalCommand(cmd, args, redirectStdout, appendStdout, redirectStderr, appendStderr)
                };

                action();
            }
            finally
            {
                if (originalOut != null)
                {
                    Console.SetOut(originalOut);
                    stdoutWriter?.Dispose();
                    stdoutStream?.Dispose();
                }

                if (originalErr != null)
                {
                    Console.SetError(originalErr);
                    stderrWriter?.Dispose();
                    stderrStream?.Dispose();
                }
            }
        }
    }

    static string? ReadLineWithTabCompletion()
    {
        var input = new StringBuilder();
        
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);
            
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return input.ToString();
            }
            else if (keyInfo.Key == ConsoleKey.Tab)
            {
                string currentInput = input.ToString();
                
                if (!currentInput.Contains(' '))
                {
                    var matches = AutocompleteCommands
                        .Where(cmd => cmd.StartsWith(currentInput, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (matches.Count == 1)
                    {
                        Console.Write("\r$ ");

                        Console.Write(new string(' ', input.Length));

                        Console.Write("\r$ ");

                        string completion = matches[0];
                        input.Clear();
                        input.Append(completion);
                        input.Append(' ');

                        Console.Write(completion + " ");
                    }
                    else
                    {
                        Console.WriteLine(input);
                        Console.WriteLine("\x07");
                    }
                }
            }
            else if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                {
                    input.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                input.Append(keyInfo.KeyChar);
                Console.Write(keyInfo.KeyChar);
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
            Console.Error.WriteLine($"cd: {path}: No such file or directory");
        }
    }

    static void HandleExternalCommand(string command, List<string> args, string? redirectStdout = null, bool appendStdout = false, string? redirectStderr = null, bool appendStderr = false)
    {
        string? executablePath = FindExecutableInPath(command);

        if (executablePath is null)
        {
            Console.Error.WriteLine($"{command}: command not found");
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
                    RedirectStandardOutput = redirectStdout != null,
                    RedirectStandardError = redirectStderr != null
                };

                foreach (var arg in args)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                using var process = Process.Start(startInfo);
                if (process is not null)
                {
                    if (redirectStdout != null && process.StandardOutput != null)
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        var fileMode = appendStdout ? FileMode.Append : FileMode.Create;
                        using var fs = new FileStream(redirectStdout, fileMode, FileAccess.Write);
                        using var writer = new StreamWriter(fs);
                        writer.Write(output);
                    }

                    if (redirectStderr != null && process.StandardError != null)
                    {
                        string errorOutput = process.StandardError.ReadToEnd();
                        var fileMode = appendStderr ? FileMode.Append : FileMode.Create;
                        using var fs = new FileStream(redirectStderr, fileMode, FileAccess.Write);
                        using var writer = new StreamWriter(fs);
                        writer.Write(errorOutput);
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
                
                if (redirectStdout != null)
                {
                    var escapedRedirect = EscapeShellArgument(redirectStdout);
                    var operatorSymbol = appendStdout ? ">>" : ">";
                    shellCommand += $" {operatorSymbol} {escapedRedirect}";
                }

                if (redirectStderr != null)
                {
                    var escapedRedirect = EscapeShellArgument(redirectStderr);
                    var operatorSymbol = appendStderr ? "2>>" : "2>";
                    shellCommand += $" {operatorSymbol} {escapedRedirect}";
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