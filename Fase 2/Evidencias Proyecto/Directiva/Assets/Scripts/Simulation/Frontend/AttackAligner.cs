using UnityEngine;

public class AttackAligner : MonoBehaviour
{
    [SerializeField] private new ParticleSystem particleSystem;

    /// <summary>
    /// Aligns the attack visualizer to the given nozzle and target positions.
    /// </summary>
    /// <param name="nozzle">Start point of the attack</param>
    /// <param name="target">End point of the attack</param>
    public void Align(Vector3 nozzle, Vector3 target)
    {
        transform.forward = (target - nozzle).normalized;
        transform.position = (nozzle + target) / 2f;

        // Tell the particle system that the shape's z scale is equal to half the distance between nozzle and target
        var shape = particleSystem.shape;
        shape.scale = new Vector3(shape.scale.x, shape.scale.y, Vector3.Distance(nozzle, target) / 2f);
        particleSystem.Play();
    }
}
