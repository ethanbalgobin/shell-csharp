class Program
{
    static void Main()
    {
        bool run = true;

        while (run)
        {
        Console.Write("$ ");

        string command = Console.ReadLine();
        Console.WriteLine($"{command}: command not found");
        }
    }
}
