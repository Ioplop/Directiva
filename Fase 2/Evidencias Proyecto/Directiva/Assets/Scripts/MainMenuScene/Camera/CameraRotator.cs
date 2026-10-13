using UnityEngine;

public sealed class CameraRotator : MonoBehaviour
{
    /// <summary>
    /// Rota el objeto constantemente
    /// </summary>
    [SerializeField] private float rotationSpeed;

    private void Update()
    {
        var old_rotation = transform.rotation.eulerAngles;
        old_rotation.y = old_rotation.y + rotationSpeed * Time.deltaTime;
        transform.rotation = Quaternion.Euler(old_rotation);
    }
}
