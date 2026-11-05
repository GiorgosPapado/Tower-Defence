using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.SceneManagement;

public class GameOver : MonoBehaviour
{
    public TMPro.TextMeshProUGUI roundsText;

    private void OnEnable()
    {
        roundsText.text = PlayerStats.Rounds.ToString();
        Time.timeScale = 0f;
    }

    public void Retry()
    {
        Time.timeScale = 1f;          // OnEnable froze time; unfreeze before reloading
        GameManager.gameEnded = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Menu()
    {
        Debug.Log("Go to menu.");
    }
}
