using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum SkillStat
{
    Damage, MaxHp, HpRegen, Armor, Crit, WeaponReach, AttackSpeed, Lifesteal, MoveSpeed, Stamina,
    DashCooldown, NeedsDrain, Nutrition, Gather, StealthRange, TakedownCooldown, Investigation, ExtraJump,
    Stagger, DemonHitArmor, DashStamina, RareLoot, CrouchSpeed, Leap
}

public enum SkillForm { Both, HumanOnly, DemonOnly }

/// <summary>
/// Strom schopností (Tab → Skilly). Stred = Krv Hybrida, okolo neho 6 vetiev po 5 schopností (hlavný reťazec I → II → III + dve doplnkové).
/// Odomyká sa po poradí: najprv rodič, potom minimálny level a body. Bonusy platia v oboch formách (človek aj démon), okrem
/// schopností označených „iba démon“. Všetky bonusy sa spočítajú tu (SkillTree.Sum) a PlayerStats / ostatné skripty si ich pýtajú.
/// </summary>
public static class SkillTree
{
    public struct Effect
    {
        public SkillStat stat;
        public float amount;
        public Effect(SkillStat stat, float amount) { this.stat = stat; this.amount = amount; }
    }

    public class Node
    {
        public string id, name, glyph, parent, branch;
        public int tier;          // 0 = stred, 1–3 = hlavný reťazec, 4–5 = doplnkové
        public int cost, minLevel;
        public SkillForm form;
        public Effect[] effects;
        public float angle, radius;   // poloha v strome (stupne od osi X, vzdialenosť v jednotkách)
        public Color color;
        public string story;
    }

    public static readonly List<Node> Nodes = new List<Node>();
    private static readonly Dictionary<string, Node> byId = new Dictionary<string, Node>();
    private static readonly HashSet<string> unlocked = new HashSet<string>();

    /// <summary>Hráč je práve v podobe démona (nastavuje PlayerStats).</summary>
    public static bool Demon;

    static SkillTree()
    {
        Color battle = new Color(0.95f, 0.35f, 0.3f), body = new Color(0.4f, 0.9f, 0.45f), move = new Color(0.35f, 0.75f, 1f);
        Color survive = new Color(1f, 0.75f, 0.3f), shadow = new Color(0.65f, 0.5f, 1f), demon = new Color(0.85f, 0.3f, 0.9f);
        const SkillForm B = SkillForm.Both, D = SkillForm.DemonOnly;

        Add(new Node { id = "hybrid", name = "Krv Hybrida", glyph = "HYB", parent = null, branch = "Stred", tier = 0, cost = 1, minLevel = 1, form = SkillForm.Both,
            effects = new[] { new Effect(SkillStat.MaxHp, 0.05f), new Effect(SkillStat.Damage, 0.03f) }, color = Color.white, angle = 0f, radius = 0f,
            story = "Základ všetkého. Jediný hybrid medzi ľuďmi a démonmi sa učí spájať obe krvi." });

        // Sloty: C1–C4 = hlavný reťazec, R1–R2 = pravá dráha, L0–L2 = ľavá dráha (každá vetva má 60° výseč)
        // ---- Boj (hore) ----
        N("dmg", "Boj", battle, 90f, "C1", null, new Spec("Poškodenie I", "PÚ1", 1, 2, B, S(SkillStat.Damage, 0.08f), "Údery sú tvrdšie."));
        N("dmg", "Boj", battle, 90f, "C2", "C1", new Spec("Poškodenie II", "PÚ2", 2, 8, B, S(SkillStat.Damage, 0.12f), "Naučíš sa vkladať váhu tela do útoku."));
        N("dmg", "Boj", battle, 90f, "C3", "C2", new Spec("Poškodenie III", "PÚ3", 5, 20, B, S(SkillStat.Damage, 0.18f), "Každý zásah je rozhodujúci."));
        N("dmg", "Boj", battle, 90f, "C4", "C3", new Spec("Brutálna sila", "BRU", 7, 26, B, S(SkillStat.Stagger, 0.20f), "Zásah ťa nepustí: nepriateľ môže byť vyrušený v polovici útoku."));
        N("dmg", "Boj", battle, 90f, "R1", "C1", new Spec("Kritický úder", "KRT", 2, 5, B, S(SkillStat.Crit, 0.10f), "Vidíš slabiny protivníka."));
        N("dmg", "Boj", battle, 90f, "R2", "R1", new Spec("Rýchle ruky", "RUK", 3, 12, B, S(SkillStat.AttackSpeed, 0.12f), "Zbraň aj päste sa vracajú do pohotovosti rýchlejšie."));
        N("dmg", "Boj", battle, 90f, "L1", "C2", new Spec("Dlhší dosah", "DOS", 3, 10, B, S(SkillStat.WeaponReach, 0.25f), "Zbraň ti sedí v ruke ako súčasť tela."));

        // ---- Vitalita (vpravo hore) ----
        N("hp", "Vitalita", body, 30f, "C1", null, new Spec("Vitalita I", "ŽV1", 1, 2, B, S(SkillStat.MaxHp, 0.10f), "Telo znesie viac."));
        N("hp", "Vitalita", body, 30f, "C2", "C1", new Spec("Vitalita II", "ŽV2", 2, 8, B, S(SkillStat.MaxHp, 0.15f), "Tvrdšie svaly, hrubšie kosti."));
        N("hp", "Vitalita", body, 30f, "C3", "C2", new Spec("Vitalita III", "ŽV3", 5, 20, B, S(SkillStat.MaxHp, 0.20f), "Zranenia, ktoré by iných zabili, ťa len spomalia."));
        N("hp", "Vitalita", body, 30f, "C4", "C3", new Spec("Zrod z popola", "POP", 8, 30, B, S(SkillStat.MaxHp, 0.25f), "Čo ťa nezabije, to ťa znova poskladá."));
        N("hp", "Vitalita", body, 30f, "R1", "C1", new Spec("Hojenie", "HOJ", 2, 5, B, S(SkillStat.HpRegen, 1.0f), "Rany sa zatvárajú dvakrát rýchlejšie (keď nie si hladný)."));
        N("hp", "Vitalita", body, 30f, "R2", "R1", new Spec("Liečivé teplo", "TEP", 3, 14, B, S(SkillStat.HpRegen, 0.75f), "Regenerácia sa ešte zrýchli."));
        N("hp", "Vitalita", body, 30f, "L1", "C2", new Spec("Hrubá koža", "KOŽ", 3, 10, B, S(SkillStat.Armor, 0.08f), "Menej poškodenia z každého zásahu."));
        N("hp", "Vitalita", body, 30f, "L2", "L1", new Spec("Nezlomnosť", "NEZ", 4, 16, B, S(SkillStat.DemonHitArmor, 0.10f), "Údery démonov ťa lámu menej."));

        // ---- Pohyb (vpravo dole) ----
        N("mov", "Pohyb", move, -30f, "C1", null, new Spec("Výdrž I", "VÝ1", 1, 2, B, S(SkillStat.Stamina, 0.15f), "Výdrž sa míňa pomalšie a vracia rýchlejšie."));
        N("mov", "Pohyb", move, -30f, "C2", "C1", new Spec("Výdrž II", "VÝ2", 2, 8, B, S(SkillStat.Stamina, 0.20f), "Dýchanie pod kontrolou."));
        N("mov", "Pohyb", move, -30f, "C3", "C2", new Spec("Výdrž III", "VÝ3", 5, 20, B, S2(SkillStat.Stamina, 0.25f, SkillStat.MoveSpeed, 0.06f), "Beh je ako chôdza."));
        N("mov", "Pohyb", move, -30f, "C4", "C3", new Spec("Nekonečný dych", "DYC", 8, 30, B, S(SkillStat.Stamina, 0.30f), "Pľúca, ktoré nepoznajú únavu."));
        N("mov", "Pohyb", move, -30f, "R1", "C1", new Spec("Dvojitý skok", "SKK", 2, 5, B, S(SkillStat.ExtraJump, 1f), "Druhý skok vo vzduchu."));
        N("mov", "Pohyb", move, -30f, "R2", "R1", new Spec("Taktický úskok", "ÚSK", 2, 10, B, S(SkillStat.DashStamina, 0.20f), "Úhyb spotrebuje o 20 % menej výdrže."));
        N("mov", "Pohyb", move, -30f, "L1", "C2", new Spec("Rýchly úhyb", "ÚHY", 3, 10, B, S(SkillStat.DashCooldown, 0.30f), "Úhyb je k dispozícii o 30 % skôr."));
        N("mov", "Pohyb", move, -30f, "L2", "L1", new Spec("Bežec", "BEŽ", 4, 16, B, S(SkillStat.MoveSpeed, 0.08f), "Rýchlejšie nohy."));

        // ---- Prežitie (dole) ----
        N("sur", "Prežitie", survive, -90f, "C1", null, new Spec("Úsporné telo I", "ÚS1", 1, 2, B, S(SkillStat.NeedsDrain, 0.10f), "Hlad (človek) aj krv (démon) ubúdajú pomalšie."));
        N("sur", "Prežitie", survive, -90f, "C2", "C1", new Spec("Úsporné telo II", "ÚS2", 2, 8, B, S(SkillStat.NeedsDrain, 0.12f), "Telo si šetrí zásoby."));
        N("sur", "Prežitie", survive, -90f, "C3", "C2", new Spec("Úsporné telo III", "ÚS3", 5, 20, B, S(SkillStat.NeedsDrain, 0.15f), "Vydržíš dni bez jedla."));
        N("sur", "Prežitie", survive, -90f, "C4", "C3", new Spec("Železná vôľa", "VÔĽ", 8, 30, B, S(SkillStat.NeedsDrain, 0.20f), "Hlad a smäd poslúchajú teba, nie opačne."));
        N("sur", "Prežitie", survive, -90f, "R1", "C1", new Spec("Výživa", "VÝŽ", 2, 5, B, S(SkillStat.Nutrition, 0.25f), "Jedlo a krv ťa nasýtia o štvrtinu viac."));
        N("sur", "Prežitie", survive, -90f, "R2", "R1", new Spec("Hladový predátor", "PRE", 4, 16, B, S(SkillStat.Nutrition, 0.25f), "Z každého sústa a každej kvapky vyťažíš ešte viac."));
        N("sur", "Prežitie", survive, -90f, "L0", "C1", new Spec("Kovkop", "KOV", 2, 6, B, S(SkillStat.RareLoot, 0.15f), "15 % šanca, že pri ťažbe a zbere dostaneš dvojnásobok (platí na všetky suroviny okrem zlata a čierneho kryštálu)."));
        N("sur", "Prežitie", survive, -90f, "L1", "C2", new Spec("Zberač", "ZBR", 3, 10, B, S(SkillStat.Gather, 0.20f), "Zo zberu a ťažby dostaneš o 20 % viac surovín."));
        N("sur", "Prežitie", survive, -90f, "L2", "L1", new Spec("Majster zberu", "MAJ", 4, 18, B, S(SkillStat.Gather, 0.25f), "Vieš, kde hľadať a ako rezať."));

        // ---- Tieň (vľavo dole) ----
        N("ste", "Tieň", shadow, -150f, "C1", null, new Spec("Tichý krok I", "TK1", 1, 2, B, S(SkillStat.StealthRange, 0.10f), "Strážcovia ťa vidia z menšej vzdialenosti."));
        N("ste", "Tieň", shadow, -150f, "C2", "C1", new Spec("Tichý krok II", "TK2", 2, 8, B, S(SkillStat.StealthRange, 0.12f), "Pohyb bez zvuku."));
        N("ste", "Tieň", shadow, -150f, "C3", "C2", new Spec("Tichý krok III", "TK3", 5, 20, B, S(SkillStat.StealthRange, 0.15f), "Si skoro neviditeľný."));
        N("ste", "Tieň", shadow, -150f, "C4", "C3", new Spec("Majster tieňov", "TIE", 8, 30, B, S(SkillStat.StealthRange, 0.20f), "Tma je tvoj plášť."));
        N("ste", "Tieň", shadow, -150f, "R1", "C1", new Spec("Bystrý zrak", "ZRK", 2, 5, B, S(SkillStat.Investigation, 0.25f), "Skúmanie postáv (T) trvá o štvrtinu kratšie."));
        N("ste", "Tieň", shadow, -150f, "R2", "R1", new Spec("Detektívny inštinkt", "DET", 4, 16, B, S(SkillStat.Investigation, 0.25f), "Pozeráš sa na ľudí a vidíš, čo skrývajú."));
        N("ste", "Tieň", shadow, -150f, "L1", "C2", new Spec("Rýchly vrah", "VRH", 3, 10, B, S(SkillStat.TakedownCooldown, 0.40f), "Tiché zabitie je znova dostupné o 40 % skôr."));
        N("ste", "Tieň", shadow, -150f, "L2", "L1", new Spec("Tieňový prízrak", "PRÍ", 4, 16, B, S(SkillStat.CrouchSpeed, 0.20f), "V čupe sa pohybuješ o 20 % rýchlejšie."));

        // ---- Démon (vľavo hore) ----
        N("dem", "Démon", demon, 150f, "C1", null, new Spec("Pazúry I", "PA1", 1, 2, D, S(SkillStat.Damage, 0.10f), "Iba v podobe démona: pazúry sú ostrejšie."));
        N("dem", "Démon", demon, 150f, "C2", "C1", new Spec("Pazúry II", "PA2", 2, 8, D, S(SkillStat.Damage, 0.15f), "Iba v podobe démona: pazúry režú aj pancier."));
        N("dem", "Démon", demon, 150f, "C3", "C2", new Spec("Pazúry III", "PA3", 5, 20, D, S(SkillStat.Damage, 0.20f), "Iba v podobe démona: nezastaviteľná sila."));
        N("dem", "Démon", demon, 150f, "C4", "C3", new Spec("Tvrdá čierna koža", "ČKO", 7, 26, D, S(SkillStat.Armor, 0.15f), "Iba v podobe démona: −15 % prijatého poškodenia."));
        N("dem", "Démon", demon, 150f, "R1", "C1", new Spec("Krvavé upíjanie", "UPÍ", 2, 5, D, S(SkillStat.Lifesteal, 0.06f), "Iba v podobe démona: 6 % spôsobeného poškodenia sa ti vráti ako zdravie."));
        N("dem", "Démon", demon, 150f, "R2", "R1", new Spec("Krvavý hlad", "HLA", 4, 16, D, S(SkillStat.Lifesteal, 0.08f), "Iba v podobe démona: ďalších 8 % poškodenia sa vráti ako zdravie."));
        N("dem", "Démon", demon, 150f, "L0", "C1", new Spec("Dravý skok", "SKÔ", 2, 6, D, S(SkillStat.Leap, 1f), "Iba v podobe démona: rýchly výpad dopredu na nepriateľa (kláves H, prepisovateľný v nastaveniach, čakanie 6 s)."));
        N("dem", "Démon", demon, 150f, "L1", "C2", new Spec("Zúrivosť", "ZÚR", 3, 10, D, S(SkillStat.AttackSpeed, 0.10f), "Iba v podobe démona: útočíš o 10 % rýchlejšie."));
        N("dem", "Démon", demon, 150f, "L2", "L1", new Spec("Krvavá smršť", "SMR", 4, 18, D, S(SkillStat.AttackSpeed, 0.15f), "Iba v podobe démona: útoky sa zrýchlia ešte o 15 %."));
    }

    // ---------- Skladanie dát ----------
    struct Spec
    {
        public string name, glyph, story;
        public int cost, minLevel;
        public SkillForm form;
        public Effect[] effects;
        public Spec(string name, string glyph, int cost, int minLevel, SkillForm form, Effect[] effects, string story)
        { this.name = name; this.glyph = glyph; this.cost = cost; this.minLevel = minLevel; this.form = form; this.effects = effects; this.story = story; }
    }

    static Effect[] S(SkillStat stat, float amount) => new[] { new Effect(stat, amount) };
    static Effect[] S2(SkillStat a, float x, SkillStat b, float y) => new[] { new Effect(a, x), new Effect(b, y) };

    // Uzol vo vetve: slot určuje polohu (C1–C4 hlavný reťazec v smere vetvy, R = pravá dráha, L = ľavá dráha), rodič je slot alebo stred
    static void N(string key, string branch, Color color, float baseAngle, string slot, string parentSlot, Spec s)
    {
        float angle = baseAngle, radius;
        int tier = 4;
        switch (slot)
        {
            case "C1": radius = 1.05f; tier = 1; break;
            case "C2": radius = 2.05f; tier = 2; break;
            case "C3": radius = 3.05f; tier = 3; break;
            case "C4": radius = 4.05f; break;
            case "R1": angle += 19f; radius = 2.4f; break;
            case "R2": angle += 19f; radius = 3.4f; break;
            case "L0": angle -= 28f; radius = 1.75f; break;
            case "L1": angle -= 19f; radius = 2.4f; break;
            default: angle -= 19f; radius = 3.4f; break;
        }
        Add(new Node { id = key + slot, name = s.name, glyph = s.glyph, parent = parentSlot == null ? "hybrid" : key + parentSlot, branch = branch, tier = tier,
            cost = s.cost, minLevel = s.minLevel, form = s.form, effects = s.effects, color = color, angle = angle, radius = radius, story = s.story });
    }

    static void Add(Node n)
    {
        Nodes.Add(n);
        byId[n.id] = n;
    }

    public static Node Get(string id) => id != null && byId.TryGetValue(id, out Node n) ? n : null;
    public static bool Has(string id) => unlocked.Contains(id);

    // ---------- Odomykanie ----------
    public enum Reason { Ok, AlreadyUnlocked, NeedParent, NeedLevel, NeedPoints }

    public static Reason CanUnlock(string id, int level, int points)
    {
        Node n = Get(id);
        if (n == null) return Reason.AlreadyUnlocked;
        if (unlocked.Contains(id)) return Reason.AlreadyUnlocked;
        if (n.parent != null && !unlocked.Contains(n.parent)) return Reason.NeedParent;
        if (level < n.minLevel) return Reason.NeedLevel;
        if (points < n.cost) return Reason.NeedPoints;
        return Reason.Ok;
    }

    public static void Unlock(string id) => unlocked.Add(id);

    /// <summary>Koľko bodov je v odomknutých schopnostiach (pri „znova rozdeliť“ sa vrátia).</summary>
    public static int SpentPoints()
    {
        int sum = 0;
        foreach (string id in unlocked) { Node n = Get(id); if (n != null) sum += n.cost; }
        return sum;
    }

    public static void Clear() => unlocked.Clear();
    public static string Export() => string.Join(",", unlocked);

    public static void Import(string csv)
    {
        unlocked.Clear();
        if (string.IsNullOrEmpty(csv)) return;
        foreach (string id in csv.Split(','))
            if (byId.ContainsKey(id)) unlocked.Add(id);
    }
    public static int UnlockedCount => unlocked.Count;

    // ---------- Bonusy ----------
    static bool Applies(Node n) => n.form == SkillForm.Both || (n.form == SkillForm.DemonOnly && Demon) || (n.form == SkillForm.HumanOnly && !Demon);

    /// <summary>Súčet bonusov daného statu zo všetkých odomknutých schopností, ktoré platia v aktuálnej forme.</summary>
    public static float Sum(SkillStat stat)
    {
        float sum = 0f;
        sum += Equipment.Bonus(stat);
        foreach (string id in unlocked)
        {
            Node n = Get(id);
            if (n == null || !Applies(n)) continue;
            foreach (Effect e in n.effects) if (e.stat == stat) sum += e.amount;
        }
        return sum;
    }

    /// <summary>Súčet daného statu bez ohľadu na formu (do popisov: „spolu máš“).</summary>
    public static float SumAnyForm(SkillStat stat)
    {
        float sum = 0f;
        foreach (string id in unlocked)
        {
            Node n = Get(id);
            if (n == null) continue;
            foreach (Effect e in n.effects) if (e.stat == stat) sum += e.amount;
        }
        return sum;
    }

    public static float InvestigationFactor => Mathf.Clamp(1f - Sum(SkillStat.Investigation), 0.3f, 1f);
    public static float ArmorFactor => Mathf.Clamp(1f - Sum(SkillStat.Armor), 0.3f, 1f);

    // ---------- Popisy ----------
    public static string StatName(SkillStat s)
    {
        switch (s)
        {
            case SkillStat.Damage: return "poškodenie";
            case SkillStat.MaxHp: return "maximálne zdravie";
            case SkillStat.HpRegen: return "regenerácia zdravia";
            case SkillStat.Armor: return "menej prijatého poškodenia";
            case SkillStat.Crit: return "šanca na kritický zásah";
            case SkillStat.WeaponReach: return "dosah zbrane";
            case SkillStat.AttackSpeed: return "rýchlosť útokov";
            case SkillStat.Lifesteal: return "vrátenie poškodenia ako zdravie";
            case SkillStat.MoveSpeed: return "rýchlosť pohybu";
            case SkillStat.Stamina: return "efektivita výdrže (míňanie aj obnova)";
            case SkillStat.DashCooldown: return "kratšie čakanie na úhyb";
            case SkillStat.NeedsDrain: return "pomalší úbytok hladu / krvi";
            case SkillStat.Nutrition: return "viac sýtosti z jedla a krvi";
            case SkillStat.Gather: return "viac surovín zo zberu a ťažby";
            case SkillStat.StealthRange: return "menší dosah, z ktorého ťa zbadajú";
            case SkillStat.TakedownCooldown: return "kratšie čakanie na tiché zabitie";
            case SkillStat.Investigation: return "kratšie skúmanie postáv";
            case SkillStat.ExtraJump: return "skok navyše vo vzduchu";
            case SkillStat.Stagger: return "šanca prerušiť útok nepriateľa";
            case SkillStat.DemonHitArmor: return "menej poškodenia od démonov";
            case SkillStat.DashStamina: return "menej výdrže na úhyb";
            case SkillStat.RareLoot: return "šanca na dvojnásobný úlovok pri ťažbe";
            case SkillStat.CrouchSpeed: return "rýchlosť pohybu v čupe";
            case SkillStat.Leap: return "dravý skok";
            default: return s.ToString();
        }
    }

    public static string EffectText(Effect e)
    {
        if (e.stat == SkillStat.ExtraJump) return "+1 skok navyše vo vzduchu";
        if (e.stat == SkillStat.Leap) return "Odomkne dravý skok (kláves H)";
        return $"+{e.amount * 100f:0} % {StatName(e.stat)}";
    }

    public static string FormText(SkillForm f) => f == SkillForm.Both ? "Platí v oboch formách (človek aj démon)" : f == SkillForm.DemonOnly ? "Platí iba v podobe démona" : "Platí iba v ľudskej podobe";

    public static string Roman(int tier) => tier == 1 ? "I" : tier == 2 ? "II" : tier == 3 ? "III" : "";

    /// <summary>Zhrnutie všetkých aktívnych bonusov (bez ohľadu na formu, s poznámkou, ak platia len v jednej).</summary>
    public static string Summary()
    {
        var sb = new StringBuilder();
        var seen = new HashSet<SkillStat>();
        foreach (Node n in Nodes)
        {
            if (!unlocked.Contains(n.id)) continue;
            foreach (Effect e in n.effects)
            {
                if (!seen.Add(e.stat)) continue;
                float total = SumAnyForm(e.stat);
                string line = e.stat == SkillStat.ExtraJump ? "+1 skok navyše" : e.stat == SkillStat.Leap ? "Dravý skok (H)" : $"+{total * 100f:0} % {StatName(e.stat)}";
                if (n.form == SkillForm.DemonOnly) line += " (čiastočne iba démon)";
                sb.AppendLine(line);
            }
        }
        return sb.Length == 0 ? "Zatiaľ žiadne schopnosti." : sb.ToString().TrimEnd();
    }

    // ---------- Ukladanie ----------
    const string PrefKey = "POA.SkillNodes";

    public static void Save()
    {
        PlayerPrefs.SetString(PrefKey, string.Join(",", unlocked));
    }

    /// <summary>Načíta odomknuté schopnosti; staré uložené 4 schopnosti (poškodenie, vyšetrovanie, skok, dosah) sa prevedú na nové uzly.</summary>
    public static void Load()
    {
        unlocked.Clear();
        if (PlayerPrefs.HasKey(PrefKey))
        {
            foreach (string id in PlayerPrefs.GetString(PrefKey).Split(','))
                if (byId.ContainsKey(id)) unlocked.Add(id);
            return;
        }

        // Migrácia zo starého stromu
        bool any = false;
        if (PlayerPrefs.GetInt("POA.SkillDamage", 0) != 0) { UnlockWithParents("dmgC1"); any = true; }
        if (PlayerPrefs.GetInt("POA.SkillInvestigation", 0) != 0) { UnlockWithParents("ster1"); any = true; }
        if (PlayerPrefs.GetInt("POA.SkillDoubleJump", 0) != 0) { UnlockWithParents("movr1"); any = true; }
        if (PlayerPrefs.GetInt("POA.SkillReach", 0) != 0) { UnlockWithParents("dmgl1"); any = true; }
        if (any) Save();
    }

    static void UnlockWithParents(string id)
    {
        for (Node n = Get(id); n != null; n = Get(n.parent)) unlocked.Add(n.id);
    }
}
