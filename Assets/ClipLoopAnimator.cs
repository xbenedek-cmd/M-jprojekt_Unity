using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Animácia postavy z modelu (drevorubači) bez Animator Controllera (PlayableGraph). Berie klipy hráča (Idle a Run zo sady, rig je rovnaký
/// Mixamo), takže postava v stoji dýcha a pri pohybe beží; rýchlosť behu sleduje skutočnú rýchlosť postavy a prechod medzi nimi je plynulý.
/// Bez sady hráča sa použije klip vložený v FBX (podľa mena walk / run / idle, inak prvý).
/// </summary>
public class ClipLoopAnimator : MonoBehaviour
{
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idlePlayable, runPlayable;
    private float idleLength, runLength, idleClock, runClock, blend, runReferenceSpeed = 3.2f;
    private bool hasIdle, hasRun;
    private Vector3 lastPosition;
    private Transform mover;
    private bool ready, moving;
    private float smoothSpeed;

    public static void Attach(GameObject instance, string resourcePath, Transform mover)
    {
        AnimationClip idle = null, run = null, chop = null;
        float runReference = 3.2f;

        PlayerVisual player = Object.FindAnyObjectByType<PlayerVisual>();
        DemonAnimationSet set = player != null ? player.animationSet : null;
        if (set != null)
        {
            idle = set.idle;
            run = set.run;
            runReference = Mathf.Max(0.5f, set.runReferenceSpeed);
            // chop zostáva null: sekanie drevorubačov rieši ChopAnimation
        }
        if (idle == null && run == null) idle = PickEmbeddedClip(resourcePath);
        if (idle == null && run == null) return;

        Animator animator = instance.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = instance.AddComponent<Animator>();
        animator.enabled = true;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;

        instance.AddComponent<ClipLoopAnimator>().Init(animator, idle, run, runReference, mover, chop);
    }

    static AnimationClip PickEmbeddedClip(string path)
    {
        AnimationClip first = null, idle = null, walk = null;
        var names = new System.Text.StringBuilder();
        foreach (AnimationClip c in Resources.LoadAll<AnimationClip>(path))
        {
            if (c == null || c.name.StartsWith("__preview__")) continue;
            names.Append(c.name).Append(" (").Append(c.length.ToString("0.0")).Append(" s), ");
            string n = c.name.ToLowerInvariant();
            if ((n.Contains("walk") || n.Contains("run")) && walk == null) walk = c;
            if (n.Contains("idle") && idle == null) idle = c;
            if (first == null) first = c;
        }
        Debug.Log($"[Animácia modelu] {path}: klipy v FBX = {(names.Length > 0 ? names.ToString() : "žiadne")}");
        return walk != null ? walk : idle != null ? idle : first;
    }

    void Init(Animator animator, AnimationClip idle, AnimationClip run, float runReference, Transform moveSource, AnimationClip chopClip)
    {
        mover = moveSource != null ? moveSource : transform;
        lastPosition = mover.position;
        runReferenceSpeed = runReference;

        graph = PlayableGraph.Create("ClipLoop_" + name);
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, chopClip != null ? 3 : 2);

        // Chýbajúci klip nahradí druhý (vstup mixu sa nikdy nenechá prázdny)
        if (idle == null) idle = run;
        if (run == null) run = idle;
        idlePlayable = AnimationClipPlayable.Create(graph, idle);
        runPlayable = idle == run ? idlePlayable : AnimationClipPlayable.Create(graph, run);
        idlePlayable.SetSpeed(0.0);   // čas sa posúva ručne
        runPlayable.SetSpeed(0.0);
        idleLength = Mathf.Max(0.1f, idle.length);
        runLength = Mathf.Max(0.1f, run.length);
        hasIdle = hasRun = true;

        graph.Connect(idlePlayable, 0, mixer, 0);
        if (idlePlayable.Equals(runPlayable))
        {
            // Jediný klip: druhý vstup dostane vlastnú kópiu, aby sa dal mixovať
            runPlayable = AnimationClipPlayable.Create(graph, run);
            runPlayable.SetSpeed(0.0);
        }
        graph.Connect(runPlayable, 0, mixer, 1);
        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);
        if (chopClip != null)
        {
            chopPlayable = AnimationClipPlayable.Create(graph, chopClip);
            chopPlayable.SetSpeed(0.0);
            chopLength = Mathf.Max(0.1f, chopClip.length);
            graph.Connect(chopPlayable, 0, mixer, 2);
            mixer.SetInputWeight(2, 0f);
            hasChop = true;
        }

        var output = AnimationPlayableOutput.Create(graph, "Anim", animator);
        output.SetSourcePlayable(mixer);
        graph.Play();
        ready = true;
    }

    void Update()
    {
        if (!ready || !hasIdle || !hasRun) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = mover.position - lastPosition;
        delta.y = 0f;
        lastPosition = mover.position;

        // Teleport (veľký skok) sa nepočíta ako beh; rýchlosť sa vyhladzuje, inak by zastavenie/rozbeh po snímkoch resetovalo animáciu (preskakovanie)
        float raw = delta.magnitude / dt;
        if (raw < 20f) smoothSpeed = Mathf.Lerp(smoothSpeed, raw, 1f - Mathf.Exp(-6f * dt));
        else smoothSpeed = 0f;
        if (moving) moving = smoothSpeed > 0.25f; else moving = smoothSpeed > 0.6f;   // hysteréza
        float speed = smoothSpeed;

        blend = Mathf.MoveTowards(blend, moving ? 1f : 0f, dt * 4f);
        chopWeight = Mathf.MoveTowards(chopWeight, hasChop && chopping ? 1f : 0f, dt * 5f);
        mixer.SetInputWeight(0, (1f - blend) * (1f - chopWeight));
        mixer.SetInputWeight(1, blend * (1f - chopWeight));
        if (hasChop)
        {
            mixer.SetInputWeight(2, chopWeight);
            if (chopWeight > 0.01f) { chopClock += dt; chopPlayable.SetTime(Mathf.Repeat(chopClock, chopLength)); }
            else chopClock = 0f;   // sekanie začína vždy od zamáchnutia
        }

        idleClock += dt;
        // Beh dobieha plynule aj pri dobiehaní do cieľa (čas sa nikdy nevracia späť)
        if (blend > 0.01f) runClock += dt * Mathf.Clamp(speed / runReferenceSpeed, 0.8f, 1.4f);
        idlePlayable.SetTime(Mathf.Repeat(idleClock, idleLength));
        runPlayable.SetTime(Mathf.Repeat(runClock, runLength));
    }

    // Klipy behu nesú posun bokov dopredu a pri zacyklení sa kosť vráti späť (postava "cúva" a ide cestu dvakrát): vodorovná poloha bokov sa drží pri modeli
    void LateUpdate()
    {
        if (!ready || noHips) return;
        if (hips == null)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name.EndsWith("Hips", System.StringComparison.OrdinalIgnoreCase)) { hips = t; break; }
            if (hips == null) { noHips = true; return; }
            hipsBase = transform.InverseTransformPoint(hips.position);
            return;
        }
        Vector3 local = transform.InverseTransformPoint(hips.position);
        hips.position = transform.TransformPoint(new Vector3(hipsBase.x, local.y, hipsBase.z));
    }

    private bool noHips;
    private AnimationClipPlayable chopPlayable;
    private float chopLength, chopClock, chopWeight;
    private bool hasChop, chopping;

    /// <summary>Zapne / vypne sekanie (klip útoku mečom v slučke); postava pri ňom stojí na mieste.</summary>
    public void SetChopping(bool on) { chopping = on; }
    private Transform hips;
    private Vector3 hipsBase;

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
