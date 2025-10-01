using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Linq;
using System;
using Fusion.Photon.Realtime;


public class ServerLauncher : MonoBehaviour
{
    //chmod +x BanCuLiServer.x86_64 khi deploy mới
    private NetworkRunner runner;
    public NetworkObject networkManagerPrefab;
    async void Start()
    {
        Debug.Log("🟢 Đang chạy Start() - chuẩn bị StartGame");

        string roomName = GetArg("--roomName") ?? "DefaultRoom";
        string portStr = GetArg("--port");
        ushort port = 27015;

        if (!string.IsNullOrEmpty(portStr) && ushort.TryParse(portStr, out ushort parsedPort))
        {
            port = parsedPort;
        }

        Debug.Log($"🔌 Port sử dụng: {port}");
        Debug.Log($"🏷️ SessionName: {roomName}");

        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = false;

        // In config để xác nhận thông tin bind thực tế
        Debug.Log("🧪 Khởi tạo StartGame với địa chỉ: 0.0.0.0" + ":" + port);

        //var result = await runner.StartGame(new StartGameArgs
        //{
        //    GameMode = GameMode.Server,
        //    //Address = NetAddress.CreateFromIpPort("103.12.77.207", port),
        //    Address = NetAddress.CreateFromIpPort("0.0.0.0", port),
        //    SessionName = roomName,
        //    SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        //});
        var customSettings = PhotonAppSettings.Global.AppSettings.GetCopy();
        customSettings.FixedRegion = "asia";
        customSettings.AppVersion = PhotonAppSettings.Global.AppSettings.AppVersion;
        Debug.Log($"🌍 Sử dụng region: {customSettings.FixedRegion}");
 
        var args = new StartGameArgs
        {
            SessionName = roomName, // ✅ cần nhớ tên này
            GameMode = GameMode.Server,
            MatchmakingMode = MatchmakingMode.FillRoom,
            EnableClientSessionCreation = true,
            // SessionName = string.empty,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = 3,
            CustomPhotonAppSettings = customSettings
            //SessionProperties = new Dictionary<string, SessionProperty>
            //{
            //    { "level", (SessionProperty)myLevel }
            //}
        };
        var startTask = runner.StartGame(args);
        while (!startTask.IsCompleted)
            yield return null;

        if (!startTask.Result.Ok)
        {
            Debug.LogError($"❌ StartGame failed: {startTask.Result.ShutdownReason}");
        }
            // In lại thông tin bind thực tế
            Debug.Log($"📡 Requested bind address: 0.0.0.0:{port}");


        //if (startTask.Result.Ok)
        //{
        //    Debug.Log($"✅ Fusion Server đã khởi động cho phòng: {roomName} (port: {port})");
        //    var obj = runner.Spawn(networkManagerPrefab, Vector3.zero, Quaternion.identity);
        //    Debug.Log("✅ Spawned NetworkManager with RPC");
        //}
 
    }


    string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        foreach (var arg in args)
        {
            if (arg.StartsWith(name))
                return arg.Split('=')[1];
        }
        return null;
    }

    //void Update()
    //{
    //    if (runner.ActivePlayers.Count() == 0)
    //    {
    //        if (Time.realtimeSinceStartup > 50f)
    //        {
    //            Debug.Log("🕑 Không ai tham gia. Đóng server.");
    //            Application.Quit();
    //        }
    //    }
    //}

    


}
