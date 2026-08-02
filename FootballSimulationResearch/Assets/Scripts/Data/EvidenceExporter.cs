using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Data
{
    public class EvidenceExporter
    {
        private readonly TeamRegistry teamRegistry;

        public EvidenceExporter(TeamRegistry teamRegistry)
        {
            this.teamRegistry = teamRegistry;
        }

        private string GetEvidenceOutputFolder()
        {
            string folderPath = Path.Combine(Application.persistentDataPath, "EvidenceExports");

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return folderPath;
        }

        public void ExportTextEvidence(string fileName, string content)
        {
            string folderPath = GetEvidenceOutputFolder();
            string filePath = Path.Combine(folderPath, fileName);

            File.WriteAllText(filePath, content);

            Debug.Log($"Evidence text exported to: {filePath}");
        }

        public void ExportAverageTableCsv(string fileName, List<AverageTeamResult> averageResults)
        {
            string folderPath = GetEvidenceOutputFolder();
            string filePath = Path.Combine(folderPath, fileName);

            StringBuilder csv = new StringBuilder();

            csv.AppendLine("Position,Team,AveragePoints,AveragePosition,ActualPosition,ActualPoints,PointsError");

            for (int i = 0; i < averageResults.Count; i++)
            {
                AverageTeamResult result = averageResults[i];

                string teamName = teamRegistry.GetTeamName(result.TeamId);

                csv.AppendLine(
                    $"{i + 1}," +
                    $"{EscapeCsv(teamName)}," +
                    $"{result.AveragePoints:F2}," +
                    $"{result.AveragePosition:F2}," +
                    $"{result.ActualPosition}," +
                    $"{result.ActualPoints}," +
                    $"{result.PointsError:F2}"
                );
            }

            File.WriteAllText(filePath, csv.ToString());

            Debug.Log($"Average table CSV exported to: {filePath}");
        }

        private string EscapeCsv(string value)
        {
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }
    }
}
