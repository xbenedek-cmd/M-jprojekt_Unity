using System;
using System.Collections.Generic;
using UnityEngine;

[Flags]
public enum DemonSign { None = 0, Reflection = 1, Pupil = 2, Veins = 4, Animals = 8, Teeth = 16, Body = 32 }

/// <summary>
/// Znaky démona (Demon Marks) na maskovanom NPC: bez odrazu, oči (zrenička), neprirodzené žily,
/// vrčiace zvieratá, zuby (tesáky) a postava (anomálie). Oči, zuby, žily a postava sa dajú
/// zistiť pohľadom zblízka, odraz a zvieratá potrebujú zrkadlo / zviera nablízku. Hráč ich zisťuje pomocou DetectiveSystem. Civilista môže mať aj "falošný"
/// znak (napr. chorý s tmavými žilami), ale nikdy ich nemá dosť na odhalenie.
/// </summary>
public class DemonMarks : MonoBehaviour
{
    [Header("Znaky (čo tento NPC naozaj má)")]
    public bool noReflection = true;
    public bool pupilFlash = true;
    public bool veins = true;
    public bool animalsFear = true;
    [Tooltip("Zuby / tesáky (vidno zblízka). Nové znaky sú predvolene vypnuté - zapni ich len démonom.")]
    public bool teeth = false;
    [Tooltip("Anomálie postavy: nezvyčajné proporcie, dlhé končatiny (vidno zblízka)")]
    public bool bodyAnomaly = false;

    [Header("Nastavenia")]
    [Tooltip("Koľko rôznych znakov treba zistiť na odhalenie")]
    public int signsNeeded = 2;
    public Vector2 flashInterval = new Vector2(4f, 9f);
    public float flashDuration = 0.35f;
    public float veinsVisibleDistance = 3.5f;
    public float growlRange = 7f;

    public DemonSign Discovered { get; private set; }
    public bool ReflectionOk { get; private set; }     // odraz preverený a v poriadku
    public bool Resolved { get; set; }                 // hráč sa už rozhodol, čo s ním

    public bool IsFlashing => Time.time < flashUntil;
    public bool IsGrowlingNow => Time.time - lastGrowl < 3f;
    public bool Identified => DiscoveredCount >= signsNeeded;

    public int DiscoveredCount
    {
        get
        {
            int n = 0;
            foreach (DemonSign s in new[] { DemonSign.Reflection, DemonSign.Pupil, DemonSign.Veins, DemonSign.Animals, DemonSign.Teeth, DemonSign.Body })
                if ((Discovered & s) != 0) n++;
            return n;
        }
    }

    private Damageable dmg;
    private NPCPerception perception;
    private PlayerStats player;
    private GameObject eyes;
    private GameObject veinRoot;
    private float flashUntil;
    private float nextFlash;
    private float lastGrowl = -99f;
    private float nextGrowl;

    static readonly List<Transform> animals = new List<Transform>();
    static float nextAnimalRefresh;

    // ---------- Pomocné pre ostatné skripty ----------
    public bool Has(DemonSign s)
    {
        switch (s)
        {
            case DemonSign.Reflection: return noReflection;
            case DemonSign.Pupil: return pupilFlash;
            case DemonSign.Veins: return veins;
            case DemonSign.Animals: return animalsFear;
            case DemonSign.Teeth: return teeth;
            case DemonSign.Body: return bodyAnomaly;
            default: return false;
        }
    }

    public bool IsDiscovered(DemonSign s) => (Discovered & s) != 0;

    /// <summary>Zapíše znak. Vráti true, ak je to nový objav.</summary>
    public bool Discover(DemonSign s)
    {
        if (IsDiscovered(s)) return false;
        Discovered |= s;
        return true;
    }

    public void MarkReflectionOk() => ReflectionOk = true;

    public string Summary()
    {
        var parts = new List<string>();
        if (IsDiscovered(DemonSign.Veins)) parts.Add("neprirodzené žily");
        if (IsDiscovered(DemonSign.Pupil)) parts.Add("zmenené oči");
        if (IsDiscovered(DemonSign.Teeth)) parts.Add("tesáky");
        if (IsDiscovered(DemonSign.Body)) parts.Add("anomália postavy");
        if (IsDiscovered(DemonSign.Reflection)) parts.Add("bez odrazu");
        if (IsDiscovered(DemonSign.Animals)) parts.Add("zvieratá vrčia");
        if (ReflectionOk) parts.Add("odraz v poriadku");
        return parts.Count == 0 ? "" : string.Join(", ", parts.ToArray());
    }

    public static string WeaknessName(DamageKind k)
    {
        switch (k)
        {
            case DamageKind.Fire: return "oheň (Klan Popola)";
            case DamageKind.Lightning: return "blesk (Klan Blesku)";
            case DamageKind.Earth: return "zem a kameň (Klan Divočiny)";
            case DamageKind.Blood: return "krvavá túžba (Klan Pekelnej Krvi)";
            case DamageKind.Shadow: return "útok zo tieňa (Klan Tieňa)";
            case DamageKind.Holy: return "posvätená čepeľ";
            default: return "žiadna známa";
        }
    }

    public static Color WeaknessColor(DamageKind k)
    {
        switch (k)
        {
            case DamageKind.Fire: return new Color(1f, 0.45f, 0.05f);
            case DamageKind.Lightning: return new Color(0.4f, 0.8f, 1f);
            case DamageKind.Earth: return new Color(0.3f, 0.8f, 0.3f);
            case DamageKind.Blood: return new Color(0.7f, 0f, 0.1f);
            case DamageKind.Shadow: return new Color(0.6f, 0.3f, 0.9f);
            case DamageKind.Holy: return new Color(1f, 0.95f, 0.6f);
            default: return new Color(0.5f, 0.15f, 0.7f);
        }
    }

    public static bool AnimalNear(Vector3 pos, float range)
    {
        RefreshAnimals();
        foreach (Transform a in animals)
            if (a != null && Vector3.Distance(a.position, pos) <= range) return true;
        return false;
    }

    static void RefreshAnimals()
    {
        if (Time.time < nextAnimalRefresh) return;
        nextAnimalRefresh = Time.time + 1f;

        animals.Clear();
        foreach (BloodSource b in FindObjectsByType<BloodSource>())
            if (b != null && !b.IsDead && b.GetComponent<NPCPerception>() == null)
                animals.Add(b.transform);
    }

    // ---------- Život komponentu ----------
    void Start()
    {
        dmg = GetComponent<Damageable>();
        perception = GetComponent<NPCPerception>();
        player = FindAnyObjectByType<PlayerStats>();

        nextFlash = Time.time + UnityEngine.Random.Range(flashInterval.x, flashInterval.y);
        BuildVisuals();
    }

    void BuildVisuals()
    {
        // Blesk zreničky: malá žiarivá guľa pri očiach
        eyes = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(eyes.GetComponent<Collider>());
        eyes.name = "DemonEyes";
        eyes.transform.SetParent(transform, false);
        eyes.transform.localPosition = new Vector3(0f, 0.65f, 0.38f);
        eyes.transform.localScale = new Vector3(0.28f, 0.12f, 0.12f);
        eyes.GetComponent<Renderer>().material.color = new Color(1f, 0.1f, 0.1f);
        eyes.SetActive(false);

        // Žily: tenké pásy, ich farba prezrádza slabinu
        DamageKind weak = dmg != null ? dmg.weakness : DamageKind.Normal;
        Color vc = WeaknessColor(weak);

        veinRoot = new GameObject("DemonVeins");
        veinRoot.transform.SetParent(transform, false);

        Vector3[] pos = { new Vector3(-0.2f, 0.2f, 0.42f), new Vector3(0.16f, 0.05f, 0.44f), new Vector3(0f, 0.42f, 0.44f) };
        float[] tilt = { 18f, -22f, 4f };

        for (int i = 0; i < pos.Length; i++)
        {
            GameObject v = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(v.GetComponent<Collider>());
            v.name = "Vein";
            v.transform.SetParent(veinRoot.transform, false);
            v.transform.localPosition = pos[i];
            v.transform.localRotation = Quaternion.Euler(0f, 0f, tilt[i]);
            v.transform.localScale = new Vector3(0.05f, 0.45f, 0.05f);
            v.GetComponent<Renderer>().material.color = vc;
        }
        veinRoot.SetActive(false);
    }

    void Update()
    {
        if (player == null) return;

        bool hidden = perception != null && perception.ShowsMonster;   // monštrum nepotrebuje znaky
        float dp = Vector3.Distance(player.transform.position, transform.position);

        // Zrenička občas bleskne
        if (pupilFlash && Time.time >= nextFlash)
        {
            flashUntil = Time.time + flashDuration;
            nextFlash = Time.time + UnityEngine.Random.Range(flashInterval.x, flashInterval.y);
        }
        if (eyes != null) eyes.SetActive(pupilFlash && IsFlashing && !hidden && dp <= 12f);

        // Žily vidno zblízka
        if (veinRoot != null) veinRoot.SetActive(veins && !hidden && dp <= veinsVisibleDistance);

        // Zvieratá vrčia
        if (animalsFear && !hidden && dp <= 15f && Time.time >= nextGrowl)
        {
            RefreshAnimals();
            foreach (Transform a in animals)
            {
                if (a == null || Vector3.Distance(a.position, transform.position) > growlRange) continue;

                lastGrowl = Time.time;
                nextGrowl = Time.time + 2.5f;
                GameHUD.WorldText(a.position + Vector3.up * 1.2f, "Grrr...", new Color(1f, 0.55f, 0.3f));
                break;
            }
        }
    }
}