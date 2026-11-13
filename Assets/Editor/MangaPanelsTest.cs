using UnityEditor;
using UnityEngine;

/// <summary>Testovacie položky pre manga panely; fungujú v Play móde (každý typ panelu zvlášť + celá sekvencia).</summary>
public static class MangaPanelsTest
{
    static int counter;

    static string Key() { return "test_" + (++counter); }

    static void Log(PanelResult r)
    {
        Debug.Log("MangaPanels výsledok: dokončené=" + r.Completed + ", odmietnuté=" + r.Refused + ", voľba=" + r.LastChoice
                  + ", QTE=" + r.Qte + ", presnosť=" + r.Precision.ToString("0.00") + ", násobok poškodenia=" + r.DamageMultiplier);
    }

    [MenuItem("Progeny/Panely/Test/1 Rozprávací", false, 700)]
    public static void Narrative()
    {
        MangaPanels.Play(Key(), PanelContext.Dialogue, new[]
        {
            new PanelPage(
                PanelSpec.Narrative("Popol sa ešte hýbe. Niečo sa v ňom prebúdza.", PanelArt.Skyline, null, new Vector2(0.5f, 0.6f)),
                PanelSpec.Narrative("Jeho oči sa otvorili.", PanelArt.Eye),
                PanelSpec.Narrative("„Ty si ten, koho sme čakali.“", PanelArt.Figure))
        }, Log);
    }

    [MenuItem("Progeny/Panely/Test/2 Voľba", false, 701)]
    public static void Choice()
    {
        MangaPanels.Play(Key(), PanelContext.Decision, new[]
        {
            new PanelPage(
                PanelSpec.Choice("Zaútočiť", PanelArt.Blade),
                PanelSpec.Choice("Vyjednávať", PanelArt.Figure),
                PanelSpec.Choice("Utiecť", PanelArt.Skyline))
        }, Log);
    }

    [MenuItem("Progeny/Panely/Test/3 QTE", false, 702)]
    public static void Qte()
    {
        MangaPanels.Play(Key(), PanelContext.Boss, new[]
        {
            new PanelPage(
                PanelSpec.Narrative("Boss sa rozhodne k úderu!", PanelArt.Figure, null, new Vector2(0.7f, 0.5f)),
                PanelSpec.Qte(KeyCode.F, 2.2f, 12f, "Stlač F v zelenom pásme!"))
        }, Log);
    }

    [MenuItem("Progeny/Panely/Test/4 Výsledok", false, 703)]
    public static void Result()
    {
        MangaPanels.Play(Key(), PanelContext.Boss, new[]
        {
            new PanelPage(
                PanelSpec.Narrative("", PanelArt.Fist, null, new Vector2(0.4f, 0.5f)),
                PanelSpec.Result(ResultWord.Bam))
        }, Log);
    }

    [MenuItem("Progeny/Panely/Test/Celá sekvencia", false, 710)]
    public static void All()
    {
        MangaPanels.Play(Key(), PanelContext.Boss, new[]
        {
            new PanelPage(PanelSpec.Narrative("Boss zareve.", PanelArt.Eye), PanelSpec.Narrative("Zem sa trasie.", PanelArt.Skyline)),
            new PanelPage(PanelSpec.Choice("Zaútočiť", PanelArt.Blade), PanelSpec.Choice("Uhnúť", PanelArt.Figure)),
            new PanelPage(PanelSpec.Narrative("Vrhne sa na teba!", PanelArt.Figure), PanelSpec.Qte(KeyCode.F)),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Fist), PanelSpec.Result())
        }, Log);
    }

    [MenuItem("Progeny/Panely/Test/Pravidlo: druhé spustenie v tom istom súboji", false, 720)]
    public static void OncePerFight()
    {
        MangaPanels.Play("test_pravidlo", PanelContext.Boss, new[] { new PanelPage(PanelSpec.Narrative("Prvé spustenie.", PanelArt.Eye), PanelSpec.Narrative("OK", PanelArt.Figure)) }, Log);
        MangaPanels.Play("test_pravidlo", PanelContext.Boss, new[] { new PanelPage(PanelSpec.Narrative("Druhé spustenie.", PanelArt.Eye), PanelSpec.Narrative("NEMALO BY", PanelArt.Figure)) }, Log);
    }

    [MenuItem("Progeny/Panely/Test/1 Rozprávací", true)]
    [MenuItem("Progeny/Panely/Test/2 Voľba", true)]
    [MenuItem("Progeny/Panely/Test/3 QTE", true)]
    [MenuItem("Progeny/Panely/Test/4 Výsledok", true)]
    [MenuItem("Progeny/Panely/Test/Celá sekvencia", true)]
    [MenuItem("Progeny/Panely/Test/Pravidlo: druhé spustenie v tom istom súboji", true)]
    static bool InPlayMode() { return Application.isPlaying; }
}
