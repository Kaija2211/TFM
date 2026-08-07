using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Sim;

namespace Manager
{
    // A single player representation on the Tactics Board - either a pitch pin
    // (droppable, not draggable) or a bench card (draggable, not droppable). Both
    // roles are tappable to open Player Inspect, matching the "click a row and
    // you're there" philosophy already established by SquadListView.
    //
    // Drag visual is a lightweight ghost that follows the pointer while the
    // original card stays in place and dimmed - simpler than reparenting/restoring
    // the original, since a successful drop triggers a full board refresh anyway
    // (see ManagerPrototypeController.RefreshTacticsBoardUI) that discards every
    // card and rebuilds fresh regardless of how the drag ended.
    public class TacticsBoardPlayerCard : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public PlayerAgent Player { get; private set; }

        private bool isDraggable;
        private bool isDropTarget;
        private Action<PlayerAgent> onClicked;
        private Action<PlayerAgent, PlayerAgent> onBenchPlayerDroppedOnPin;

        private Image background;
        private Canvas rootCanvas;
        private RectTransform dragGhost;
        private bool isDragging;

        public void Configure(
            PlayerAgent player,
            bool isDraggable,
            bool isDropTarget,
            Action<PlayerAgent> onClicked,
            Action<PlayerAgent, PlayerAgent> onBenchPlayerDroppedOnPin)
        {
            Player = player;
            this.isDraggable = isDraggable;
            this.isDropTarget = isDropTarget;
            this.onClicked = onClicked;
            this.onBenchPlayerDroppedOnPin = onBenchPlayerDroppedOnPin;

            TryGetComponent(out background);
            rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // Unity's own click-vs-drag suppression (eventData.eligibleForClick, cleared
            // the moment a drag starts) didn't hold up here - a real drag onto a pin still
            // fired this afterward, opening Player Inspect mid-gesture and short-circuiting
            // the drag's normal end-of-gesture cleanup (confirmed live: the ghost froze in
            // place on the Inspect screen it navigated to). isDragging is a second,
            // explicit guard against that regardless of why Unity's own tracking missed it.
            if (isDragging)
            {
                return;
            }

            onClicked?.Invoke(Player);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!isDraggable || rootCanvas == null)
            {
                return;
            }

            isDragging = true;

            if (background != null)
            {
                background.color = ManagerUITheme.Accent;
            }

            GameObject ghostObject = new GameObject("DragGhost", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            ghostObject.transform.SetParent(rootCanvas.transform, false);
            ghostObject.transform.SetAsLastSibling();

            dragGhost = ghostObject.GetComponent<RectTransform>();
            dragGhost.sizeDelta = new Vector2(150f, 46f);
            ghostObject.GetComponent<Image>().color = ManagerUITheme.CardNeutral;
            ghostObject.GetComponent<CanvasGroup>().alpha = 0.9f;
            ghostObject.GetComponent<CanvasGroup>().blocksRaycasts = false;

            ManagerUITheme.BuildLabel(
                ghostObject.transform,
                $"{Player.Name} · {Player.PrimaryPosition}",
                13,
                ManagerUITheme.TextPrimary,
                TextAlignmentOptions.Center,
                FontStyles.Bold);

            UpdateGhostPosition(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isDraggable || dragGhost == null)
            {
                return;
            }

            UpdateGhostPosition(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!isDraggable)
            {
                return;
            }

            EndDragVisual();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (!isDropTarget || eventData.pointerDrag == null)
            {
                return;
            }

            if (!eventData.pointerDrag.TryGetComponent(out TacticsBoardPlayerCard draggedCard) || !draggedCard.isDraggable)
            {
                return;
            }

            // Must clean up the dragged card's ghost/highlight HERE, before invoking the
            // sub callback - not left to rely on OnEndDrag firing afterward. The sub
            // callback triggers a full board rebuild that destroys every bench card,
            // including the one currently being dragged; Destroy() makes a UnityEngine
            // Object compare == null immediately (even though actual destruction is
            // deferred to end of frame), so Unity's EventSystem sees pointerDrag as null
            // by the time it goes to call OnEndDrag and silently skips it - orphaning the
            // ghost on screen forever. Confirmed live: dragging worked, but the floating
            // name box never went away after a successful drop.
            draggedCard.EndDragVisual();

            onBenchPlayerDroppedOnPin?.Invoke(draggedCard.Player, Player);
        }

        // Destroys the drag ghost and restores the card's normal background - called from
        // OnEndDrag for a drag that didn't land on a valid target, and explicitly from
        // OnDrop (on the target) for one that did, since OnEndDrag can't be relied on to
        // fire in that case (see OnDrop's comment above).
        private void EndDragVisual()
        {
            isDragging = false;

            if (background != null)
            {
                background.color = ManagerUITheme.CardNeutralAlt;
            }

            if (dragGhost != null)
            {
                Destroy(dragGhost.gameObject);
                dragGhost = null;
            }
        }

        private void UpdateGhostPosition(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)rootCanvas.transform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint);

            dragGhost.anchoredPosition = localPoint;
        }
    }
}
