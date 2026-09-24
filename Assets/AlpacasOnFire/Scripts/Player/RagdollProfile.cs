using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 一根上關節的骨頭要怎麼設定。
    ///
    /// **骨頭是用「名稱」指到的，不是 Transform 參考。**
    /// `Alpaca_Player.prefab` 每次跑「1. 建置佔位資產」都會整個重建，
    /// 任何指向 prefab 內部節點的參考都會斷；名稱不會。
    ///
    /// **所有尺寸都是世界公尺**，不是骨頭的本地單位。
    /// MD_Alpaca 的 Armature 縮放是 100（Blender 匯出的慣例），再乘上 Visual 的 0.5，
    /// 每根骨頭的實際縮放是 50 倍 —— 用本地單位填的話，0.06 的半徑會變成 3 公尺。
    /// 程式會自己除掉骨頭的縮放，這裡填你在場景裡看到的大小就好。
    /// </summary>
    [System.Serializable]
    public class RagdollBone
    {
        [Tooltip("骨頭在 Armature 底下的名稱，例如 spine.002、backLeg.L.001。")]
        public string boneName;

        [Tooltip("取消勾選就整根跳過，不用把它從清單刪掉（方便試哪幾根要上）。")]
        public bool enabled = true;

        [Tooltip("質量（公斤）。軀幹要重、四肢要輕，比例比絕對值重要。")]
        public float mass = 1f;

        [Tooltip("膠囊半徑（公尺）。")]
        public float radius = 0.06f;

        [Tooltip("膠囊長度（公尺）。0 = 自動：量到下一節骨頭的距離。")]
        public float length = 0f;

        [Tooltip("關節的主軸（骨頭的 local space）。腿通常是 Vector3.right。")]
        public Vector3 jointAxis = Vector3.right;

        [Tooltip("繞主軸的擺動上下限（度）。低限一般給負值。")]
        public float limitLow = -45f;
        public float limitHigh = 45f;

        [Tooltip("另外兩軸的擺動上限（度）。四肢設小一點才不會像麵條。")]
        public float swingLimit = 25f;

        [Tooltip("這根的 spring 倍率，乘在 GameTuning.RagdollMaxSpring 上。頭尾可以軟一點。")]
        public float springScale = 1f;
    }

    /// <summary>
    /// Ragdoll 的骨架設定。
    ///
    /// **為什麼是一個獨立的資產而不是 prefab 上的欄位：**
    /// 玩家 prefab 每次建置都會被重建，填在 Inspector 上的數值下一次就沒了。
    /// 放在自己的 asset 裡，建置流程只是把它指給 `RagdollRig`，手調的數值就留得住。
    ///
    /// 建立方式：羊駝很忙 > Ragdoll > 1. 掃描羊駝骨架。
    /// </summary>
    [CreateAssetMenu(fileName = "RagdollProfile",
                     menuName = "AlpacasOnFire/Ragdoll Profile", order = 100)]
    public class RagdollProfile : ScriptableObject
    {
        /// <summary>
        /// 目前的單位版本。
        ///   1（或 0）= 舊版，尺寸是骨頭本地單位（會被放大 50 倍，是錯的）
        ///   2        = 世界公尺
        /// 掃描選單看到舊版本會整份重設，不會保留那些錯單位的數值。
        /// </summary>
        public const int CurrentUnitsVersion = 2;

        /// **預設值必須是 0，不能寫成 CurrentUnitsVersion。** Unity 讀舊資產時，
        /// 檔案裡沒有的欄位會保留欄位初始值 —— 預設寫 2 的話，舊的錯單位資產
        /// 讀進來也會是 2，遷移永遠不會觸發。新建資產時由掃描選單明確設成 2。
        [HideInInspector] public int unitsVersion;

        [Tooltip("根骨的名稱。它不上關節 —— 它是所有關節鏈的起點，也是被扶正、被栓住的對象。")]
        public string rootBoneName = "spine.001";

        [Tooltip("根骨（骨盆＋軀幹）的質量。")]
        public float rootMass = 4f;

        [Tooltip("根骨膠囊半徑（公尺）。羊駝身體很粗，這顆通常是最大的。")]
        public float rootRadius = 0.2f;

        [Tooltip("根骨膠囊長度（公尺）。0 = 自動。")]
        public float rootLength = 0f;

        // ================================================================ 手感
        //
        // **這一區全部可以在 Play 中直接改，下一次倒地就生效。**
        // ScriptableObject 在 Play 中改的值離開 Play 之後會保留（跟場景物件不一樣），
        // 所以調到滿意就直接停下來，不用抄數字。
        //
        // 預設值取自 GameTuning 的同名常數。那些常數仍然是「出廠值」，
        // 但實際執行讀的是這裡 —— 常數沒辦法邊看邊調，每改一次都要重新編譯。

        [Header("手感：時間（Play 中可直接改）")]
        [Tooltip("完全癱軟的秒數上限。實際是 min(這個, 整段 × 癱軟最多佔比)。")]
        public float limpSeconds = GameTuning.RagdollLimpSeconds;

        [Tooltip("癱軟最多佔整段倒地時間的比例。")]
        [Range(0.1f, 0.9f)] public float limpMaxShare = GameTuning.RagdollLimpMaxShare;

        [Tooltip("癱軟之後剩下的時間，掙扎佔多少（其餘是站回）。")]
        [Range(0.1f, 0.9f)] public float struggleShare = GameTuning.RagdollStruggleShare;

        [Tooltip("站起來之後，從 ragdoll 姿勢混回動畫的秒數。")]
        public float blendBackTime = GameTuning.RagdollBlendBackTime;

        [Header("手感：力道（Play 中可直接改）")]
        [Tooltip("掙扎階段的力道比例。越低越「撐不直」。")]
        [Range(0f, 1f)] public float struggleSpring = GameTuning.RagdollStruggleSpring;

        [Tooltip("關節站直時的 slerpDrive.positionSpring。越大四肢越硬、越像機器人。")]
        public float maxSpring = GameTuning.RagdollMaxSpring;

        [Tooltip("關節的 slerpDrive.maximumForce。力道上限。")]
        public float maxForce = GameTuning.RagdollMaxForce;

        [Tooltip("把骨盆扶正的彈簧。越大站得越快越挺；越小掙扎越久、越趴。")]
        public float hipsSpring = GameTuning.RagdollHipsSpring;

        [Tooltip("恢復時抵銷多少重力（跟著力道比例一起升）。1 = 站回時完全抵銷，身體的重量" +
                 "不會把骨盆往下扯，站得起來。調低 = 站起來更吃力、更會下垂。")]
        [Range(0f, 1f)] public float antiGravity = 1f;

        [Tooltip("骨盆離膠囊最遠多少公尺，超過就拉回來。")]
        public float leashRadius = GameTuning.RagdollLeashRadius;

        [Tooltip("被炸到時往上彈的速度（公尺/秒）。0 = 不彈，只跟著擊退平飛。")]
        public float kickUp = 1.2f;

        [Header("骨頭（質量、尺寸、角度上限：Play 中改，下一次倒地生效；jointAxis 要重新 Play）")]
        [Tooltip("要上關節的骨頭。順序不重要，程式會自己照階層找父骨頭。")]
        public RagdollBone[] bones = new RagdollBone[0];

        public RagdollBone Find(string boneName)
        {
            if (bones == null || string.IsNullOrEmpty(boneName)) return null;
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (b != null && b.enabled && b.boneName == boneName) return b;
            }
            return null;
        }
    }
}
