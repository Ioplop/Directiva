using System.Collections.Generic;
using UnityEngine;

public class UnitPatrol : MonoBehaviour
{
    private Rigidbody rb;

    private static float minDistance = 1f;
    private static float maxCollisionTime = 1f;

    [SerializeField] private float speed = 5f;
    [SerializeField] private float acceleration = 5f;
    private Vector3 target;

    private int collisions = 0;
    private float collisionTime = 0f;

    private void FindNewRandomTarget()
    {
        // Determinar posición aleatoria objetivo
        target = new Vector3(Random.Range(-50f, 50f), 0f, Random.Range(-50f, 50f));
    }

    private void MoveToTarget()
    {
        // Acelerar hacia el objetivo
        var distance = target - transform.position;
        var vel = rb.linearVelocity;
        vel += distance.normalized * acceleration * Time.fixedDeltaTime;
        vel = Vector3.ClampMagnitude(vel, speed);
        // Apuntar y moverse
        transform.forward = vel.normalized;
        rb.linearVelocity = vel;

        // Cambiar objetivo si lo alcanzamos
        if (minDistance * minDistance > distance.sqrMagnitude)
        {
            FindNewRandomTarget();
        }
    }

    private void Start()
    {
        // Obtener referencia rigidbody
        rb = GetComponent<Rigidbody>();
        FindNewRandomTarget();
    }

    private void FixedUpdate()
    {
        collisionTime += Time.fixedDeltaTime;
        if (maxCollisionTime < collisionTime)
        {
            collisionTime = 0f;
            FindNewRandomTarget();
        }
        MoveToTarget();
    }

    private void OnCollisionExit(Collision collision)
    {
        collisions--;
        if (collisions == 0)
        {
            collisionTime = 0;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        collisions++;
        FindNewRandomTarget();
    }
}
