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
            onClicked?.Invoke(Player);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!isDraggable || rootCanvas == null)
            {
                return;
            }

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

            onBenchPlayerDroppedOnPin?.Invoke(draggedCard.Player, Player);
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
