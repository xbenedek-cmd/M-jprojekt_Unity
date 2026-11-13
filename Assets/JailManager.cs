using UnityEngine;

public class JailManager : MonoBehaviour
{
    public static JailManager Instance { get; private set; }
    public static bool MenuOpen { get; private set; }
    public static bool IsInJail { get; private set; }

    [Header("Nastavenia zatknutia")]
    public int fineAmount = 100;
    public float jailTimeSeconds = 30f;

    [Header("Pozície (nastaví sa automaticky cez Progeny)")]
    public Vector3 jailPosition = new Vector3(0, 1f, 0);
    public Vector3 releasePosition = new Vector3(0, 1f, 5f);

    private PlayerStats player;
    private PlayerMovement movement;
    private float releaseTime;
    private GUIStyle titleStyle, textStyle;

    /// <summary>
    /// Scéna bez JailManagera (menu Progeny → Vytvoriť JailManager sa nespustilo): vojaci by hráča nikdy nezatkli.
    /// Doplní sa správca s uzavretou celou mimo mesta; prepustenie je na mieste, kde hra začala.
    /// </summary>
    public static void EnsureExists(Vector3 playerStart)
    {
        if (Instance != null || FindAnyObjectByType<JailManager>() != null) return;

        JailManager jm = new GameObject("JailManager").AddComponent<JailManager>();

        // Cela má vlastnú podlahu, takže nezávisí od terénu
        Vector3 cell = playerStart + new Vector3(0f, 300f, 600f);
        GameObject root = new GameObject("Jail_Platform");
        root.transform.position = cell;

        CreateCellPart(root.transform, new Vector3(0f, -0.25f, 0f), new Vector3(6f, 0.5f, 6f));
        CreateCellPart(root.transform, new Vector3(0f, 3f, 0f), new Vector3(6f, 0.2f, 6f));
        for (int i = 0; i < 4; i++)
        {
            Quaternion rot = Quaternion.Euler(0f, i * 90f, 0f);
            CreateCellPart(root.transform, rot * new Vector3(0f, 1.5f, 3f), new Vector3(6f, 3f, 0.2f), rot);
        }

        jm.jailPosition = cell + Vector3.up * 1f;
        jm.releasePosition = playerStart + Vector3.up * 0.5f;
    }

    static void CreateCellPart(Transform parent, Vector3 localPos, Vector3 scale, Quaternion? rot = null)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = rot ?? Quaternion.identity;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().material.color = new Color(0.12f, 0.12f, 0.12f);
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerStats>();
        if (player != null)
            movement = player.GetComponent<PlayerMovement>();
    }

    public void StartArrest()
    {
        if (MenuOpen || IsInJail) return;
        if (player != null && player.IsDemon) return;   // démona nezatknú, len naháňajú
        MenuOpen = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) movement = player.GetComponent<PlayerMovement>();
            return;
        }

        if (MenuOpen)
        {
            if (GameInput.GetKeyDown(KeyCode.Alpha1)) PayFine();
            else if (GameInput.GetKeyDown(KeyCode.Alpha2)) GoToJail();
            return;
        }

        if (IsInJail)
        {
            if (Time.time >= releaseTime)
            {
                ReleaseFromJail();
            }
            else if (GameInput.GetKeyDown(KeyCode.Alpha1) && player.money >= fineAmount)
            {
                player.TrySpend(fineAmount);
                ReleaseFromJail();
                GameHUD.Message($"Zaplatil si kauciu {fineAmount} a ideš von.", 4f);
            }
        }
    }

    void PayFine()
    {
        if (player != null && player.money >= fineAmount)
        {
            player.TrySpend(fineAmount);
            MenuOpen = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            ResetCrime();
            GameHUD.Message($"Zaplatil si pokutu {fineAmount} a ideš slobodný.", 4f);
        }
        else
        {
            GameHUD.Message($"Nemáš dosť peňazí (treba {fineAmount}).", 3f);
        }
    }

    void GoToJail()
    {
        MenuOpen = false;
        IsInJail = true;
        releaseTime = Time.time + jailTimeSeconds;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (player != null)
        {
            // Teleportácia
            player.transform.position = jailPosition;

            // OPRAVA: Zamrazenie hráčovej fyziky, aby sa nepohol a netlačil klietku
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }
        GameHUD.Message($"Si vo väzení na {jailTimeSeconds} sekúnd. Môžeš zaplatiť kauciu stlačením [1].", 5f);
    }

    void ReleaseFromJail()
    {
        IsInJail = false;
        if (player != null)
        {
            // OPRAVA: Odmrazenie fyziky
            Rigidbody rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }

            player.transform.position = releasePosition;

            // Po prepustení hráč vstane z podlahy
            PlayerVisual visual = player.GetComponent<PlayerVisual>();
            float standLength = visual != null && visual.ModelAnimator != null ? visual.ModelAnimator.PlayOneShot(DemonModelAnimator.OneShot.StandUp) : 0f;
            if (standLength > 0f && movement != null) movement.LockMovement(standLength);
        }
        ResetCrime();
        GameHUD.Message("Bol si prepustený z väzenia.", 4f);
    }

    void ResetCrime()
    {
        CivilianAI.LastPlayerCrimeTime = -999f;
        SoldierAI.ClearPlayerHostility();
        CityGuardNPC.ClearPlayerHostility();
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (IsInJail)
        {
            float timeLeft = Mathf.Max(0, releaseTime - Time.time);
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.red } };
            GUI.Label(new Rect(Screen.width / 2f - 200, 50, 400, 50), $"VÄZENIE: {timeLeft:0} s", style);

            if (player != null && player.money >= fineAmount)
            {
                var subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
                GUI.Label(new Rect(Screen.width / 2f - 200, 80, 400, 50), $"[1] Zaplatiť kauciu ({fineAmount})", subStyle);
            }
            return;
        }

        if (!MenuOpen) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        }

        float w = 500f, h = 250f;
        Rect box = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.9f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.contentColor = new Color(1f, 0.3f, 0.3f);
        GUI.Label(new Rect(box.x, box.y + 20f, w, 40f), "VOJACI ŤA CHYTILI!", titleStyle);
        GUI.contentColor = Color.white;

        GUI.Label(new Rect(box.x, box.y + 70f, w, 30f), "Spáchal si zločin. Ako to vyriešime?", textStyle);

        bool canPay = player.money >= fineAmount;

        GUI.enabled = canPay;
        if (GUI.Button(new Rect(box.x + 40f, box.y + 115f, w - 80f, 36f), $"[1] Zaplatiť pokutu ({fineAmount} peňazí)"))
            PayFine();
        GUI.enabled = true;

        if (GUI.Button(new Rect(box.x + 40f, box.y + 160f, w - 80f, 36f), $"[2] Nechať sa zavrieť ({jailTimeSeconds:0} sekúnd)"))
            GoToJail();

        GUI.contentColor = new Color(1f, 1f, 1f, 0.5f);
        GUI.Label(new Rect(box.x, box.y + 205f, w, 30f), "Klikni na možnosť alebo stlač 1 / 2", textStyle);

        GUI.contentColor = Color.white;
    }
}