using UnityEngine;

public class Floater : MonoBehaviour
{
    [SerializeField] private float oscilationSpeed = 2f;
    [SerializeField] private float oscilationAmplitude = 0.2f;

    private float startY;
    private float delta;

    private void Start()
    {
        startY = transform.position.y;
        delta = Random.Range(0.0f, 2.0f * Mathf.PI);
    }

    void Update()
    {
        var newPos = transform.position;
        newPos.y = startY + oscilationAmplitude * Mathf.Cos(oscilationSpeed * Time.time + delta);
        transform.position = newPos;
    }
}
