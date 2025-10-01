using UnityEngine;
using System.Linq;
using System;
using System.Collections;
using Fusion.Photon.Realtime;

public class ServerLauncher : MonoBehaviour
{
    [SerializeField]
    private RoomPoolManager? _roomPoolManager;

    private void Awake()
    {
        if (_roomPoolManager == null)
        {
            _roomPoolManager = GetComponent<RoomPoolManager>();
        }

        if (_roomPoolManager == null)
        {
            _roomPoolManager = gameObject.AddComponent<RoomPoolManager>();
        }
    }

    private IEnumerator Start()
    {
        Debug.Log("🟢 ServerLauncher.Start() - Initialising dedicated server room pool.");

        string roomPrefix = GetArg("--roomName") ?? "DedicatedRoom";
        string portStr = GetArg("--port");
        ushort basePort = 27015;

        if (!string.IsNullOrEmpty(portStr) && ushort.TryParse(portStr, out ushort parsedPort))
        {
            basePort = parsedPort;
        }

        var customSettings = PhotonAppSettings.Global.AppSettings.GetCopy();
        customSettings.FixedRegion = "asia";
        customSettings.AppVersion = PhotonAppSettings.Global.AppSettings.AppVersion;
        Debug.Log($"🌍 Using Photon region: {customSettings.FixedRegion}");

        if (_roomPoolManager == null)
        {
            Debug.LogError("❌ RoomPoolManager is missing. Unable to initialise rooms.");
            yield break;
        }

        yield return _roomPoolManager.InitialisePool(customSettings, basePort, roomPrefix);

        Debug.Log("✅ Dedicated server room pool initialised successfully.");
    }

    private string? GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        var argMatch = args.FirstOrDefault(arg => arg.StartsWith(name) && arg.Contains('='));

        if (argMatch != null)
        {
            return argMatch.Split('=').Skip(1).FirstOrDefault();
        }

        return null;
    }
}
