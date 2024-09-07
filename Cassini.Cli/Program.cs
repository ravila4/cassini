using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using Cassini.Printer;

namespace Cassini.Cli;

class Program
{
    private static string MainboardID = "ed5847f5c1d70100";
    private static string PrinterID = "f25273b12b094c5a8b9513a30ca60049";

    //
    // Global options:
    //
    //   -h, --help
    //   --printer <host>
    //
    // Subcommands:
    //   status
    //   files [-R] [-l] [<path>]
    //   upload <file> [<destination>]
    //   delete <file>...
    //   print <file>

    static async Task<Saturn4> GetPrinter(string? printerHostname)
    {
        var hostnameLookup = Dns.GetHostEntry(printerHostname);
        return await Saturn4.Connect(hostnameLookup.AddressList[0]);
    }

    public static async Task StatusCommand(string? printerHostname)
    {
        var printer = await GetPrinter(printerHostname);
        Console.WriteLine(printer.Status);
    }

    public static async Task FilesCommand(string? printerHostname, string? path, bool recurse, bool longFormat)
    {
        var printer = await GetPrinter(printerHostname);
        var queue = new Queue<string>();
        queue.Enqueue(path ?? "/usb");
       
        while (queue.Count > 0) {
            var currentPath = queue.Dequeue();
            Console.WriteLine(currentPath + ":");
            var files = await printer.ListFiles(currentPath);
            foreach (var file in files)
            {
                Console.WriteLine(file.Name);
                if (recurse && file.IsDirectory)
                {
                    queue.Enqueue(currentPath + "/" + file.Name);
                }
            }

            Console.WriteLine();
        }
    }

    public static async Task Main(string[] args)
    {
        var printerOption = new Option<string?>(new[] { "--printer", "-p" }, "Printer host");
        var recurseOption = new Option<bool>(new[] { "--recurse", "-R" }, "Recurse into subdirectories");
        var longOption = new Option<bool>(new[] { "--long", "-l" }, "Long listing format");

        var rootCommand = new RootCommand();
        rootCommand.AddGlobalOption(printerOption);
       
        var statusCommand = new Command("status", "Get printer status");
        statusCommand.SetHandler(StatusCommand, printerOption);
        
        var filesCommand = new Command("files", "List files on printer");
        var pathArgument = new Argument<string?>("path", "Path to list");
        filesCommand.AddAlias("ls");
        filesCommand.AddOption(recurseOption);
        filesCommand.AddOption(longOption);
        filesCommand.AddArgument(pathArgument);
        filesCommand.SetHandler(async (context) => {
            var printerHost = context.ParseResult.GetValueForOption(printerOption);
            var recurse = context.ParseResult.GetValueForOption(recurseOption);
            var longFormat = context.ParseResult.GetValueForOption(longOption);
            var path = context.ParseResult.GetValueForArgument(pathArgument);
            await FilesCommand(printerHost, path, recurse, longFormat);
        });
      
        var deleteCommand = new Command("delete", "Delete files on printer");
        var deleteFileArgument = new Argument<string[]>("file", "File to delete");
        deleteCommand.AddAlias("rm");
        deleteCommand.AddArgument(deleteFileArgument);
        deleteCommand.SetHandler(async (context) => {
            var printerHost = context.ParseResult.GetValueForOption(printerOption);
            var files = context.ParseResult.GetValueForArgument(deleteFileArgument);
            var printer = await GetPrinter(printerHost);
            await printer.DeleteFiles(files);
        });
        
        var uploadCommand = new Command("upload", "Upload a file to the printer");
        var fileArgument = new Argument<string>("file", "File to upload");
        var destinationArgument = new Argument<string?>("destination", "Destination path (defaults to /usb/filename)");
        uploadCommand.AddArgument(fileArgument);
        uploadCommand.AddArgument(destinationArgument);
        uploadCommand.SetHandler(async (context) => {
            var printerHost = context.ParseResult.GetValueForOption(printerOption);
            var file = context.ParseResult.GetValueForArgument(fileArgument);
            var destination = context.ParseResult.GetValueForArgument(destinationArgument);
            var printer = await GetPrinter(printerHost);
            await printer.UploadFile(file, destination ?? $"/usb/{Path.GetFileName(file)}");
        });

        var printCommand = new Command("print", "Print a file on the printer");
        var printFileArgument = new Argument<string>("file", "File to print");
        printCommand.AddArgument(printFileArgument);
        printCommand.SetHandler(async (context) => {
            var printerHost = context.ParseResult.GetValueForOption(printerOption);
            var file = context.ParseResult.GetValueForArgument(printFileArgument);
            var printer = await GetPrinter(printerHost);
            await printer.StartPrinting(file);
        });
        
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(filesCommand);
        rootCommand.AddCommand(deleteCommand);
        rootCommand.AddCommand(uploadCommand);
        rootCommand.AddCommand(printCommand);

        await rootCommand.InvokeAsync(args);
    }
}