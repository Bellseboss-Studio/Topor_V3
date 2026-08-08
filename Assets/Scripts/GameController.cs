using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Owns GameRules, wraps the serialized AnimationCurve into the pure Func seam,
// drives spawn scheduling, routes tap input into hits, updates the HUD.
// Direct references only — no DI, no events.
public sealed class GameController : MonoBehaviour
{
    [Header("Difficulty")]
    [SerializeField] private AnimationCurve difficultyCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 2f), new Keyframe(1f, 0f));
    [SerializeField] private float matchDurationSec = 60f;

    [Header("Mole timing (ms)")]
    [SerializeField] private float upWindowMs = 1500f;
    [SerializeField] private float riseMs = 250f;
    [SerializeField] private float sinkMs = 250f;

    [Header("Lives")]
    [SerializeField] private int initialLives = 3;

    [Header("Spawning")]
    [SerializeField] private float baseSpawnIntervalSec = 3f;

    [Header("References")]
    [SerializeField] private Mole[] moles;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Text scoreText;   // "Puntos: N"
    [SerializeField] private Text livesText;   // "Frutas: N"

    private GameRules _rules;
    private InputAction _tapAction;
    private InputAction _pointAction;
    private float _elapsedMs;
    private float _spawnCooldownMs;

    private void Awake()
    {
        var cfg = new GameRulesConfig
        {
            MatchDurationMs = matchDurationSec * 1000f,
            UpWindowMs = upWindowMs,
            RiseDurationMs = riseMs,
            SinkDurationMs = sinkMs,
            InitialLives = initialLives,
            HoleCount = moles != null ? moles.Length : 0,
            BaseSpawnIntervalMs = baseSpawnIntervalSec * 1000f,
            IntensityCurve = p => difficultyCurve.Evaluate(p), // curve -> pure seam
        };

        _rules = new GameRules(cfg, () => UnityEngine.Random.value);
        _rules.StartMatch();
        if (moles != null)
            for (int i = 0; i < moles.Length; i++)
                if (moles[i] != null) moles[i].Bind(_rules, i);

        _spawnCooldownMs = _rules.SpawnIntervalMs(0f);

        // Input created in code (no generated class, no asset plumbing).
        _tapAction = new InputAction("Tap", InputActionType.Button);
        _tapAction.AddBinding("<Mouse>/leftButton");
        _tapAction.AddBinding("<Touchscreen>/touch*/press");
        _tapAction.performed += OnTap;

        _pointAction = new InputAction("Point", InputActionType.Value, "<Pointer>/position");
    }

    private void OnEnable()
    {
        _tapAction?.Enable();
        _pointAction?.Enable();
    }

    private void OnDisable()
    {
        _tapAction?.Disable();
        _pointAction?.Disable();
    }

    private void Update()
    {
        if (_rules == null) return;

        float deltaMs = Time.deltaTime * 1000f;
        _elapsedMs += deltaMs;

        _rules.Update(_elapsedMs); // expire moles -> lives/GameOver

        _spawnCooldownMs -= deltaMs;
        if (_spawnCooldownMs <= 0f)
        {
            _spawnCooldownMs = _rules.SpawnIntervalMs(_elapsedMs);
            _rules.TrySpawn(_elapsedMs);
        }

        if (moles != null)
            for (int i = 0; i < moles.Length; i++)
                if (moles[i] != null) moles[i].SyncFromRules(_elapsedMs);

        UpdateHud();
    }

    private void OnTap(InputAction.CallbackContext context)
    {
        if (_rules == null || _rules.IsGameOver || moles == null) return;

        Vector2 screenPos = _pointAction.ReadValue<Vector2>();
        Vector3 world = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, mainCamera.nearClipPlane));

        for (int i = 0; i < moles.Length; i++)
        {
            if (moles[i] == null) continue;
            if (moles[i].ContainsPoint(world) && _rules.TryHit(i, _elapsedMs))
            {
                moles[i].PlayHitJuice();
                break; // one mole per tap
            }
        }
    }

    private void UpdateHud()
    {
        if (scoreText != null) scoreText.text = "Puntos: " + _rules.Score;
        if (livesText != null) livesText.text = "Frutas: " + _rules.Lives;
    }
}
