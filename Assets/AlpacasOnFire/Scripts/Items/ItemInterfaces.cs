using AlpacasOnFire.Interaction;

namespace AlpacasOnFire.Items
{
    /// <summary>
    /// 「手上的東西」對「地上的另一個物品」能做什麼（雙重分派）。
    /// 例如：手上是箱子 -> 對地上的衣服 = 裝箱；手上是噴槍 -> 對地上的染劑罐 = 填裝。
    /// Phase 2 要加新的組合，只要讓新工具實作這個介面，不用改 CarriableItem。
    /// </summary>
    public interface IItemUser
    {
        /// <param name="execute">false = 只問「行不行」並給提示字；true = 真的執行（StateAuthority）。</param>
        bool TryUseOnItem(CarriableItem target, in InteractionContext ctx, bool execute, out string prompt);
    }

    /// <summary>
    /// 「手上的東西」對「可穿衣對象（隊友／人偶）」能做什麼。
    /// 例如：手上是剃毛器 -> 對隊友 = 剃毛；手上是衣服 -> 穿上；手上是飾品 -> 裝飾品。
    /// </summary>
    public interface IGarmentHostUser
    {
        bool TryUseOnHost(IGarmentHost host, in InteractionContext ctx, bool execute, out string prompt);
    }

    /// <summary>
    /// 能接住「被丟過來的物品」的東西（機台、交貨窗口……）。
    ///
    /// 這是丟接系統與產線的橋樑：物品在滯空時撞到有實作這個介面的東西，
    /// 而且對方收得下，就直接進去，不用有人站在旁邊按 Space。
    /// 跟 IInteractable 分開是因為這條路徑**沒有玩家**——
    /// 沒有 InteractionContext、沒有手上的東西可以消耗。
    /// </summary>
    public interface IThrownItemReceiver
    {
        /// <summary>收不收得下這個飛過來的物品（唯讀，任何端都可能呼叫）。</summary>
        bool CanAcceptThrown(CarriableItem item);

        /// <summary>
        /// 收下它。只在 StateAuthority 呼叫。
        /// 回傳 true 代表已經收下，呼叫端會把物品 Despawn 掉。
        /// </summary>
        bool AcceptThrown(CarriableItem item);
    }

    /// <summary>持續按住 E 使用的工具（目前只有噴槍）。</summary>
    public interface IHoldTool
    {
        /// <summary>每個網路 tick 呼叫一次，只在 StateAuthority 執行。</summary>
        void ToolTick(in InteractionContext ctx, bool held, float deltaTime);
    }
}
