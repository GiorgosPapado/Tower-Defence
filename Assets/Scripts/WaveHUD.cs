using UnityEngine;

public class WaveHUD : MonoBehaviour
{
    public TMPro.TextMeshProUGUI text;
    public WaveSpawner spawner;

    private void Update()
    {
        if (text == null || spawner == null) return;

        int shown = Mathf.Max(1, spawner.WaveNumber);
        string wave = spawner.TotalWaves > 0
            ? "WAVE " + shown + " / " + spawner.TotalWaves
            : "WAVE " + shown;

        text.text = wave;
    }
}
