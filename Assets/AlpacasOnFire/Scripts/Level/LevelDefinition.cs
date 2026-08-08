using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Level
{
    /// <summary>
    /// 一個關卡的完整定義。可序列化成 JSON，也可以當 ScriptableObject 資產存在專案裡。
    /// Phase 3 的多關卡系統會直接吃這份格式。
    /// </summary>
    [CreateAssetMenu(fileName = "Level_New", menuName = "羊駝很忙/Level Definition")]
    public class LevelDefinition : ScriptableObject
    {
        [Header("基本資料")]
        public string levelName = "新關卡";
        public string sceneName = "";
        [Min(10f)] public float durationSeconds = GameTuning.LevelDurationSeconds;

        [Header("星級門檻（通關分數）")]
        public int star1 = GameTuning.Star1Threshold;
        public int star2 = GameTuning.Star2Threshold;
        public int star3 = GameTuning.Star3Threshold;

        [Header("訂單內容池")]
        public PatternType[] orderPatterns = { PatternType.TShirt };
        public DyeColorType[] orderColors = { DyeColorType.White, DyeColorType.Red };
        public AccessoryType[] orderAccessories = { AccessoryType.None };
        [Range(0f, 1f)] public float accessoryChance = 0f;

        [Header("放置的元件")]
        public List<LevelElementRecord> elements = new();

        // ---------------- 存檔 / 讀檔 ----------------

        [Serializable]
        private class Payload
        {
            public int version = 1;
            public string levelName;
            public string sceneName;
            public float durationSeconds;
            public int star1, star2, star3;
            public PatternType[] orderPatterns;
            public DyeColorType[] orderColors;
            public AccessoryType[] orderAccessories;
            public float accessoryChance;
            public List<LevelElementRecord> elements;
        }

        public string ToJson(bool pretty = true)
        {
            var p = new Payload
            {
                levelName = levelName,
                sceneName = sceneName,
                durationSeconds = durationSeconds,
                star1 = star1, star2 = star2, star3 = star3,
                orderPatterns = orderPatterns,
                orderColors = orderColors,
                orderAccessories = orderAccessories,
                accessoryChance = accessoryChance,
                elements = elements,
            };
            return JsonUtility.ToJson(p, pretty);
        }

        public void FromJson(string json)
        {
            var p = JsonUtility.FromJson<Payload>(json);
            if (p == null) throw new ArgumentException("關卡 JSON 格式不正確。");

            levelName = p.levelName;
            sceneName = p.sceneName;
            durationSeconds = p.durationSeconds;
            star1 = p.star1; star2 = p.star2; star3 = p.star3;
            orderPatterns = p.orderPatterns ?? new[] { PatternType.TShirt };
            orderColors = p.orderColors ?? new[] { DyeColorType.White };
            orderAccessories = p.orderAccessories ?? new[] { AccessoryType.None };
            accessoryChance = p.accessoryChance;
            elements = p.elements ?? new List<LevelElementRecord>();
        }

        public LevelElementRecord Add(LevelElementType type, Vector3 position, Vector3 euler = default)
        {
            var rec = new LevelElementRecord { type = type, position = position, rotationEuler = euler };
            elements.Add(rec);
            return rec;
        }

        public int CountOf(LevelElementType type)
        {
            int n = 0;
            foreach (var e in elements) if (e.type == type) n++;
            return n;
        }
    }
}
