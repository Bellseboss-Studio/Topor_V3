using UnityEngine;

// Dumb presenter for one crop. ZERO game rules live here: the controller tells it
// when it is threatened (blink) or stolen (hide + brief fade). No animation system,
// no coroutines — Update-driven timers only.
public sealed class Crop : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform spriteTransform;
    [SerializeField] private Color stolenTint = new Color(0.15f, 0.15f, 0.15f, 0.6f);

    private const float StealFadeMs = 350f;
    private const float ThreatBlinkScale = 1.18f;

    private Color _baseColor = Color.white;
    private bool _threatened;
    private bool _stolen;
    private float _stealTimerMs;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteTransform == null) spriteTransform = spriteRenderer != null ? spriteRenderer.transform : transform;
        if (spriteRenderer != null) _baseColor = spriteRenderer.color;
    }

    // Controller-driven blink while any telegraphing mole targets this crop.
    public void SetThreatened(bool flash)
    {
        if (_stolen) return; // stolen stays dead — no flash
        _threatened = flash;
    }

    // Hide + brief fade. Invoked from a drained escape event, same frame as the steal.
    public void SetStolen()
    {
        if (_stolen) return;
        _stolen = true;
        _stealTimerMs = StealFadeMs;
        _threatened = false;
    }

    public bool IsStolen => _stolen;

    private void Update()
    {
        if (spriteRenderer == null || spriteTransform == null) return;

        if (_stolen)
        {
            _stealTimerMs -= Time.deltaTime * 1000f;
            float t = Mathf.Clamp01(1f - _stealTimerMs / StealFadeMs);
            float eased = t * t; // ease-in shrink
            if (spriteTransform != null)
                spriteTransform.localScale = new Vector3(1f - 0.85f * eased, 1f - 0.85f * eased, 1f);
            if (spriteRenderer != null)
                spriteRenderer.color = Color.Lerp(_baseColor, stolenTint, eased);
            if (t >= 1f)
            {
                if (spriteRenderer != null) spriteRenderer.enabled = false;
                _stealTimerMs = 0f;
            }
            return;
        }

        if (_threatened)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 14f); // ~9 Hz blink
            float s = 1f + (ThreatBlinkScale - 1f) * pulse;
            if (spriteTransform != null)
                spriteTransform.localScale = new Vector3(s, s, 1f);
            if (spriteRenderer != null)
                spriteRenderer.color = Color.Lerp(_baseColor, Color.yellow, pulse * 0.7f);
            return;
        }

        if (spriteTransform != null) spriteTransform.localScale = Vector3.one;
        if (spriteRenderer != null) spriteRenderer.color = _baseColor;
    }
}