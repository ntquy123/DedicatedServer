// ========================
// GameSessionNetworker_Server.cs
// ========================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Fusion;

public class GameSessionNetworker : MonoBehaviour
{
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestUserList(string roomId)
    {
        Debug.Log($"🛠️ [SERVER] Nhận RPC_RequestUserList - roomId: {roomId}");
        StartCoroutine(GetUsers(roomId));
    }

    IEnumerator GetUsers(string roomId)
    {
        string url = $"{ApiConfig.BaseUrl}/getUserRooms/{roomId}";
        UnityWebRequest req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            string json = "{\"users\":" + req.downloadHandler.text + "}";
            var wrapper = JsonUtility.FromJson<PlayerListWrapper>(json);
            RPC_SendUserList(roomId, wrapper.users);
        }
        else
        {
            Debug.LogError($"❌ Lỗi khi gọi API getUserRooms: {req.error}");
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SendUserList(string roomId, List<Player> users)
    {
        Debug.Log($"📤 Gửi danh sách {users.Count} người chơi trong phòng {roomId}");
    }
}



[System.Serializable]
public class PlayerListWrapper
{
    public List<Player> users;
}
