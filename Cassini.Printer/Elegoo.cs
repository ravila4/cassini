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
[JsonSerializable(typeof(Elegoo.GetFileListData))]
[JsonSerializable(typeof(Elegoo.DeleteFilesCommand))]
[JsonSerializable(typeof(Elegoo.FileListItem))]
[JsonSerializable(typeof(Elegoo.StartPrintingData))]
public partial class JsonSourceGenContext : JsonSerializerContext
{
}

public class Elegoo
{
    public static string MakeRequestId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public enum MachineStatus
    {
        Idle = 0,
        Printing = 1,
        FileTransferring = 2,
        ExposureTesting = 3,
        SelfTest = 4,
    }

    public enum PrintStatus
    {
        Idle = 0,
        Homing = 1,
        Dropping = 2,
        Exposing = 3,
        Lifting = 4,
        Pausing = 5,
        Paused = 6,
        Stopping = 7,
        Stopped = 8,
        Complete = 9,
        FileChecking = 10,
    }

    public enum PrintInfoError
    {
        None = 0,
        ChecksumError = 1,
        FileError = 2,
        InvalidResolution = 3,
        UnknownFormat = 4,
        UnknownModel = 5,
    }

    // In the history detail list
    public enum ErrorStatusReason
    {
        Ok = 0,
        OverTemperature = 1,
        CalibrationFailed = 2,
        ResinLow = 3,
        ResinRequirementsTooHigh = 4,
        NoResin = 5,
        ForeignObject = 6,
        AutoLevelFailed = 7,
        ModelDetachment = 8,
        StrainGaugeOffline = 9,
        LcdScreenAbnormal = 10,
        ReleaseFilmMax = 11,
        UsbDriveRemoved = 12,
        XAxisMotorAnomaly = 13,
        ZAxisMotorAnomaly = 14,
        ResinAbnormalHigh = 15,
        ResinAbnormalLow = 16,
        HomeCalibrationFailed = 17,
        ModelOnPlatform = 18,
        PrintingException = 19,
        MotorMovementAbnormality = 20,
        NoModelDetected = 21,
        ModelWarpingDetected = 22,
        HomeFailedYDeprecated = 23,
        FileError = 24,
        CameraError = 25,
        NetworkError = 26,
        ServerConnectFailed = 27,
        DisconnectApp = 28,
        CheckAutoResinFeeder = 29,
        ContainerResinLow = 30,
        BottleDisconnect = 31,
        FeedTimeout = 32,
        TankTempSensorOffline = 33,
        TankTempSensorError = 34,
    }

    public enum Command
    {
        RequestStatus = 0,
        RequestAttributes = 1,
        // Takes "Filename" and "StartLayer"
        StartPrinting = 128,
        
        // Empty args
        PausePrinting = 129,
        StopPrinting = 130,
        ResumePrinting = 131,
        StopFeedingMaterial = 132,
        SkipPreheating = 133,
        
        // Takes "Name" for new name
        ChangePrinterName = 192,
        
        // ???
        TerminateFileTransfer = 255,
        
        // Takes "Url", which can be "/usb/" or "/local/". Or any path underneath.
        GetFileList = 258,
        BatchDeleteFiles = 259,
        // Hmm -- SDCP docs have these as 320 and 321
        GetHistoryList = 319, // ; response "Data": { "HistoryData": [ guid, guid, ... ] }
        GetHistoryDetail = 320, // "Data": { "Id": [ guid, ... ] } ; response "HistoryDetailList": [ { lots } ]
        
        
        SetCameraEnabled = 386, // "Data": { "Enable": 0 } ; response contains VideoUrl
        SetTimeLapseEnabled = 387,
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

    public struct StartPrintingData
    {
        public string Filename { get; set; }
        public int StartLayer { get; set; }
    }

    public struct DeleteFilesCommand
    {
        public string[] FileList { get; set; }
        public string[] FolderList { get; set; }
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

    public struct GetFileListData
    {
        public string Url { get; set; }
    }

    public struct FileListItem
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("usedSize")]
        public long UsedSize { get; set; }
        [JsonPropertyName("totalSize")]
        public long TotalSize { get; set; }
        [JsonPropertyName("type")]
        public int TypeCode { get; set; }
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

    public class UploadFileData
    {
        public int Check { get; set; }

        public int Offset { get; set; }

        public string Uuid { get; set; }

        public long TotalSize { get; set; }

        public string Md5 { get; set; }

        public byte[] File { get; set; }
    }
}