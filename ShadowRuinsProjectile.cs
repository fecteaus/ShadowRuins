using UnityEngine;

/// <summary>Simple local arcane bolt visual used by the E spell.</summary>
public class ShadowRuinsProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float lifetime = 3.2f;
    public void Initialize(Vector3 dir) { direction = dir.normalized; }
    private void Update()
    {
        transform.position += direction * (14f * Time.deltaTime);
        lifetime -= Time.deltaTime;
        transform.localScale = Vector3.one * Mathf.Lerp(0.18f, 0.06f, 1f - lifetime / 3.2f);
        if (lifetime <= 0f) Destroy(gameObject);
    }
}
