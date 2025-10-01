using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Player : MonoBehaviour
{
    public string tagPlyer;
    public string fullname;
    public int powerForce;
    public int exactRatio;
    public Sprite avatar; // Hình ảnh đại diện của người chơi
    // Xác định đây là AI hay người chơi thật
    public Rigidbody ball;  // Viên bi của người chơi này
    public Animator animator;
    public GameObject playerbody;

    public int score;

    public float distance; // điểm thi mức quyết định thứ tự 
    public StatusPlayer statusPlayer;
    public bool isHolding;
    public bool isAI;
    public bool isDestroy;
    public Player(string tagPlyer,
        string fullname,
        int powerForce,
        int exactRatio,
        Sprite avatar,
        bool isAI,
        Rigidbody ball,
        Animator animator,
        GameObject playerbody,
        int score,
        bool isDestroy,
        float distance,
        StatusPlayer statusPlayer,
        bool isHolding)
    {
        this.tagPlyer = tagPlyer;
        this.fullname = fullname;
        this.powerForce = powerForce;
        this.exactRatio = exactRatio;
        this.avatar = avatar;
        this.isAI = isAI;
        this.ball = ball;
        this.animator = animator;
        this.playerbody = playerbody;
        this.score = score;
        this.isDestroy = isDestroy;
        this.distance = distance;
        this.statusPlayer = statusPlayer;
        this.isHolding = isHolding;
    }
}


