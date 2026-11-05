using UnityEngine;
using UnityEngine.UI;

public class Enemies : MonoBehaviour
{
    public float startSpeed = 10f;

    [HideInInspector]
    public float speed;

    public float health = 100;
    public int value = 30;

    // Remaining path length, kept up to date by EnemyMovement. Used for "First/Last" targeting.
    [HideInInspector]
    public float distanceToGoal;

    [Header("Unity Stuff")]
    public Image HealthBar;

    private float maxHealth;
    private float slowPct;
    private float slowTimer;
    private bool isDead;

    public float MaxHealth { get { return maxHealth; } }
    public bool IsDead { get { return isDead; } }

    private void Awake()
    {
        health *= Difficulty.HealthMultiplier;   // balance tester / training hook, 1 in normal play
        maxHealth = Mathf.Max(1f, health);
        speed = startSpeed;
    }

    private void OnEnable()
    {
        EnemyRegistry.Register(this);
    }

    private void OnDisable()
    {
        EnemyRegistry.Unregister(this);
    }

    private void Update()
    {
        ApplySlowDecay(Time.deltaTime);
    }

    public void ApplySlowDecay(float dt)
    {
        if (slowTimer > 0f)
        {
            slowTimer -= dt;
            speed = startSpeed * (1f - slowPct);
        }
        else
        {
            speed = startSpeed;
        }
    }

    public void TakeDamage(float amount)
    {
        if (isDead) return;

        health -= amount;

        if (HealthBar != null)
            HealthBar.fillAmount = Mathf.Clamp01(health / maxHealth);

        if (health <= 0)
        {
            Die();
        }
    }

    /// <summary>Slow for a short time; call every frame while a slow source is active.</summary>
    public void Slow(float pct, float duration = 0.25f)
    {
        slowPct = Mathf.Clamp01(pct);
        slowTimer = duration;
        speed = startSpeed * (1f - slowPct);
    }

    /// <summary>Enemy reached the end of the path: costs a life, cannot be killed afterwards.</summary>
    public void Leak()
    {
        if (isDead) return;
        isDead = true;
        PlayerStats.Lives--;

        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }

    void Die()
    {
        isDead = true;
        PlayerStats.Money += value;

        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
    }
}
