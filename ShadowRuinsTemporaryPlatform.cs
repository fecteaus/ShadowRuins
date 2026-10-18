using UnityEngine;

/// <summary>Removes a Stone Step platform after a short duration.</summary>
public class ShadowRuinsTemporaryPlatform : MonoBehaviour
{
    private float lifetime = 12f;
    private void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f) Destroy(gameObject);
    }
}
