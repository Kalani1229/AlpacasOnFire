using System.Collections.Generic;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 動物睡覺的樣子。**這是夜晚「做不了事」唯一的視覺訊號**，比任何 UI 都清楚。
    ///
    /// 目前沒有睡覺的動畫 clip，所以用四件事湊出來：
    ///   1. 身體往下沉一點（所有看得見的子物件一起沉，毛撮不會浮在半空）
    ///   2. 調暗（沒被主程式上色的 Renderer 由這裡調；身體與毛撮由呼叫端乘上 DimFactor）
    ///   3. Animator 放慢成「呼吸」的速度，並停在待機（isWalking 由呼叫端壓成 false）
    ///   4. 頭上飄一個「Zzz」
    ///
    /// 純 C#，由 WoolNpc / WildBeast 在 Render() 呼叫，**只讀同步的狀態**
    /// （NPC 自己的 StateRaw），所以每個 client 看到的都一樣。沒有任何網路狀態。
    /// 之後有睡覺 clip 時，只要把第 3 點換成播 clip。
    /// </summary>
    public sealed class NpcSleepVisual
    {
        private const float SinkMeters = 0.18f;
        private const float BlendSeconds = 0.8f;
        private const float BreathSpeed = 0.2f;

        /// <summary>睡著時顏色乘上這個。呼叫端自己上色的 Renderer 要用它。</summary>
        public const float DimFactor = 0.5f;

        private Transform _root;
        private readonly List<(Transform t, Vector3 basePos)> _sinkers = new();
        private readonly List<(Renderer r, Color[] baseColors)> _dimmers = new();
        private Animator _animator;
        private TextMesh _zzz;
        private float _t;          // 0 = 醒著、1 = 睡熟
        private bool _dimmed;

        /// <param name="root">NPC 根物件。</param>
        /// <param name="selfTinted">呼叫端每次自己上色的 Renderer（身體、毛撮）——這裡不碰它們的顏色。</param>
        public void Bind(Transform root, IEnumerable<Renderer> selfTinted)
        {
            _root = root;
            _sinkers.Clear();
            _dimmers.Clear();

            var skip = new HashSet<Renderer>();
            if (selfTinted != null)
                foreach (var r in selfTinted) if (r != null) skip.Add(r);

            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                // 顧客看板與互動錨點不是身體的一部分
                if (child.name == "CustomerSign" || child.name == "InteractionAnchor") continue;
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;
                _sinkers.Add((child, child.localPosition));
            }

            foreach (var (t, _) in _sinkers)
            {
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (skip.Contains(r)) continue;
                    var mats = r.sharedMaterials;
                    var colors = new Color[mats.Length];
                    for (int m = 0; m < mats.Length; m++)
                        colors[m] = BaseColorOf(mats[m]);
                    _dimmers.Add((r, colors));
                }
            }

            _animator = root.GetComponentInChildren<Animator>(true);
        }

        private static Color BaseColorOf(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        /// <summary>每幀呼叫。asleep 來自同步狀態。</summary>
        public void Tick(bool asleep)
        {
            if (_root == null) return;

            float target = asleep ? 1f : 0f;
            if (Mathf.Approximately(_t, target) && _dimmed == asleep && (!asleep || _zzz != null))
            {
                if (asleep) AnimateZzz();
                return;
            }

            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / BlendSeconds);

            // 1. 下沉
            foreach (var (t, basePos) in _sinkers)
                if (t != null) t.localPosition = basePos + Vector3.down * (SinkMeters * _t);

            // 2. 調暗：一次切換就好（夜晚本身的光照已經在慢慢變暗了）
            if (_dimmed != asleep) ApplyDim(asleep);

            // 3. 呼吸速度
            if (_animator != null) _animator.speed = Mathf.Lerp(1f, BreathSpeed, _t);

            // 4. Zzz
            if (asleep) { EnsureZzz(); AnimateZzz(); }
            else if (_zzz != null && _zzz.gameObject.activeSelf) _zzz.gameObject.SetActive(false);
        }

        private void ApplyDim(bool dim)
        {
            _dimmed = dim;
            var mpb = new MaterialPropertyBlock();
            foreach (var (r, colors) in _dimmers)
            {
                if (r == null) continue;
                for (int m = 0; m < colors.Length; m++)
                {
                    mpb.Clear();
                    if (dim)
                    {
                        var c = colors[m] * DimFactor; c.a = colors[m].a;
                        mpb.SetColor("_BaseColor", c);
                        mpb.SetColor("_Color", c);
                    }
                    // 醒來：清空的 block = 拿掉覆寫，回到材質原色
                    r.SetPropertyBlock(mpb, m);
                }
            }
        }

        private void EnsureZzz()
        {
            if (_zzz != null)
            {
                if (!_zzz.gameObject.activeSelf) _zzz.gameObject.SetActive(true);
                return;
            }

            var go = new GameObject("Zzz");
            go.transform.SetParent(_root, false);
            _zzzBaseY = TopOfBody() + 0.35f;

            _zzz = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _zzz.font = font;
            _zzz.text = "Zzz";
            _zzz.fontSize = 48;
            _zzz.characterSize = 0.06f;
            _zzz.anchor = TextAnchor.MiddleCenter;
            _zzz.color = new Color(0.85f, 0.9f, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>輕輕上下飄、面向鏡頭。</summary>
        private void AnimateZzz()
        {
            if (_zzz == null) return;
            var t = _zzz.transform;
            float bob = Mathf.Sin(Time.time * 1.6f + _root.GetInstanceID() * 0.37f) * 0.06f;
            t.position = _root.position + Vector3.up * (_zzzBaseY + bob);

            var cam = Camera.main;
            if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);
        }

        private float _zzzBaseY = 1.5f;

        /// <summary>身體頂端離根物件多高（只看會下沉的那些子物件，不含看板）。</summary>
        private float TopOfBody()
        {
            float top = 0f;
            bool any = false;
            foreach (var (t, _) in _sinkers)
            {
                if (t == null) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    float h = r.bounds.max.y - _root.position.y;
                    top = any ? Mathf.Max(top, h) : h;
                    any = true;
                }
            }
            return any ? top : 1.2f;
        }
    }
}
