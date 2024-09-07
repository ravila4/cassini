using System.Net;
using System.Net.WebSockets;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

    public event EventHandler<PrinterStatus>? StatusChanged;
    public event EventHandler<PrinterAttributes>? AttributesChanged;

    public Task ReceiveTask => _receiveTask;

    public Task<List<FileListItem>> ListFiles(string path)
    {
        return SendRequestAsync(Elegoo.Command.GetFileList, new Elegoo.GetFileListData() { Url = path })
            .ContinueWith(t =>
            {
                var json = t.Result;
                var dd = json.RootElement.GetProperty("Data").GetProperty("Data");
                if (dd.GetIntProperty("Ack") != 0)
                {
                    throw new Exception($"Command failed");
                }

                var files = new List<FileListItem>();
                foreach (var item in dd.GetProperty("FileList").EnumerateArray())
                {
                    var name = item.GetStringProperty("name")!;
                    var type = item.GetIntProperty("type")!;
                    var size = item.GetIntProperty("usedSize") ?? 0;

                    // we're going to strip the prefix here for sanity
                    if (name.StartsWith(path + "/"))
                    {
                        name = name.Remove(0, path.Length + 1);
                    }
                    else
                    {
                        Console.Error.WriteLine($"Warning: file list item '{name}' doesn't start with '{path}/'");
                    }

                    // type == 0 is a directory, 
                    files.Add(new FileListItem()
                    {
                        Name = name,
                        Size = (long) size,
                        IsDirectory = type == 0,
                    });
                }

                return files;
            });
    }

    public Task StartPrinting(string file, int layerNumber = 0)
    {
        return SendRequestAsync(Elegoo.Command.StartPrinting, new Elegoo.StartPrintingData() { Filename = file, StartLayer = layerNumber })
            .ContinueWith(t =>
            {
                var json = t.Result;
                var dd = json.RootElement.GetProperty("Data").GetProperty("Data");
                if (dd.GetIntProperty("Ack") != 0)
                {
                    throw new Exception($"Command failed");
                }
            });
    }

    internal Task SimpleCommandRequest(Elegoo.Command command)
    {
        return SendRequestAsync(command, new Elegoo.EmptyCommandData())
            .ContinueWith(t =>
            {
                var json = t.Result;
                var dd = json.RootElement.GetProperty("Data").GetProperty("Data");
                if (dd.GetIntProperty("Ack") != 0)
                {
                    throw new Exception($"Command failed");
                }
            });
    }
    
    public Task PausePrinting()
    {
        return SimpleCommandRequest(Elegoo.Command.PausePrinting);
    }

    public Task ResumePrinting()
    {
        return SimpleCommandRequest(Elegoo.Command.ResumePrinting);
    }

    public Task StopPrinting()
    {
        return SimpleCommandRequest(Elegoo.Command.StopPrinting);
    }

    public Task DeleteFiles(IEnumerable<string> files, IEnumerable<string>? folders = null)
    {
        return SendRequestAsync(Elegoo.Command.BatchDeleteFiles, new Elegoo.DeleteFilesCommand() { FileList = files.ToArray(), FolderList = folders?.ToArray() ?? Array.Empty<string>() })
            .ContinueWith(t =>
            {
                var json = t.Result;
                var dd = json.RootElement.GetProperty("Data").GetProperty("Data");
                if (dd.GetIntProperty("Ack") != 0)
                {
                    throw new Exception($"Command failed");
                }
            });
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
                    JsonElement dd = data.Value.MaybeProperty("Data").Value;
                    action(dd, json);
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

    public async Task UploadFile(string path, string? destinationName = null)
    {
        await UploadFile(new Uri($"http://{Address}:3030/uploadFile/upload"), path, destinationName);
    }

    public async static Task UploadFile(Uri address, string path, string? destinationName = null)
    {
        // Uploading a file is a multipart/form-data PUT to http://${MainboardIP}:3030/uploadFile/upload
        // Each chunk can be a maximum of 1MB in size. Each form has these fields:
        //
        // There are request parameters (form fields?):
        // S-File-MD5
        // Check: 1
        // Offset: the byte offset of the total of this chunk
        // Uuid: an uuid, same for each part
        // TotalSize: the total byte size
        // File: the binary chunk
   
        var file = File.OpenRead(path);
        var totalSize = file.Length;
        var uuid = Guid.NewGuid().ToString();
        var buffer = new byte[1024 * 1024];
        var offset = 0;
        var md5 = MD5.Create();
        var md5Hash = md5.ComputeHash(file);
        var md5String = BitConverter.ToString(md5Hash).Replace("-", "").ToLower();
       
        var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "Cassini.Printer");
        //http.DefaultRequestHeaders.Add("Accept", "application/json");
        //http.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        //http.DefaultRequestHeaders.Add("Connection", "keep-alive");
        //http.DefaultRequestHeaders.Add("Content-Type", "multipart/form-data");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("multipart/form-data"));
        
        file.Seek(0, SeekOrigin.Begin);
        while (true)
        {
            var bytesRead = file.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            var chunk = new byte[bytesRead];
            Array.Copy(buffer, chunk, bytesRead);

            var form = new MultipartFormDataContent();
            form.Headers.ContentType.MediaType = "multipart/form-data"; // ?
            form.Add(new StringContent(md5String), "S-File-MD5");
            form.Add(new StringContent("1"), "Check");
            form.Add(new StringContent(offset.ToString()), "Offset");
            form.Add(new StringContent(uuid), "Uuid");
            form.Add(new StringContent(totalSize.ToString()), "TotalSize");
            form.Add(new ByteArrayContent(chunk), "File", destinationName ?? Path.GetFileName(path));

            var resultMessage = await http.PostAsync(address, form);
            var result = await resultMessage.Content.ReadAsStringAsync();

            offset += bytesRead;
            Console.WriteLine($"Uploaded chunk: {offset},{bytesRead} -> {result}");
        }
    }

        /*
         * // 2. Create the url 
string url = "https://myurl.com/api/...";
string filename = "myFile.png";
// In my case this is the JSON that will be returned from the post
string result = "";
// 1. Create a MultipartPostMethod
// "NKdKd9Yk" is the boundary parameter

using (var formContent = new MultipartFormDataContent("NKdKd9Yk"))
{
    formContent.Headers.ContentType.MediaType = "multipart/form-data";
    // 3. Add the filename C:\\... + fileName is the path your file
    Stream fileStream = System.IO.File.OpenRead("C:\\Users\\username\\Pictures\\" + fileName);
    formContent.Add(new StreamContent(fileStream), fileName, fileName);

    using (var client = new HttpClient())
    {
        // Bearer Token header if needed
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _bearerToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("multipart/form-data"));

        try
        {
            // 4.. Execute the MultipartPostMethod
            var message = await client.PostAsync(url, formContent);
            // 5.a Receive the response
            result = await message.Content.ReadAsStringAsync();                
        }
        catch (Exception ex)
        {
            // Do what you want if it fails.
            throw ex;
        }
    }    
}

// 5.b Process the reponse Get a usable object from the JSON that is returned
MyObject myObject = JsonConvert.DeserializeObject<MyObject>(result);

         */
}