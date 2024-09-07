using System.Net;
using System.Net.WebSockets;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Cassini.Printer;

public class MockPrinter : IPrinter, IPrinterCamera, IDisposable
{
    bool _done = false;
    string _printingFile = null;
    int _printingLayer = 0;

    public static Task<MockPrinter> Connect(IPAddress address, string name)
    {
        return new Task<MockPrinter>(() => { return new MockPrinter(address, name); });
    }

    readonly Dictionary<string, Action<JsonElement, JsonDocument>> _requestReplyHandlers = new();

    readonly Dictionary<string, Action<JsonDocument>> _topicHandlers = new();

    readonly Task _receiveTask;

    internal MockPrinter(IPAddress address, string name)
    {
        PrinterId = name;
        MainboardId = "mb" + name;
        Name = name;
        
        _receiveTask = StartReceiveTask();
    }

    public string Name { get; private set; }

    public IPAddress Address { get; }

    public string MainboardId { get; private set; }

    public string PrinterId { get; private set;  }

    public PrinterStatus Status { get; private set; }

    public PrinterAttributes Attributes { get; private set; }

    public event EventHandler<PrinterStatus>? StatusChanged;
    public event EventHandler<PrinterAttributes>? AttributesChanged;

    public Task ReceiveTask => _receiveTask;

    public Task<List<FileListItem>> ListFiles(string path)
    {
        return Task.Delay(500).ContinueWith((_) =>
        {
            List<FileListItem> files = new();
            foreach (var f in new string[] { "file1", "file2", "file3" })
            {
                files.Add(new FileListItem()
                {
                    Name = f,
                    Size = 1024,
                    IsDirectory = false,
                });
            }

            return files;
        });
    }

    public Task StartPrinting(string file, int layerNumber = 0)
    {
        _printingFile = file;
        _printingLayer = layerNumber;
        return Task.Delay(500).ContinueWith((_) =>
        {
            Status = PrinterStatus.Printing;
            StatusChanged?.Invoke(this, Status);
        });
    }

    public Task PausePrinting()
    {
        return Task.Delay(500).ContinueWith((_) =>
        {
            Status = PrinterStatus.Paused;
            StatusChanged?.Invoke(this, Status);
        });
    }

    public Task ResumePrinting()
    {
        return Task.Delay(500).ContinueWith((_) =>
        {
            Status = PrinterStatus.Printing;
            StatusChanged?.Invoke(this, Status);
        });
    }

    public Task StopPrinting()
    {
        return Task.Delay(500).ContinueWith((_) =>
        {
            Status = PrinterStatus.Idle;
            StatusChanged?.Invoke(this, Status);
        });
    }

    public Task DeleteFiles(IEnumerable<string> files, IEnumerable<string>? folders = null)
    {
        return Task.Delay(500);
    }

    public bool CameraAvailable => throw new NotImplementedException();

    public Task<Uri> EnableCamera()
    {
        return SendRequestAsync(Elegoo.Command.SetCameraEnabled, new Elegoo.SetCameraEnabledData() { Enabled = 1 })
            .ContinueWith(t =>
            {
                var json = t.Result;
                var dd = json.RootElement.GetProperty("Data").GetProperty("Data");
                if (dd.GetIntProperty("Ack") != 0)
                {
                    throw new Exception("Failed to enable camera");
                }

                return new Uri(dd.GetStringProperty("VideoUrl")!);
            });
    }

    public void DisableCamera()
    {
    }

    void OnReceiveStatus(JsonDocument json)
    {
        try
        {
            var status = json.RootElement.GetProperty("Status");
            Console.WriteLine($"Got status: {status}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed parsing status: {e}");
        }
    }

    void OnReceiveAttributes(JsonDocument json)
    {
        try
        {
            var attrs = json.RootElement.GetProperty("Attributes");

            PrinterAttributes n = default;

            n.Name = attrs.GetStringProperty("Name")!;
            n.MachineBrand = attrs.GetStringProperty("BrandName")!;
            n.MachineName = attrs.GetStringProperty("MachineName")!;

            var res = attrs.GetStringProperty("Resolution")!.Split("x").Select(int.Parse).ToArray();
            n.Resolution = new IntSize2() { x = res[0], y = res[1] };
            
            var size = attrs.GetStringProperty("XYZsize")!.Split("x").Select(float.Parse).ToArray();
            n.SizeInMm = new FloatSize3() { x = size[0], y = size[1], z = size[2] };

            n.SupportedFileTypes = attrs.GetStringArrayProperty("SupportedFileTypes")!;
            n.Capabilities = attrs.GetStringArrayProperty("Capabilities")!;

            Name = n.Name;
            Attributes = n;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed parsing attributes: {e}");
        }
    }

    //
    // Message Receiving
    //
    async Task StartReceiveTask()
    {
        while (!_done)
        {
            await Task.Delay(2000);
            MockUpdateStatus();
        }
    }

    void MockUpdateStatus()
    {
    }

    public void Dispose()
    {
        _done = true;
        _receiveTask.Wait();
    }

    public async Task UploadFile(string path, string? destinationName = null)
    {
        await Task.Delay(1000);
    }

    public async static Task UploadFile(Uri address, string path, string? destinationName = null)
    {
        await Task.Delay(1000);
    }
}