using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Cassini.Printer;

namespace Cassini.Cli;

class Program
{
    private static string MainboardID = "ed5847f5c1d70100";
    private static string PrinterID = "f25273b12b094c5a8b9513a30ca60049";

    static async Task Main(string[] args)
    {
        var printer = await Saturn4.Connect(PrinterID, MainboardID, System.Net.IPAddress.Parse("192.168.7.199"));

        await printer.ReceiveTask;
    }
}