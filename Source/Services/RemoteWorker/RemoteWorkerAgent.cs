using System;
using System.IO;
using System.Net;

namespace FastBuild.Dashboard.Services.RemoteWorker;

internal class RemoteWorkerAgent : IRemoteWorkerAgent
{
    private RemoteWorkerAgent()
    {
    }

    public string FilePath { get; private set; }
    public bool IsLocal { get; private set; }
    public string Version { get; private set; }
    public string User { get; private set; }
    public string HostName { get; private set; }
    public string IPv4Address { get; private set; }
    public string DomainName { get; private set; }
    public string FQDN { get; private set; }
    public string CPUs { get; private set; }
    public string Memory { get; private set; }
    public string Mode { get; private set; }

    public static RemoteWorkerAgent CreateFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var worker = new RemoteWorkerAgent();
        worker.FilePath = filePath;

        try
        {
            foreach (var line in File.ReadAllLines(filePath))
            {
                var propertyName = "";
                var propertyValue = "";

                try
                {
                    var data = line.Split(':');
                    propertyName = data[0].Trim().Replace(" ", "");
                    propertyValue = data[1].Trim();

                    var property = typeof(RemoteWorkerAgent).GetProperty(propertyName);
                    property.SetValue(worker, propertyValue);
                }
                catch
                {
                    //Console.WriteLine($"WARNING: {filePath} has invalid values (Property: {propertyName} - Value: {propertyValue}).");
                    return null;
                }
            }
        }
        catch
        {
            //Console.WriteLine($"WARNING: {filePath} is not valid.");
            return null;
        }

        if (worker.HostName == Dns.GetHostName())
            worker.IsLocal = true;
        else
            worker.IsLocal = false;

        return worker;
    }

    public static RemoteWorkerAgent CreateFromMsgBuffer(byte[] buffer, ref int startIndex)
    {
        // version as string (uint32_t, chars)
        uint versionLength = BitConverter.ToUInt32(buffer, startIndex);
        string version = System.Text.Encoding.UTF8.GetString(buffer, startIndex + 4, (int)versionLength);
        startIndex += 4 + (int)versionLength;
        // userName as string (uint32_t, chars)
        uint userNameLength = BitConverter.ToUInt32(buffer, startIndex);
        string userName = System.Text.Encoding.UTF8.GetString(buffer, startIndex + 4, (int)userNameLength);
        startIndex += 4 + (int)userNameLength;
        // hostName as string (uint32_t, chars)
        uint hostNameLength = BitConverter.ToUInt32(buffer, startIndex);
        string hostName = System.Text.Encoding.UTF8.GetString(buffer, startIndex + 4, (int)hostNameLength);
        startIndex += 4 + (int)hostNameLength;
        // domainName as string (uint32_t, chars)
        uint domainNameLength = BitConverter.ToUInt32(buffer, startIndex);
        string domainName = System.Text.Encoding.UTF8.GetString(buffer, startIndex + 4, (int)domainNameLength);
        startIndex += 4 + (int)domainNameLength;
        // mode as string (uint32_t, chars)
        uint modeLength = buffer[startIndex];
        string mode = System.Text.Encoding.UTF8.GetString(buffer, startIndex + 4, (int)modeLength);
        startIndex += 4 + (int)modeLength;
        // num processors to use as uint32_t
        uint numProcessorsToUse = BitConverter.ToUInt32(buffer, startIndex);
        startIndex += 4;
        // num processors as uint32_t
        uint numProcessors = BitConverter.ToUInt32(buffer, startIndex);
        startIndex += 4;
        // memory MB as uint32_t
        uint memoryMB = BitConverter.ToUInt32(buffer, startIndex);
        startIndex += 4;
        // ip address as uint32_t
        uint ipAddressUint = BitConverter.ToUInt32(buffer, startIndex);
        string ipAddress = new IPAddress(ipAddressUint).ToString();
        startIndex += 4;


        // Use parsed data to create worker
        var worker = new RemoteWorkerAgent();
        worker.IPv4Address = ipAddress;
        worker.HostName = hostName;
        worker.User = userName;
        worker.CPUs = $"{numProcessorsToUse}/{numProcessors}";
        worker.Memory = memoryMB.ToString();
        worker.Mode = mode;
        worker.Version = version;

        if (worker.HostName == Dns.GetHostName())
            worker.IsLocal = true;
        else
            worker.IsLocal = false;

        return worker;
    }
}