using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

class Program
{
    static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "echo", "exit", "quit", "type", "pwd", "cd", "history"
    };

    static readonly string[] AutocompleteCommands = { "echo", "exit" };

    private static readonly List<string> _history = new();

    static void Main()
    {
        bool run = true;

        while (run)
        {
            Console.Write("$ ");

            string? input = ReadLineWithTabCompletion();
            if (string.IsNullOrWhiteSpace(input)) break;

            input = input.Trim();


            var parsedArgs = ParseCommand(input);
            if (parsedArgs.Count == 0)
            {
                continue;
            }
            
            _history.Add(input);

            var pipeIndices = new List<int>();
            for (int i = 0; i < parsedArgs.Count; i++)
            {
                if (parsedArgs[i] == "|")
                {
                    pipeIndices.Add(i);
                }
            }

            if (pipeIndices.Count > 0)
            {
                var stages = new List<List<string>>();
                int start = 0;

                foreach (var pipeIdx in pipeIndices)
                {
                    if (pipeIdx > start)
                    {
                        stages.Add(parsedArgs.GetRange(start, pipeIdx - start));
                    }
                    start = pipeIdx + 1;
                }

                if (start < parsedArgs.Count)
                {
                    stages.Add(parsedArgs.GetRange(start, parsedArgs.Count - start));
                }

                if (stages.Count > 0)
                {
                    HandleMultiStagePipeline(stages);
                    continue;
                }
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
                    "history" => () => HandleHistory(args),
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

    static void HandleHistory(List<string> args)
    {
        if (args.Count >= 2 && args[0] == "-r")
        {
            string filePath = args[1];

            try
            {
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);

                    foreach (var line in lines)
                    {
                        // Skip empty lines
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            _history.Add(line);
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"history: {filePath}: No such file or directory");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"history: {filePath}: {ex.Message}");
            }

            return;
        }
        else if (args.Count >= 2 && args[0] == "-w")
        {
            string filePath = args[1];
            
            try
            {
                using (var writer = new StreamWriter(filePath, false))
                {
                    for (int i = 0; i < _history.Count; i++)
                    {
                        writer.WriteLine(_history[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"history: {filePath}: {ex.Message}");
            }

            return;
        }
        
        int limit = 0;
        if (args.Count > 0 && int.TryParse(args[0], out int result))
        {
            limit = result;
        }
        
        if (limit <= 0)
        {
            for (int i = 0; i < _history.Count; i++)
            {
                Console.WriteLine($"{i + 1,5}  {_history[i]}");
            }
        }
        else
        {
            int startIndex = Math.Max(0, _history.Count - limit);
            for (int i = startIndex; i < _history.Count; i++)
            {
                Console.WriteLine($"{i + 1,5}  {_history[i]}");
            }
        }
    }

    static void HandleMultiStagePipeline(List<List<string>> stages)
    {
        if (stages.Count == 0)
            return;

        try
        {
            var commandInfo = new List<(string cmd, List<string> args, bool isBuiltin, string? execPath)>();
            
            foreach (var stage in stages)
            {
                if (stage.Count == 0)
                {
                    Console.Error.WriteLine("Empty pipeline stage");
                    return;
                }
                
                string cmd = stage[0];
                var args = stage.Skip(1).ToList();
                bool isBuiltin = Builtins.Contains(cmd.ToLowerInvariant());
                string? execPath = null;
                
                if (!isBuiltin)
                {
                    execPath = FindExecutableInPath(cmd);
                    if (execPath == null)
                    {
                        Console.Error.WriteLine($"{cmd}: command not found");
                        return;
                    }
                }
                
                commandInfo.Add((cmd, args, isBuiltin, execPath));
            }

            if (!OperatingSystem.IsWindows() && commandInfo.All(c => !c.isBuiltin))
            {
                var pipelineParts = new List<string>();
                
                foreach (var (cmd, args, _, execPath) in commandInfo)
                {
                    var escapedCmd = EscapeShellArgument(cmd);
                    var escapedPath = EscapeShellArgument(execPath!);
                    var escapedArgs = string.Join(" ", args.Select(EscapeShellArgument));
                    
                    var part = $"exec -a {escapedCmd} {escapedPath} {escapedArgs}".TrimEnd();
                    pipelineParts.Add(part);
                }
                
                var pipelineCommand = string.Join(" | ", pipelineParts);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    ArgumentList = { "-c", pipelineCommand },
                    UseShellExecute = false
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit();
                }
            }
            else
            {
                HandleMultiStagePipelineWithBuiltins(commandInfo);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error executing pipeline: {ex.Message}");
        }
    }

    static void HandleMultiStagePipelineWithBuiltins(List<(string cmd, List<string> args, bool isBuiltin, string? execPath)> commandInfo)
    {
        var streams = new List<MemoryStream>();
        
        try
        {
            Stream? currentInput = null;
            
            for (int i = 0; i < commandInfo.Count; i++)
            {
                var (cmd, args, isBuiltin, execPath) = commandInfo[i];
                bool isLastStage = (i == commandInfo.Count - 1);
                
                MemoryStream? outputStream = null;
                if (!isLastStage)
                {
                    outputStream = new MemoryStream();
                    streams.Add(outputStream);
                }
                
                if (isBuiltin)
                {
                    TextReader? originalIn = null;
                    TextWriter? originalOut = null;
                    
                    try
                    {
                        if (currentInput != null)
                        {
                            currentInput.Position = 0;
                            var reader = new StreamReader(currentInput);
                            originalIn = Console.In;
                            Console.SetIn(reader);
                        }
                        
                        if (outputStream != null)
                        {
                            var writer = new StreamWriter(outputStream) { AutoFlush = true };
                            originalOut = Console.Out;
                            Console.SetOut(writer);
                        }
                        
                        switch (cmd.ToLowerInvariant())
                        {
                            case "echo": HandleEcho(args); break;
                            case "type": HandleType(args); break;
                            case "pwd": HandlePwd(); break;
                            case "cd": HandleCd(string.Join(" ", args) ?? ""); break;
                            case "history": HandleHistory(args); break;
                        }
                        
                        if (originalOut != null)
                        {
                            Console.Out.Flush();
                        }
                    }
                    finally
                    {
                        if (originalIn != null)
                            Console.SetIn(originalIn);
                        if (originalOut != null)
                            Console.SetOut(originalOut);
                    }
                }
                else
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = execPath!,
                        UseShellExecute = false,
                        RedirectStandardInput = currentInput != null,
                        RedirectStandardOutput = outputStream != null
                    };
                    
                    foreach (var arg in args)
                        startInfo.ArgumentList.Add(arg);
                    
                    using var process = Process.Start(startInfo);
                    if (process != null)
                    {
                        if (currentInput != null && process.StandardInput != null)
                        {
                            currentInput.Position = 0;
                            currentInput.CopyTo(process.StandardInput.BaseStream);
                            process.StandardInput.Close();
                        }
                        
                        if (outputStream != null && process.StandardOutput != null)
                        {
                            process.StandardOutput.BaseStream.CopyTo(outputStream);
                        }
                        
                        process.WaitForExit();
                    }
                }
                
                currentInput = outputStream;
            }
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream?.Dispose();
            }
        }
    }

    static string? ReadLineWithTabCompletion()
    {
        var input = new StringBuilder();
        bool lastKeyWasTab = false;
        int historyIndex = _history.Count;
        string currentTypedInput = "";
        
        while (true)
        {
            var keyInfo = Console.ReadKey(intercept: true);

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return input.ToString();
            }
            else if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                if (historyIndex > 0)
                {
                    if (historyIndex == _history.Count)
                    {
                        currentTypedInput = input.ToString();
                    }

                    historyIndex--;

                    Console.Write("\r$ ");
                    Console.Write(new string(' ', input.Length));
                    Console.Write("\r$ ");

                    input.Clear();
                    input.Append(_history[historyIndex]);
                    Console.Write(_history[historyIndex]);
                }

                lastKeyWasTab = false;
            }
            else if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                if (historyIndex < _history.Count)
                {
                    historyIndex++;

                    Console.Write("\r$ ");
                    Console.Write(new string(' ', input.Length));
                    Console.Write("\r$ ");

                    input.Clear();
                    if (historyIndex < _history.Count)
                    {
                        input.Append(_history[historyIndex]);
                        Console.Write(_history[historyIndex]);
                    }
                    else
                    {
                        input.Append(currentTypedInput);
                        Console.Write(currentTypedInput);
                    }
                }

                lastKeyWasTab = false;
            }
            else if (keyInfo.Key == ConsoleKey.Tab)
            {
                string currentInput = input.ToString();

                if (!currentInput.Contains(' '))
                {
                    var allCompletions = GetCompletionMatches(currentInput);

                    if (allCompletions.Count == 1)
                    {
                        Console.Write("\r$ ");
                        Console.Write(new string(' ', input.Length));
                        Console.Write("\r$ ");

                        string completion = allCompletions[0];
                        input.Clear();
                        input.Append(completion);
                        input.Append(' ');

                        Console.Write(completion + " ");

                        lastKeyWasTab = false;
                    }
                    else if (allCompletions.Count > 1)
                    {
                        string lcp = GetLongestCommonPrefix(allCompletions);

                        if (lcp.Length > currentInput.Length)
                        {
                            Console.Write("\r$ ");
                            Console.Write(new string(' ', input.Length));
                            Console.Write("\r$ ");

                            input.Clear();
                            input.Append(lcp);

                            Console.Write(lcp);

                            lastKeyWasTab = false;
                        }
                        else
                        {
                            if (lastKeyWasTab)
                            {
                                Console.WriteLine();
                                Console.WriteLine(string.Join("  ", allCompletions));
                                Console.Write("$ " + currentInput);

                                lastKeyWasTab = false;
                            }
                            else
                            {
                                Console.Write("\x07");
                                lastKeyWasTab = true;
                            }
                        }
                    }
                    else
                    {
                        Console.Write("\x07");
                        lastKeyWasTab = false;
                    }
                }
                else
                {
                    lastKeyWasTab = false;
                }
            }
            else if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0)
                {
                    input.Length--;
                    Console.Write("\b \b");
                }
                lastKeyWasTab = false;
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                input.Append(keyInfo.KeyChar);
                Console.Write(keyInfo.KeyChar);
                lastKeyWasTab = false;
            }
            else
            {
                lastKeyWasTab = false;
            }
        }
    }

    static string GetLongestCommonPrefix(List<string> strings)
    {
        if (strings.Count == 0)
            return "";
        
        if (strings.Count == 1)
            return strings[0];
        
        string prefix = strings[0];
        
        for (int i = 1; i < strings.Count; i++)
        {
            int j = 0;
            while (j < prefix.Length && j < strings[i].Length && 
                   prefix[j] == strings[i][j])
            {
                j++;
            }
            
            prefix = prefix.Substring(0, j);
            
            if (prefix.Length == 0)
                break;
        }
        
        return prefix;
    }
    
    static List<string> GetCompletionMatches(string prefix)
    {
        var matches = new HashSet<string>();

        foreach (var cmd in AutocompleteCommands)
        {
            if (cmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(cmd);
            }
        }

        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (var rawDir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = rawDir.Trim();

                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    continue;

                try
                {
                    var files = Directory.GetFiles(dir);

                    foreach (var file in files)
                    {
                        string fileName = Path.GetFileName(file);

                        if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && HasExecutePermission(file))
                        {
                            if (OperatingSystem.IsWindows())
                            {
                                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                                string ext = Path.GetExtension(fileName).ToUpperInvariant();

                                var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD;.COM";
                                if (pathext.Contains(ext, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (nameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        matches.Add(nameWithoutExt);
                                    }
                                }

                                matches.Add(fileName);
                            }
                            else
                            {
                                matches.Add(fileName);
                            }
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        return matches.OrderBy(m => m).ToList();
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