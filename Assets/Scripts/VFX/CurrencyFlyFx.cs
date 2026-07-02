using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Currency-gain feedback: spawns a small burst of coin icons that fly from a world
// position (sell location, harvest tile, enemy kill spot, ...) to the matching
// currency counter in TopLeftHUD, then pulses the counter on arrival. Pure UGUI
// (ScreenSpaceOverlay), runtime-built, no prefabs — same "no scene setup" pattern
// as SynergyBuffFx.Mote for world VFX.
public static class CurrencyFlyFx
{
    static Canvas _canvas;
    static RectTransform _root;

    static void EnsureCanvas()
    {
        if (_canvas != null) return;
        var go = new GameObject("CurrencyFlyCanvas", typeof(Canvas), typeof(CanvasScaler));
        Object.DontDestroyOnLoad(go);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 95;   // above HUD/objectives, below tutorial hint (120)
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        _root = (RectTransform)go.transform;
    }

    // Spawns coins flying from `worldPos` (projected to screen) to the block or
    // turret currency counter. Call alongside the matching ResourceManager
    // Add*Currency/RefundBlock call. `amount` just scales how many coins pop (visual
    // weight for a bigger gain), not the counted value itself.
    public static void Fly(Vector3 worldPos, bool isTurret, int amount = 1)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(worldPos);
        if (sp.z < 0f) return;   // behind the camera — nothing sensible to draw
        FlyFromScreen(sp, isTurret, amount);
    }

    public static void FlyFromScreen(Vector2 screenPos, bool isTurret, int amount = 1)
    {
        var hud = TopLeftHUD.Instance;
        if (hud == null) return;
        EnsureCanvas();

        int count = Mathf.Clamp(1 + amount / 8, 1, 6);
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("CurrencyCoin", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(20f, 20f);
            rt.position = screenPos;

            var img = go.AddComponent<Image>();
            img.sprite = UIRoundedRect.Get(10);
            img.color = isTurret ? new Color(1f, 0.65f, 0.30f) : new Color(0.55f, 0.95f, 1.00f);
            img.raycastTarget = false;

            go.AddComponent<CurrencyCoinRunner>().Begin(rt, hud, isTurret, i);
        }
    }
}

// One flying coin: eases along a small arc from its spawn point to the live HUD
// counter position (re-sampled every frame so it tracks layout), scaling down as it
// lands, then pulses the counter and self-destructs.
class CurrencyCoinRunner : MonoBehaviour
{
    RectTransform _rt;
    TopLeftHUD _hud;
    bool _isTurret;
    Vector2 _start;
    Vector2 _arcOffset;
    float _t;
    float _duration;

    public void Begin(RectTransform rt, TopLeftHUD hud, bool isTurret, int index)
    {
        _rt = rt; _hud = hud; _isTurret = isTurret;
        _start = rt.position;
        // Slight per-coin stagger/arc so a burst of several doesn't overlap perfectly.
        _arcOffset = new Vector2(Random.Range(-40f, 40f), Random.Range(40f, 90f));
        _duration = 0.55f + index * 0.05f;
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        while (_t < _duration)
        {
            _t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            float ease = 1f - Mathf.Pow(1f - k, 3f);   // ease-out cubic

            Vector2 target = _isTurret ? _hud.TurretCounterScreenPos : _hud.BlockCounterScreenPos;
            // Arc: rise then settle toward target, flattening out as it nears the end.
            Vector2 arc = _arcOffset * Mathf.Sin(k * Mathf.PI) * (1f - ease * 0.5f);
            _rt.position = Vector2.Lerp(_start, target, ease) + arc;
            _rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.35f, ease);
            yield return null;
        }

        if (_isTurret) _hud.PulseTurretCounter(); else _hud.PulseBlockCounter();
        Destroy(gameObject);
    }
}
