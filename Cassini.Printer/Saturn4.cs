using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Cassini.Printer;

public class Saturn4 : IPrinter, IPrinterCamera, IDisposable
{
    private const int kReceiveBufferSize = 16 * 1024;

    public static Task<Saturn4> Connect(IPAddress address)
    {
        return Connect(address, new UriBuilder("ws", address.ToString(), 3030, "websocket").Uri);
    }

    public static async Task<Saturn4> Connect(IPAddress address, Uri websocketUri)
    {
        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(websocketUri, CancellationToken.None);
        return new Saturn4(address, webSocket);
    }

    readonly ClientWebSocket _webSocket;

    readonly Dictionary<string, Action<JsonElement, JsonDocument>> _requestReplyHandlers = new();

    readonly Dictionary<string, Action<JsonDocument>> _topicHandlers = new();

    readonly Task _receiveTask;

    internal Saturn4(IPAddress address, ClientWebSocket webSocket)
    {
        // Filled in later
        PrinterId = "";
        MainboardId = "";
        Name = "";
        
        Address = address;
        _webSocket = webSocket;

        _topicHandlers.Add("sdcp/status/", OnReceiveStatus);
        _topicHandlers.Add("sdcp/attributes/", OnReceiveAttributes);

        SendRequest(Elegoo.Command.RequestStatus, new Elegoo.EmptyCommandData(), (data, json) => {
            // The first response to this, we want to pull out the PrinterId and MainboardId.
            PrinterId = json.RootElement.GetStringProperty("Id")!;
            MainboardId = json.RootElement.GetProperty("Data").GetStringProperty("MainboardID")!;
            SendRequest(Elegoo.Command.RequestAttributes);
        });

        _receiveTask = StartReceiveTask();

        // Filled in from attributes later
        Name = "";
    }

    public string Name { get; private set; }

    public IPAddress Address { get; }

    public string MainboardId { get; private set; }

    public string PrinterId { get; private set;  }

    public PrinterStatus Status { get; private set; }

    public PrinterAttributes Attributes { get; private set; }
    
    public Task ReceiveTask => _receiveTask;

    public Task UploadFile(string path, string? destinationName = null)
    {
        throw new NotImplementedException();
    }

    public Task<List<string>> ListFiles()
    {
        throw new NotImplementedException();
    }

    public bool CameraAvailable => throw new NotImplementedException();

    public Task<Uri> EnableCamera()
    {
        return SendRequestAsync(Elegoo.Command.SetCameraEnabled, new Elegoo.SetCameraEnabledData() { Enabled = 1 })
            .ContinueWith(t =>
            {
                var json = t.Result;
                var datadata = json.RootElement.GetProperty("Data").GetProperty("Data");
                if (datadata.GetIntProperty("Ack") != 0)
                {
                    throw new Exception("Failed to enable camera");
                }

                return new Uri(datadata.GetStringProperty("VideoUrl")!);
            });
    }

    public void DisableCamera()
    {
        SendRequest(Elegoo.Command.SetCameraEnabled, new Elegoo.SetCameraEnabledData() { Enabled = 0 });
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
        var buffer = new byte[kReceiveBufferSize];

        while (_webSocket.State == WebSocketState.Open)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine("Received: " + message);
                
                HandleReceivedMessage(new ArraySegment<byte>(buffer, 0, result.Count));
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                Console.WriteLine("Closed: " + result.CloseStatusDescription);
            }
        }
    }

    void HandleReceivedMessage(ArraySegment<byte> utf8Message)
    {
        // Note: we don't Dispose() this explicitly, because we may be passing around
        // JsonElements from it that will be alive outside of this scope
        var json = JsonDocument.Parse(utf8Message);

        string? topic = json.RootElement.GetStringProperty("Topic");
        JsonElement? data = json.RootElement.MaybeProperty("Data");

        if (topic != null && topic.StartsWith("sdcp/response/"))
        {
            if (data.HasValue)
            {
                var reqId = data.Value.GetStringProperty("RequestID")!;
                if (_requestReplyHandlers.Remove(reqId, out var action))
                {
                    JsonElement datadata = data.Value.MaybeProperty("Data").Value;
                    action(datadata, json);
                }
            }
            else
            {
                Console.WriteLine($"Warning: got topic {topic} but Data has no RequestID: {data}");
            }

            return;
        }

        if (topic == null)
        {
            if (_topicHandlers.TryGetValue("NULL", out var nullHandler))
            {
                nullHandler(json);
            }
            else
            {
                Console.WriteLine($"No topic and no NULL handler: {json}");
            }
        }
        else
        {
            var handled = false;
            foreach (var (key, handler) in _topicHandlers)
            {
                if (topic.StartsWith(key))
                {
                    handled = true;
                    handler(json);
                }
            }

            if (!handled)
            {
                Console.WriteLine($"Not handled: {json}");
            }
        }
    }

    //
    // Message Sending Helpers
    //
    
    async void SendMessage<T>(T t)
    {
        var msg = JsonSerialize(t);
        Console.WriteLine(msg);
        await _webSocket.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text,
            WebSocketMessageFlags.EndOfMessage, CancellationToken.None);
    }

    void SendRequest(Elegoo.Command command, Action<JsonElement, JsonElement?>? replyHandler = null)
    {
        SendRequest(command, new Elegoo.EmptyCommandData());
    }

    void SendRequest<T>(Elegoo.Command command, T data, Action<JsonElement, JsonDocument>? replyHandler = null)
    {
        var requestId = Elegoo.MakeRequestId();
        var cmdData = new Elegoo.BasicCommandRequestData()
        {
            Cmd = command,
            Data = JsonSerializeToElement(data),
            
            From = 1,
            MainboardID = MainboardId,
            RequestID = requestId,
            TimeStamp = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds(),
        };

        var message = new Elegoo.BasicCommandWrapper()
        {
            Id = PrinterId,
            Data = JsonSerializeToElement(cmdData),
        };

        if (replyHandler != null)
        {
            _requestReplyHandlers[requestId] = replyHandler;
        }

        SendMessage(message);
    }

    public async Task<JsonDocument> SendRequestAsync<T>(Elegoo.Command command, T data)
    {
        var tcs = new TaskCompletionSource<JsonDocument>();

        SendRequest(command, data, (_, json) => {
            tcs.SetResult(json);
        });

        return await tcs.Task;
    }

    static string JsonSerialize<T>(T t)
    {
        return JsonSerializer.Serialize(t, typeof(T), JsonSourceGenContext.Default);
    }

    static JsonElement JsonSerializeToElement<T>(T t)
    {
        return JsonSerializer.SerializeToElement(t, typeof(T), JsonSourceGenContext.Default);
    }

    public void Dispose()
    {
        _webSocket.Dispose();
        _receiveTask.Wait();
    }
}