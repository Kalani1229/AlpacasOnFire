using AlpacasOnFire.Stall;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpacasOnFire.Core
{
    /// <summary>
    /// 白天／夜晚的光照。**純本機視覺**，沒有任何網路狀態 ——
    /// 從同步的 StallManager.State 推導（它本來就同步），所以每個 client 的天色一致。
    ///
    ///   白天（Exploring / Deploying / Open）      -> 場景原本的光照，一點都不動
    ///   夜晚（Night / Settling / RunOver，run 模式）-> 見下面五項
    ///
    /// 夜晚要真的「黑」，五個來源都得壓下去，少一個畫面就還是亮的：
    ///
    ///   1. 太陽：降到地平線附近、強度壓低、偏藍
    ///   2. **環境光探針（ambientProbe）**：這是最容易漏的一個。
    ///      只改 RenderSettings.ambientSkyColor 等三色**在執行期不會生效** ——
    ///      Unity 把環境光烘成一組球諧係數（SH），改顏色不會重算它。
    ///      所以這裡直接改 ambientProbe 本身（白天的係數 × 縮放 × 藍色調）。
    ///   3. 環境反射：預設反射來自天空盒，URP 的材質會反射出白天的天空。壓低 reflectionIntensity。
    ///   4. 天空盒：預設的程序天空在太陽貼近地平線時會變成亮橘色的黃昏。
    ///      **不換天空盒**，只把它的曝光壓低 —— 用執行期複製的材質，專案裡的資產不會被改到。
    ///   5. 霧：不開。
    ///
    /// 切換用 NightFadeSeconds 平滑過渡 —— 瞬間切看起來像 bug。
    /// 由 StallManager 在 run 模式開場時自動建立（EnsureExists），不用手動掛。
    /// Stall_Test（非 run 模式）永遠不會變暗。
    /// </summary>
    public class DayNightLighting : MonoBehaviour
    {
        [Tooltip("留空會自己找：RenderSettings.sun，找不到就用場景裡第一盞 Directional Light。")]
        [SerializeField] private Light _sun;

        [Header("夜晚（Play 中可以直接在 [DayNightLighting] 上調）")]
        [Tooltip("太陽離地平線幾度（方位不變）。")]
        [SerializeField] private float _nightElevation = 20f;
        [Tooltip("太陽強度 = 白天 × 這個值。")]
        [SerializeField, Range(0f, 1f)] private float _nightIntensityScale = 0.06f;
        [SerializeField] private Color _nightSunColor = new Color(0.45f, 0.55f, 1f);
        [Tooltip("環境光 = 白天 × 這個值 × 藍色調。")]
        [SerializeField, Range(0f, 1f)] private float _nightAmbientScale = 0.12f;
        [SerializeField] private Color _nightAmbientTint = new Color(0.55f, 0.65f, 1f);
        [Tooltip("天空與環境反射 = 白天 × 這個值。")]
        [SerializeField, Range(0f, 1f)] private float _nightSkyScale = 0.06f;

        private static DayNightLighting _instance;

        // 白天的原始值（開場記一次）
        private bool _cached;
        private Quaternion _daySunRot;
        private float _daySunIntensity;
        private Color _daySunColor;
        private Color _dayAmbSky, _dayAmbEquator, _dayAmbGround, _dayAmbFlat;
        private float _dayAmbIntensity;
        private SphericalHarmonicsL2 _dayProbe;
        private float _dayReflection;
        private Material _daySkybox;
        private Material _nightSkybox;     // 執行期複製出來的，只改它
        private float _daySkyExposure = 1f;
        private bool _skyHasExposure;

        private float _t = -1f;   // 0 = 白天、1 = 夜晚；-1 = 還沒套用過

        public static void EnsureExists()
        {
            if (_instance != null) return;
            _instance = FindAnyObjectByType<DayNightLighting>();
            if (_instance != null) return;
            _instance = new GameObject("[DayNightLighting]").AddComponent<DayNightLighting>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
        }

        private void CacheDay()
        {
            if (_cached) return;

            if (_sun == null) _sun = RenderSettings.sun;
            if (_sun == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { _sun = l; break; }
            }

            if (_sun != null)
            {
                _daySunRot = _sun.transform.rotation;
                _daySunIntensity = _sun.intensity;
                _daySunColor = _sun.color;
            }

            _dayAmbSky = RenderSettings.ambientSkyColor;
            _dayAmbEquator = RenderSettings.ambientEquatorColor;
            _dayAmbGround = RenderSettings.ambientGroundColor;
            _dayAmbFlat = RenderSettings.ambientLight;
            _dayAmbIntensity = RenderSettings.ambientIntensity;
            _dayProbe = RenderSettings.ambientProbe;
            _dayReflection = RenderSettings.reflectionIntensity;

            _daySkybox = RenderSettings.skybox;
            if (_daySkybox != null && _daySkybox.HasProperty("_Exposure"))
            {
                _nightSkybox = new Material(_daySkybox) { name = _daySkybox.name + " (Night)" };
                _daySkyExposure = _daySkybox.GetFloat("_Exposure");
                _skyHasExposure = true;
            }

            _cached = true;
        }

        private static bool WantNight()
        {
            var stall = StallManager.Instance;
            return stall != null && stall.Object != null && stall.Object.IsValid && stall.AnimalsAsleep;
        }

        private void Update()
        {
            CacheDay();

            float target = WantNight() ? 1f : 0f;
            if (_t >= 0f && Mathf.Approximately(_t, target)) return;
            if (_t < 0f) _t = 0f;

            _t = Mathf.MoveTowards(_t, target, Time.deltaTime / Mathf.Max(0.01f, GameTuning.NightFadeSeconds));
            Apply(Mathf.SmoothStep(0f, 1f, _t));
        }

        /// <summary>在 Inspector 調數值時，夜裡立刻看到結果。</summary>
        private void OnValidate()
        {
            if (Application.isPlaying && _cached && _t > 0f) Apply(Mathf.SmoothStep(0f, 1f, _t));
        }

        private void Apply(float k)
        {
            // 1. 太陽：方位（yaw）保留白天的，仰角壓低
            if (_sun != null)
            {
                var dayEuler = _daySunRot.eulerAngles;
                var nightRot = Quaternion.Euler(_nightElevation, dayEuler.y, dayEuler.z);
                _sun.transform.rotation = Quaternion.Slerp(_daySunRot, nightRot, k);
                _sun.intensity = Mathf.Lerp(_daySunIntensity, _daySunIntensity * _nightIntensityScale, k);
                _sun.color = Color.Lerp(_daySunColor, _nightSunColor, k);
            }

            // 2. 環境光：三色照樣改（給還會讀它們的東西），但真正生效的是 ambientProbe
            RenderSettings.ambientSkyColor = Color.Lerp(_dayAmbSky, NightAmbient(_dayAmbSky), k);
            RenderSettings.ambientEquatorColor = Color.Lerp(_dayAmbEquator, NightAmbient(_dayAmbEquator), k);
            RenderSettings.ambientGroundColor = Color.Lerp(_dayAmbGround, NightAmbient(_dayAmbGround), k);
            RenderSettings.ambientLight = Color.Lerp(_dayAmbFlat, NightAmbient(_dayAmbFlat), k);
            RenderSettings.ambientIntensity = Mathf.Lerp(_dayAmbIntensity, _dayAmbIntensity * _nightAmbientScale, k);
            RenderSettings.ambientProbe = ScaledProbe(k);

            // 3. 環境反射
            RenderSettings.reflectionIntensity = Mathf.Lerp(_dayReflection, _dayReflection * _nightSkyScale, k);

            // 4. 天空：只壓曝光，不換天空盒
            if (_skyHasExposure && _nightSkybox != null)
            {
                _nightSkybox.SetFloat("_Exposure", Mathf.Lerp(_daySkyExposure, _daySkyExposure * _nightSkyScale, k));
                var want = k > 0.001f ? _nightSkybox : _daySkybox;
                if (RenderSettings.skybox != want) RenderSettings.skybox = want;
            }
        }

        /// <summary>白天的環境光探針逐通道乘上「縮放 × 藍色調」，k 在白天與夜晚之間內插。</summary>
        private SphericalHarmonicsL2 ScaledProbe(float k)
        {
            var sh = _dayProbe;
            for (int c = 0; c < 3; c++)
            {
                float scale = Mathf.Lerp(1f, _nightAmbientScale * _nightAmbientTint[c], k);
                for (int i = 0; i < 9; i++) sh[c, i] = _dayProbe[c, i] * scale;
            }
            return sh;
        }

        private Color NightAmbient(Color day)
        {
            var c = day * _nightAmbientScale * _nightAmbientTint;
            c.a = day.a;
            return c;
        }

        /// <summary>離開場景（或停止 Play）時把光照還原，天空盒換回原本的資產。</summary>
        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_cached && _t > 0f) Apply(0f);
            if (_cached && _daySkybox != null) RenderSettings.skybox = _daySkybox;
            if (_nightSkybox != null) Destroy(_nightSkybox);
        }
    }
}
