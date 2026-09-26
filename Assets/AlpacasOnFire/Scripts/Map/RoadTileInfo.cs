using UnityEngine;

namespace AlpacasOnFire.Map
{
    /// <summary>
    /// 標記「這片路面是以幾公尺見方設計的」。
    ///
    /// **只有佔位路面會掛這個。** RandomMapBuilder 生成道路時看到它，
    /// 就把這一片的水平尺寸縮放成場景上的 tileSize —— 佔位路面固定做成 4 公尺，
    /// 但場景的 tileSize 可能是 4、6、10，不縮放的話路面之間會有縫或互相重疊。
    ///
    /// 美術交的路面 prefab 沒有這個元件，原樣生成，不會被縮放。
    /// 所以這個元件不會改變任何既有 prefab 的行為。
    /// </summary>
    [DisallowMultipleComponent]
    public class RoadTileInfo : MonoBehaviour
    {
        [Tooltip("這片路面設計時的邊長（公尺）。生成時會縮放到 RandomMapBuilder.tileSize。")]
        [Min(0.01f)] public float designTileSize = 4f;
    }
}
