using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Paused : MonoBehaviour
{
    public GameObject ui;
    public KeyCode key =  KeyCode.P;

    private void Update()
    {
        if(Input.GetKeyDown(key))
            Toggle();
    }



    public void Toggle()
    {
        ui.SetActive(!ui.activeSelf);

        if (ui.activeSelf)
        {
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = 1f;
        }
    }
}
