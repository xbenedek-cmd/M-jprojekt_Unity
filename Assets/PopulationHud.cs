using UnityEngine;

/// <summary>
/// Malý panel vpravo hore: počty civilistov, odboja a vojakov, utečenci, čas do rastu populácie, tlak na civilistov,
/// stabilita portálu, povolania a stromy temnoty. Kláves F2 ho zapína / vypína.
/// </summary>
public class PopulationHud : MonoBehaviour
{
    private KeyCode toggleKey => KeyBindings.Get("popHud", KeyCode.F2);
    [SerializeField] private bool visible = true;

    private DayNightCycle clock;
    private GUIStyle label, title;

    void Update()
    {
        if (GameInput.GetKeyDown(toggleKey)) visible = !visible;
        if (clock == null && Time.frameCount % 60 == 0) clock = FindAnyObjectByType<DayNightCycle>();
    }

    void OnGUI()
    {
        if (Inventory.MenuOpen) return;   // okno inventára prekrýva celú obrazovku
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!visible || JailManager.IsInJail) return;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            label.normal.textColor = Color.white;
            title = new GUIStyle(label) { fontStyle = FontStyle.Bold, fontSize = 14 };
        }

        int day = clock != null ? clock.Day : 1;
        var lines = new System.Collections.Generic.List<string>();

        if (CityPopulation.HasZones)
        {
            lines.Add($"Civilisti <b>{CityPopulation.Count(PopulationGroup.Civilians)}</b>   Odboj <b>{CityPopulation.Count(PopulationGroup.Rebels)}</b>   Vojaci <b>{CityPopulation.Count(PopulationGroup.Soldiers)}</b>");
            int growth = CityPopulation.DaysToGrowth(day);
            if (growth > 0) lines.Add($"Rast o {growth} d   Utečenci {PopulationPressure.Refugees}");
        }
        else lines.Add($"Civilisti {CityPopulation.AliveCivilians()} (pool zón nevytvorený)");

        float pressure = PopulationPressure.CurrentPressure;
        lines.Add($"Tlak na civilistov {pressure * 100f:0} %{(pressure >= 0.5f ? " <color=#ff6a5a>(utekajú)</color>" : "")}");

        if (PortalGate.All.Count > 0)
            lines.Add($"Portál {PortalStability.Stability:0} %{(PortalStability.Outage ? " <color=#ff6a5a>(výpadok)</color>" : "")}");

        string professions = "";
        foreach (Profession p in System.Enum.GetValues(typeof(Profession)))
            if (p != Profession.None && ProfessionTrust.Get(p) > 0f) professions += $"{ProfessionTrust.Name(p)} {ProfessionTrust.Get(p):0}  ";
        if (professions.Length > 0) lines.Add($"Dôvera: {professions}");

        lines.Add($"Ľudskosť <b>{SoulBalance.Humanity:0}</b> ({SoulBalance.Ending}){(SoulBalance.Rejected ? " <color=#ff6a5a>(odmietnutý)</color>" : "")}   Počasie: {WeatherSystem.Label}");
        lines.Add($"Mesto: spokojnosť <b>{CityManagement.Satisfaction:0}</b>{(CityManagement.Protesting ? " <color=#ff6a5a>(protesty)</color>" : "")}{(CityManagement.CivilWarDays > 0 ? " <color=#ff3030>(vojna)</color>" : "")}   Konvoj: {(ConvoySystem.CheapActive ? "na ceste" : ConvoySystem.EscortActive ? "eskorta" : $"deň {ConvoySystem.NextOfferDay}")}");
        if (Story.Act >= 1 && Story.Ending == null)
        {
            string pathName = Story.Path == StoryPath.Human ? "Človek" : Story.Path == StoryPath.Demon ? "Démon" : Story.Path == StoryPath.Balance ? "Rovnováha" : "nevybraná";
            lines.Add($"Príbeh: akt {Story.Act}, cesta <b>{pathName}</b>");
            if (Story.Act == 2 && Story.Path != StoryPath.None)
                foreach (var goal in StoryDirector.Goals(Story.Path))
                    lines.Add($"  {(goal.done ? "<color=#7aff9a>✔</color>" : "✘")} {goal.text}");
        }
        int income = SettlementEconomy.DailyIncome();
        if (income > 0) lines.Add($"Príjem osady: <b>{income}</b>/deň   [0] duša  [9] osada");

        if (DemonTree.All.Count > 0) lines.Add($"Stromy temnoty {DemonTree.All.Count}   Pevnosť {DemonNature.Hardness}/{DemonNature.MaxHardness}");

        float w = 330f, lineHeight = 20f, h = 26f + lines.Count * lineHeight;
        // Pod pravým panelom HUD-u; keď sa pod neho nad minimapu nezmestí (otvorená nápoveda F1), nekreslí sa
        float top = 130f;
        if (HudUi.RightBottomPixels > 0f)
        {
            top = HudUi.RightBottomPixels + 8f;
            float mapTop = Screen.height - 225f * (Screen.height / 720f);   // nad rámom minimapy
            if (top + h > mapTop) return;
        }
        Rect box = new Rect(Screen.width - w - 12f, top, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(box.x + 8f, box.y + 3f, w - 16f, 20f), $"Deň {day}  [{toggleKey}]", title);
        for (int i = 0; i < lines.Count; i++)
            GUI.Label(new Rect(box.x + 8f, box.y + 24f + i * lineHeight, w - 16f, lineHeight), lines[i], label);
    }
}
