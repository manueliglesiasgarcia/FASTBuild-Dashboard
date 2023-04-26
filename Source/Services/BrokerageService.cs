using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Caliburn.Micro;
using FastBuild.Dashboard.Services.RemoteWorker;
using FastBuild.Dashboard.Services.Worker;
using System.Net.Sockets;

namespace FastBuild.Dashboard.Services;

public class WorkerListChangedEventArgs : EventArgs
{
    public HashSet<IRemoteWorkerAgent> RemoteWorkers { get; set; }
}

internal class BrokerageService : IBrokerageService
{
    private const string WorkerPoolRelativePath = @"broker\22.windows";

    private bool _isUpdatingWorkers;

    private string[] _workerNames;

    public BrokerageService()
    {
        _workerNames = new string[0];

        var checkTimer = new Timer(5000);
        checkTimer.Elapsed += CheckTimer_Elapsed;
        checkTimer.AutoReset = true;
        checkTimer.Enabled = true;
        UpdateWorkers();
    }

    public string[] WorkerNames
    {
        get => _workerNames;
        private set
        {
            var oldCount = _workerNames.Length;
            _workerNames = value;

            if (oldCount != _workerNames.Length) WorkerCountChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string BrokeragePath
    {
        get => Environment.GetEnvironmentVariable("FASTBUILD_BROKERAGE_PATH");
        set => Environment.SetEnvironmentVariable("FASTBUILD_BROKERAGE_PATH", value);
    }

    public string CoordinatorAddress
    {
        get => Environment.GetEnvironmentVariable("FASTBUILD_COORDINATOR");
        set => Environment.SetEnvironmentVariable("FASTBUILD_COORDINATOR", value);
    }

    public event EventHandler WorkerCountChanged;
    public event EventHandler<WorkerListChangedEventArgs> WorkerListChanged;

    private void CheckTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
        UpdateWorkers();
    }

    private void UpdateWorkers()
    {
        if (_isUpdatingWorkers)
            return;

        _isUpdatingWorkers = true;
        HashSet<IRemoteWorkerAgent> remoteWorkers = new HashSet<IRemoteWorkerAgent>();

        try
        {
            var brokeragePath = BrokeragePath;
            var coordinatorAddress = CoordinatorAddress;
            if (string.IsNullOrEmpty(brokeragePath) && string.IsNullOrEmpty(coordinatorAddress))
            {
                remoteWorkers = new HashSet<IRemoteWorkerAgent>();
                WorkerNames = new string[0];
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(coordinatorAddress))
                {
                    // Send TCP packate to coordinator to get list of workers on port 31264+128.
                    int port = 31264 + 128;
                    TcpClient client = new TcpClient(coordinatorAddress, port);

                    // Send message Protocol::MsgRequestWorkerList of size 12+4 in format:
                    // uint32_t BytesToRead = Message - 4
                    // uint8_t m_MsgType = MSG_REQUEST_WORKER_LIST = 12
                    // uint8_t m_MsgSize = 12
                    // bool m_HasPayload = false
                    // char m_Padding1 = 0
                    // uint32_t m_ProtocolVersion = 22
                    // uint8_t m_Platform = Env::Platform = 0
                    // bool m_RequestWorkerInfo = 1
                    byte[] message = new byte[16];
                    message[0] = (byte)(message.Length - 4);
                    message[4] = 12;
                    message[5] = 12;
                    message[6] = 0;
                    message[7] = 0;
                    message[8] = 22;
                    message[12] = 0;
                    message[13] = 1;
                    // Send message.
                    NetworkStream stream = client.GetStream();
                    stream.Write(message, 0, message.Length);

                    // Read response.
                    byte[] buffer = new byte[1024*16];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    // Parse response if valid type.
                    if (bytesRead >= 12
                        && buffer[0] == 4 // MsgWorkerList size.
                        && buffer[4] == 13 // MSG_WORKER_LIST = 13
                        && buffer[5] == 4 // MsgWorkerList size.
                        && buffer[6] == 1) // m_HasPayload = true
                    {
                        int index = 16;
                        while (index < bytesRead)
                        {
                            // Get IP address from 4 bytes in buffer to string.
                            IRemoteWorkerAgent worker = RemoteWorkerAgent.CreateFromMsgBuffer(buffer, ref index);
                            if (worker == null)
                                continue;

                            remoteWorkers.Add(worker);

                            if (worker.IsLocal) IoC.Get<IWorkerAgentService>().SetLocalWorker(worker);
                        }
                    }
                    
                    return;
                }
            }
            catch
            {
                remoteWorkers = new HashSet<IRemoteWorkerAgent>();
                WorkerNames = new string[0];
            }

            try
            {
                WorkerNames = Directory.GetFiles(Path.Combine(brokeragePath, WorkerPoolRelativePath))
                    .Select(Path.GetFullPath)
                    .ToArray();

                foreach (var workerFile in WorkerNames)
                {
                    IRemoteWorkerAgent worker = RemoteWorkerAgent.CreateFromFile(workerFile);
                    if (worker == null)
                        continue;

                    remoteWorkers.Add(worker);

                    if (worker.IsLocal) IoC.Get<IWorkerAgentService>().SetLocalWorker(worker);
                }
            }
            catch (IOException)
            {
                remoteWorkers = new HashSet<IRemoteWorkerAgent>();
                WorkerNames = new string[0];
            }
        }
        finally
        {
            var args = new WorkerListChangedEventArgs();
            args.RemoteWorkers = remoteWorkers;

            WorkerListChanged?.Invoke(this, args);

            _isUpdatingWorkers = false;
        }
    }
}
