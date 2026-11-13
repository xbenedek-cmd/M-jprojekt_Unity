using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zvuk a atmosféra bez audio súborov: všetky efekty, prostredie a hudba sa generujú kódom pri štarte.
///  - Efekty: kroky, švihy a údery meča, zranenie, výstrel veže, blesk, výroba, poplach, portál, stavba.
///  - Prostredie (plynulý prechod podľa miesta): mesto, divočina (vietor a vtáky), Megakorporácia (hukot a pípanie).
///  - Hudba: pokojná pri skúmaní, svižná pri boji (démon naháňa hráča alebo je poplach vojakov) a krátky poplachový tón.
/// Hlasitosti (hlavná, hudba, efekty) sa nastavujú v pauze a ukladajú do PlayerPrefs.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public enum Sfx { Footstep, Swing, SwingHeavy, SwordHit, Hurt, TowerShot, Lightning, Craft, Alert, Portal, Build, Bird }

    private const int Rate = 22050;
    private const float TwoPi = Mathf.PI * 2f;

    public static GameAudio Instance { get; private set; }

    public static float MasterVolume { get => Get("vol_master", 0.8f); set => Set("vol_master", value); }
    public static float MusicVolume { get => Get("vol_music", 0.5f); set => Set("vol_music", value); }
    public static float SfxVolume { get => Get("vol_sfx", 0.8f); set => Set("vol_sfx", value); }

    static float Get(string key, float fallback)
    {
        try { return PlayerPrefs.GetFloat(key, fallback); } catch { return fallback; }
    }

    static void Set(string key, float value)
    {
        try { PlayerPrefs.SetFloat(key, Mathf.Clamp01(value)); } catch { /* bez uloženia */ }
    }

    public static void Play(Sfx sfx, Vector3 position)
    {
        if (Instance != null) Instance.PlayAt(sfx, position);
    }

    private readonly Dictionary<Sfx, AudioClip[]> clips = new Dictionary<Sfx, AudioClip[]>();
    private AudioSource[] pool;
    private int nextSource;
    private AudioSource cityLoop, wildLoop, corpLoop, exploreMusic, combatMusic;
    private AudioClip[] tracks;   // skladby z Resources/Music (ak sú), inak generovaná pokojná hudba
    private int lastTrack = -1;
    private float nextTrackAt = -1f;
    private bool ready;

    private PlayerStats player;
    private PlayerMovement movement;
    private PlayerStealth stealth;
    private Vector3 lastPosition;
    private float stepProgress;
    private float lastHealth = -1f;
    private float nextBird;
    private float combatUntil;
    private bool wasCombat;
    private float nextZoneRefresh;
    private Bounds corpBounds;
    private readonly List<Bounds> wildBounds = new List<Bounds>();
    private float cityVolume, wildVolume, corpVolume, exploreVolume, combatVolume;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<GameAudio>() != null) return;
        new GameObject("GameAudio").AddComponent<GameAudio>();
    }

    void Awake()
    {
        Instance = this;

        // Hra potrebuje jedného poslucháča; ak ho kamera nemá, pridá sa na tento objekt
        if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

        pool = new AudioSource[10];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = gameObject.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Klipy sa generujú postupne (jeden za snímok), aby štart nezasekol
    IEnumerator Start()
    {
        Add(Sfx.Footstep, Step(1, 0.12f), Step(2, 0.16f), Step(3, 0.1f));
        yield return null;
        Add(Sfx.Swing, Swing(0.25f, 0.5f));
        Add(Sfx.SwingHeavy, Swing(0.4f, 0.3f));
        Add(Sfx.SwordHit, Clang(0.4f, 820f), Clang(0.4f, 700f));
        yield return null;
        Add(Sfx.Hurt, Hurt());
        Add(Sfx.TowerShot, Zap(0.25f, 1300f, 200f));
        Add(Sfx.Lightning, Crackle());
        yield return null;
        Add(Sfx.Craft, Craft());
        Add(Sfx.Alert, Alert());
        Add(Sfx.Portal, Whoosh());
        Add(Sfx.Build, Knock());
        Add(Sfx.Bird, Chirp(), Chirp(1.2f));
        yield return null;

        cityLoop = Loop("Mesto", CityAmbient());
        yield return null;
        wildLoop = Loop("Divočina", WildAmbient());
        corpLoop = Loop("Megakorporácia", CorpAmbient());
        yield return null;
        tracks = Resources.LoadAll<AudioClip>("Music");
        if (tracks != null && tracks.Length > 0)
        {
            exploreMusic = Loop("Hudba", PickTrack());
            exploreMusic.loop = false;
        }
        else exploreMusic = Loop("Hudba", ExploreMusic());
        yield return null;
        combatMusic = Loop("Hudba boj", CombatMusic());

        ready = true;
    }

    // Ďalšia skladba náhodne, nikdy dvakrát po sebe
    AudioClip PickTrack()
    {
        int index = UnityEngine.Random.Range(0, tracks.Length);
        if (tracks.Length > 1 && index == lastTrack) index = (index + 1 + UnityEngine.Random.Range(0, tracks.Length - 1)) % tracks.Length;
        lastTrack = index;
        return tracks[index];
    }

    // Po dohraní skladby krátka pauza (8–20 s) a ďalšia
    void PlaylistTick()
    {
        if (tracks == null || tracks.Length == 0 || exploreMusic == null || AudioListener.pause) return;
        if (exploreMusic.isPlaying) { nextTrackAt = -1f; return; }

        if (nextTrackAt < 0f) nextTrackAt = Time.unscaledTime + UnityEngine.Random.Range(8f, 20f);
        if (Time.unscaledTime < nextTrackAt) return;

        exploreMusic.clip = PickTrack();
        exploreMusic.Play();
        nextTrackAt = -1f;
    }

    void Add(Sfx sfx, params AudioClip[] variants) => clips[sfx] = variants;

    AudioSource Loop(string name, AudioClip clip)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.volume = 0f;
        source.spatialBlend = 0f;
        source.Play();
        return source;
    }

    void PlayAt(Sfx sfx, Vector3 position)
    {
        if (!ready || !clips.TryGetValue(sfx, out AudioClip[] variants) || variants.Length == 0) return;

        AudioSource source = pool[nextSource];
        nextSource = (nextSource + 1) % pool.Length;

        source.transform.position = position;
        source.spatialBlend = 1f;
        source.minDistance = 4f;
        source.maxDistance = 45f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.volume = SfxVolume * MasterVolume * (sfx == Sfx.Bird ? 0.4f : 1f);
        source.pitch = UnityEngine.Random.Range(0.93f, 1.07f);
        source.PlayOneShot(variants[UnityEngine.Random.Range(0, variants.Length)]);
    }

    void PlayClipAt(AudioClip clip, Vector3 position, float volume)
    {
        if (!ready) return;

        AudioSource source = pool[nextSource];
        nextSource = (nextSource + 1) % pool.Length;

        source.transform.position = position;
        source.spatialBlend = 1f;
        source.minDistance = 4f;
        source.maxDistance = 45f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.volume = SfxVolume * MasterVolume * volume;
        source.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
        source.PlayOneShot(clip);
    }

    // ---------- Každý snímok: kroky, zranenie, prostredie, hudba ----------
    void Update()
    {
        if (!ready) return;

        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null)
            {
                movement = player.GetComponent<PlayerMovement>();
                stealth = player.GetComponent<PlayerStealth>();
                lastPosition = player.transform.position;
                lastHealth = player.Health;
            }
            return;
        }

        float dt = Time.unscaledDeltaTime;
        Footsteps(Time.deltaTime);
        Hurting();
        Ambience(dt);
        MusicState(dt);
    }

    void Footsteps(float dt)
    {
        if (dt <= 0f) return;

        Vector3 delta = player.transform.position - lastPosition;
        lastPosition = player.transform.position;
        delta.y = 0f;

        float speed = delta.magnitude / dt;
        bool grounded = movement == null || movement.IsGrounded;
        if (speed < 0.8f || speed > 25f || !grounded) { stepProgress = Mathf.Min(stepProgress, 0.5f); return; }

        stepProgress += delta.magnitude / (movement != null && movement.IsSprinting ? 3.1f : 2.2f);   // dĺžka kroku (väčšia = zvuk chôdze redší)
        if (stepProgress < 1f) return;

        stepProgress = 0f;
        Sfx step = Sfx.Footstep;
        // Zvuk podľa povrchu pod nohami (balík footsteps_pack_2); bez neho sa použije generovaný krok
        AudioClip real = FootstepSurfaces.Pick(player.transform.position, movement != null && movement.IsSprinting);
        if (real != null) PlayClipAt(real, player.transform.position, stealth != null && stealth.IsCrouched ? 0.3f : 0.75f);
        else PlayAt(step, player.transform.position);
    }

    void Hurting()
    {
        float health = player.Health;
        if (lastHealth >= 0f && health < lastHealth - 0.5f) PlayAt(Sfx.Hurt, player.transform.position);
        lastHealth = health;
    }

    void Ambience(float dt)
    {
        if (Time.unscaledTime >= nextZoneRefresh)
        {
            nextZoneRefresh = Time.unscaledTime + 8f;
            RefreshZones();
        }

        Vector3 p = player.transform.position;
        bool inCorp = corpBounds.size.x > 0f && Contains(corpBounds, p);
        bool inWild = false;
        foreach (Bounds b in wildBounds)
            if (Contains(b, p)) { inWild = true; break; }

        float k = 1f - Mathf.Exp(-dt * 0.8f);
        cityVolume = Mathf.Lerp(cityVolume, !inCorp && !inWild ? 1f : 0f, k);
        wildVolume = Mathf.Lerp(wildVolume, inWild ? 1f : 0f, k);
        corpVolume = Mathf.Lerp(corpVolume, inCorp ? 1f : 0f, k);

        float ambient = SfxVolume * MasterVolume * 0.2f;   // šum prostredia je potichu
        cityLoop.volume = cityVolume * ambient * 0.8f;
        wildLoop.volume = wildVolume * ambient;
        corpLoop.volume = corpVolume * ambient;

        // Vtáky len v divočine
        if (inWild && !MainQuest.QuietWoods && Time.time >= nextBird)
        {
            nextBird = Time.time + UnityEngine.Random.Range(4f, 11f);
            PlayAt(Sfx.Bird, p + UnityEngine.Random.insideUnitSphere * 15f + Vector3.up * 6f);
        }
    }

    static bool Contains(Bounds b, Vector3 p) => Mathf.Abs(p.x - b.center.x) <= b.extents.x && Mathf.Abs(p.z - b.center.z) <= b.extents.z;

    void RefreshZones()
    {
        GameObject corp = GameObject.Find("Zem_Megakorporacia");
        Renderer corpRenderer = corp != null ? corp.GetComponent<Renderer>() : null;
        corpBounds = corpRenderer != null ? corpRenderer.bounds : new Bounds();

        wildBounds.Clear();
        foreach (string key in new[] { "Divocina", "Kanon", "Les", "Pustatina", "Mociare" })
        {
            GameObject ground = GameObject.Find("Zem_" + key);
            Renderer r = ground != null ? ground.GetComponent<Renderer>() : null;
            if (r != null) wildBounds.Add(r.bounds);
        }
    }

    // Boj: démon naháňa hráča alebo vojaci majú poplach v okolí; hudba prejde plynulo a na začiatku zaznie poplachový tón
    void MusicState(float dt)
    {
        bool combat = false;
        Vector3 p = player.transform.position;

        foreach (DemonEnemy demon in DemonEnemy.All)
            if (demon != null && demon.IsChasing && (demon.transform.position - p).sqrMagnitude < 30f * 30f) { combat = true; break; }

        if (!combat)
            foreach (SoldierAI soldier in SoldierAI.All)
                if (soldier != null && !soldier.IsDead && soldier.State == SoldierState.Alert && (soldier.transform.position - p).sqrMagnitude < 30f * 30f) { combat = true; break; }

        if (combat) combatUntil = Time.unscaledTime + 4f;
        bool inCombat = Time.unscaledTime < combatUntil;

        if (inCombat && !wasCombat) PlayAt(Sfx.Alert, p);
        wasCombat = inCombat;

        float k = 1f - Mathf.Exp(-dt * 1.2f);
        exploreVolume = Mathf.Lerp(exploreVolume, inCombat ? 0f : 1f, k);
        combatVolume = Mathf.Lerp(combatVolume, inCombat ? 1f : 0f, k);

        float music = MusicVolume * MasterVolume * 0.5f;
        exploreMusic.volume = exploreVolume * music;
        combatMusic.volume = combatVolume * music;
        PlaylistTick();
    }

    // ---------- Syntéza ----------
    static AudioClip Make(string name, float seconds, Func<int, float, float> sample, bool loop = false)
    {
        int count = Mathf.CeilToInt(seconds * Rate);
        float[] data = new float[count];
        for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(sample(i, i / (float)Rate), -1f, 1f);

        AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

    static AudioClip Step(int seed, float length)
    {
        var rng = new System.Random(seed);
        float lp = 0f;
        float cutoff = 0.06f + seed * 0.03f;
        return Make("Krok", length, (i, t) =>
        {
            lp += (Noise(rng) - lp) * cutoff;
            return lp * Mathf.Exp(-t * 38f) * 2.2f;
        });
    }

    static AudioClip Swing(float length, float highCutoff)
    {
        var rng = new System.Random(7);
        float lp = 0f;
        return Make("Švih", length, (i, t) =>
        {
            float u = t / length;
            lp += (Noise(rng) - lp) * Mathf.Lerp(0.04f, highCutoff, u);
            return lp * Mathf.Sin(Mathf.PI * u) * 1.4f;
        });
    }

    static AudioClip Clang(float length, float freq)
    {
        var rng = new System.Random(3);
        return Make("Úder", length, (i, t) =>
        {
            float tone = Mathf.Sin(TwoPi * freq * t) * 0.4f + Mathf.Sin(TwoPi * freq * 1.6f * t) * 0.3f + Mathf.Sin(TwoPi * freq * 2.5f * t) * 0.2f;
            return tone * Mathf.Exp(-t * 14f) + Noise(rng) * Mathf.Exp(-t * 70f) * 0.6f;
        });
    }

    static AudioClip Hurt()
    {
        var rng = new System.Random(11);
        return Make("Zranenie", 0.25f, (i, t) => Mathf.Sin(TwoPi * (140f - 200f * t) * t) * Mathf.Exp(-t * 12f) * 0.8f + Noise(rng) * Mathf.Exp(-t * 30f) * 0.3f);
    }

    static AudioClip Zap(float length, float from, float to)
    {
        float phase = 0f;
        return Make("Výstrel", length, (i, t) =>
        {
            phase += TwoPi * Mathf.Lerp(from, to, t / length) / Rate;
            return Mathf.Sin(phase) * Mathf.Exp(-t * 9f) * 0.6f;
        });
    }

    static AudioClip Crackle()
    {
        var rng = new System.Random(5);
        float burst = 0f;
        return Make("Blesk", 0.35f, (i, t) =>
        {
            if (rng.NextDouble() < 0.0025) burst = 1f;
            burst *= 0.9985f;
            return Noise(rng) * (0.15f + burst) * Mathf.Exp(-t * 7f);
        });
    }

    static AudioClip Craft()
    {
        return Make("Výroba", 0.6f, (i, t) =>
        {
            float a = Mathf.Sin(TwoPi * 900f * t) * Mathf.Exp(-t * 18f);
            float t2 = Mathf.Max(0f, t - 0.28f);
            float b = t >= 0.28f ? Mathf.Sin(TwoPi * 1100f * t2) * Mathf.Exp(-t2 * 18f) : 0f;
            return (a + b) * 0.5f;
        });
    }

    static AudioClip Alert()
    {
        return Make("Poplach", 0.55f, (i, t) =>
        {
            float f = t < 0.2f ? 440f : 660f;
            float local = t < 0.2f ? t : t - 0.2f;
            return Mathf.Sin(TwoPi * f * t) * Mathf.Exp(-local * 6f) * 0.5f;
        });
    }

    static AudioClip Whoosh()
    {
        float phase = 0f;
        return Make("Portál", 0.7f, (i, t) =>
        {
            phase += TwoPi * Mathf.Lerp(200f, 900f, t / 0.7f) / Rate;
            return Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * t / 0.7f) * (0.5f + 0.5f * Mathf.Sin(TwoPi * 14f * t)) * 0.5f;
        });
    }

    static AudioClip Knock()
    {
        var rng = new System.Random(9);
        return Make("Stavba", 0.3f, (i, t) => Mathf.Sin(TwoPi * 190f * t) * Mathf.Exp(-t * 16f) * 0.7f + Noise(rng) * Mathf.Exp(-t * 60f) * 0.4f);
    }

    static AudioClip Chirp(float pitch = 1f)
    {
        return Make("Vták", 0.18f, (i, t) =>
        {
            float f = (2400f + 700f * Mathf.Sin(TwoPi * 14f * t)) * pitch;
            return Mathf.Sin(TwoPi * f * t) * Mathf.Sin(Mathf.PI * t / 0.18f) * 0.4f;
        });
    }

    // Slučky: dlhé (30–55 s, aby sa opakovanie nepoznalo) so sínusmi s celým počtom periód (bez škrabnutia) a šumom zrkadleným v strede
    static float[] MirroredNoise(int seed, int samples, float cutoff)
    {
        var rng = new System.Random(seed);
        float[] half = new float[samples / 2];
        float lp = 0f;
        for (int i = 0; i < half.Length; i++)
        {
            lp += (Noise(rng) - lp) * cutoff;
            half[i] = lp;
        }
        return half;
    }

    static float MirrorAt(float[] half, int i)
    {
        int n = half.Length;
        return i < n ? half[i] : half[Mathf.Clamp(2 * n - 1 - i, 0, n - 1)];
    }

    static AudioClip CityAmbient()
    {
        const float length = 48f;
        float[] noise = MirroredNoise(21, Mathf.RoundToInt(length * Rate), 0.015f);
        return Make("Ruch mesta", length, (i, t) =>
            MirrorAt(noise, i) * 6f * (0.7f + 0.3f * Mathf.Sin(TwoPi * t / length * 3f)) + Mathf.Sin(TwoPi * 55f * t) * 0.05f + Mathf.Sin(TwoPi * 82.5f * t) * 0.03f, true);
    }

    static AudioClip WildAmbient()
    {
        const float length = 53f;
        float[] noise = MirroredNoise(33, Mathf.RoundToInt(length * Rate), 0.05f);
        return Make("Vietor", length, (i, t) => MirrorAt(noise, i) * 4f * (0.55f + 0.45f * Mathf.Sin(TwoPi * t / length * 5f)), true);
    }

    static AudioClip CorpAmbient()
    {
        return Make("Hukot", 40f, (i, t) =>
        {
            float hum = Mathf.Sin(TwoPi * 50f * t) * 0.12f + Mathf.Sin(TwoPi * 100f * t) * 0.08f + Mathf.Sin(TwoPi * 200f * t) * 0.03f;
            float local = t % 2f;
            float beep = local < 0.06f ? Mathf.Sin(TwoPi * 1760f * t) * Mathf.Sin(Mathf.PI * local / 0.06f) * 0.05f : 0f;
            return hum + beep;
        }, true);
    }

    // Pokojná hudba: štyri akordy po 4 s (Am – F – C – G) ako pomaly nábehajúci pad
    static AudioClip ExploreMusic()
    {
        float[][] chords =
        {
            new[] { 110f, 130.8f, 164.8f }, new[] { 87.3f, 130.8f, 174.6f }, new[] { 130.8f, 164.8f, 196f }, new[] { 98f, 123.5f, 146.8f }
        };

        return Make("Hudba", 16f, (i, t) =>
        {
            int chord = Mathf.Min(3, (int)(t / 4f));
            float u = (t - chord * 4f) / 4f;
            float env = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), 1.5f);

            float sum = 0f;
            foreach (float f in chords[chord]) sum += Mathf.Sin(TwoPi * f * t) + 0.3f * Mathf.Sin(TwoPi * f * 2f * t);
            float shimmer = Mathf.Sin(TwoPi * 659.25f * t) * 0.05f * (0.5f + 0.5f * Mathf.Sin(TwoPi * t / 8f));
            return sum * 0.1f * env + shimmer * env;
        }, true);
    }

    // Bojová hudba: 120 úderov za minútu, basový pulz, šum ako činel a akordový úder každé štyri doby
    static AudioClip CombatMusic()
    {
        var rng = new System.Random(15);
        return Make("Hudba boj", 8f, (i, t) =>
        {
            float beat = t % 0.5f;
            float bass = Mathf.Sin(TwoPi * 55f * t) * Mathf.Exp(-beat * 8f) * 0.5f;
            float hat = (t % 0.25f) < 0.04f ? Noise(rng) * 0.12f * Mathf.Exp(-(t % 0.25f) * 60f) : 0f;
            float stab = 0f;
            if (t % 2f < 0.35f)
            {
                float local = t % 2f;
                stab = (Mathf.Sin(TwoPi * 220f * t) + Mathf.Sin(TwoPi * 261.6f * t) + Mathf.Sin(TwoPi * 329.6f * t)) * Mathf.Exp(-local * 9f) * 0.08f;
            }
            return bass + hat + stab;
        }, true);
    }
}
