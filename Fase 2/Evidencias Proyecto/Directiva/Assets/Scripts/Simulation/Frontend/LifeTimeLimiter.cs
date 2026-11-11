using UnityEngine;

public class LifeTimeLimiter : MonoBehaviour
{
    [SerializeField] private float lifeSpan = 0f;

    private void Start()
    {
        // Ensure that the time of our destruction is relative to the spawn time.
        lifeSpan += Time.time;
    }

    private void FixedUpdate()
    {
        // Destroy the object if the current time has passed the life span.
        if (Time.time >= lifeSpan)
        {
            Destroy(gameObject);
        }
    }
}
