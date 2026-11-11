using UnityEngine;

public class GatherAligner : MonoBehaviour
{
    [SerializeField] private new ParticleSystem particleSystem;
    [SerializeField] private float cooldownDuration = 2f;
    private Transform target;
    private Transform nozzle;
    private bool stopped = false;

    /// <summary>
    /// Aligns the gather visualizer to the given nozzle and target positions.
    /// </summary>
    /// <param name="nozzle">Start point of the attack</param>
    /// <param name="target">End point of the attack</param>
    private void Align(Vector3 nozzle, Vector3 target)
    {
        transform.forward = (target - nozzle).normalized;
        transform.position = (nozzle + target) / 2f;

        // Tell the particle system that the shape's z scale is equal to half the distance between nozzle and target
        var shape = particleSystem.shape;
        shape.scale = new Vector3(shape.scale.x, shape.scale.y, Vector3.Distance(nozzle, target) / 2f);
    }

    private void LateUpdate()
    {
        if (nozzle != null && target != null)
        {
            Align(nozzle.position, target.position);
        }
        else if (!stopped)
        {
            stopped = true;
            particleSystem.Stop(
                true,
                ParticleSystemStopBehavior.StopEmitting
            );
        }
    }

    /// <summary>
    /// Tells the gather visualizer what transforms it should use to align
    /// </summary>
    /// <param name="nozzle">Nozzle of the gatherer unit</param>
    /// <param name="target">Center of the gathered resource</param>
    public void SetAlignment(Transform nozzle,  Transform target)
    {
        this.nozzle = nozzle;
        this.target = target;
        particleSystem.Play();
    }

    /// <summary>
    /// Tells the gathering effect to stop.
    /// </summary>
    public void End()
    {
        particleSystem.Stop(
            true,
            ParticleSystemStopBehavior.StopEmitting
        );
        Destroy(gameObject, cooldownDuration);
    }
}
