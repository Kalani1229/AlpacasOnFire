using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Level
{
    /// <summary>
    /// 掛在場景中「由關卡編輯器放出來」的物件上，記住它對應到哪一筆 LevelElementRecord。
    /// 這樣你在 Scene 視窗直接拖動物件之後，可以用「從場景回寫」把座標同步回關卡資料。
    /// </summary>
    public class LevelElementTag : MonoBehaviour
    {
        public string recordId;
        public LevelElementType type;
        public string variant = "";
        public int intParam;
    }
}
