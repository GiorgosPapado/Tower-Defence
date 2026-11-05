using UnityEngine;

[RequireComponent(typeof(Enemies))]
public class EnemyMovement : MonoBehaviour
{
    private Transform target;
    private int wavepointIndex = 0;

    private Enemies enemy;

    void Start()
    {
        enemy = GetComponent<Enemies>();
        target = WayPoints.points[0];
    }

    private void Update()
    {
        Vector3 dir = target.position - transform.position;
        transform.Translate(dir.normalized * enemy.speed * Time.deltaTime, Space.World);

        if (Vector3.Distance(transform.position, target.position) <= 0.2f)
        {
            GetNextWaypoint();
            if (this == null) return;   // destroyed at the end of the path
        }

        enemy.distanceToGoal = RemainingDistance();
    }

    float RemainingDistance()
    {
        float d = Vector3.Distance(transform.position, target.position);
        for (int i = wavepointIndex; i < WayPoints.points.Length - 1; i++)
        {
            d += Vector3.Distance(WayPoints.points[i].position, WayPoints.points[i + 1].position);
        }
        return d;
    }

    void GetNextWaypoint()
    {
        if (wavepointIndex >= WayPoints.points.Length - 1)
        {
            EndPath();
            return;
        }

        wavepointIndex++;
        target = WayPoints.points[wavepointIndex];
    }

    void EndPath()
    {
        PlayerStats.Lives--;
        Destroy(gameObject);
    }
}
