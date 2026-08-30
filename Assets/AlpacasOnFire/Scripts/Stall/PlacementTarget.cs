using AlpacasOnFire.Interaction;
using AlpacasOnFire.Player;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 放置預覽期間的「Space 捕捉器」。
    ///
    /// 這是本批唯一比較繞的一段，說明一下為什麼要這樣做：
    /// 規格要求「佈置模式的 Space = 放下機台」，但 Space 是由既有的 PlayerInteractor
    /// 分派給準心前方的 IInteractable 的，而我不能修改 PlayerInteractor / PlayerController。
    ///
    /// 解法是順著既有架構走：在放置預覽期間，於玩家準心正前方放一個看不見的
    /// trigger 碰撞體，讓它以「最高優先權」的 IInteractable 身分參加候選，
    /// 於是 PlayerInteractor 一定會選中它 —— 等於在不改任何既有程式碼的前提下
    /// 把 Space 導向放置流程。取消預覽之後它就被銷毀，Space 立刻恢復原本的行為。
    ///
    /// 它**不是網路物件**：位置完全由玩家的 [Networked] 位置與朝向推導，
    /// 每個用戶端各自算出來的結果一致，狀態權威上的那一份就是真正會被呼叫 Interact 的。
    /// 真正的放置座標在 Interact 裡用 ctx.Origin / ctx.Direction 重新算，不依賴這顆碰撞體的位置。
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class PlacementTarget : MonoBehaviour, IInteractable
    {
        private const float ForwardOffset = 1.0f;
        private const float CaptureRadius = 1.6f;

        private PlayerController _player;
        private PlayerStallAgent _agent;

        public static PlacementTarget Create(PlayerController player, PlayerStallAgent agent)
        {
            var go = new GameObject($"[PlacementTarget_{player.name}]");
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = CaptureRadius;

            var t = go.GetComponent<PlacementTarget>();
            if (t == null) t = go.AddComponent<PlacementTarget>();
            t._player = player;
            t._agent = agent;
            t.Follow();
            return t;
        }

        private void Update() => Follow();
        private void FixedUpdate() => Follow();

        /// <summary>貼在玩家準心前方。差個一兩幀不影響 —— 捕捉半徑遠大於一幀的位移。</summary>
        private void Follow()
        {
            if (_player == null || _player.Object == null)
            {
                Destroy(gameObject);
                return;
            }
            transform.position = _player.HeadAnchor.position + _player.AimDirection * ForwardOffset;
        }

        // ---------------- IInteractable ----------------

        public Transform InteractionAnchor => transform;

        /// <summary>放置預覽期間必須壓過所有東西，包含手提箱（3）與機台（1~2）。</summary>
        public int InteractionPriority => 99;

        public bool CanInteract(in InteractionContext ctx)
        {
            if (_agent == null || _player == null) return false;
            if (ctx.Player != _player) return false;          // 只回應自己的主人
            return _agent.HasPending;
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;
            return _agent.BuildPrompt(in ctx);
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return;
            _agent.ConfirmPlacement(in ctx);
        }
    }
}
