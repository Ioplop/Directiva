using System.Collections;
using UnityEngine;

public class ShieldHitAnimator : MonoBehaviour
{
    [SerializeField] private float duration = 0.5f;

    private static readonly int HitDirId = Shader.PropertyToID("_HitDir");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int CenterId = Shader.PropertyToID("_Center");

    private Renderer rend;
    private Material mat;
    private Vector3 localCenter;
    private Coroutine routine;

    private void Awake()
    {
        rend = GetComponent<Renderer>();
        mat = rend.material; // copia propia para este escudo
        localCenter = GetComponent<MeshFilter>().sharedMesh.bounds.center;
        mat.SetVector(CenterId, localCenter);
        mat.SetFloat(FadeId, 0f);
        rend.enabled = false;
    }

    // Llamar cuando la entidad recibe un ataque con el escudo activo
    public void ShowHit(Vector3 attackerWorldPosition)
    {
        Vector3 worldDir = attackerWorldPosition - transform.TransformPoint(localCenter);
        Vector3 localDir = transform.InverseTransformDirection(worldDir).normalized;
        mat.SetVector(HitDirId, localDir);

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Flash());
    }

    private IEnumerator Flash()
    {
        rend.enabled = true;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            mat.SetFloat(FadeId, 1f - t / duration);
            yield return null;
        }
        mat.SetFloat(FadeId, 0f);
        rend.enabled = false;
        routine = null;
    }
    private void OnDestroy()
    {
        if (mat != null)
            Destroy(mat);
    }
}