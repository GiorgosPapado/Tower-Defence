using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static bool gameEnded = false;

    public GameObject gameOverUI;

    void Update()
    {
        if (gameEnded)
            return;

        if (Input.GetKeyDown("e"))
        {
            EndGame();
        }


        if (PlayerStats.Lives <= 0)
        {
            EndGame();
        }
    }

    void EndGame()
    {
        gameEnded = true;
        gameOverUI.SetActive(true);

    }
}
