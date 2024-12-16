using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting file watcher...");
        await FileWatcher.StartWatching();
    }
}
