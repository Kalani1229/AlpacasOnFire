using AlpacasOnFire.Core;
using AlpacasOnFire.Stall;
using UnityEngine;

namespace AlpacasOnFire.Casino
{
    /// <summary>
    /// 刮刮樂櫃檯。賭場建築之後再做，這一批先借出生廣場：擺在**床的旁邊**。
    ///
    /// 生滅由 CasinoState 管（夜晚 Spawn、天亮 Despawn，只有主機做）。
    /// 位置在 Spawned() 裡每一端自己算（跟床同一套「家」的位置，算出來都一樣），
    /// 所以 prefab 不需要 NetworkTransform。
    ///
    /// 外觀全部是執行期生的佔位幾何：一個矮櫃 + 一塊招牌（寫「頭獎 500」，不印完整賠率表）
    /// + 一盞小燈（夜裡要找得到）。prefab 上只有 NetworkObject、這支、一個碰撞體。
    /// </summary>
    public class ScratchCardCounter : MinigameMachine
    {
        /// <summary>櫃檯在床的哪一側、離多遠（床的大哥會往另一側讓開）。</summary>
        private const float SideOffset = -2.6f;

        private bool _visualBuilt;

        /// <summary>
        /// 櫃檯的位置：家（第一個出生點）往床的左手邊移一段，面向廣場。
        /// 任何端都可以算，結果一樣。
        /// </summary>
        public static void HomePlacement(StallManager stall, out Vector3 position, out Quaternion rotation)
        {
            Bed.HomeFrame(stall, out var home, out var front);
            var side = Vector3.Cross(Vector3.up, front).normalized;
            position = home + side * SideOffset;
            // 櫃檯的正面（招牌那一面）朝廣場，玩家從廣場走過來就看得到字
            rotation = Quaternion.LookRotation(-front, Vector3.up);
        }

        public override void Spawned()
        {
            var stall = StallManager.Instance;
            if (stall != null)
            {
                HomePlacement(stall, out var pos, out var rot);
                transform.SetPositionAndRotation(pos, rot);
            }
            BuildVisual();
        }

        // ---------------- MinigameMachine ----------------

        protected override bool Available
        {
            get
            {
                var stall = StallManager.Instance;
                return stall != null && stall.Object != null && stall.Object.IsValid
                       && stall.RunMode && stall.IsNight;
            }
        }

        protected override string PlayLabel
        {
            get
            {
                var casino = CasinoState.Instance;
                int left = casino != null ? casino.CardsLeft : 0;
                return left > 0 ? $"玩刮刮樂（剩 {left} 張）" : "玩刮刮樂（今天賣完了）";
            }
        }

        protected override void OpenPanelLocal() => ScratchCardPanel.Open(this);

        // ---------------- 外觀（佔位） ----------------

        private void BuildVisual()
        {
            if (_visualBuilt) return;
            _visualBuilt = true;

            // 矮櫃：local -Z 是正面（面向廣場）
            Box("Desk", new Vector3(0f, 0.5f, 0f), new Vector3(1.8f, 1.0f, 0.8f), new Color(0.55f, 0.18f, 0.22f));
            Box("Top", new Vector3(0f, 1.03f, 0f), new Vector3(1.9f, 0.06f, 0.9f), new Color(0.9f, 0.75f, 0.3f));

            // 招牌：立在櫃子後緣，字朝正面
            Box("SignPost", new Vector3(0f, 1.55f, 0.3f), new Vector3(0.08f, 1.0f, 0.08f), new Color(0.3f, 0.3f, 0.32f));
            Box("Sign", new Vector3(0f, 2.15f, 0.3f), new Vector3(1.7f, 0.65f, 0.08f), new Color(0.12f, 0.1f, 0.16f));

            var text = new GameObject("SignText");
            text.transform.SetParent(transform, false);
            // 招牌板的正面那一側（-Z）。TextMesh 是給「站在 -Z、看向 +Z」的人讀的，剛好不用轉
            text.transform.localPosition = new Vector3(0f, 2.15f, 0.25f);
            var tm = text.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = "刮刮樂\n頭獎 500";
            tm.fontSize = 64;
            tm.characterSize = 0.035f;
            tm.lineSpacing = 0.9f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(1f, 0.85f, 0.35f);
            var mr = text.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 夜裡的燈
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 2.8f, -0.6f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 2f;
            light.color = new Color(1f, 0.6f, 0.5f);
            light.shadows = LightShadows.None;
        }

        private void Box(string name, Vector3 localPos, Vector3 size, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }
    }
}
