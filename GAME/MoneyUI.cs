using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class MoneyUI : MonoBehaviour
{
    public TMPro.TextMeshProUGUI moneyText;

    // Update is called once per frame
    void Update()
    {
        moneyText.text = PlayerStats.Money.ToString() + "€";
    }
}
