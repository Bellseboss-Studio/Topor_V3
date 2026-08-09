using UnityEngine;

// Thin presenter: reads phase + timestamps from GameRules and turns them into
// scale/position + hit feedback. ZERO game rules live here. Visuals are additive —
// escape juice (~0.8s hop carrying the fruit) plays in parallel to the rules sink
// and never affects game timing (design D6).
//
// Animation bridge (B-6): when animator is assigned (drag-drop subclass via Inspector),
// intents dispatch to the animator and mole's own inline visuals are disabled for that
// lifecycle. When animator is null, SyncFromRules + PlayHitJuice/PlayEscapeJuice work
// exactly as before (zero visual regression).
public sealed class Mole : MonoBehaviour
{
    [SerializeField] private float hitRadius = 0.6f; // grid-v2: 0.9–1.2u visuals, covers Rising scale
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform spriteTransform;
    [SerializeField] private SpriteRenderer fruitSprite; // carried fruit during escape juice (optional)
    [SerializeField] private Transform fruitTransform;

    // Animation bridge: assign a MoleAnimator subclass to delegate visuals.
    // Null = use existing SyncFromRules pipeline (zero regression).
    [SerializeField] private MoleAnimator animator;

    private GameRules _rules;
    private int _index;

    private Vector2 _phaseScale = Vector2.zero; // from SyncFromRules (rise/sink squash)
    private float _juiceTimerMs;                // > 0 while hit pop/flash active
    private float _escapeJuiceMs;               // > 0 while escape carry-off runs

    private const float JuiceDurationMs = 150f;
    private const float PopScale = 1.3f;
    private const float EscapeJuiceDurationMs = 800f; // ~0.8s carry-off (presentation only)
    private const float HopHeight = 0.6f;

    private Color _normalColor = Color.white;

    /// <summary>
    /// SetSpecies triggered only at cycle start. Reset to false when a new lifecycle begins.
    /// </summary>
    private bool _speciesDispatched;

    private void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteTransform == null) spriteTransform = spriteRenderer != null ? spriteRenderer.transform : transform;
        if (spriteRenderer != null) _normalColor = spriteRenderer.color;
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
            case MolePhase.Telegraphing:
                // Telegraphing hides the mole completely (gameplay: the warning only
                // shows the threatened fruit — the spawn hole must NOT be readable).
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

        // Re-spawn guard (D6): a fresh lifecycle started in this hole — any leftover
        // escape juice from the previous occupant must be cut immediately. (Rules sink
        // is 250ms but the juice runs 800ms, so the juice may outlive Sinking->Sunk;
        // it is only cancelled when a NEW mole takes the hole again.)
        MolePhase phase = _rules.GetPhase(_index);
        if (_escapeJuiceMs > 0f &&
            phase != MolePhase.Sinking && phase != MolePhase.Sunk)
        {
            CancelEscapeJuice();
        }
    }

    public bool ContainsPoint(Vector2 worldPoint)
    {
        return Vector2.Distance(transform.position, worldPoint) <= hitRadius;
    }

    // --- Animation bridge dispatch (B-6) ---

    /// <summary>
    /// Receives a phase-edge intent from the tracker (via GameController).
    /// If animator is assigned, dispatches to the virtual callback and skips inline visuals.
    /// If animator is null, this is a no-op — SyncFromRules handles visuals.
    ///
    /// SetSpecies is called exactly once per lifecycle: on the first Hide (telegraph mole)
    /// or first Rise (ninja). The _speciesDispatched flag prevents repeated calls.
    /// </summary>
    public void OnIntent(MoleIntentEvent ev)
    {
        if (animator != null)
        {
            // Species delivery: one-shot at cycle start
            if (ev.Species != null && !_speciesDispatched &&
                (ev.Intent == MoleIntent.Hide || ev.Intent == MoleIntent.Rise))
            {
                animator.SetSpecies(ev.Species);
                _speciesDispatched = true;
            }

            switch (ev.Intent)
            {
                case MoleIntent.Hide:   animator.OnHide(); break;
                case MoleIntent.Rise:   animator.OnRise(); break;
                case MoleIntent.Search: animator.OnSearch(); break;
                case MoleIntent.Sink:   animator.OnSink(); break;
                case MoleIntent.Hit:    break; // dispatched via OnRawHit, not OnIntent
                case MoleIntent.Escape: break; // dispatched via OnRawEscape, not OnIntent
                case MoleIntent.Reset:
                    _speciesDispatched = false; // ready for next lifecycle
                    animator.OnReset();
                    break;
            }
        }
        // else: null animator → SyncFromRules continues unchanged (zero regression)
    }

    /// <summary>
    /// Routes a raw hit event from TryHit (input phase).
    /// Dispatches to animator.OnHit() if assigned; otherwise falls back to PlayHitJuice().
    /// </summary>
    public void OnRawHit()
    {
        if (animator != null)
            animator.OnHit();
        else
            PlayHitJuice();
    }

    /// <summary>
    /// Routes a raw escape event from DrainEscapes.
    /// Dispatches to animator.OnEscape(ev) if assigned; otherwise falls back to PlayEscapeJuice().
    /// </summary>
    public void OnRawEscape(CropStealEvent ev)
    {
        if (animator != null)
            animator.OnEscape(ev);
        else
            PlayEscapeJuice();
    }

    public void PlayHitJuice()
    {
        _juiceTimerMs = JuiceDurationMs;
    }

    // Carry-off juice: mole holds the fruit visibly, does a short hop arc, then
    // sinks with it. Runs ~0.8s in parallel with the rules sink — presentation only.
    public void PlayEscapeJuice()
    {
        _escapeJuiceMs = EscapeJuiceDurationMs;
        if (spriteTransform != null) spriteTransform.localPosition = Vector3.zero;
        if (fruitSprite != null) fruitSprite.enabled = true;
        if (fruitTransform != null) fruitTransform.localPosition = new Vector3(0.25f, 0f, 0f);
    }

    private void CancelEscapeJuice()
    {
        _escapeJuiceMs = 0f;
        if (fruitSprite != null) fruitSprite.enabled = false;
        if (spriteTransform != null) spriteTransform.localPosition = Vector3.zero;
        if (fruitTransform != null) fruitTransform.localPosition = Vector3.zero;
    }

    private void Update()
    {
        bool escapeJuice = _escapeJuiceMs > 0f;
        bool hitJuice = _juiceTimerMs > 0f;

        if (escapeJuice)
        {
            _escapeJuiceMs -= Time.deltaTime * 1000f;
            float p = Mathf.Clamp01(1f - _escapeJuiceMs / EscapeJuiceDurationMs); // 0..1
            float arc = Mathf.Sin(p * Mathf.PI); // 0..1..0 hop

            if (spriteTransform != null)
            {
                spriteTransform.localPosition = new Vector3(0.4f * p, arc * HopHeight, 0f);
                spriteTransform.localScale = new Vector3(_phaseScale.x, _phaseScale.y, 1f);
            }
            // Fruit lifts with the mole, then drops as the mole sinks with it.
            if (fruitTransform != null)
                fruitTransform.localPosition = new Vector3(0.25f, -0.35f + 0.35f * arc, 0f);

            if (_escapeJuiceMs <= 0f)
                CancelEscapeJuice();
            return;
        }

        if (hitJuice)
        {
            _juiceTimerMs -= Time.deltaTime * 1000f;
            float progress = Mathf.Clamp01(1f - _juiceTimerMs / JuiceDurationMs);
            float eased = 1f - (1f - progress) * (1f - progress); // ease-out
            float pop = Mathf.Lerp(PopScale, 1f, eased);

            if (spriteTransform != null)
                spriteTransform.localScale = new Vector3(_phaseScale.x * pop, _phaseScale.y * pop, 1f);
            if (spriteRenderer != null)
                spriteRenderer.color = Color.Lerp(Color.white, _normalColor, eased);
            return;
        }

        if (spriteTransform != null)
        {
            spriteTransform.localScale = new Vector3(_phaseScale.x, _phaseScale.y, 1f);
            spriteTransform.localPosition = Vector3.zero;
            if (spriteRenderer != null) spriteRenderer.color = _normalColor;
        }
    }
}