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
        if (enemy.IsDead) return;

        // MoveTowards never overshoots, so enemies cannot orbit a waypoint at high speed.
        transform.position = Vector3.MoveTowards(transform.position, target.position, enemy.speed * Time.deltaTime);

        if ((transform.position - target.position).sqrMagnitude <= 0.0025f)
        {
            if (!AdvanceWaypoint()) return;   // leaked: enemy is gone
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

    bool AdvanceWaypoint()
    {
        if (wavepointIndex >= WayPoints.points.Length - 1)
        {
            enemy.Leak();
            return false;
        }

        wavepointIndex++;
        target = WayPoints.points[wavepointIndex];
        return true;
    }
}
