using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 需要「處理一段時間」的機台共用基底（縫紉機、果汁機）。
    /// 進度條是本地視覺，實際狀態靠 [Networked] 同步。
    /// </summary>
    public abstract class MachineBase : NetworkInteractable
    {
        [Header("Machine")]
        [SerializeField] protected Transform _outputAnchor;
        [SerializeField] protected Transform _progressBar;
        [SerializeField] protected Renderer _statusLight;

        [Networked] public NetworkBool Processing { get; set; }
        [Networked] public TickTimer ProcessTimer { get; set; }
        [Networked] public float ProcessDuration { get; set; }

        private MaterialPropertyBlock _mpb;

        public Transform OutputAnchor => _outputAnchor != null ? _outputAnchor : transform;

        public float Progress01
        {
            get
            {
                if (!Processing || ProcessDuration <= 0f) return 0f;
                float remaining = ProcessTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(1f - remaining / ProcessDuration);
            }
        }

        protected void BeginProcess(float seconds)
        {
            if (!HasStateAuthority) return;
            Processing = true;
            ProcessDuration = seconds;
            ProcessTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            GameAudio.PlayAt(SfxId.MachineStart, transform.position);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !Processing) return;
            if (!ProcessTimer.Expired(Runner)) return;

            Processing = false;
            ProcessTimer = default;
            OnProcessComplete();
            GameAudio.PlayAt(SfxId.MachineDone, transform.position);
        }

        protected abstract void OnProcessComplete();

        public override void Render()
        {
            if (_progressBar != null)
            {
                bool show = Processing;
                if (_progressBar.gameObject.activeSelf != show) _progressBar.gameObject.SetActive(show);
                if (show)
                {
                    var s = _progressBar.localScale;
                    _progressBar.localScale = new Vector3(Mathf.Max(0.02f, Progress01), s.y, s.z);
                }
            }

            if (_statusLight != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                var c = StatusColor();
                _statusLight.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _statusLight.SetPropertyBlock(_mpb);
            }
        }

        protected virtual Color StatusColor()
            => Processing ? new Color(0.95f, 0.75f, 0.15f) : new Color(0.25f, 0.85f, 0.35f);
    }
}
