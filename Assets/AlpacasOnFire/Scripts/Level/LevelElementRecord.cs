using System;
using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Level
{
    /// <summary>
    /// 關卡中一個被放置的元件。**這就是存檔格式的最小單位**。
    ///
    /// Phase 3 要升級成正式的多關卡資料驅動系統時會沿用這個格式，
    /// 所以：新增欄位請往後加、不要刪除或改變既有欄位的意義，
    /// 也不要把場景物件直接寫死在 .unity 檔裡當作唯一真實來源。
    /// </summary>
    [Serializable]
    public class LevelElementRecord
    {
        public string id = Guid.NewGuid().ToString("N")[..8];
        public LevelElementType type = LevelElementType.SewingMachine;
        public Vector3 position;
        public Vector3 rotationEuler;
        public Vector3 scale = Vector3.one;

        /// <summary>
        /// 型別專屬參數，用字串保存以便向後相容：
        ///  - DyeSource*：染料顏色名稱
        ///  - AccessoryDispenser：飾品種類
        ///  - Npc：NPC 種類（Phase 2 才有意義，本階段只是佔位）
        ///  - Wall / FloorTile：不使用
        /// </summary>
        public string variant = "";

        /// <summary>備用數值參數（Phase 2/3 用，例如 NPC 巡邏路線編號）。</summary>
        public int intParam;

        public LevelElementRecord Clone() => new()
        {
            id = Guid.NewGuid().ToString("N")[..8],
            type = type,
            position = position,
            rotationEuler = rotationEuler,
            scale = scale,
            variant = variant,
            intParam = intParam,
        };
    }
}
