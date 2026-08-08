using UnityEngine;

// Thin presenter: reads phase + timestamps from GameRules and turns them into
// scale/position + hit feedback. ZERO game rules live here.
public sealed class Mole : MonoBehaviour
{
    [SerializeField] private float hitRadius = 1f;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform spriteTransform;

    private GameRules _rules;
    private int _index;

    private Vector2 _phaseScale = Vector2.zero; // from SyncFromRules (rise/sink squash)
    private float _juiceTimerMs;                // > 0 while pop/flash active
    private const float JuiceDurationMs = 150f;
    private const float PopScale = 1.3f;
    private Color _normalColor = Color.white;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteTransform == null) spriteTransform = spriteRenderer != null ? spriteRenderer.transform : transform;
    }

    public void Bind(GameRules rules, int index)
    {
        _rules = rules;
        _index = index;
        if (spriteRenderer != null) _normalColor = spriteRenderer.color;
    }

    public void SyncFromRules(float nowMs)
    {
        if (_rules == null) return;
        float phaseStart = _rules.GetPhaseStartMs(_index);

        switch (_rules.GetPhase(_index))
        {
            case MolePhase.Sunk:
                _phaseScale = Vector2.zero;
                break;

            case MolePhase.Rising:
                float tRise = Mathf.Clamp01((nowMs - phaseStart) / Mathf.Max(1f, _rules.RiseDurationMs));
                _phaseScale = new Vector2(0.2f + 0.8f * tRise, 0.2f + 0.8f * tRise);
                break;

            case MolePhase.Up:
                _phaseScale = Vector2.one;
                break;

            case MolePhase.Sinking:
                float tSink = Mathf.Clamp01((nowMs - phaseStart) / Mathf.Max(1f, _rules.SinkDurationMs));
                float s = 1f - 0.8f * tSink;
                _phaseScale = new Vector2(s, s);
                break;
        }
    }

    public bool ContainsPoint(Vector2 worldPoint)
    {
        return Vector2.Distance(transform.position, worldPoint) <= hitRadius;
    }

    public void PlayHitJuice()
    {
        _juiceTimerMs = JuiceDurationMs;
    }

    private void Update()
    {
        // Juice is presentation-only, driven here (no coroutines).
        if (_juiceTimerMs > 0f)
        {
            _juiceTimerMs -= Time.deltaTime * 1000f;
            float progress = Mathf.Clamp01(1f - _juiceTimerMs / JuiceDurationMs);
            float eased = 1f - (1f - progress) * (1f - progress); // ease-out
            float pop = Mathf.Lerp(PopScale, 1f, eased);

            if (spriteTransform != null)
                spriteTransform.localScale = new Vector3(_phaseScale.x * pop, _phaseScale.y * pop, 1f);

            if (spriteRenderer != null)
                spriteRenderer.color = Color.Lerp(Color.white, _normalColor, eased);
        }
        else if (spriteTransform != null)
        {
            spriteTransform.localScale = new Vector3(_phaseScale.x, _phaseScale.y, 1f);
        }
    }
}
