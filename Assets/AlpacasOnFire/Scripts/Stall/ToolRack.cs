using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 工具架：剃毛器與噴槍的家。
    ///
    /// 為什麼需要它 —— 剃毛器與噴槍是手持工具，不是機台，本來沒有辦法「站在格子上」。
    /// 但網格佈局要成立，每一台裝備都必須佔一格、都要能被記進佈局、都要能用同一套方式重擺。
    /// 所以給它們一個架子：架子是 DeployableDevice（進網格、能重擺、會被記住），
    /// 架子上的工具則是一般的 CarriableItem（照舊可以撿起、丟出、接住）。
    ///
    /// 架子本身沒有生產邏輯 —— 開箱時生一支工具放上去，工具被拿走之後
    /// 空手對著架子按 Space 可以再要一支（不然工具掉進地形縫隙整攤就廢了）。
    /// </summary>
    public class ToolRack : NetworkInteractable
    {
        [Header("Tool Rack")]
        [SerializeField] private Transform _toolAnchor;
        [SerializeField] private Renderer _toolIcon;

        /// <summary>目前架上／場上這支工具的 NetworkId。無效代表工具不在了。</summary>
        [Networked] public NetworkId ToolId { get; set; }

        private MaterialPropertyBlock _mpb;
        private bool _spawnedOnce;

        public Transform ToolAnchor => _toolAnchor != null ? _toolAnchor : transform;

        private DeployableDevice _deployable;
        private DeployableDevice Deployable =>
            _deployable != null ? _deployable : _deployable = GetComponentInChildren<DeployableDevice>(true);

        /// <summary>這個架子放的是哪一種工具，由 DeployableDevice 的裝備類型決定。</summary>
        public ItemKind ToolKind
        {
            get
            {
                var dev = Deployable;
                return dev != null ? StallCatalog.RackToolKind(dev.DeviceType) : ItemKind.None;
            }
        }

        public override int InteractionPriority => 1;

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            // 開箱後第一個 tick 生出工具。放在這裡而不是 Spawned()，
            // 是因為 Spawned() 當下 DeployableDevice 的 [Networked] 裝備類型還沒套用完。
            if (_spawnedOnce) return;
            _spawnedOnce = true;
            SpawnTool();
        }

        private bool ToolAlive()
        {
            if (!ToolId.IsValid || Runner == null) return false;
            // IsValid：已經 Despawn 但還沒清掉的物件不算還在
            return Runner.TryFindObject(ToolId, out var obj) && obj != null && obj.IsValid;
        }

        private void SpawnTool()
        {
            if (!HasStateAuthority) return;

            var kind = ToolKind;
            if (kind == ItemKind.None) return;

            var tool = ItemFactory.Spawn(Runner, kind, default, ToolAnchor.position,
                                         ToolAnchor.rotation);
            if (tool != null) ToolId = tool.Object.Id;
        }

        // ---------------- IInteractable ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            if (stall != null && stall.IsArrangeMode) return false;  // 佈置模式讓 DeployableDevice 處理
            if (!ctx.IsEmptyHanded) return false;
            return !ToolAlive();
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            string name = ToolKind == ItemKind.Shears ? "剃毛器" : "噴槍";
            if (ToolAlive()) return $"{name}架";
            if (!ctx.IsEmptyHanded) return "先空出雙手";
            return $"[Space] 再拿一支{name}";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !ctx.IsEmptyHanded) return;
            if (ToolAlive()) return;

            SpawnTool();

            // 生完直接放到玩家手上，省一次彎腰
            if (ToolId.IsValid && Runner.TryFindObject(ToolId, out var obj) && obj != null && obj.IsValid)
            {
                var item = obj.GetComponent<CarriableItem>();
                if (item != null) ctx.Player.Carry.TryPickup(item);
            }
        }

        // ---------------- 表現 ----------------

        public override void Render()
        {
            if (_toolIcon == null) return;

            // 架上有沒有工具，用小圖示的顏色表示
            var c = ToolAlive()
                ? PlaceholderPalette.ShearsBlade
                : new Color(0.3f, 0.3f, 0.32f);

            _mpb ??= new MaterialPropertyBlock();
            _toolIcon.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _toolIcon.SetPropertyBlock(_mpb);
        }
    }
}
