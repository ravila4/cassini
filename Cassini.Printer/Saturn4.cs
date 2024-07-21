using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Cassini.Printer;

public class Saturn4 : IPrinter, IPrinterCamera, IDisposable
{
    private const int kReceiveBufferSize = 16 * 1024;

    public static Task<Saturn4> Connect(string printerId, string mainboardId, IPAddress address)
    {
        return Connect(printerId, mainboardId, address, new UriBuilder("ws", address.ToString(), 3030, "websocket").Uri);
    }

    public static async Task<Saturn4> Connect(string printerId, string mainboardId, IPAddress address, Uri websocketUri)
    {
        var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(websocketUri, CancellationToken.None);
        return new Saturn4(printerId, mainboardId, address, webSocket);
    }

    readonly ClientWebSocket _webSocket;

    Dictionary<string, Action<JsonElement, JsonElement?>> _requestReplyHandlers = new();

    Dictionary<string, Action<JsonDocument>> _topicHandlers = new();

    Task _receiveTask;
    
    internal Saturn4(string printerId, string mainboardId, IPAddress address, ClientWebSocket webSocket)
    {
        PrinterId = printerId;
        MainboardId = mainboardId;
        Address = address;
        _webSocket = webSocket;

        _topicHandlers.Add("sdcp/status/", OnReceiveStatus);
        _topicHandlers.Add("sdcp/attributes/", OnReceiveAttributes);
        
        SendRequest(Elegoo.Command.RequestStatus);
        SendRequest(Elegoo.Command.RequestAttributes);
        
        _receiveTask = StartReceiveTask();
    }

    public string Name { get; private set; }

    public IPAddress Address { get; }

    public string MainboardId { get; }

    public string PrinterId { get; }

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
        throw new NotImplementedException();
    }

    public Task DisableCamera()
    {
        throw new NotImplementedException();
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

            var res = attrs.GetStringProperty("Resolution")!.Split("x").Select(s => int.Parse(s)).ToArray();
            n.Resolution = new IntSize2() { x = res[0], y = res[1] };
            
            var size = attrs.GetStringProperty("XYZsize")!.Split("x").Select(s => float.Parse(s)).ToArray();
            n.SizeInMm = new FloatSize3() { x = size[0], y = size[1], z = size[2] };

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
                
                HandleReceivedMessage(buffer);
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
                    JsonElement? datadata = data.Value.MaybeProperty("Data");
                    action(data.Value, datadata);
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

    void SendRequest<T>(Elegoo.Command command, T data, Action<JsonElement, JsonElement?>? replyHandler = null)
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