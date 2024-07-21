using System.Net;

namespace Cassini.Printer;

public enum PrinterStatus
{
    Unknown,
    Offline,
    Idle,
    Printing,
    Paused,
}

public struct IntSize2
{
    public int x;
    public int y;
}

public struct FloatSize3
{
    public float x;
    public float y;
    public float z;
}

public struct PrinterAttributes
{
    public string Name;
    public string MachineName;
    public string MachineBrand;
    public IntSize2 Resolution;
    public FloatSize3 SizeInMm;
    public string[] SupportedFileTypes;
    public string[] Capabilities;
}

public interface IPrinter : IDisposable
{
    public string Name { get; }
    public IPAddress Address { get; }
    public string MainboardId { get; }
    public string PrinterId { get; }
    
    public PrinterStatus Status { get; }
    public PrinterAttributes Attributes { get; }

    public Task UploadFile(string path, string? destinationName = null);
    public Task<List<string>> ListFiles();
}

public interface IPrinterCamera
{
    public bool CameraAvailable { get; }

    public Task<Uri> EnableCamera();
    public void DisableCamera();
}