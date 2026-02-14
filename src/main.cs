class Program
{
    static void Main()
    {
        bool run = true;

        while (run)
        {
            Console.Write("$ ");

            string? command = Console.ReadLine();

            if (command == "exit")
            {
                run = false;
                break;
            }
        
            Console.WriteLine($"{command}: command not found");
        }
    }
}
