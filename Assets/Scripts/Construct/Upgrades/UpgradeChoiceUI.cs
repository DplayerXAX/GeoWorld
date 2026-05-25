using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Stub UI for the "pick 1 of N" upgrade choice. Currently auto-picks the
// first card after a short delay so the architecture is testable end-to-end
// before real UI is authored.
//
// Real implementation TODO:
//   • Render a row of cards (icon / name / description / rarity)
//   • Pause input on the rest of the game until a card is clicked
//   • Animate in / out
//   • Call Pick(index) from card button OnClick
//
// Public contract:
//   • Show(cards, onPicked) — UpgradeManager calls this with the offer.
//   • Pick(index)           — the UI calls this when the player clicks
//                             a card. Triggers the onPicked callback.
public class UpgradeChoiceUI : MonoBehaviour
{
    public static UpgradeChoiceUI Instance;

    [Tooltip("Seconds before the stub auto-picks the first card. Used only while real UI is not implemented.")]
    [Min(0f)] public float stubAutoPickDelay = 0.5f;

    Action<UpgradeCard> _onPicked;
    IList<UpgradeCard>  _currentOffer;
    Coroutine           _stubRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
    }

    public bool IsShowing => _currentOffer != null;

    public void Show(IList<UpgradeCard> cards, Action<UpgradeCard> onPicked)
    {
        if (cards == null || cards.Count == 0)
        {
            onPicked?.Invoke(null);
            return;
        }

        _currentOffer = cards;
        _onPicked     = onPicked;

        Debug.Log($"[UpgradeChoiceUI] Offering {cards.Count} cards:");
        for (int i = 0; i < cards.Count; i++)
            Debug.Log($"  [{i}] {cards[i].displayName} ({cards[i].rarity})  tags=[{string.Join(",", cards[i].tags)}]");

        // STUB — remove once real UI is wired.
        if (_stubRoutine != null) StopCoroutine(_stubRoutine);
        _stubRoutine = StartCoroutine(AutoPickStub());
    }

    IEnumerator AutoPickStub()
    {
        if (stubAutoPickDelay > 0f) yield return new WaitForSeconds(stubAutoPickDelay);
        Pick(0);
        _stubRoutine = null;
    }

    // Hook this up to your card-button OnClick (or a debug button) once
    // the real UI exists. index is into the offer list passed to Show().
    public void Pick(int index)
    {
        if (_currentOffer == null) return;

        var card = (index >= 0 && index < _currentOffer.Count) ? _currentOffer[index] : null;

        var cb = _onPicked;
        _currentOffer = null;
        _onPicked     = null;

        cb?.Invoke(card);
    }

    // Optional: cancel the current offer without picking. Useful if the run
    // ends (game over) mid-choice. Currently no callers — kept for future use.
    public void Cancel()
    {
        if (_stubRoutine != null) { StopCoroutine(_stubRoutine); _stubRoutine = null; }
        _currentOffer = null;
        _onPicked     = null;
    }
}
