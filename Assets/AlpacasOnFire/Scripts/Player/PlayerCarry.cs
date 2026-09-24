using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>玩家手上的東西：拾取、丟出、接住、消耗。</summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [Networked] public NetworkId HeldId { get; set; }

        private PlayerController _player;

        public bool HasItem => HeldId.IsValid;

        public CarriableItem Held
        {
            get
            {
                if (!HeldId.IsValid || Runner == null) return null;
                return Runner.TryFindObject(HeldId, out var obj) ? obj.GetComponent<CarriableItem>() : null;
            }
        }

        public override void Spawned()
        {
            _player = GetComponent<PlayerController>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            TickAutoCatch();
            ReconcileHeld();
        }

        /// <summary>
        /// 「手上拿著什麼」是**兩邊各存一份**的狀態：
        ///   PlayerCarry.HeldId    玩家 -> 物品
        ///   CarriableItem.HolderId 物品 -> 玩家
        ///
        /// 兩邊都是 [Networked]，而且是分開寫的，所以有機會對不起來。
        /// **只要往「物品認為自己被拿著、玩家卻不這麼認為」的方向歪，症狀就固定是那個 bug：**
        /// 物品貼在手上（CarriableItem.LateUpdate 只看 HolderId），
        /// 但是用不了（所有玩法只看 Carry.Held，也就是 HeldId），
        /// 而其他操作完全正常 —— 因為除了這件物品以外什麼都沒壞。
        ///
        /// 與其一個一個堵住會讓它們分岔的路徑，不如每個 tick 對一次帳。
        /// 之後再有人寫出新的分岔，也會在下一個 tick 自己被修好。
        /// </summary>
        private void ReconcileHeld()
        {
            if (!HeldId.IsValid) return;

            // 1. 指到一個已經不存在的物件（被別人 Despawn 了）
            if (Runner == null || !Runner.TryFindObject(HeldId, out var obj) || obj == null)
            {
                Debug.LogWarning("[攜帶] 手上的物件已經不存在了，清掉持有狀態。");
                HeldId = default;
                return;
            }

            var item = obj.GetComponent<CarriableItem>();
            if (item == null)
            {
                Debug.LogWarning("[攜帶] 手上的物件不是 CarriableItem，清掉持有狀態。");
                HeldId = default;
                return;
            }

            // 2. 兩邊對不起來：物品身上寫的持有者不是我
            //    （或它根本不覺得被拿著）-> 以玩家這邊為準，把物品拉回來
            if (item.HolderId != Object.Id)
            {
                Debug.LogWarning($"[攜帶] {item.DisplayName} 的持有者對不起來（物品說是 {item.HolderId}、" +
                                 $"玩家說是我 {Object.Id}），重新綁定。");
                item.AttachTo(_player);
            }
        }

        // ---------------- 拾取 / 放下 ----------------

        /// <summary>
        /// 接手一個**剛生出來、而且持有者已經在生成回呼裡寫好**的物品。
        ///
        /// 跟 TryPickup 分開，是因為 TryPickup 會擋掉 `!Object.IsValid` 的物件 ——
        /// 那道檢查是為了「不要撿已經被 Despawn 的東西」，但 Fusion 剛生出來
        /// 還沒 Spawned() 的物件也會踩到它。連線時那個空窗期很常見，
        /// 結果就是東西生出來了卻沒進手裡。見 ItemFactory.SpawnIntoHands。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void AdoptSpawned(CarriableItem item)
        {
            if (!HasStateAuthority || item == null || item.Object == null) return;

            HeldId = item.Object.Id;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
        }

        public bool TryPickup(CarriableItem item)
        {
            if (!HasStateAuthority || item == null) return false;
            // 已經被 Despawn 的物件不能撿 —— 撿了會留下一個指不到東西的 HeldId
            if (item.Object == null || !item.Object.IsValid) return false;
            if (HasItem || item.IsHeld) return false;

            item.AttachTo(_player);
            HeldId = item.Object.Id;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
            return true;
        }

        public void Drop()
        {
            if (!HasStateAuthority) return;
            var item = Held;
            HeldId = default;
            if (item == null) return;

            item.DetachToGround(_player.HandAnchor.position + _player.transform.forward * 0.4f);
            GameAudio.PlayAt(SfxId.Drop, transform.position);
        }

        /// <summary>
        /// 騰出手來拿新東西：手上有東西就先放掉，讓接下來的拿取一定成功。
        ///
        /// 為什麼不是「手上有東西就不給拿」：那會逼玩家為了拿一份毛先找地方放東西，
        /// 在限時的攤位上是純粹的摩擦。主動接住（TryManualCatch）早就是這個手感了 ——
        /// 接到了就把原本手上的東西放到腳邊，這裡跟它一致。
        ///
        /// 一律放到腳邊、不銷毀 —— 玩家看得到、撿得回來。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void MakeRoomForPickup()
        {
            if (!HasStateAuthority || !HasItem) return;
            Drop();
        }

        /// <summary>手上的東西被用掉了（縫紉機吃掉羊毛、衣服穿到人偶上……）。</summary>
        public void ConsumeHeld()
        {
            if (!HasStateAuthority) return;
            var item = Held;
            HeldId = default;
            if (item != null && item.Object != null)
                Runner.Despawn(item.Object);
        }

        /// <summary>
        /// 把手上的東西交出去但不銷毀（例如手提箱展開成攤位）。
        ///
        /// **一定要連物品身上的 HolderId 一起清掉。** 只清 HeldId 的話，
        /// 物品會繼續貼在手上（LateUpdate 只看 HolderId）卻再也用不了、放不下 ——
        /// 那正是「東西卡在手上」那個 bug 的其中一條來源。
        /// </summary>
        public CarriableItem ReleaseHeld()
        {
            if (!HasStateAuthority) return null;

            var item = Held;
            HeldId = default;

            if (item != null) item.ReleaseHolder();
            return item;
        }

        // 隨身剃毛器（E）已經移除。剃毛不再是「拿著工具去用」，
        // 而是對著羊或隊友按左鍵就直接剃 —— 剃毛器只在動作的那一瞬間伸出來。
        // 見 PlayerController.TriggerShearVisual() 與 WoolNpc.Interact()。

        // ---------------- Q：點按放下、長按蓄力丟出 ----------------

        /// <summary>Q 已經按住多久。走 [Networked] 才能讓所有人看到蓄力動作。</summary>
        [Networked] public float ThrowCharge { get; set; }

        /// <summary>Q 現在是不是按著的。</summary>
        [Networked] public NetworkBool ThrowCharging { get; set; }

        /// <summary>蓄力進度 0~1。手上沒東西時是 0。道具視覺（卡車舉高）會讀它。</summary>
        public float ThrowCharge01
        {
            get
            {
                var item = Held;
                if (item == null || !ThrowCharging) return 0f;
                return Mathf.Clamp01(ThrowCharge / Mathf.Max(0.01f, item.ThrowChargeSeconds));
            }
        }

        /// <summary>Q 按下的那一刻。只在 StateAuthority 呼叫。</summary>
        public void BeginThrowCharge()
        {
            if (!HasStateAuthority) return;
            ThrowCharge = 0f;
            ThrowCharging = true;
        }

        /// <summary>Q 按著的每一個 tick。只在 StateAuthority 呼叫。</summary>
        public void TickThrowCharge(float deltaTime)
        {
            if (!HasStateAuthority || !ThrowCharging) return;

            // 手上的東西中途不見了（被機台吃掉之類）就取消蓄力
            if (!HasItem) { ThrowCharging = false; ThrowCharge = 0f; return; }

            ThrowCharge += deltaTime;
        }

        /// <summary>
        /// Q 放開的那一刻。**這裡決定是放下還是丟出。**
        ///
        /// 點按（不到 0.12 秒）-> 放下，掉在腳邊。
        /// 按住再放開 -> 丟出，初速按蓄力比例插值，所以蓄一半就飛一半遠。
        ///
        /// 不要求蓄滿才能丟：慌張的時候還是丟得出去，只是丟不遠。
        /// 蓄力是射程的旋鈕，不是能不能用的開關。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void ReleaseThrowCharge(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            float held = ThrowCharge;
            bool wasCharging = ThrowCharging;
            ThrowCharging = false;
            ThrowCharge = 0f;

            if (!wasCharging || !HasItem) return;

            if (held < GameTuning.ThrowTapSeconds) { Drop(); return; }

            var item = Held;
            float power = Mathf.Clamp01(held / Mathf.Max(0.01f, item.ThrowChargeSeconds));
            float speed = Mathf.Lerp(GameTuning.ThrowSpeedMin, GameTuning.ThrowSpeed, power);

            HeldId = default;
            item.LaunchFrom(_player, ctx.Direction, speed);
            GameAudio.PlayAt(SfxId.Throw, transform.position);
        }

        // ---------------- 接住 ----------------

        /// <summary>找一個接得到的滯空物品，最近的優先。</summary>
        public CarriableItem FindCatchable(float radius, bool requireFacing)
        {
            CarriableItem best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < CarriableItem.All.Count; i++)
            {
                var item = CarriableItem.All[i];
                if (item == null) continue;
                if (!item.CanBeCaughtBy(_player, radius, requireFacing, out float d)) continue;
                if (d < bestDist) { bestDist = d; best = item; }
            }
            return best;
        }

        /// <summary>
        /// 自動接住：空手、大致面向、在小範圍內就直接拿到，什麼都不用按。
        /// 每個 tick 在狀態權威端跑。
        /// </summary>
        private void TickAutoCatch()
        {
            if (HasItem) return;

            var item = FindCatchable(GameTuning.CatchAutoRadius, requireFacing: true);
            if (item == null) return;

            Catch(item);
        }

        /// <summary>
        /// 主動接住（互動鍵）：範圍比自動大一點、也不要求面向。
        ///
        /// **接到了才會把原本手上的東西放到腳邊**；沒接到就什麼都不做，
        /// 手上的東西不會白白掉出去。回傳有沒有接到，讓呼叫端決定要不要改跑情境互動。
        /// </summary>
        public bool TryManualCatch()
        {
            if (!HasStateAuthority) return false;

            var item = FindCatchable(GameTuning.CatchManualRadius, requireFacing: false);
            if (item == null) return false;

            if (HasItem) Drop();   // 接到了才拋棄手上的東西
            Catch(item);
            return true;
        }

        private void Catch(CarriableItem item)
        {
            item.AttachTo(_player);
            HeldId = item.Object.Id;
            GameAudio.PlayAt(SfxId.Catch, transform.position);
        }

        // ---------------- E：持續使用工具 ----------------

        public void TickTool(in InteractionContext ctx, bool held, float deltaTime)
        {
            if (!HasStateAuthority) return;
            if (Held is IHoldTool tool)
                tool.ToolTick(in ctx, held, deltaTime);
        }

        /// <summary>
        /// 按下左鍵：手上是可用的道具就出手。
        ///
        /// 回傳 true 代表「這次左鍵被道具吃掉了」——**打不到人也算吃掉**。
        /// 不然揮空的時候會掉回情境互動，順手把腳邊的羊毛撿起來、大蔥還被放下。
        /// 手上有武器就是揮武器。
        ///
        /// 卡車回 false（它不是武器，是要用 Q 丟的重物），所以拿著卡車
        /// 仍然可以按左鍵操作機台。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public bool TryPrank(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return false;
            return Held is Prank.PrankTool prank && prank.TryUse(in ctx);
        }
    }
}
