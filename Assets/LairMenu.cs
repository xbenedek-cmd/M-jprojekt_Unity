using UnityEngine;

/// <summary>Menu na F7: mutácie, príšery a totemy, tajná skrýša s maskovacou dielňou, infiltrátori.</summary>
public class LairMenu : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    private PlayerStats player;
    private int tab;
    private string status;
    private GUIStyle rich;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => MenuOpen = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<LairMenu>() != null) return;
        new GameObject("LairMenu").AddComponent<LairMenu>();
    }

    void Update()
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.IsDead || PauseMenu.AtStartup || PauseMenu.IsOpen) { if (MenuOpen) Close(); return; }

        if (MenuOpen)
        {
            if (GameInput.GetKeyDown(KeyCode.Escape) || GameInput.GetKeyDown(KeyBindings.Get("lair", KeyCode.F7))) Close();
            return;
        }
        if (MangaDialog.Active || SoulMenu.MenuOpen || GameHUD.MenuOpen || Inventory.MenuOpen || WanderingMerchant.MenuOpen || PlotManager.MenuOpen) return;

        if (GameInput.GetKeyDown(KeyBindings.Get("lair", KeyCode.F7)))
        {
            MenuOpen = true;
            status = null;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Close()
    {
        if (!MenuOpen) return;
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable() => Close();

    void OnGUI()
    {
        if (!MenuOpen || player == null) return;
        if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

        float h = Mathf.Min(640f, Screen.height - 20f);
        Rect box = new Rect((Screen.width - 760f) * 0.5f, (Screen.height - h) * 0.5f, 760f, h);
        GUI.color = new Color(0.04f, 0.03f, 0.05f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, box.height - 28f));
        GUILayout.Label("<size=22><b>TEMNÁ MOC A SKRÝŠA</b></size>", rich);
        tab = GUILayout.Toolbar(tab, new[] { "Mutácie", "Príšery a totemy", "Skrýša", "Infiltrátori" }, GUILayout.Height(28f));
        GUILayout.Space(6f);

        if (tab == 0) DrawMutations();
        else if (tab == 1) DrawBeasts();
        else if (tab == 2) DrawHideout();
        else DrawInfiltrators();

        GUILayout.FlexibleSpace();
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, rich);
        GUILayout.Label("[F7] / Esc zavrie");
        GUILayout.EndArea();
    }

    void DrawMutations()
    {
        GUILayout.Label($"Démonická úroveň: <b>{player.demonLevel}</b> (mutácie od úrovne {Lair.MinLevel}). Voľné body: <b>{Lair.PointsFree(player)}</b> z {Lair.PointsTotal(player)}.", rich);
        GUILayout.Space(4f);

        for (int i = 0; i < Lair.Names.Length; i++)
        {
            var m = (Mutation)i;
            string blocked = Lair.Blocked(m, player);
            bool owned = Lair.Has(m);
            string label = owned ? $"✔ {Lair.Names[i]}" : blocked != null ? $"{Lair.Names[i]}  ({blocked})" : $"{Lair.Names[i]}  — získať ({Lair.Cost(m)})";

            GUI.enabled = blocked == null;
            if (GUILayout.Button(label, GUILayout.Height(28f)))
                status = Lair.Buy(m, player) ? $"Mutácia získaná: {Lair.Names[i]}." : "Nedá sa.";
            GUI.enabled = true;
            GUILayout.Label(Lair.Descriptions[i], rich);
        }
    }

    void DrawBeasts()
    {
        GUILayout.Label($"Zotročené zvieratá: <b>{Lair.Beasts.Count}</b>/{Lair.MaxBeasts}   Totemy: <b>{Lair.Totems.Count}</b>/{Lair.MaxTotems}", rich);
        GUILayout.Space(4f);

        GUI.enabled = Lair.Has(Mutation.Convert) && player.IsDemon;
        if (GUILayout.Button("Premeň najbližšieho civilistu (4 m, 30 krvi)", GUILayout.Height(30f))) status = Lair.ConvertCivilian(player);
        GUI.enabled = Lair.Has(Mutation.Enslave) && player.IsDemon;
        if (GUILayout.Button("Zotroč najbližšie zviera (8 m, 20 krvi)", GUILayout.Height(30f))) status = Lair.EnslaveBeast(player);
        GUI.enabled = Lair.Has(Mutation.Totems);
        if (GUILayout.Button("Postav totem tu (50 peňazí, 3 kryštály, 10 kameňov)", GUILayout.Height(30f))) status = Lair.PlaceTotem(player);
        GUI.enabled = true;

        GUILayout.Space(6f);
        GUILayout.Label("Totem v okruhu 25 m lieči démona a dopĺňa krv. Po čase k nemu prídu Chytači príšer: vojaci, ktorí totem ničia. Zotročené zvieratá útočia na nepriateľských démonov, ktorí ťa prenasledujú.", rich);
        foreach (Totem t in Lair.Totems)
            if (t != null) GUILayout.Label($"• Totem, {Vector3.Distance(player.transform.position, t.transform.position):0} m");
        if (!Lair.Has(Mutation.Convert) && !Lair.Has(Mutation.Enslave) && !Lair.Has(Mutation.Totems))
            GUILayout.Label("Najprv získaj mutácie v karte Mutácie.");
    }

    void DrawHideout()
    {
        if (!Lair.HideoutBuilt)
        {
            GUILayout.Label("Tajná skrýša stojí mimo mesta (aspoň 120 m od parciel). Je v nej maskovacia dielňa: parfumy, falošné doklady, uniformy.", rich);
            if (GUILayout.Button("Zriadiť skrýšu tu (300 peňazí, 15 kameňov)", GUILayout.Height(32f))) status = Lair.BuildHideout(player);
            return;
        }

        bool near = Lair.NearHideout(player.transform.position);
        GUILayout.Label(near ? "Si v skrýši, dielňa je k dispozícii." : $"Skrýša je {Vector3.Distance(player.transform.position, Lair.HideoutPos):0} m odtiaľto. Dielňa funguje len v nej.", rich);
        GUILayout.Space(6f);

        GUI.enabled = near;
        if (GUILayout.Button($"Parfum (60 peňazí, 1 kryštál)   {Left(Lair.PerfumeLeft)}", GUILayout.Height(30f))) status = Lair.Craft("perfume", player);
        GUILayout.Label("Zakryje démonický pach: civilisti sa ťa nebojú, ani keď ťa ľudia odmietajú.", rich);
        if (GUILayout.Button($"Falošné doklady (120 peňazí, 5 šrotu)   {Left(Lair.DocsLeft)}", GUILayout.Height(30f))) status = Lair.Craft("docs", player);
        GUILayout.Label("Démon vo vojenskej zóne nevyvolá podozrenie.", rich);
        if (GUILayout.Button($"Uniforma (200 peňazí, 10 šrotu)   {Left(Lair.UniformLeft)}", GUILayout.Height(30f))) status = Lair.Craft("uniform", player);
        GUILayout.Label("Vojaci ti veria, odhalenie rastie len 40 % rýchlosťou a vojenská zóna ťa neprezradí.", rich);
        GUI.enabled = true;
    }

    static string Left(float seconds) => seconds > 0f ? $"[aktívne {Mathf.CeilToInt(seconds / 60f)} min]" : "";

    void DrawInfiltrators()
    {
        int alive = 0, revealed = 0;
        foreach (Infiltrator i in Infiltrator.All)
        {
            if (i == null || !i.Alive) continue;
            alive++;
            if (i.Revealed) revealed++;
        }
        GUILayout.Label($"V meste sa skrývajú vyšší démoni v ľudskej maske. Živí: <b>{alive}</b>, odhalení: <b>{revealed}</b>, zabití: <b>{Lair.InfiltratorsKilled}</b>.", rich);
        GUILayout.Label("Cez deň pôsobia ako civilisti. Neodhalený infiltrátor denne znižuje spokojnosť mesta o 4 a občas zmizne civilista. Odhaľ ho detektívom (T) podľa znakov: žily, oči, tesáky, postava, chýbajúci odraz a vrčiace zvieratá. Jeho zabitie dá +5 spokojnosti a 100 peňazí.", rich);
        foreach (Infiltrator i in Infiltrator.All)
            if (i != null && i.Alive && i.Revealed)
                GUILayout.Label($"• odhalený infiltrátor, {Vector3.Distance(player.transform.position, i.transform.position):0} m");
    }
}
