using System.Collections.Generic;
using UnityEngine;

namespace AlpacasOnFire.Core
{
    /// <summary>音效事件列舉 —— 規格書第九節要求保留的觸發點。</summary>
    public enum SfxId
    {
        ShipSuccess,     // 出貨成功
        ShipFail,        // 出貨失敗
        OrderTimeout,    // 訂單超時扣款
        Shear,           // 剃毛成功
        AccessoryAttach, // 飾品裝上
        Spray,           // 噴槍噴出染劑
        Catch,           // 接住丟出的物品
        Throw,           // 丟出物品
        Pickup,          // 拾取
        Drop,            // 放下
        DressOn,         // 人偶穿上衣服
        DressOff,        // 人偶脫下衣服
        MachineStart,    // 機台開始處理
        MachineDone,     // 機台完成
        LevelEnd,        // 關卡結束

        // ---- 擺攤系統（本批新增）----
        StallOpen,       // 打開手提箱、襯布攤開
        StallPopOut,     // 裝備從箱子裡彈出到格子上
        StallClose,      // 收攤
        DevicePlace,     // 機台放下
        DevicePickup,    // 機台拿起
        PlaceRejected,   // 放置不合法
        BellRing,        // 敲開張鈴
        BusinessOpen,    // 開張
        BusinessClose,   // 營業時間結束
    }

    /// <summary>
    /// 佔位音效：用程式產生的簡單合成音，之後換成真正的 AudioClip 時
    /// 只要改 Resolve()，所有呼叫端（GameAudio.Play(SfxId.X)）都不用動。
    /// </summary>
    public static class GameAudio
    {
        private static AudioSource _source;
        private static readonly Dictionary<SfxId, AudioClip> Cache = new();

        // (頻率 Hz, 長度秒, 音量, 是否上升滑音)
        private static (float freq, float len, float vol, float slide) Recipe(SfxId id) => id switch
        {
            SfxId.ShipSuccess     => (880f, 0.28f, 0.5f,  1.5f),
            SfxId.ShipFail        => (180f, 0.35f, 0.5f,  0.6f),
            SfxId.OrderTimeout    => (140f, 0.45f, 0.45f, 0.5f),
            SfxId.Shear           => (620f, 0.16f, 0.4f,  0.8f),
            SfxId.AccessoryAttach => (1200f, 0.10f, 0.35f, 1.2f),
            SfxId.Spray           => (300f, 0.18f, 0.25f, 1.0f),
            SfxId.Catch           => (760f, 0.12f, 0.4f,  1.3f),
            SfxId.Throw           => (520f, 0.12f, 0.35f, 0.7f),
            SfxId.Pickup          => (660f, 0.09f, 0.3f,  1.2f),
            SfxId.Drop            => (330f, 0.09f, 0.3f,  0.8f),
            SfxId.DressOn         => (720f, 0.20f, 0.4f,  1.25f),
            SfxId.DressOff        => (480f, 0.20f, 0.4f,  0.8f),
            SfxId.MachineStart    => (400f, 0.15f, 0.35f, 1.1f),
            SfxId.MachineDone     => (940f, 0.22f, 0.45f, 1.2f),
            SfxId.LevelEnd        => (523f, 0.60f, 0.55f, 1.6f),
            SfxId.StallOpen       => (330f, 0.34f, 0.45f, 1.8f),
            SfxId.StallPopOut     => (820f, 0.12f, 0.35f, 1.45f),
            SfxId.StallClose      => (600f, 0.30f, 0.45f, 0.5f),
            SfxId.DevicePlace     => (240f, 0.14f, 0.45f, 0.7f),
            SfxId.DevicePickup    => (500f, 0.11f, 0.35f, 1.3f),
            SfxId.PlaceRejected   => (160f, 0.16f, 0.40f, 0.85f),
            SfxId.BellRing        => (1480f, 0.55f, 0.50f, 1.02f), // 清脆、幾乎不滑音，像敲鈴
            SfxId.BusinessOpen    => (700f, 0.45f, 0.55f, 1.7f),
            SfxId.BusinessClose   => (620f, 0.50f, 0.50f, 0.55f),
            _                     => (440f, 0.15f, 0.3f,  1.0f),
        };

        private static AudioSource Source
        {
            get
            {
                if (_source == null)
                {
                    var go = new GameObject("[GameAudio]");
                    Object.DontDestroyOnLoad(go);
                    _source = go.AddComponent<AudioSource>();
                    _source.playOnAwake = false;
                    _source.spatialBlend = 0f;
                }
                return _source;
            }
        }

        private static AudioClip Resolve(SfxId id)
        {
            if (Cache.TryGetValue(id, out var cached) && cached != null) return cached;

            var (freq, len, vol, slide) = Recipe(id);
            const int rate = 44100;
            int samples = Mathf.Max(16, Mathf.RoundToInt(rate * len));
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / samples;
                float f = Mathf.Lerp(freq, freq * slide, t);
                phase += 2f * Mathf.PI * f / rate;
                float env = Mathf.Sin(Mathf.PI * t);          // 淡入淡出，避免爆音
                data[i] = Mathf.Sin(phase) * env * vol;
            }

            var clip = AudioClip.Create("sfx_" + id, samples, 1, rate, false);
            clip.SetData(data, 0);
            Cache[id] = clip;
            return clip;
        }

        /// <summary>2D 播放（UI／全域事件）。</summary>
        public static void Play(SfxId id, float volumeScale = 1f)
        {
            if (!Application.isPlaying) return;
            Source.PlayOneShot(Resolve(id), volumeScale);
        }

        /// <summary>3D 播放（世界中的事件）。目前佔位音一律 2D，保留簽章供之後替換。</summary>
        public static void PlayAt(SfxId id, Vector3 worldPos, float volumeScale = 1f)
        {
            if (!Application.isPlaying) return;
            AudioSource.PlayClipAtPoint(Resolve(id), worldPos, volumeScale);
        }
    }
}
