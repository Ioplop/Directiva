using UnityEngine;

/// <summary>
/// Contains the visual references and behaviors required
/// to represent a simulation entity.
/// </summary>
public class VisualEntity : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform nozzle;
    [SerializeField] private Transform center;
    [SerializeField] private SideColorGiver side;

    [Header("Death")]
    [SerializeField] private GameObject deathEffectPrefab;

    private bool isDead;

    public Transform Nozzle => nozzle != null ? nozzle : transform;
    public Transform Center => center != null ? center : transform;

    public Vector3 NozzlePosition => Nozzle.position;
    public Vector3 CenterPosition => Center.position;

    public SideColorGiver Side => side;

    /// <summary>
    /// Plays the death effect and destroys this visual entity.
    /// </summary>
    public void Die()
    {
        if (isDead)
            return;

        isDead = true;

        if (deathEffectPrefab != null)
        {
            Instantiate(
                deathEffectPrefab,
                transform.position,
                Quaternion.identity
            );
        }

        Destroy(gameObject);
    }
}