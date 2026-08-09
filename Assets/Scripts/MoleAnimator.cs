using UnityEngine;

/// <summary>
/// Extension point for mole animation. Derive from this class, override the virtual
/// methods you want to control, and drop the subclass onto a Mole GameObject.
///
/// Implementation-agnostic: no mandatory Unity Animator reference. The user can use
/// Animator, frame-by-frame sprites, code-driven tweens, or any other technique inside
/// the subclass.
///
/// Defaults reproduce the HEAD procedural behavior (scale tweens, hit pop, escape hop)
/// so the game remains playable without a custom animator.
///
/// When wired (via Mole.animator), Mole dispatches intents to these virtual callbacks
/// and stops its own inline visuals.
/// </summary>
public class MoleAnimator : MonoBehaviour
{
    // --- Serialized fields (assignable in Inspector) ---

    [SerializeField] protected SpriteRenderer spriteRenderer;
    [SerializeField] protected Transform spriteTransform;
    [SerializeField] protected SpriteRenderer fruitSprite;
    [SerializeField] protected Transform fruitTransform;

    // --- Pure static helpers (EditMode-testable, zero Time dependency) ---

    /// <summary>
    /// Linear scale progression for the rise phase: 0.2 (barely visible) → 1.0 (full).
    /// Matches the exact math in Mole.SyncFromRules at HEAD.
    /// </summary>
    public static float RiseScale01(float t) => 0.2f + 0.8f * Mathf.Clamp01(t);

    /// <summary>
    /// Linear scale progression for the sink phase: 1.0 (full) → 0.2 (hidden).
    /// Inverse of RiseScale01. Matches Mole.SyncFromRules at HEAD.
    /// </summary>
    public static float SinkScale01(float t) => 1f - 0.8f * Mathf.Clamp01(t);

    /// <summary>
    /// Ease-out pop multiplier: 1.3 (max) → 1.0 (normal) with quadratic decay.
    /// At progress=0 returns max pop, at progress=1 returns normal.
    /// Matches the exact math in Mole.Update hit juice at HEAD.
    /// </summary>
    public static float PopScale(float progress)
    {
        float p = Mathf.Clamp01(progress);
        float eased = 1f - (1f - p) * (1f - p); // quadratic ease-out
        return Mathf.Lerp(PopScaleMax, 1f, eased);
    }

    // --- Constants (matching Mole.cs HEAD) ---

    protected const float PopScaleMax = 1.3f;
    protected const float JuiceDurationMs = 150f;
    protected const float EscapeJuiceDurationMs = 800f;
    protected const float HopHeight = 0.6f;

    // --- Internal state (mirrors Mole.cs HEAD) ---

    protected Vector2 _phaseScale = Vector2.zero;
    protected float _juiceTimerMs;
    protected float _escapeJuiceMs;
    protected Color _normalColor = Color.white;

    // Tween state for default rise/sink animations
    private float _tweenElapsedMs;
    private float _tweenDurationMs;
    private float _tweenStartScale;
    private float _tweenEndScale;
    private bool _tweenActive;

    // --- Lifecycle ---

    protected virtual void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteTransform == null)
            spriteTransform = spriteRenderer != null ? spriteRenderer.transform : transform;
        if (spriteRenderer != null)
            _normalColor = spriteRenderer.color;
    }

    // --- Virtual callbacks (one-shot intent signals) ---

    /// <summary>Sunk→Telegraphing: mole about to surface. Default: invisible.</summary>
    public virtual void OnHide()
    {
        _phaseScale = Vector2.zero;
        _tweenActive = false;
        CancelEscapeJuice();
    }

    /// <summary>
    /// Sunk→Rising or Telegraphing→Rising: mole emerges.
    /// Default: tween scale 0.2→1.0 over 250ms, driven by Update.
    /// </summary>
    public virtual void OnRise()
    {
        _tweenStartScale = 0.2f;
        _tweenEndScale = 1f;
        _tweenDurationMs = 250f;
        _tweenElapsedMs = 0f;
        _tweenActive = true;
        CancelEscapeJuice();
    }

    /// <summary>
    /// Rising→Up: mole fully visible, loop idle.
    /// Default: hold scale at 1.0 (no tween — idle until next intent).
    /// </summary>
    public virtual void OnSearch()
    {
        _tweenActive = false;
        _phaseScale = Vector2.one;
    }

    /// <summary>
    /// Up→Sinking or Rising→Sinking: mole retreats.
    /// Default: tween scale 1.0→0.2 over 250ms, driven by Update.
    /// </summary>
    public virtual void OnSink()
    {
        _tweenStartScale = 1f;
        _tweenEndScale = 0.2f;
        _tweenDurationMs = 250f;
        _tweenElapsedMs = 0f;
        _tweenActive = true;
    }

    /// <summary>
    /// Hit event from TryHit. Default: pop ×1.3, 150ms ease-out with white flash.
    /// </summary>
    public virtual void OnHit()
    {
        _juiceTimerMs = JuiceDurationMs;
    }

    /// <summary>
    /// Escape event from DrainEscapes with crop steal data.
    /// Default: 800ms hop arc (height 0.6) while carrying the fruit sprite.
    /// </summary>
    public virtual void OnEscape(CropStealEvent ev)
    {
        _escapeJuiceMs = EscapeJuiceDurationMs;
        if (spriteTransform != null)
            spriteTransform.localPosition = Vector3.zero;
        if (fruitSprite != null)
            fruitSprite.enabled = true;
        if (fruitTransform != null)
            fruitTransform.localPosition = new Vector3(0.25f, 0f, 0f);
    }

    /// <summary>
    /// Sinking→Sunk: mole fully hidden, cycle complete.
    /// Default: snap scale to zero, cancel all active juices, reset color/position.
    /// </summary>
    public virtual void OnReset()
    {
        _phaseScale = Vector2.zero;
        _tweenActive = false;
        CancelEscapeJuice();
        _juiceTimerMs = 0f;
        if (spriteTransform != null)
        {
            spriteTransform.localScale = Vector3.one;
            spriteTransform.localPosition = Vector3.zero;
        }
        if (spriteRenderer != null)
            spriteRenderer.color = _normalColor;
    }

    /// <summary>
    /// Species info received at spawn (before first intent).
    /// Default: no-op. Override to swap sprites, colors, or material per species.
    /// </summary>
    public virtual void SetSpecies(MoleSpecies species)
    {
        // no-op — subclasses override to customize per species
    }

    // --- Update (drives default procedural animations) ---

    protected virtual void Update()
    {
        bool escapeJuice = _escapeJuiceMs > 0f;
        bool hitJuice = _juiceTimerMs > 0f;

        if (escapeJuice)
        {
            UpdateEscapeJuice();
            return; // escape takes priority — blocks other visuals
        }

        if (hitJuice)
        {
            UpdateHitJuice();
            return; // hit juice blocks normal visuals while active
        }

        UpdatePhaseTween();
        ApplyPhaseScale();
    }

    private void UpdateEscapeJuice()
    {
        _escapeJuiceMs -= Time.deltaTime * 1000f;
        float p = Mathf.Clamp01(1f - _escapeJuiceMs / EscapeJuiceDurationMs);
        float arc = Mathf.Sin(p * Mathf.PI); // 0→1→0 hop

        if (spriteTransform != null)
        {
            spriteTransform.localPosition = new Vector3(0.4f * p, arc * HopHeight, 0f);
            spriteTransform.localScale = new Vector3(_phaseScale.x, _phaseScale.y, 1f);
        }
        if (fruitTransform != null)
            fruitTransform.localPosition = new Vector3(0.25f, -0.35f + 0.35f * arc, 0f);

        if (_escapeJuiceMs <= 0f)
            CancelEscapeJuice();
    }

    private void UpdateHitJuice()
    {
        _juiceTimerMs -= Time.deltaTime * 1000f;
        float progress = Mathf.Clamp01(1f - Mathf.Max(0f, _juiceTimerMs) / JuiceDurationMs);
        float pop = PopScale(progress); // reuse pure helper

        if (spriteTransform != null)
            spriteTransform.localScale = new Vector3(_phaseScale.x * pop, _phaseScale.y * pop, 1f);
        if (spriteRenderer != null)
        {
            float eased = 1f - (1f - progress) * (1f - progress);
            spriteRenderer.color = Color.Lerp(Color.white, _normalColor, eased);
        }

        if (_juiceTimerMs <= 0f)
            ApplyPhaseScale(); // revert to clean state after juice expires
    }

    private void UpdatePhaseTween()
    {
        if (!_tweenActive) return;

        _tweenElapsedMs += Time.deltaTime * 1000f;
        float t = Mathf.Clamp01(_tweenElapsedMs / Mathf.Max(1f, _tweenDurationMs));
        float scale = Mathf.Lerp(_tweenStartScale, _tweenEndScale, t);
        _phaseScale = new Vector2(scale, scale);

        if (_tweenElapsedMs >= _tweenDurationMs)
            _tweenActive = false;
    }

    private void ApplyPhaseScale()
    {
        if (spriteTransform != null)
        {
            spriteTransform.localScale = new Vector3(_phaseScale.x, _phaseScale.y, 1f);
            spriteTransform.localPosition = Vector3.zero;
            if (spriteRenderer != null)
                spriteRenderer.color = _normalColor;
        }
    }

    /// <summary>
    /// Cancels the escape hop juice, disables fruit sprite, resets transforms.
    /// Called by OnHide, OnRise, OnReset, and when the juice timer naturally expires.
    /// </summary>
    protected void CancelEscapeJuice()
    {
        _escapeJuiceMs = 0f;
        if (fruitSprite != null)
            fruitSprite.enabled = false;
        if (spriteTransform != null)
            spriteTransform.localPosition = Vector3.zero;
        if (fruitTransform != null)
            fruitTransform.localPosition = Vector3.zero;
    }
}
