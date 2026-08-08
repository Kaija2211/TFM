using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sim;

namespace Manager
{
    // Runtime-generated scrollable league table grid, parallel to SquadListView. One
    // header row plus one row per club (#, Club, Pl, GD, Form, Pts), with the managed
    // club's row highlighted in accent green. Built and laid out entirely in code, same
    // convention as every other list in Manager Mode. Shows GD (goal difference) rather
    // than separate GF/GA columns, and a Form column (last-5 results) - both are a
    // Manager Mode display choice only, unconnected to LeagueTable.Entry's own
    // GoalsFor/GoalsAgainst fields or any Research Mode data (which stay untouched,
    // still used by Research Mode's evaluation output). Form's actual W/D/L history
    // comes from a caller-supplied resolver, not from LeagueTable.Entry itself - see
    // ManagerPrototypeController's recentFormByTeamId.
    public class LeagueTableView : MonoBehaviour
    {
        // Assign to the "Content" RectTransform of a standard Unity Scroll View.
        [SerializeField] private RectTransform rowContainer;
        [SerializeField] private float rowHeight = 28f;
        [SerializeField] private float headerRowHeight = 22f;
        [SerializeField] private int fontSize = 13;

        private static readonly float[] ColumnFractions = { 0.09f, 0.35f, 0.14f, 0.14f, 0.14f, 0.14f };
        private static readonly string[] ColumnHeaders = { "#", "CLUB", "PL", "GD", "FORM", "PTS" };
        private static readonly TextAlignmentOptions[] ColumnAlignments =
        {
            TextAlignmentOptions.MidlineRight, TextAlignmentOptions.MidlineLeft, TextAlignmentOptions.MidlineRight,
            TextAlignmentOptions.MidlineRight, TextAlignmentOptions.MidlineLeft, TextAlignmentOptions.MidlineRight
        };

        private readonly List<GameObject> spawnedRows = new();

        public void Populate(IReadOnlyList<LeagueTable.Entry> sortedEntries, Func<int, string> teamNameResolver, int highlightedTeamId, Func<int, string> formResolver)
        {
            Clear();

            if (rowContainer == null || sortedEntries == null)
            {
                return;
            }

            EnsureLayoutComponents();

            spawnedRows.Add(BuildRow(ColumnHeaders, headerRowHeight, ManagerUITheme.PanelDark, ManagerUITheme.TextMuted, bold: true));

            for (int i = 0; i < sortedEntries.Count; i++)
            {
                LeagueTable.Entry entry = sortedEntries[i];
                bool highlighted = entry.TeamId == highlightedTeamId;

                int goalDifference = entry.GoalsFor - entry.GoalsAgainst;

                string[] cells =
                {
                    (i + 1).ToString(),
                    teamNameResolver(entry.TeamId),
                    entry.Played.ToString(),
                    goalDifference > 0 ? $"+{goalDifference}" : goalDifference.ToString(),
                    formResolver != null ? formResolver(entry.TeamId) : string.Empty,
                    entry.Points.ToString()
                };

                Color background = highlighted ? ManagerUITheme.CardNeutral : ManagerUITheme.CardNeutralAlt;
                Color textColor = highlighted ? ManagerUITheme.Accent : ManagerUITheme.TextBody;

                spawnedRows.Add(BuildRow(cells, rowHeight, background, textColor, bold: highlighted));
            }
        }

        public void Clear()
        {
            foreach (GameObject row in spawnedRows)
            {
                if (row != null)
                {
                    Destroy(row);
                }
            }

            spawnedRows.Clear();
        }

        private void EnsureLayoutComponents()
        {
            if (!rowContainer.TryGetComponent(out VerticalLayoutGroup layout))
            {
                layout = rowContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.spacing = 1f;

            if (!rowContainer.TryGetComponent(out ContentSizeFitter fitter))
            {
                fitter = rowContainer.gameObject.AddComponent<ContentSizeFitter>();
            }

            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private GameObject BuildRow(IReadOnlyList<string> cells, float height, Color background, Color textColor, bool bold)
        {
            GameObject row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            row.transform.SetParent(rowContainer, false);

            LayoutElement layoutElement = row.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = height;
            layoutElement.flexibleWidth = 1f;

            row.GetComponent<Image>().color = background;

            float x = 0f;

            for (int i = 0; i < cells.Count && i < ColumnFractions.Length; i++)
            {
                GameObject cell = new GameObject($"Cell_{i}", typeof(RectTransform));
                cell.transform.SetParent(row.transform, false);

                RectTransform cellRect = cell.GetComponent<RectTransform>();
                cellRect.anchorMin = new Vector2(x, 0f);
                cellRect.anchorMax = new Vector2(x + ColumnFractions[i], 1f);
                cellRect.offsetMin = new Vector2(4f, 0f);
                cellRect.offsetMax = new Vector2(-4f, 0f);

                TextAlignmentOptions alignment = i < ColumnAlignments.Length ? ColumnAlignments[i] : TextAlignmentOptions.MidlineRight;

                ManagerUITheme.BuildLabel(cell.transform, cells[i], fontSize, textColor, alignment, bold ? FontStyles.Bold : FontStyles.Normal);

                x += ColumnFractions[i];
            }

            return row;
        }
    }
}
