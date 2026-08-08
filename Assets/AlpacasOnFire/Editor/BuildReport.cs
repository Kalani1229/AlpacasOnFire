using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 建置過程的逐步紀錄，同時寫到 Console 與專案根目錄的
    /// AlpacasOnFire_BuildReport.txt，方便把整段過程貼給別人看。
    /// </summary>
    public static class BuildReport
    {
        private static readonly StringBuilder Sb = new();

        public static string FilePath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "AlpacasOnFire_BuildReport.txt");

        public static void Begin(string title)
        {
            Sb.Clear();
            Sb.AppendLine($"=== {title} ===");
            Sb.AppendLine($"時間：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Sb.AppendLine($"Unity：{Application.unityVersion}");
            Sb.AppendLine();
        }

        public static void Line(string message)
        {
            Sb.AppendLine(message);
        }

        public static void Info(string message)
        {
            Sb.AppendLine(message);
            Debug.Log("[羊駝很忙] " + message);
        }

        public static void Warn(string message)
        {
            Sb.AppendLine("[警告] " + message);
            Debug.LogWarning("[羊駝很忙] " + message);
        }

        public static void Error(string message)
        {
            Sb.AppendLine("[錯誤] " + message);
            Debug.LogError("[羊駝很忙] " + message);
        }

        public static void Exception(string label, Exception e)
        {
            Sb.AppendLine($"[例外] {label}: {e.GetType().Name}: {e.Message}");
            Sb.AppendLine(e.StackTrace);
            Debug.LogError($"[羊駝很忙] {label}: {e.Message}");
            Debug.LogException(e);
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, Sb.ToString());
                Debug.Log($"[羊駝很忙] 建置報告已寫到：{FilePath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[羊駝很忙] 寫入建置報告失敗：{e.Message}");
            }
        }
    }
}
