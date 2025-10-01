using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System.Linq;
using System;
using System.Threading.Tasks; // Cần thiết cho async/await
using Fusion.Photon.Realtime;
using System.Collections; // Cần thiết nếu dùng Coroutine

// Lớp này nên implement INetworkRunnerCallbacks để xử lý lỗi trực tiếp, nhưng ta sẽ giữ nó đơn giản.

public class ServerLauncher : MonoBehaviour
{
    // Sử dụng [SerializeField] cho biến private để dễ dàng gán qua Editor
    [SerializeField]
    private NetworkObject _networkManagerPrefab;
    private NetworkRunner _runner;

    // Khởi tạo trong Awake để đảm bảo sẵn sàng trước Start
    private void Awake()
    {
        // Khởi tạo runner ở đây để nó có thể được sử dụng trong các hàm khác ngay lập tức
        _runner = gameObject.AddComponent<NetworkRunner>();
    }

    // Start là Coroutine để xử lý quá trình StartGame không đồng bộ
    IEnumerator Start()
    {
        Debug.Log("🟢 Đang chạy Start() - Chuẩn bị khởi động Server Dedicated.");

        // 1. Phân tích tham số dòng lệnh
        string roomName = GetArg("--roomName") ?? "DefaultRoom";
        string portStr = GetArg("--port");
        ushort port = 27015;

        if (!string.IsNullOrEmpty(portStr) && ushort.TryParse(portStr, out ushort parsedPort))
        {
            port = parsedPort;
        }

        // 2. Cấu hình Photon Settings
        var customSettings = PhotonAppSettings.Global.AppSettings.GetCopy();
        // **Quan trọng:** Thiết lập Region và AppVersion phải khớp với Client
        customSettings.FixedRegion = "asia";
        customSettings.AppVersion = PhotonAppSettings.Global.AppSettings.AppVersion;
        Debug.Log($"🌍 Sử dụng region: {customSettings.FixedRegion}");

        // 3. Khởi tạo StartGameArgs
        var args = new StartGameArgs
        {
            SessionName = roomName,
            GameMode = GameMode.Server,
            // MatchmakingMode: FillRoom là mặc định, không cần thiết lập lại
            // EnableClientSessionCreation: Chỉ cần thiết cho GameMode.Host, không cần cho Server

            // Đối với Dedicated Server, ta chỉ cần binding vào IP (0.0.0.0 là mặc định cho tất cả các card mạng)
            Address = NetAddress.CreateFromIpPort("0.0.0.0", port),

            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = 3, // Giới hạn số lượng người chơi
            CustomPhotonAppSettings = customSettings,
            //ProvideInput = false // Đã set ở Awake, nhưng đặt lại ở đây cho rõ ràng
        };

        Debug.Log($"🧪 Khởi tạo StartGame với địa chỉ: 0.0.0.0:{port}");

        // 4. Chạy StartGame và đợi kết quả (sử dụng Task/Coroutine)
        var startTask = _runner.StartGame(args);

        // Đợi cho Task hoàn thành
        while (!startTask.IsCompleted)
            yield return null;

        var result = startTask.Result;

        // 5. Xử lý kết quả
        if (result.Ok)
        {
            Debug.Log($"✅ Fusion Server đã khởi động thành công cho phòng: {roomName} (port: {port})");

            // Spawn NetworkManager sau khi Server khởi động thành công
            //if (_networkManagerPrefab != null)
            //{
            //    var obj = _runner.Spawn(_networkManagerPrefab, Vector3.zero, Quaternion.identity);
            //    Debug.Log($"✅ Spawned NetworkManager: {obj.name}");
            //}
            //else
            //{
            //    Debug.LogError("❌ networkManagerPrefab chưa được gán!");
            //}
        }
        else
        {
            Debug.LogError($"❌ StartGame failed: {result.ShutdownReason}");
            // Quan trọng: Đóng ứng dụng nếu Server khởi động thất bại
            Application.Quit();
        }
    }


    string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        // Kiểm tra xem tham số có tên (name) tồn tại và có chứa '=' không
        var argMatch = args.FirstOrDefault(arg => arg.StartsWith(name) && arg.Contains('='));

        if (argMatch != null)
        {
            // Trả về phần tử thứ hai sau dấu '='
            return argMatch.Split('=').Skip(1).FirstOrDefault();
        }
        return null;
    }
}