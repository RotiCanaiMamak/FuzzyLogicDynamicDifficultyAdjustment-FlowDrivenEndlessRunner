using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("References")]
    public PlayerController player;

    [Header("Speed")]
    public float initialSpeed = 1f;

    public float CurrentSpeed { get; private set; }
    public bool IsRunning { get; private set; }

    private float sessionTime = 0f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        StartGame();
    }

    void Update()
    {
        if (!IsRunning)
        {
            return;
        }
        sessionTime += Time.deltaTime;
    }

    public void StartGame()
    {
        IsRunning = true;
        sessionTime = 0f;
        SetSpeed(initialSpeed);
    }

    public void SetSpeed(float speed)
    {
        CurrentSpeed = speed;
        player.SetSpeed(speed);
    }

    public float GetSessionTime()
    {
        return sessionTime;
    }
}