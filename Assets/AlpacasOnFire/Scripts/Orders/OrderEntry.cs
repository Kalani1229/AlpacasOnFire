using AlpacasOnFire.Core;
using Fusion;

namespace AlpacasOnFire.Orders
{
    /// <summary>訂單板上的一格。放在 NetworkArray 裡同步。</summary>
    public struct OrderEntry : INetworkStruct
    {
        public NetworkBool Active;
        public GarmentSpec Spec;
        public float Remaining;   // 剩餘秒數
        public float Duration;    // 總秒數（畫 Countdown Border 用）
        public int Id;            // 給 UI 判斷是不是同一張卡

        public float Remaining01 => Duration <= 0f ? 0f : UnityEngine.Mathf.Clamp01(Remaining / Duration);
        public int Reward => GameTuning.OrderBaseReward
                           + (Spec.Accessory != AccessoryType.None ? GameTuning.OrderAccessoryBonus : 0);
    }
}
