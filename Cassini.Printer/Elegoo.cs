using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cassini.Printer;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(Elegoo.BasicCommandWrapper))]
[JsonSerializable(typeof(Elegoo.BasicCommandRequestData))]
[JsonSerializable(typeof(Elegoo.EmptyCommandData))]
[JsonSerializable(typeof(Elegoo.SetCameraEnabledData))]
[JsonSerializable(typeof(Elegoo.SetCameraEnabledReply))]
[JsonSerializable(typeof(Elegoo.PrinterStatus))]
public partial class JsonSourceGenContext : JsonSerializerContext
{
}

public class Elegoo
{
    public static string MakeRequestId()
    {
        return Guid.NewGuid().ToString("N");
    }
    
    public enum Command
    {
        RequestStatus = 0,
        RequestAttributes = 1,
        GetHistoryList = 319, // ; response "Data": { "HistoryData": [ guid, guid, ... ] }
        GetHistoryDetail = 320, // "Data": { "Id": [ guid, ... ] } ; response "HistoryDetailList": [ { lots } ]
        SetCameraEnabled = 386, // "Data": { "Enable": 0 } ; response contains VideoUrl
    }

    public struct BasicCommandRequestData
    {
        public Command Cmd { get; set; }
        public JsonElement Data { get; set; }
        public string RequestID { get; set; }
        public string MainboardID { get; set; }
        public long TimeStamp { get; set; }
        public int From { get; set; }
    }

    public struct EmptyCommandData
    {
    }

    public struct SetCameraEnabledData
    {
        public int Enabled { get; set; }
    }

    public struct SetCameraEnabledReply
    {
        public int Ack { get; set; }
        public string? VideoUrl { get; set; }
    }
    
    public struct BasicCommandWrapper
    {
        public string Id { get; set; }
        public JsonElement Data { get; set; }
    }

    public struct PrinterStatus
    {
        public struct PrinterStatusInfo
        {
            public int Status { get; internal set; }
            public int CurrentLayer { get; internal set; }
            public int TotalLayer { get; internal set; }
            public int CurrentTicks { get; internal set; }
            public int TotalTicks { get; internal set; }
            public int ErrorNumber { get; internal set; }
            public string FileName { get; internal set; }
            public string TaskId { get; internal set; }
        }

        public List<int> CurrentStatus { get; internal set; }
        public double PrintScreen { get; internal set; }
        public int ReleaseFilm { get; internal set; }
        public double TempOfUVLed { get; internal set; }
        public PrinterStatusInfo PrintInfo { get; internal set; }
    }
}