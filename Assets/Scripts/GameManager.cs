using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static bool gameEnded = false;
    public static GameManager Instance;

    public GameObject gameOverUI;
    public GameObject winUI;   // optional; give it a button wired to GameOver.Retry

    void Awake()
    {
        Instance = this;
        gameEnded = false;       // static: must be reset on every scene load / retry
        Time.timeScale = 1f;
    }

    void Update()
    {
        if (gameEnded)
            return;

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.E))   // debug shortcut, editor only
        {
            EndGame();
        }
#endif

        if (PlayerStats.Lives <= 0)
        {
            EndGame();
        }
    }

    void EndGame()
    {
        gameEnded = true;
        if (gameOverUI != null)
            gameOverUI.SetActive(true);
    }

    public void Win()
    {
        if (gameEnded) return;

        // The last enemy may have leaked on the same frame: that is a loss, not a win.
        if (PlayerStats.Lives <= 0)
        {
            EndGame();
            return;
        }

        gameEnded = true;

        if (winUI != null)
        {
            winUI.SetActive(true);
            Time.timeScale = 0f;
        }
        else
        {
            Debug.Log("You win! Assign GameManager.winUI to show a screen.");
        }
    }
}
