using UnityEngine;

public partial class PlacementController
{
    RainBlockPickup _heldRainPickup;

    static string EnvironmentRangeLabel(TurretController turret)
    {
        var env = ChapterEnvironmentController.Instance;
        return env != null && env.IsInMist(turret)
            ? $" (mist -{(1f - env.Profile.mistRangeMultiplier) * 100f:0}%)" : "";
    }

    public bool TryGrabRainBlock(RainBlockPickup pickup)
    {
        if (pickup == null || pickup.Data == null || GameFlowManager.SettlementUp) return false;
        if (currentBlock != null || mode == PlacementMode.Edit || _batchMoving)
        { ShowPlacementPopup("Place or cancel what you're holding first."); return false; }
        if (!TutorialDirector.CanPurchase(pickup.Data))
        { ShowPlacementPopup("Follow the current tutorial step first."); return false; }
        ClearBoardSelection();
        _heldRainPickup = pickup;
        _heldRainGranted = true;
        currentBlock = pickup.Data;
        currentSynergyColor = pickup.Color;
        currentColor = pickup.Tint;
        _pendingShopPrice = 0;
        isPickingUpObject = false;
        lastObjectCells = null;
        activePhysicsObject = pickup.gameObject;
        _ghostAnchorSnap = true;
        pickup.SetHeld(true);
        EnterEditMode(null);
        return true;
    }

    void ReleaseHeldRainBlock()
    {
        if (_heldRainPickup == null) return;
        _heldRainPickup.SetHeld(false);
        _heldRainPickup = null;
        _heldRainGranted = false;
        activePhysicsObject = null;
        currentBlock = null;
        _pendingShopPrice = 0;
        isPickingUpObject = false;
    }

    public void ClearHeldRainBlock()
    {
        if (_heldRainPickup == null) return;
        var pickup = _heldRainPickup;
        CancelEditMode();
        if (pickup != null) ChapterEnvironmentController.ReleaseObject(pickup.gameObject);
    }
}
