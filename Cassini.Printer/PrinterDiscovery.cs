using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Cassini.Printer;

public static class PrinterDiscovery
{
    public abstract class DiscoveryResult
    {
        public string Hostname { get; internal set; }
        public string Name { get; internal set; }
        public string MainboardId { get; internal set; }
        public string PrinterId { get; internal set; }
        public abstract Task<IPrinter> Connect();
    }
    
    public class Saturn4Result : DiscoveryResult
    {
        Saturn4 m_Printer = null;
        Task<IPrinter> m_ConnectionTask = null;
        
        public override async Task<IPrinter> Connect()
        {
            if (m_Printer != null)
                return m_Printer;
            
            if (m_ConnectionTask != null)
                return await m_ConnectionTask;

            var hostnameLookup = Dns.GetHostEntry(Hostname);
            m_ConnectionTask = Saturn4.Connect(hostnameLookup.AddressList[0]).ContinueWith((p) => {
                m_Printer = p.Result;
                m_ConnectionTask = null;
                return (IPrinter) m_Printer;
            });

            return await m_ConnectionTask;
        }
    }

    public static async Task<DiscoveryResult> DiscoverPrinter(string hostname)
    {
        return new Saturn4Result
        {
            Hostname = hostname,
            Name = hostname,
            MainboardId = "Saturn4",
            PrinterId = "Saturn4"
        };
    }

    public static async Task<IList<DiscoveryResult>> DiscoverPrinters(int timeoutMs = 5000)
    {
        var results = new List<DiscoveryResult>();
        
        // Broadcast "M99999" via UDP to port 3000, and listen to packets on port 3000
        // for 5 seconds. Each response is a JSON object.
       
        var udpClient = new UdpClient(3000);
        udpClient.EnableBroadcast = true;
        udpClient.Send(new byte[] { (byte)'M', (byte)'9', (byte)'9', (byte)'9', (byte)'9', (byte)'9' }, 7, new IPEndPoint(IPAddress.Broadcast, 3000));

        CancellationTokenSource cts = new CancellationTokenSource();
        cts.CancelAfter(timeoutMs);
        while (true)
        {
            try
            {
                var reply = await udpClient.ReceiveAsync(cts.Token);
                Console.WriteLine($"Received {reply.Buffer.Length} bytes from {reply.RemoteEndPoint}");
                Console.WriteLine(Encoding.UTF8.GetString(reply.Buffer));
            } catch (OperationCanceledException)
            {
                break;
            }
        }

        return results;
    }
}