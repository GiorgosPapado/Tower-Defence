using UnityEngine;

public class MoneyUI : MonoBehaviour
{
    public TMPro.TextMeshProUGUI moneyText;

    void Update()
    {
        moneyText.text = "$" + PlayerStats.Money.ToString();
    }
}
