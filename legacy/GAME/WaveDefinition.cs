using UnityEngine;

/// <summary>One wave of enemies. Create via Assets > Create > TowerDefence > Wave.</summary>
[CreateAssetMenu(menuName = "TowerDefence/Wave", fileName = "Wave")]
public class WaveDefinition : ScriptableObject
{
    [System.Serializable]
    public class Group
    {
        public Transform enemyPrefab;
        public int count = 1;
        public float spawnInterval = 0.5f;
        public float delayBefore = 0f;
    }

    public Group[] groups;

    public int TotalEnemies
    {
        get
        {
            int total = 0;
            if (groups == null) return 0;
            foreach (Group g in groups) total += Mathf.Max(0, g.count);
            return total;
        }
    }
}
