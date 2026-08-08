using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Owns GameRules, wraps the serialized AnimationCurve into the pure Func seam,
// drives spawn scheduling, routes tap input into hits, drains the escape queue
// into the presenters, and updates the HUD (score + remaining time). Direct
// references only — no DI, no events, no colliders.
public sealed class GameController : MonoBehaviour
{
    // grid-v2 (A1/P7): hole -> candidate crop indexes, serialized as rows so the
    // scene author can mirror the 10-hole adjacency table (row index = hole index
    // = Mole n binding). Pure data; rules consume it as int[][].
    [Serializable]
    internal class HoleAdjacency
    {
        public int[] Crops;
    }

    [Header("Difficulty")]
    [SerializeField] private AnimationCurve difficultyCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 2f), new Keyframe(1f, 0f));
    [SerializeField] private float matchDurationSec = 60f;

    [Header("Mole timing (ms)")]
    [SerializeField] private float telegraphMs = 800f;
    [SerializeField] private float upWindowMs = 1500f;
    [SerializeField] private float riseMs = 250f;
    [SerializeField] private float sinkMs = 250f;

    [Header("Spawning")]
    [SerializeField] private float baseSpawnIntervalSec = 3f;

    [Header("References")]
    [SerializeField] private Mole[] moles;          // one per hole (17, per adjacency table)
    [SerializeField] private Crop[] crops;          // the 2x3 grid (6) — crops ARE lives
    [SerializeField] private Hole[] holes;          // one per hole (17) — visual presenters
    [SerializeField] private HoleAdjacency[] holeAdjacencies; // 17 rows, index = hole = Mole n
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Text scoreText;        // "Puntos: N"
    [SerializeField] private Text remainingTimeText; // "01:00" ticking down (A5)

    private GameRules _rules;
    private HoleVisuals _holeVisuals;
    private InputAction _tapAction;
    private InputAction _pointAction;
    private float _elapsedMs;
    private float _spawnCooldownMs;
    private float _lastHudSecond = -1f;

    private void Awake()
    {
        int holeCount = moles != null ? moles.Length : 0;
        int cropCount = crops != null ? crops.Length : 0;

        var cfg = new GameRulesConfig
        {
            MatchDurationMs = matchDurationSec * 1000f,
            TelegraphDurationMs = telegraphMs,
            UpWindowMs = upWindowMs,
            RiseDurationMs = riseMs,
            SinkDurationMs = sinkMs,
            CropCount = cropCount,
            HoleCount = holeCount,
            // grid-v2 adjacency: serialized rows -> pure int[][] config (P1). The
            // rules are never bound to the scene; the table IS the source of truth.
            HoleCandidates = holeAdjacencies != null
                ? Array.ConvertAll(holeAdjacencies, a => a != null && a.Crops != null ? a.Crops : Array.Empty<int>())
                : null,
            BaseSpawnIntervalMs = baseSpawnIntervalSec * 1000f,
            IntensityCurve = p => difficultyCurve.Evaluate(p), // curve -> pure seam
        };

        _rules = new GameRules(cfg, () => UnityEngine.Random.value);
        _rules.StartMatch();
        if (moles != null)
            for (int i = 0; i < moles.Length; i++)
                if (moles[i] != null) moles[i].Bind(_rules, i);

        _holeVisuals = new HoleVisuals(cfg.HoleCount, 150f);

        _spawnCooldownMs = _rules.SpawnIntervalMs(0f);

        // Input created in code (no generated class, no asset plumbing).
        _tapAction = new InputAction("Tap", InputActionType.Button);
        _tapAction.AddBinding("<Pointer>/press");       // works for mouse + touch in simulator
        _tapAction.AddBinding("<Mouse>/leftButton");    // explicit mouse fallback
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

        // 1. Advance rules: Telegraph -> Rise -> Up -> (escape steals crop at T).
        _rules.Update(_elapsedMs);

        // 2. Same-frame escape drain: visuals consume the queue immediately; rules
        //    never wait on animation (design D2 / data flow).
        var escapes = _rules.DrainEscapes();
        for (int e = 0; e < escapes.Count; e++)
        {
            var ev = escapes[e];
            if (moles != null && ev.MoleIndex >= 0 && ev.MoleIndex < moles.Length && moles[ev.MoleIndex] != null)
                moles[ev.MoleIndex].PlayEscapeJuice();
            if (crops != null && ev.CropIndex >= 0 && ev.CropIndex < crops.Length && crops[ev.CropIndex] != null)
                crops[ev.CropIndex].SetStolen();
        }

        // 3. Spawn scheduling.
        _spawnCooldownMs -= deltaMs;
        if (_spawnCooldownMs <= 0f)
        {
            _spawnCooldownMs = _rules.SpawnIntervalMs(_elapsedMs);
            _rules.TrySpawn(_elapsedMs);
        }

        // 4. Presenters read rules state.
        if (moles != null)
            for (int i = 0; i < moles.Length; i++)
                if (moles[i] != null) moles[i].SyncFromRules(_elapsedMs);

        if (holes != null && _holeVisuals != null)
            for (int i = 0; i < holes.Length; i++)
                if (holes[i] != null) holes[i].SetState(_holeVisuals.StateFor(i, _rules.GetPhase(i), _elapsedMs), _elapsedMs);

        UpdateCrops();
        UpdateHud();
    }

    private void OnTap(InputAction.CallbackContext context)
    {
        if (_rules == null || _rules.IsGameOver || moles == null || _holeVisuals == null) return;

        Vector2 screenPos = _pointAction.ReadValue<Vector2>();
        // Camera at z=-10, holes at z=0 → distance = 10
        float camToWorldZ = -mainCamera.transform.position.z;
        Vector3 world = mainCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, camToWorldZ));

        // Order holes by distance to the tap, then hit the FIRST hittable mole in
        // range. Edge-click bug: holes are 0.75/0.7u apart but a risen mole's visual
        // spans up to ~1.2u, so the closest hole to the tap can be a Sunk neighbour
        // while the real target is the second-closest. Trying all in range, in order,
        // restores the grid-v2 "hit the first hittable" behaviour (no colliders — math).
        int n = moles.Length;
        int[] order = new int[n];
        float[] distSq = new float[n];
        for (int i = 0; i < n; i++) { order[i] = i; distSq[i] = float.MaxValue; if (moles[i] != null) distSq[i] = (world - moles[i].transform.position).sqrMagnitude; }
        System.Array.Sort(order, (a, b) => distSq[a].CompareTo(distSq[b]));

        const float HitRadiusSq = 0.36f; // 0.6^2
        for (int k = 0; k < n; k++)
        {
            int i = order[k];
            if (moles[i] == null || distSq[i] > HitRadiusSq) break; // beyond 0.6u
            if (_rules.TryHit(i, _elapsedMs))
            {
                if (moles[i] != null) moles[i].PlayHitJuice();
                _holeVisuals.RegisterTap(i, true, _elapsedMs);
                return;
            }
        }

        // No hittable mole inside the radius: flash a miss on the nearest hole only
        // if the tap actually landed on/near a hole (V6-S3: tap > 0.6u → no flash).
        if (n > 0 && distSq[order[0]] <= HitRadiusSq)
            _holeVisuals.RegisterTap(order[0], false, _elapsedMs);
    }

    // Flash the crop while any telegraphing mole threatens it; stolen crops are
    // handled by Crop.SetStolen themselves.
    private void UpdateCrops()
    {
        if (crops == null) return;

        for (int c = 0; c < crops.Length; c++)
        {
            if (crops[c] == null) continue;
            bool threatened = false;
            for (int m = 0; m < moles.Length && !threatened; m++)
            {
                if (moles[m] == null) continue;
                // Only moles in the telegraph phase announce their target crop.
                if (_rules.GetPhase(m) == MolePhase.Telegraphing && _rules.ThreatenedCrop(m) == c)
                    threatened = true;
            }
            crops[c].SetThreatened(threatened);
        }
    }

    private void UpdateHud()
    {
        if (scoreText != null) scoreText.text = "Puntos: " + _rules.Score;

        // Timer (A5): remaining match time, updated at least once per second, MM:SS.
        if (remainingTimeText != null)
        {
            float remainingMs = Mathf.Max(0f, matchDurationSec * 1000f - _elapsedMs);
            int second = (int)(remainingMs / 1000f);
            if (second != _lastHudSecond)
            {
                _lastHudSecond = second;
                remainingTimeText.text = GameRules.FormatRemainingMs(remainingMs);
            }
        }
    }
}