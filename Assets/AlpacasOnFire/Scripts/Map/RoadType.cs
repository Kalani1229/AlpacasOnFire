namespace AlpacasOnFire.Map
{
    /// <summary>
    /// 道路的等級。兩種路都佔一格，差別只在畫多寬、用哪一組 prefab。
    /// **道路寬窄是純視覺**：碰撞與可行走範圍完全一樣。一律往後加，不重排。
    /// </summary>
    public enum RoadType : byte
    {
        None     = 0,
        Arterial = 1,   // 幹道：貫穿全圖的直線，車道寬
        Alley    = 2,   // 支道：其餘的迷宮走廊，車道窄
    }
}
