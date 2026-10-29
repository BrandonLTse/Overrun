using UnityEngine;

public sealed class OverrunHud : MonoBehaviour
{
    public Transform player;
    public WaveDirector director;
    GUIStyle small, title, body, cardTitle, weaponName, ammo;
    void OnGUI()
    {
        if (OverrunMenus.InputBlocked) return;
        var actor = player ? player.GetComponent<OverrunPlayer>() : null;
        if (!actor || !director) return;
        if (small == null)
        {
            small = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            small.normal.textColor = new Color(.8f, .86f, .91f);
            title = new GUIStyle(small) { fontSize = 28, fontStyle = FontStyle.Bold };
            cardTitle = new GUIStyle(small) { fontSize = 21, fontStyle = FontStyle.Bold };
            body = new GUIStyle(small) { fontSize = 17, wordWrap = true };
            weaponName = new GUIStyle(small) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold, wordWrap = false };
            ammo = new GUIStyle(weaponName) { fontSize = 27 };
        }
        var modules = actor.GetComponent<OverrunUpgradeSystem>();
        var weapons = actor.GetComponent<OverrunWeaponSystem>();
        if (!weapons) return;
        if (modules.Choosing) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.018f, .025f, .035f, .94f));
        var previous = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) * .5f, (Screen.height - 540 * scale) * .5f), Quaternion.identity, Vector3.one * scale);
        if (modules.Choosing)
        {
            DrawChoices(modules);
            GUI.matrix = previous;
            return;
        }
        // Gameplay status anchors to the actual screen edges at every aspect ratio.
        GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
        float width = Screen.width / scale, height = Screen.height / scale;
        GUI.Label(new Rect(18, 12, width - 36, 28), $"HP {Mathf.CeilToInt(actor.Health)}/{actor.maxHealth}     WAVE {Mathf.Max(1, director.WaveNumber)}     GUARD {modules.GuardCharges}", small);
        string status = director.State == WaveDirector.Phase.Breather ? $"Next wave in {Mathf.CeilToInt(director.Countdown)} seconds" :
            director.State == WaveDirector.Phase.Reward ? "Wave cleared — choose a module" :
            $"{director.AliveCount} zombies alive";
        GUI.Label(new Rect(18, 37, width - 36, 28), status, small);
        var weaponPanel = new Rect(width - 248, height - 102, 230, 84);
        Fill(weaponPanel, new Color(.018f, .025f, .035f, .85f));
        GUI.Label(new Rect(weaponPanel.x + 14, weaponPanel.y + 7, 202, 24), weapons.WeaponName, weaponName);
        GUI.Label(new Rect(weaponPanel.x + 14, weaponPanel.y + 31, 202, 43), weapons.AmmoLabel, ammo);
        if (weapons.ShowingUnlockNotice) GUI.Label(new Rect(width - 348, weaponPanel.y - 30, 330, 26), "AR UNLOCKED", weaponName);
        GUI.matrix = previous;
    }

    void DrawChoices(OverrunUpgradeSystem modules)
    {
        GUI.Label(new Rect(36, 74, 890, 45), $"WAVE {director.WaveNumber} CLEARED", title);
        GUI.Label(new Rect(36, 122, 890, 42), "Choose one module. Click a card or press 1, 2 or 3. The next wave waits for your choice.", body);
        for (int i = 0; i < modules.Choices.Count; i++)
        {
            var choice = modules.Choices[i]; var rect = new Rect(36 + i * 300, 182, 284, 246);
            bool hover = rect.Contains(Event.current.mousePosition);
            Fill(rect, hover ? new Color(.13f, .19f, .24f) : new Color(.075f, .105f, .14f));
            Color accent = choice.kind == OverrunUpgrade.BankShot ? new Color(1, .7f, .3f) : choice.kind == OverrunUpgrade.BreachShot ? new Color(1, .4f, .24f) : new Color(.3f, .85f, 1);
            Fill(new Rect(rect.x, rect.y, rect.width, 4), accent);
            GUI.Label(new Rect(rect.x + 16, rect.y + 17, 250, 30), $"[{i + 1}]  {choice.Title}", cardTitle);
            GUI.Label(new Rect(rect.x + 16, rect.y + 65, 250, 142), choice.Description, body);
            GUI.Label(new Rect(rect.x + 16, rect.y + 212, 250, 26), choice.rank > 0 ? "PERMANENT MODULE" : "TACTICAL SUPPLY — ONE WAVE", small);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) { modules.Choose(i); break; }
        }
        GUI.Label(new Rect(36, 452, 890, 35), "No enemies enter while choosing. WASD stays active. Five seconds to prepare after you pick.", small);
    }

    static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = previous;
    }
}
