using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Interaction
{
    /// <summary>
    /// 一次互動嘗試的所有情境資料。之後 Phase 2 要加偷竊／搶奪／吐口水，
    /// 只需要在這裡補欄位，不用改動任何既有 IInteractable 實作的簽章。
    /// </summary>
    public readonly struct InteractionContext
    {
        public readonly PlayerController Player;
        public readonly CarriableItem Held;   // 玩家手上的東西，沒有則為 null
        public readonly Vector3 Origin;       // 射線起點（頭部）
        public readonly Vector3 Direction;    // 射線方向（＝角色朝向＝鏡頭朝向）
        public readonly NetworkRunner Runner;

        public InteractionContext(PlayerController player, CarriableItem held,
                                  Vector3 origin, Vector3 direction, NetworkRunner runner)
        {
            Player = player;
            Held = held;
            Origin = origin;
            Direction = direction;
            Runner = runner;
        }

        public ItemKind HeldKind => Held != null ? Held.Kind : ItemKind.None;
        public bool HasHeld => Held != null;
        public bool IsEmptyHanded => Held == null;
    }

    /// <summary>
    /// 所有可互動物件的共用介面（地面物品、機台、隊友、人偶、箱子、郵箱……）。
    ///
    /// 規則：
    ///  - CanInteract / GetPrompt 必須是唯讀的，會在任何用戶端被呼叫（用來畫 HUD 提示）。
    ///  - Interact 只會在 StateAuthority 上被呼叫，可以安全地改動 [Networked] 狀態、Spawn/Despawn。
    ///  - 要新增一種互動類型時，寫一個新的實作類別就好，不要回頭改 PlayerInteractor。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>互動判定與提示 UI 的參考點。</summary>
        Transform InteractionAnchor { get; }

        /// <summary>同樣距離內誰優先。數字大的贏（例如手上有羊毛時縫紉機優先於地面物品）。</summary>
        int InteractionPriority { get; }

        /// <summary>這個情境下能不能互動（唯讀）。</summary>
        bool CanInteract(in InteractionContext ctx);

        /// <summary>HUD 上顯示的提示文字（唯讀）。</summary>
        string GetPrompt(in InteractionContext ctx);

        /// <summary>實際執行互動。只會在 StateAuthority 上被呼叫。</summary>
        void Interact(in InteractionContext ctx);
    }

    /// <summary>
    /// 右鍵（次要互動）。跟 IInteractable 分開，因為不是每個東西都需要第二種操作。
    ///
    /// 分派規則（在 PlayerController 裡）：**手上拿著 IHoldTool 時不觸發**。
    /// 理由是右鍵本來就是「持續使用手上的工具」（噴槍、染劑刷），
    /// 拿著那些東西時右鍵永遠該是工具的動作，不能被準心前方的東西搶走。
    /// 反過來說，空手或拿著羊毛時右鍵一定打得到次要互動。
    ///
    /// 跟 IInteractable 一樣：CanSecondaryInteract / GetSecondaryPrompt 必須唯讀，
    /// SecondaryInteract 只會在 StateAuthority 上被呼叫。
    /// </summary>
    public interface ISecondaryInteractable
    {
        bool CanSecondaryInteract(in InteractionContext ctx);
        string GetSecondaryPrompt(in InteractionContext ctx);
        void SecondaryInteract(in InteractionContext ctx);
    }

    /// <summary>
    /// 可以被穿上衣服的對象：人偶與隊友都實作它。
    /// 噴槍與飾品都只認這個介面，不管對方是人偶還是玩家。
    /// </summary>
    public interface IGarmentHost
    {
        Transform GarmentAnchor { get; }
        bool HasGarment { get; }
        GarmentSpec Garment { get; }
        /// <summary>把衣服穿上。只在 StateAuthority 呼叫。</summary>
        bool TryWear(GarmentSpec spec);
        /// <summary>脫下衣服並回傳規格。只在 StateAuthority 呼叫。</summary>
        bool TryTakeOff(out GarmentSpec spec);
    }
}
