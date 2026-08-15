using System;
using System.Collections.Generic;
using Manager.Save;
using Sim;

namespace Manager
{
    // Generic manager message list (session 13) - phase 3 of the manager influence arc,
    // see project_manager_influence_arc in memory (captaincy, fitness/condition, and
    // morale already shipped; this was the last unclaimed piece). Transfer bid results
    // are the first real message type, but the shape (Type/Title/Body/matchday/read
    // state) is deliberately generic so future message types can reuse it without a
    // rework - Thomas's own framing when this was floated.
    //
    // Display text is baked into Title/Body at creation time rather than the message
    // holding a live reference to whatever it's about - a real inbox message is a
    // snapshot of what was true when it arrived, not a live view that should keep
    // re-rendering off a PlayerAgent that might not even exist anymore (see
    // ActionPlayer's own comment below for why that matters for save/load).
    public class InboxMessage
    {
        public string Id;
        public InboxMessageType Type;
        public string Title;
        public string Body;
        public int MatchdayReceived;
        public bool IsRead;

        // Only set for a message with a real pending action attached (currently just an
        // accepted bid awaiting Sign/Walk Away) - null for purely informational messages
        // (scouting reports, declines). Deliberately NOT persisted through save/load: an
        // AI club's squad regenerates fresh every session (see ManagerSaveData's own
        // "no AI-vs-AI transfer activity exists for a saved roster to matter to" note),
        // so a PlayerAgent reference from a prior session can't be meaningfully restored
        // regardless of whether the target was an AI-squad player or a scouted prospect.
        // ManagerTransferNegotiation refunds the escrow for any still-pending bid at
        // save time instead (see ManagerSaveData.PendingBidRefundOnLoad) rather than
        // leaving a dangling, unactionable message behind.
        public PlayerAgent ActionPlayer;
        public bool HasPendingAction => ActionPlayer != null;

        // Collapsed-banner UI state (session 13 - Thomas: banner shows just the
        // headline, expand to reveal the body, ahead of the longer Youth scouting-
        // report text about to start landing here). Pure runtime UI state, deliberately
        // not persisted - collapsed-by-default on every fresh Inbox visit is the right
        // starting point regardless of what was expanded last time.
        public bool IsExpanded;
    }

    public class ManagerInbox
    {
        private readonly List<InboxMessage> messages = new();

        public IReadOnlyList<InboxMessage> Messages => messages;

        // Session 16 - a brand new career starting mid-session (OnConfirmTeamClicked)
        // never reset this, so a second career in the same Play Mode/app session opened
        // with the previous career's entire Inbox still attached (real bug Thomas hit
        // live). RestoreFromSave already does this same messages.Clear() for the load
        // path; this is the equivalent for starting fresh instead of restoring.
        public void Clear()
        {
            messages.Clear();
        }

        public int UnreadCount
        {
            get
            {
                int count = 0;
                foreach (InboxMessage m in messages)
                {
                    if (!m.IsRead) count++;
                }
                return count;
            }
        }

        public InboxMessage Add(InboxMessageType type, string title, string body, int matchdayReceived, PlayerAgent actionPlayer = null)
        {
            InboxMessage message = new InboxMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = type,
                Title = title,
                Body = body,
                MatchdayReceived = matchdayReceived,
                IsRead = false,
                ActionPlayer = actionPlayer
            };

            messages.Add(message);
            return message;
        }

        public void MarkRead(InboxMessage message)
        {
            if (message != null) message.IsRead = true;
        }

        public void MarkAllReadAndCollapse()
        {
            foreach (InboxMessage message in messages)
            {
                message.IsRead = true;
                message.IsExpanded = false;
            }
        }

        // Called once Sign/Walk Away has actually been actioned - clears the pending-
        // action flag so the message becomes a plain historical record from then on,
        // safe to persist through save/load like any other resolved message.
        public void ResolveAction(InboxMessage message)
        {
            if (message != null) message.ActionPlayer = null;
        }

        // Messages still awaiting a Sign/Walk Away action are excluded - see
        // ActionPlayer's own comment for why they can't be meaningfully restored.
        public List<InboxMessageSaveData> BuildSaveList()
        {
            List<InboxMessageSaveData> result = new List<InboxMessageSaveData>();

            foreach (InboxMessage m in messages)
            {
                if (m.HasPendingAction) continue;

                result.Add(new InboxMessageSaveData
                {
                    Type = m.Type,
                    Title = m.Title,
                    Body = m.Body,
                    MatchdayReceived = m.MatchdayReceived,
                    IsRead = m.IsRead
                });
            }

            return result;
        }

        public void RestoreFromSave(List<InboxMessageSaveData> saved)
        {
            messages.Clear();

            if (saved == null) return;

            foreach (InboxMessageSaveData dto in saved)
            {
                messages.Add(new InboxMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = dto.Type,
                    Title = dto.Title,
                    Body = dto.Body,
                    MatchdayReceived = dto.MatchdayReceived,
                    IsRead = dto.IsRead,
                    ActionPlayer = null
                });
            }
        }
    }
}
