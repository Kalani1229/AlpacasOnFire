using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 被惡搞的狀態。**玩家與 NPC 掛的是同一支** —— 狀態完全一樣，
    /// 只有「反應」不同（玩家要抽掉控制、NPC 要停止逃跑），那部分各自去讀。
    ///
    /// 三個 [Networked] 計時器就是全部的狀態：
    ///   BlindTimer      視覺干擾
    ///   KnockbackTimer  擊退（配一個方向與初速）
    ///   StaggerTimer    失控倒地
    ///
    /// 刻意**不記錄是誰打的**。惡搞的效果不該因為兇手是誰而不同，
    /// 而且不記的話，之後加第四個道具就只是「這三個欄位的另一種組合」。
    ///
    /// 全部走 [Networked]，所以所有端看到的倒地時間、推的方向都一致，
    /// 不會出現「我這邊已經爬起來了、隊友畫面上我還趴著」。
    /// </summary>
    [DisallowMultipleComponent]
    public class StaggerStatus : NetworkBehaviour
    {
        [Networked] public TickTimer BlindTimer { get; set; }
        [Networked] public TickTimer KnockbackTimer { get; set; }
        [Networked] public TickTimer StaggerTimer { get; set; }

        /// <summary>擊退的水平方向與初速。方向已經正規化。</summary>
        [Networked] public Vector3 KnockbackDir { get; set; }
        [Networked] public float KnockbackSpeed { get; set; }

        /// <summary>畫面震動的殘餘時間。純表現，但走 [Networked] 才不會只有主機看得到。</summary>
        [Networked] public TickTimer ShakeTimer { get; set; }

        public bool Blinded    => !BlindTimer.ExpiredOrNotRunning(Runner);
        public bool Staggered  => !StaggerTimer.ExpiredOrNotRunning(Runner);
        public bool KnockedBack => !KnockbackTimer.ExpiredOrNotRunning(Runner);
        public bool Shaking    => !ShakeTimer.ExpiredOrNotRunning(Runner);

        /// <summary>視覺干擾的濃度 0~1，末段會自己淡掉，不會突然恢復。</summary>
        public float Blind01
        {
            get
            {
                float left = BlindTimer.RemainingTime(Runner) ?? 0f;
                if (left <= 0f) return 0f;
                // 最後 1 秒線性淡出
                return Mathf.Clamp01(left);
            }
        }

        /// <summary>
        /// 目前這一刻的擊退速度（會隨時間衰減到 0）。
        /// 給移動端直接加在移動向量上用。
        /// </summary>
        public Vector3 CurrentKnockback
        {
            get
            {
                float left = KnockbackTimer.RemainingTime(Runner) ?? 0f;
                if (left <= 0f || KnockbackSpeed <= 0f) return Vector3.zero;

                // 線性衰減：一開始最猛，尾巴自然收掉，不會硬生生停住
                float total = Mathf.Max(0.01f, GameTuning.LeekKnockbackSeconds);
                return KnockbackDir * (KnockbackSpeed * Mathf.Clamp01(left / total));
            }
        }

        // ---------------- 施加效果（只在 StateAuthority）----------------

        public void Blind(float seconds)
        {
            if (!HasStateAuthority || seconds <= 0f) return;

            // 取比較長的那個，不要讓後面一口比較弱的口水縮短前面那口
            float left = BlindTimer.RemainingTime(Runner) ?? 0f;
            BlindTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(left, seconds));
        }

        public void Knockback(Vector3 direction, float speed, float seconds)
        {
            if (!HasStateAuthority || speed <= 0f || seconds <= 0f) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;

            KnockbackDir = direction.normalized;
            KnockbackSpeed = speed;
            KnockbackTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            Shake(GameTuning.LeekShakeSeconds);
        }

        public void Stagger(float seconds)
        {
            if (!HasStateAuthority || seconds <= 0f) return;

            float left = StaggerTimer.RemainingTime(Runner) ?? 0f;
            StaggerTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(left, seconds));
            Shake(seconds * 0.4f);
        }

        public void Shake(float seconds)
        {
            if (!HasStateAuthority || seconds <= 0f) return;
            float left = ShakeTimer.RemainingTime(Runner) ?? 0f;
            ShakeTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(left, seconds));
        }

        /// <summary>全部清掉（收攤、重生之類的場合）。</summary>
        public void ClearAll()
        {
            if (!HasStateAuthority) return;
            BlindTimer = default;
            KnockbackTimer = default;
            StaggerTimer = default;
            ShakeTimer = default;
            KnockbackSpeed = 0f;
        }
    }
}
