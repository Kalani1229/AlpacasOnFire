using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Networking
{
    /// <summary>按鍵編號。Phase 2 新增互動（偷竊／吐口水）時往後加即可。</summary>
    public enum GameButton
    {
        Interact   = 0, // Space 或 滑鼠左鍵
        ThrowCatch = 1, // Q
        UseTool    = 2, // 滑鼠右鍵（按住）—— 塗抹之類的持續動作
        DefaultTool = 3, // E —— 拿出／收起隨身剃毛器（v6）
    }

    /// <summary>
    /// 每個 tick 由本機玩家送出的輸入。
    /// Yaw/Pitch 用「絕對角度」而不是「本次位移量」——因為採共用朝向模型，
    /// 由擁有輸入權的用戶端決定看向哪裡，狀態權威直接套用，不會有累積誤差。
    /// </summary>
    public struct NetInput : INetworkInput
    {
        public NetworkButtons Buttons;
        public Vector2 Move;   // x = 左右平移, y = 前後（已經是相對角色朝向的原始輸入）
        public float Yaw;      // 角色 + 鏡頭共用的水平角度（度）
        public float Pitch;    // 只影響鏡頭與互動射線（度）

        public bool GetButton(GameButton b) => Buttons.IsSet(b);
        public void SetButton(GameButton b, bool value) => Buttons.Set((int)b, value);
    }
}
