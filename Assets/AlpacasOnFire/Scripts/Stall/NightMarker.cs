using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 夜晚的收工柱子。**下一批會整個換成債主** —— 狀態機只認 StallManager.EndNight()，
    /// 換掉這支不用動任何其他程式。
    ///
    /// 它**不是網路物件**，做法跟 PlacementTarget 一樣：
    ///   - 每一端各自依同步的狀態生出來 —— 進入 Night 就生、離開就收
    ///   - 位置由同步的資料推導（襯布中心與朝向，或第一個廣場），每一端算出來一樣
    ///   - 互動只會在房主（狀態權威）那一份上被呼叫，房主那份再呼叫 EndNight()
    /// 這樣不用新 prefab、不用重建資產、不用 Rebuild Prefab Table。
    ///
    /// 外觀是一根會發光的柱子 + 一盞點光源：夜裡整個畫面都暗了，它一定要找得到。
    /// </summary>
    public class NightMarker : MonoBehaviour, IInteractable
    {
        private const float Height = 2.6f;
        private const float Radius = 0.28f;

        private static NightMarker _instance;

        private Light _light;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;

        /// <summary>每幀由 StallManager.Render() 呼叫：夜晚就確保存在，否則收掉。</summary>
        public static void Sync(StallManager stall)
        {
            bool want = stall != null && stall.IsNight;

            if (!want)
            {
                if (_instance != null) Destroy(_instance.gameObject);
                _instance = null;
                return;
            }

            if (_instance == null) _instance = Create();
            _instance.transform.position = PositionFor(stall);
        }

        public static void DestroyInstance()
        {
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        /// <summary>
        /// 襯布展開：放在鈴鐺的位置（襯布背緣、手提箱旁邊）—— 夜裡鈴鐺已經收掉了，那一格是空的。
        /// 沒展開：放在第一個廣場（玩家出生的地方）旁邊；沒有城市（理論上 run 模式不會發生）就放原點附近。
        /// </summary>
        private static Vector3 PositionFor(StallManager stall)
        {
            if (stall.MatDeployed)
                return StallGeometry.BellRestPosition(stall.MatCenter, stall.MatYaw);

            var map = FindAnyObjectByType<Map.RandomMapBuilder>();
            if (map != null && map.PlazaCenters.Count > 0)
            {
                var wish = map.PlazaCenters[0] + Vector3.back * 4f;
                return Map.NavUtil.SnapToNavMesh(wish, 3f, out var p) ? p : map.PlazaCenters[0];
            }
            return new Vector3(0f, 0f, -4f);
        }

        private static NightMarker Create()
        {
            var root = new GameObject("[NightMarker]");

            // 柱身：圓柱，底部貼地（圓柱的軸心在中間，所以往上抬半高）
            var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Pillar";
            pillar.transform.SetParent(root.transform, false);
            pillar.transform.localPosition = Vector3.up * (Height * 0.5f);
            pillar.transform.localScale = new Vector3(Radius * 2f, Height * 0.5f, Radius * 2f);

            var marker = root.AddComponent<NightMarker>();
            marker._renderer = pillar.GetComponent<Renderer>();
            marker._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mat = GlowMaterial();
            if (mat != null) marker._renderer.sharedMaterial = mat;

            // 光源：夜裡照亮周圍一圈，遠遠就看得到
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = Vector3.up * (Height + 0.3f);
            marker._light = lightGo.AddComponent<Light>();
            marker._light.type = LightType.Point;
            marker._light.range = 9f;
            marker._light.intensity = 3f;
            marker._light.color = new Color(1f, 0.82f, 0.45f);
            marker._light.shadows = LightShadows.None;

            return marker;
        }

        private static Material _glowMat;

        /// <summary>不受光照影響的材質，夜裡才不會跟著變暗。找不到就用預設材質（仍有點光源撐著）。</summary>
        private static Material GlowMaterial()
        {
            if (_glowMat != null) return _glowMat;
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            _glowMat = new Material(shader) { name = "M_NightMarkerGlow" };
            return _glowMat;
        }

        private void Update()
        {
            // 呼吸般地明暗，比靜止的柱子更容易被眼角捕捉到
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);
            var c = Color.Lerp(new Color(1f, 0.7f, 0.3f), new Color(1f, 0.95f, 0.7f), pulse);

            if (_renderer != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                _renderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _renderer.SetPropertyBlock(_mpb);
            }
            if (_light != null) _light.intensity = Mathf.Lerp(2.2f, 3.6f, pulse);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---------------- IInteractable ----------------

        public Transform InteractionAnchor => transform;

        /// <summary>跟開張鈴同級（3）。</summary>
        public int InteractionPriority => 3;

        private static bool LocalIsHost
        {
            get
            {
                var stall = StallManager.Instance;
                return stall != null && stall.Runner != null && stall.Runner.IsServer;
            }
        }

        public bool CanInteract(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            // 非房主也回 true，提示字才會出現（告訴他在等房主）
            return ctx.Player != null && stall != null && stall.IsNight;
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;
            return LocalIsHost ? "[左鍵] 收工，進入隔天" : "等房主收工";
        }

        /// <summary>
        /// 只會在房主（狀態權威）那一份上被呼叫。房主判定跟開張鈴一樣：
        /// 「按的這個玩家的輸入權 == 本機玩家」就是房主本人在按。
        /// </summary>
        public void Interact(in InteractionContext ctx)
        {
            var stall = StallManager.Instance;
            if (stall == null || !stall.HasStateAuthority || !stall.IsNight) return;
            if (ctx.Player == null || ctx.Player.Object == null) return;

            if (ctx.Player.Object.InputAuthority != stall.Runner.LocalPlayer)
            {
                stall.NoticeTo(ctx.Player.Object.InputAuthority, "只有房主可以收工");
                return;
            }

            GameAudio.PlayAt(SfxId.BellRing, transform.position);
            stall.EndNight();
        }
    }
}
