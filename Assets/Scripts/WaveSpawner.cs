using System.Collections;
using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [Header("Legacy endless mode (used when 'Waves' is empty)")]
    public Transform enemyPrefab;

    public Transform spawnPoint;

    public float timeBetweenWaves = 5f;
    private float countdown = 2f;

    [Header("Data-driven waves (Assets > Create > TowerDefence > Wave)")]
    public WaveDefinition[] waves;

    private int waveIndex = 0;
    private bool spawning = false;

    public int WaveNumber { get { return waveIndex; } }
    public int TotalWaves { get { return waves == null ? 0 : waves.Length; } }
    public float Countdown { get { return countdown; } }
    private bool HasWaves { get { return waves != null && waves.Length > 0; } }

    void Update()
    {
        if (GameManager.gameEnded || spawning)
            return;

        if (HasWaves && waveIndex >= waves.Length)
        {
            // All waves spawned: the player wins once every enemy is gone.
            if (EnemyRegistry.Count == 0 && GameManager.Instance != null)
                GameManager.Instance.Win();
            return;
        }

        if (countdown <= 0f)
        {
            StartCoroutine(SpawnWave());
            countdown = timeBetweenWaves;
        }

        countdown -= Time.deltaTime;
    }

    /// <summary>Hook this to a "Next wave" button.</summary>
    public void SkipCountdown()
    {
        if (!spawning) countdown = 0f;
    }

    IEnumerator SpawnWave()
    {
        spawning = true;
        PlayerStats.Rounds++;

        if (HasWaves)
        {
            WaveDefinition wave = waves[waveIndex];
            waveIndex++;
            if (wave != null && wave.groups != null)
            {
                foreach (WaveDefinition.Group g in wave.groups)
                {
                    if (g.delayBefore > 0f)
                        yield return new WaitForSeconds(g.delayBefore);

                    for (int i = 0; i < g.count; i++)
                    {
                        SpawnEnemy(g.enemyPrefab);
                        yield return new WaitForSeconds(g.spawnInterval);
                    }
                }
            }
        }
        else
        {
            waveIndex++;
            for (int i = 0; i < waveIndex; i++)
            {
                SpawnEnemy(enemyPrefab);
                yield return new WaitForSeconds(0.5f);
            }
        }

        spawning = false;
    }

    void SpawnEnemy(Transform prefab)
    {
        if (prefab == null) return;
        Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
    }
}
