using ConsoleAppFramework;
using ncmdump_net.src;

internal class Program
{
    static private readonly List<string> InputFiles = [];
    private static void Main(string[] args)
    {
        var i = 0;
        for (; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                InputFiles.Add(args[i].Trim('\'', '"'));
            }
        }

        if (i < args.Length)
        {
            args = args[i..];
        }
        else
        {
            args = [];
        }
        ConsoleApp.Version = "1.0";
        ConsoleApp.Run(args, Commands.Process);
    }

    static class Commands
    {
        public static void Process(string o = "", string? d = null, bool r = false)
        {
            foreach (string inputFile in InputFiles)
            {
                Console.WriteLine($"[Info] Processing '{inputFile}'");
                try
                {
                    ProcessFile(inputFile, o);
                }
                catch (Exception)
                {
                    Console.WriteLine($"[Error] Failed to process file '{inputFile}");
                }
            }
            if (d is not null)
            {
                ProcessDirectory(d, o, r);
            }
        }
    }

    private static void ProcessFile(string filePath, string outputDir)
    {
        // skip if the extension is not .ncm
        if (!Path.GetExtension(filePath).Equals(".ncm", StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        // process the file
        NeteaseCloudMusic? currentFile;
        try
        {
            currentFile = new NeteaseCloudMusic(filePath);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Reading '{filePath}' failed: {e.Message}");
            throw new Exception("failed to process file");
        }

        try
        {
            currentFile.Dump(outputDir);
            try
            {
                currentFile.FixMetadata(true);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Warning] Fixing metadata of '{filePath}' failed: {e.Message}");
                throw new Exception("failed to process file");
            }
            Console.WriteLine($"[Done] Processed '{filePath}' to '{currentFile.GetDumpFilePath()}'");
            return;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[Error] Processing '{filePath}' failed: {e.Message}");
            throw new Exception("failed to process file");
        }
    }

    private static void ProcessDirectory(string directoryPath, string outputDir, bool recursive)
    {
        var dir = new DirectoryInfo(directoryPath);
        if (!dir.Exists)
        {
            Console.WriteLine($"[Error] Directory '{directoryPath}' does not exist");
            return;
        }
        foreach (var file in dir.GetFiles("*.ncm"))
        {
            Console.WriteLine($"[Info] Processing '{file.FullName}'");
            try
            {
                ProcessFile(file.FullName, outputDir);
            }
            catch (Exception)
            {
                Console.WriteLine($"[Error] Failed to process file '{file.FullName}");
            }
        }
        if (recursive)
        {
            foreach (var subdir in dir.GetDirectories())
            {
                ProcessDirectory(subdir.FullName, outputDir, recursive);
            }
        }
    }
}