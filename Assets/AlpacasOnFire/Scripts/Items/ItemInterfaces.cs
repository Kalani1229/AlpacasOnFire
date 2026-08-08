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

    /// <summary>持續按住 E 使用的工具（目前只有噴槍）。</summary>
    public interface IHoldTool
    {
        /// <summary>每個網路 tick 呼叫一次，只在 StateAuthority 執行。</summary>
        void ToolTick(in InteractionContext ctx, bool held, float deltaTime);
    }
}
