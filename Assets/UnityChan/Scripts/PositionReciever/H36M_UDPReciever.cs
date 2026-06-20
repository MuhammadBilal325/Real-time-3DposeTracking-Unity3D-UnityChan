using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;


public class H36M_UDPReciever : MonoBehaviour
{
    [Header("UDP")][SerializeField] private int port = 5005;
    [Header("Confidence")][SerializeField] private float confidenceThreshold = 0.5f;
    [Header("Scaling")][SerializeField] private float targetSpineLength = 0.5f;
    private readonly int HIPS = 0, SPINE1 = 8;


    private Socket socket;
    private Thread receiveThread;
    private bool isRunning = true;
    private Vector3[] currentPositions = new Vector3[17];
    private Vector2[] current2DPositions = new Vector2[17];
    private Vector2 imageDimensions = new Vector2(1, 1);
    private bool gotFirstData = false;
    private readonly object dataLock3D = new object();
    private readonly object dataLock2D = new object();

    void Start()
    {
        for (int i = 0; i < currentPositions.Length; i++) currentPositions[i] = Vector3.zero;
        StartUDP();
    }

    void StartUDP()
    {
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
            receiveThread = new Thread(ReceiveData) { IsBackground = true };
            receiveThread.Start();
            Debug.Log($"UDP listening on port {port}");
        }
        catch (System.Exception e) { Debug.LogError($"UDP init fail: {e.Message}"); }
    }

    void ReceiveData()
    {
        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
        byte[] buffer = new byte[4096];

        Vector3[] positions = new Vector3[17];
        Vector2[] positions2D = new Vector2[17];
        Vector2 imageSize = new Vector2(1, 1);
        float[] confidence = new float[17];
        while (isRunning)
        {
            try
            {
                int len = socket.ReceiveFrom(buffer, ref remoteEP);
                int totalFloats = (len - 1) / 4; // Total floats in the datagram, excluding the first byte for flags
                float[] vals = new float[totalFloats];
                //Copy the received bytes into a float array, skipping the first byte which is used for flags
                byte flags = buffer[0];
                System.Buffer.BlockCopy(buffer, 1, vals, 0, len - 1);
                //Flags shows which data is included: bit 0 = 3D pose, bit 1 = 2D pose, bit 2 = confidence
                //Datagram layout: [flags (1 byte)][pose_3d (17*3*4 bytes)]
                //[image_width (4 bytes)][image_height (4 bytes)][pose_2d (17*2*4 bytes)]
                //[confidence_2d (17*4 bytes)]
                bool hasConfidence = (flags & 0x4) != 0;
                bool has3D = (flags & 0x1) != 0;
                bool has2D = (flags & 0x2) != 0;

                int index = 0;
                if (has3D)
                {
                    for (int i = 0; i < 17; i++)
                    {
                        float px = vals[index];
                        float py = vals[index + 1];
                        float pz = vals[index + 2];
                        index += 3;
                        positions[i] = new Vector3(px, py, -pz);
                    }
                }
                if (has2D)
                {
                    imageSize.x = vals[index];
                    imageSize.y = vals[index + 1];
                    index += 2;
                    for (int i = 0; i < 17; i++)
                    {
                        float px = vals[index];
                        float py = vals[index + 1];
                        index += 2;
                        positions2D[i] = new Vector2(px, py);
                    }
                }
                //If confidence data is included, check if any limb falls below confidence threshold and if so, skip updating the positions
                if (hasConfidence)
                {
                    bool lowConfidence = false;
                    for (int i = 0; i < 17; i++)
                    {
                        index += 1;
                        if (vals[index - 1] < confidenceThreshold)
                        {
                            lowConfidence = true;
                            break;
                        }

                    }
                    if (lowConfidence) continue;
                }

                float spineLen = Vector3.Distance(positions[HIPS], positions[SPINE1]);
                if (spineLen > 0.001f)
                {
                    float scale = targetSpineLength / spineLen;
                    for (int i = 0; i < 17; i++) positions[i] *= scale;
                }

                // Rotate the positions by 90 degrees around the X-axis to match Unity's coordinate system
                Quaternion rotation = Quaternion.Euler(90, 0, 0);
                for (int i = 0; i < positions.Length; i++)
                    positions[i] = rotation * positions[i];



                lock (dataLock3D) { currentPositions = positions; }
                lock (dataLock2D) { current2DPositions = positions2D; imageDimensions = imageSize; }
                gotFirstData = true;


            }
            catch (System.Exception e) { if (isRunning) Debug.LogError($"UDP recv: {e.Message}"); }
        }
    }



    void OnDestroy() { isRunning = false; receiveThread?.Join(500); socket?.Close(); }

    public Vector3[] GetLatestPositions()
    {
        lock (dataLock3D) { return (Vector3[])currentPositions.Clone(); }
    }
    public Vector2[] GetLatest2DPositions()
    {
        lock (dataLock2D) { return (Vector2[])current2DPositions.Clone(); }
    }
    public Vector2 GetImageDimensions()
    {
        lock (dataLock2D) { return imageDimensions; }
    }

    public bool HasReceivedData() => gotFirstData;



}