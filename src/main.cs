class Program
{
    static void Main()
    {
        bool run = true;

        while (run)
        {
            Console.Write("$ ");

            string? input = Console.ReadLine();
            if (input is null) break; // Ctrl+Z/Ctrl+D ends input

            input = input.Trim();

            var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            string args = parts.Length == 2 ? parts[1] : "";

            Action action = cmd switch
            {
                "" => () => { }
                ,
                "echo" => () => HandleEcho(args),
                "exit" or "quit" => () => run = false,
                _ => () => Console.WriteLine($"{cmd}: command not found")
            };

            action();
        }
    }

    static void HandleEcho(string statement)
    {
        Console.WriteLine(statement);
    }
}
