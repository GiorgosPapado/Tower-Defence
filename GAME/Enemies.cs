using UnityEngine;
using UnityEngine.UI;

public class Enemies : MonoBehaviour
{
    public float startSpeed = 10f;

    [HideInInspector]
    public float speed;

    public float health = 100;
    public int value = 30;

    [Header("Unity Stuff")]
    public Image HealthBar;

    private void Start()
    {
        speed = startSpeed;
    }

    public void TakeDamage(float amount)
    {
        health -= amount;

        HealthBar.fillAmount = health / 100f;

        if(health <= 0)
        {
            Die();
        }
    }

    public void Slow(float pct)
    {
        speed = startSpeed * (1f - pct);
    }

    void Die()
    {
        PlayerStats.Money += value;
        Destroy(gameObject);
    }

}
