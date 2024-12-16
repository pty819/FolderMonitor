using System;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Collections.Generic;

public class FileWatcher
{
    private static CancellationTokenSource cts = new CancellationTokenSource();

    public static async Task StartWatching()
    {
        // Create a channel for file change events
        var channel = Channel.CreateUnbounded<FileChangeEvent>();

        // Read the JSON file
        if (!File.Exists("folders.json"))
        {
            Console.WriteLine("Error: 'folders.json' file not found.");
            return;
        }

        string jsonString = await File.ReadAllTextAsync("folders.json");
        var folders = JsonSerializer.Deserialize<Folders>(jsonString);

        if (folders == null || folders.folders == null)
        {
            Console.WriteLine("Error: Invalid JSON format in 'folders.json'.");
            return;
        }

        // Start multiple producers
        var producerTasks = new List<Task>();
        foreach (var folder in folders.folders)
        {
            if (folder == null || string.IsNullOrEmpty(folder.path) || string.IsNullOrEmpty(folder.name))
            {
                Console.WriteLine($"Error: Invalid folder entry in 'folders.json'.");
                continue;
            }

            if (!Directory.Exists(folder.path))
            {
                Console.WriteLine($"Warning: The directory '{folder.path}' does not exist. Skipping.");
                continue;
            }

            producerTasks.Add(Task.Run(async () => await ProduceFileChanges(folder, channel.Writer, cts.Token)));
        }

        // Start multiple consumers
        var consumerTasks = new Task[Environment.ProcessorCount];
        for (int i = 0; i < consumerTasks.Length; i++)
        {
            consumerTasks[i] = Task.Run(() => ConsumeFileChanges(channel.Reader, cts.Token));
        }

        // Wait for the user to hit 'q' to quit the sample.
        Console.WriteLine("Press 'q' to quit the sample.");
        await Task.Run(() =>
        {
            while (Console.Read() != 'q') ;
        }, cts.Token);

        // Stop the consumers and producers
        cts.Cancel();
        try
        {
            await Task.WhenAll(producerTasks);
            await Task.WhenAll(consumerTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling tasks
        }
    }

    private static async Task ProduceFileChanges(Folder folder, ChannelWriter<FileChangeEvent> writer, CancellationToken cancellationToken)
    {
        try
        {
            // Create a new FileSystemWatcher for the folder
            using (var watcher = new FileSystemWatcher())
            {
                watcher.Path = folder.path;
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
                watcher.Filter = "*.*";
                watcher.IncludeSubdirectories = true; // Enable recursive monitoring
                watcher.Changed += async (source, e) => await OnChanged(source, e, folder.name, writer, cancellationToken);
                watcher.Created += async (source, e) => await OnChanged(source, e, folder.name, writer, cancellationToken);
                watcher.Deleted += async (source, e) => await OnChanged(source, e, folder.name, writer, cancellationToken);
                watcher.Renamed += async (source, e) => await OnRenamed(source, e, folder.name, writer, cancellationToken);

                // Begin watching
                watcher.EnableRaisingEvents = true;
                Console.WriteLine($"Started watching folder: {folder.path} and its subdirectories");

                // Wait for cancellation
                await Task.Delay(-1, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelling tasks
            Console.WriteLine($"FileSystemWatcher for folder {folder.path} was canceled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting up FileSystemWatcher for folder: {folder.path}. Error: {ex.Message}");
        }
    }

    private static async Task OnChanged(object source, FileSystemEventArgs e, string folderName, ChannelWriter<FileChangeEvent> writer, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Event detected in folder: {folderName}, File: {e.FullPath}, ChangeType: {e.ChangeType}");
        await writer.WriteAsync(new FileChangeEvent(folderName, e.FullPath, e.ChangeType), cancellationToken);
    }

    private static async Task OnRenamed(object source, RenamedEventArgs e, string folderName, ChannelWriter<FileChangeEvent> writer, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Event detected in folder: {folderName}, File: {e.OldFullPath} renamed to {e.FullPath}");
        await writer.WriteAsync(new FileChangeEvent(folderName, e.FullPath, WatcherChangeTypes.Renamed, e.OldFullPath), cancellationToken);
    }

    private static async Task ConsumeFileChanges(ChannelReader<FileChangeEvent> reader, CancellationToken cancellationToken)
    {
        while (await reader.WaitToReadAsync(cancellationToken))
        {
            while (reader.TryRead(out var fileChangeEvent))
            {
                if (fileChangeEvent.ChangeType == WatcherChangeTypes.Renamed)
                {
                    Console.WriteLine($"Folder: {fileChangeEvent.FolderName}, File: {fileChangeEvent.OldFullPath} renamed to {fileChangeEvent.FilePath}");
                }
                else
                {
                    Console.WriteLine($"Folder: {fileChangeEvent.FolderName}, File: {fileChangeEvent.FilePath} {fileChangeEvent.ChangeType}");
                }
            }
        }
    }
}

public class Folders
{
    public required List<Folder> folders { get; set; }
}

public class Folder
{
    public required string path { get; set; }
    public required string name { get; set; }
}

public class FileChangeEvent
{
    public string FolderName { get; set; }
    public string FilePath { get; set; }
    public WatcherChangeTypes ChangeType { get; set; }
    public string? OldFullPath { get; set; }

    public FileChangeEvent(string folderName, string filePath, WatcherChangeTypes changeType, string? oldFullPath = null)
    {
        FolderName = folderName;
        FilePath = filePath;
        ChangeType = changeType;
        OldFullPath = oldFullPath;
    }
}
