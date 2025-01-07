using UnityEngine;

public class GameController : MonoBehaviour
{
    public static GameController instance;
    
    // public GameSettings gameSettings;
    // public PlayerController player;

    private void Awake()
    {
        // Singleton pattern implementation
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
    }

    private void Update()
    {
    }

    private void FixedUpdate()
    {
    }
}
