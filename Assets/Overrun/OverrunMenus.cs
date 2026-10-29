using UnityEngine;
using UnityEngine.InputSystem;

/// One owner for menu state, pause time and input routing. No scene/build-index dependency.
[DefaultExecutionOrder(-1000)]
public sealed class OverrunMenus : MonoBehaviour
{
    public enum ScreenState { MainMenu, Playing, Paused, GameOver }
    public enum PendingAction { None, Restart, MainMenu }
    public static OverrunMenus Instance { get; private set; }
    public static bool GameplayBlocked => Instance && Instance.State != ScreenState.Playing;
    public static bool InputBlocked => GameplayBlocked || (Instance && Instance.transitionFrame == Time.frameCount);
    public ScreenState State { get; private set; } = ScreenState.MainMenu;
    public PendingAction Confirmation { get; private set; }
    public bool ShowingControls { get; private set; }
    public bool IsReady => ready;
    public int RoundReached { get; private set; }
    public int BestRound => OverrunRunRecord.BestRound;
    OverrunRuntimeBootstrap runtime;
    OverrunPlayer player;
    WaveDirector director;
    int selected, transitionFrame = -1;
    bool ready, previousAudioPause;
    float previousTimeScale;
    GUIStyle heading, subtitle, body, small, buttonText, eyebrow;
    static readonly Color Amber = new Color(1, .69f, .28f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { Instance = null; }

    public void Initialize(OverrunRuntimeBootstrap source, OverrunPlayer actor, WaveDirector waves)
    {
        Instance = this; runtime = source; player = actor; director = waves;
        previousTimeScale = Time.timeScale; previousAudioPause = AudioListener.pause;
        ready = true; SetState(ScreenState.MainMenu);
    }

    void SetState(ScreenState state)
    {
        State = state; selected = 0; Confirmation = PendingAction.None; ShowingControls = false;
        transitionFrame = Time.frameCount;
        Time.timeScale = state == ScreenState.Playing ? 1 : 0;
        AudioListener.pause = state != ScreenState.Playing;
        player.ClearMenuInput();
    }

    public void StartRun()
    {
        if (!ready) return;
        RoundReached = 0; runtime.ResetRun(true); SetState(ScreenState.Playing); OverrunAudio.UI(OverrunSound.RunStart);
    }
    public void EndRun()
    {
        if (!ready || State != ScreenState.Playing) return;
        RoundReached = Mathf.Max(1, director.WaveNumber);
        OverrunRunRecord.Record(RoundReached);
        SetState(ScreenState.GameOver); OverrunAudio.UI(OverrunSound.GameOver);
    }
    public void ReturnToMainMenu()
    {
        if (!ready || State != ScreenState.GameOver) return;
        runtime.ResetRun(false); SetState(ScreenState.MainMenu); OverrunAudio.UI(OverrunSound.MenuBack);
    }
    public void Pause()
    {
        if (ready && State == ScreenState.Playing) { SetState(ScreenState.Paused); OverrunAudio.UI(OverrunSound.Pause); }
    }
    public void Resume()
    {
        if (ready && State == ScreenState.Paused) { SetState(ScreenState.Playing); OverrunAudio.UI(OverrunSound.Resume); }
    }
    public void RequestAction(PendingAction action)
    {
        if (State != ScreenState.Paused || action == PendingAction.None) return;
        Confirmation = action; ShowingControls = false; selected = 0; OverrunAudio.UI(OverrunSound.MenuAccept);
    }
    public void ConfirmAction()
    {
        if (State != ScreenState.Paused) return;
        if (Confirmation == PendingAction.Restart) StartRun();
        else if (Confirmation == PendingAction.MainMenu)
        {
            runtime.ResetRun(false); SetState(ScreenState.MainMenu); OverrunAudio.UI(OverrunSound.MenuBack);
        }
    }
    public void CancelPanel() { Confirmation = PendingAction.None; ShowingControls = false; selected = 0; OverrunAudio.UI(OverrunSound.MenuBack); }

    void Update()
    {
        if (!ready) return;
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            if (ShowingControls || Confirmation != PendingAction.None) CancelPanel();
            else if (State == ScreenState.Playing) Pause();
            else if (State == ScreenState.Paused) Resume();
            return;
        }
        if (State == ScreenState.Playing) return;
        int count = ShowingControls ? 1 : Confirmation != PendingAction.None ? 2 : State == ScreenState.GameOver ? 2 : State == ScreenState.MainMenu ? 3 : 4;
        int previousSelected = selected;
        if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) selected = (selected + count - 1) % count;
        if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame) selected = (selected + 1) % count;
        if (previousSelected != selected) OverrunAudio.UI(OverrunSound.MenuMove);
        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame) Activate(selected);
    }

    void Activate(int index)
    {
        if (ShowingControls) { CancelPanel(); return; }
        if (Confirmation != PendingAction.None) { if (index == 0) CancelPanel(); else ConfirmAction(); return; }
        if (State == ScreenState.GameOver)
        {
            if (index == 0) StartRun(); else if (index == 1) ReturnToMainMenu();
        }
        else if (State == ScreenState.MainMenu)
        {
            if (index == 0) StartRun();
            else if (index == 1) { ShowingControls = true; selected = 0; OverrunAudio.UI(OverrunSound.MenuAccept); }
            else if (index == 2 && !Application.isEditor) Application.Quit();
        }
        else if (State == ScreenState.Paused)
        {
            if (index == 0) Resume();
            else if (index == 1) RequestAction(PendingAction.Restart);
            else if (index == 2) RequestAction(PendingAction.MainMenu);
            else if (index == 3) { ShowingControls = true; selected = 0; OverrunAudio.UI(OverrunSound.MenuAccept); }
        }
    }

    void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        if (ready) { Time.timeScale = previousTimeScale; AudioListener.pause = previousAudioPause; }
    }

    void OnGUI()
    {
        if (!ready || State == ScreenState.Playing) return;
        EnsureStyles();
        int oldDepth = GUI.depth; GUI.depth = -100;
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.018f, .024f, .031f, State == ScreenState.MainMenu ? .99f : .95f));
        var matrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) * .5f, (Screen.height - 540 * scale) * .5f), Quaternion.identity, Vector3.one * scale);
        try
        {
            Fill(new Rect(42, 42, 6, 430), Amber);
            GUI.Label(new Rect(72, 42, 800, 26), "OVERRUN  /  SURVIVAL PROTOCOL", eyebrow);
            if (ShowingControls) DrawControls();
            else if (Confirmation != PendingAction.None) DrawConfirmation();
            else if (State == ScreenState.GameOver) DrawGameOver();
            else DrawHome();
            GUI.Label(new Rect(72, 493, 840, 24), State == ScreenState.GameOver ? "W / S or arrows to navigate    ·    Enter to select" : "W / S or arrows to navigate    ·    Enter to select    ·    Esc to pause / go back", small);
        }
        finally { GUI.matrix = matrix; GUI.depth = oldDepth; }
    }

    void DrawHome()
    {
        bool main = State == ScreenState.MainMenu;
        GUI.Label(new Rect(68, 84, 840, 82), main ? "OVERRUN" : "PAUSED", heading);
        if (!main) GUI.Label(new Rect(72, 169, 820, 42), $"Wave {Mathf.Max(1, director.WaveNumber)}  /  {Mathf.CeilToInt(player.Health)} HP  /  your run is on hold", subtitle);
        string[] labels = main ? new[] { "START RUN", "HOW TO PLAY", Application.isEditor ? "QUIT — STANDALONE BUILD ONLY" : "QUIT GAME" } :
            new[] { "RESUME", "RESTART RUN", "RETURN TO MAIN MENU", "HOW TO PLAY" };
        for (int i = 0; i < labels.Length; i++)
        {
            bool enabled = !(main && i == 2 && Application.isEditor);
            if (Button(new Rect(72, 222 + i * 47, 392, 40), labels[i], i, enabled)) { Activate(i); break; }
        }
        if (!main)
        {
            Fill(new Rect(510, 231, 378, 210), new Color(.055f, .075f, .09f));
            DrawUpgrades();
        }
    }

    void DrawGameOver()
    {
        GUI.Label(new Rect(68, 84, 840, 82), "GAME OVER", heading);
        Fill(new Rect(72, 212, 392, 112), new Color(.055f, .075f, .09f));
        Fill(new Rect(494, 212, 394, 112), new Color(.055f, .075f, .09f));
        GUI.Label(new Rect(90, 224, 354, 26), "ROUND REACHED", eyebrow);
        GUI.Label(new Rect(90, 249, 354, 72), RoundReached.ToString(), heading);
        GUI.Label(new Rect(512, 224, 354, 26), "HIGHEST ROUND", eyebrow);
        GUI.Label(new Rect(512, 249, 354, 72), BestRound.ToString(), heading);
        if (Button(new Rect(72, 360, 392, 52), "START NEW RUN", 0)) StartRun();
        else if (Button(new Rect(494, 360, 394, 52), "RETURN TO MAIN MENU", 1)) ReturnToMainMenu();
    }

    void DrawUpgrades()
    {
        GUI.Label(new Rect(534, 249, 330, 30), "YOUR UPGRADES", eyebrow);
        var modules = player.GetComponent<OverrunUpgradeSystem>();
        float y = 287; int count = 0;
        for (int i = 0; i < 4; i++)
        {
            var kind = (OverrunUpgrade)i; int rank = modules.Rank(kind);
            if (rank == 0) continue;
            GUI.Label(new Rect(534, y, 330, 26), new OverrunUpgradeChoice(kind, rank).Title, body);
            y += 26; count++;
        }
        var supply = modules.PendingSupply ?? ((director.State == WaveDirector.Phase.Spawning || director.State == WaveDirector.Phase.Clearing) ? modules.ActiveSupply : null);
        if (supply.HasValue)
        {
            GUI.Label(new Rect(534, y, 330, 26), OverrunUpgradeSystem.Title(supply.Value) + (modules.PendingSupply.HasValue ? " — NEXT WAVE" : " — THIS WAVE"), eyebrow);
            count++;
        }
        if (count == 0) GUI.Label(new Rect(534, 289, 330, 88), "No upgrades chosen yet.\nClear a wave to choose your first module.", body);
    }

    void DrawControls()
    {
        GUI.Label(new Rect(68, 87, 840, 70), "HOW TO PLAY", heading);
        GUI.Label(new Rect(72, 181, 200, 190), "WASD\nMOUSE\nLEFT MOUSE\nR\n1 / 2\n1 / 2 / 3\nESCAPE", subtitle);
        GUI.Label(new Rect(302, 181, 580, 190), "Move through the building\nAim your weapon\nPistol: click / AR: hold\nReload (pistol: 12 rounds / AR: 30)\nEquip pistol / AR (unlocks wave 3)\nChoose a module on the reward screen\nPause / resume", subtitle);
        GUI.Label(new Rect(72, 365, 800, 52), "Clear each wave for an upgrade. Bandages occasionally drop and restore 15 HP; full-health players leave them on the ground.", body);
        if (Button(new Rect(72, 426, 392, 45), "BACK", 0)) CancelPanel();
    }

    void DrawConfirmation()
    {
        bool restart = Confirmation == PendingAction.Restart;
        GUI.Label(new Rect(68, 92, 850, 78), restart ? "RESTART RUN?" : "LEAVE THIS RUN?", heading);
        GUI.Label(new Rect(72, 199, 780, 92), "Your current wave, health, weapon unlocks and modules will be reset.\nThis cannot be undone. A fresh run starts with the pistol.", subtitle);
        if (Button(new Rect(72, 337, 392, 52), "CANCEL — KEEP THIS RUN", 0)) CancelPanel();
        if (Button(new Rect(494, 337, 394, 52), restart ? "RESTART RUN" : "RETURN TO MAIN MENU", 1)) ConfirmAction();
    }

    bool Button(Rect rect, string label, int index, bool enabled = true)
    {
        bool hover = enabled && rect.Contains(Event.current.mousePosition);
        if (hover && (Event.current.type == EventType.MouseMove || Event.current.type == EventType.MouseDown))
        {
            if (selected != index) OverrunAudio.UI(OverrunSound.MenuMove);
            selected = index;
        }
        bool focus = enabled && (hover || selected == index);
        Fill(rect, focus ? new Color(.16f, .19f, .2f) : new Color(.065f, .084f, .1f));
        Fill(new Rect(rect.x, rect.y, 3, rect.height), focus ? Amber : new Color(.17f, .23f, .27f));
        GUI.Label(new Rect(rect.x + 18, rect.y, rect.width - 28, rect.height), label, enabled ? buttonText : small);
        return enabled && GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    void EnsureStyles()
    {
        if (heading != null) return;
        body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
        body.normal.textColor = new Color(.79f, .84f, .87f);
        heading = new GUIStyle(body) { fontSize = 58, fontStyle = FontStyle.Bold, wordWrap = false };
        heading.normal.textColor = new Color(.96f, .95f, .9f);
        subtitle = new GUIStyle(body) { fontSize = 20 };
        small = new GUIStyle(body) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
        small.normal.textColor = new Color(.48f, .57f, .62f);
        eyebrow = new GUIStyle(body) { fontSize = 14, fontStyle = FontStyle.Bold }; eyebrow.normal.textColor = Amber;
        buttonText = new GUIStyle(body) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
    }
    static void Fill(Rect rect, Color color)
    {
        var previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
    }
}
