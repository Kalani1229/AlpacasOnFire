using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.UI;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 縫紉機：吃 3 份羊毛，選版型後開始處理，時間到產出成品（白色，未染色）。
    /// </summary>
    public class SewingMachine : MachineBase
    {
        [Networked] public int WoolCount { get; set; }
        [Networked] public int PatternRaw { get; set; }

        [SerializeField] private Renderer[] _woolSlots;
        private MaterialPropertyBlock _mpb;

        public PatternType SelectedPattern => (PatternType)PatternRaw;
        public bool ReadyToSew => !Processing && WoolCount >= GameTuning.SewingWoolRequired;

        public override int InteractionPriority => 1;

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (Processing) return false;
            if (ctx.HeldKind == ItemKind.Wool && WoolCount < GameTuning.SewingWoolRequired) return true;
            return ReadyToSew && ctx.IsEmptyHanded;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (Processing) return $"縫製中… {Progress01 * 100f:F0}%";
            if (ctx.HeldKind == ItemKind.Wool && WoolCount < GameTuning.SewingWoolRequired)
                return $"[Space] 放入羊毛（{WoolCount}/{GameTuning.SewingWoolRequired}）";
            if (ReadyToSew && ctx.IsEmptyHanded) return "[Space] 選擇版型";
            return $"羊毛 {WoolCount}/{GameTuning.SewingWoolRequired}";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Processing) return;

            if (ctx.HeldKind == ItemKind.Wool && WoolCount < GameTuning.SewingWoolRequired)
            {
                WoolCount++;
                ctx.Player.Carry.ConsumeHeld();
                GameAudio.PlayAt(SfxId.Pickup, transform.position);
                return;
            }

            if (!ReadyToSew || !ctx.IsEmptyHanded) return;

            // 離線（GameMode.Single）或主機自己操作時，狀態權威就是本機，直接開 UI；
            // 遠端用戶端才需要走 RPC。
            if (ctx.Player.Object.HasInputAuthority)
                PatternSelectPanel.Open(this);
            else
                RPC_OpenPatternSelect(ctx.Player.Object.InputAuthority);
        }

        /// <summary>叫指定玩家的用戶端打開選版型介面。</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OpenPatternSelect([RpcTarget] PlayerRef player)
        {
            PatternSelectPanel.Open(this);
        }

        /// <summary>用戶端選好版型後回報給狀態權威。</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_SelectPattern(int pattern)
        {
            if (Processing || WoolCount < GameTuning.SewingWoolRequired) return;
            if (pattern == (int)PatternType.None) return;

            PatternRaw = pattern;
            WoolCount -= GameTuning.SewingWoolRequired;
            BeginProcess(GameTuning.SewingProcessSeconds);
        }

        protected override void OnProcessComplete()
        {
            var spec = GarmentSpec.Create(SelectedPattern, DyeColorType.White, AccessoryType.None);
            ItemFactory.Spawn(Runner, ItemKind.Garment, spec, OutputAnchor.position);
        }

        public override void Render()
        {
            base.Render();
            if (_woolSlots == null) return;

            _mpb ??= new MaterialPropertyBlock();
            for (int i = 0; i < _woolSlots.Length; i++)
            {
                if (_woolSlots[i] == null) continue;
                _woolSlots[i].enabled = i < WoolCount;
            }
        }
    }
}
