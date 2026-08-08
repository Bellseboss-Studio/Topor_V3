using UnityEngine;

public sealed class Hole : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _renderer;
    [SerializeField] private Color dimColor = new Color(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color emphasisColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color hitFlashColor = new Color(0.2f, 0.9f, 0.2f, 1f);
    [SerializeField] private Color missFlashColor = new Color(0.9f, 0.2f, 0.2f, 1f);

    private HoleVisualState _state;

    private void Awake()
    {
        if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
    }

    public void SetState(HoleVisualState state, float nowMs)
    {
        _state = state;
    }

    private void Update()
    {
        if (_renderer == null) return;

        switch (_state)
        {
            case HoleVisualState.Dim:
                _renderer.color = dimColor;
                break;
            case HoleVisualState.Emphasis:
                _renderer.color = emphasisColor;
                break;
            case HoleVisualState.HitFlash:
                _renderer.color = hitFlashColor;
                break;
            case HoleVisualState.MissFlash:
                _renderer.color = missFlashColor;
                break;
        }
    }
}