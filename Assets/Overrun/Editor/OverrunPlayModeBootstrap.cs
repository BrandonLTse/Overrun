using UnityEditor;
using System.IO;

/// Unity's no-domain/no-scene-reload mode can retain managed startup state.
/// Use the same idempotent runtime setup without changing user editor settings.
[InitializeOnLoad]
public static class OverrunPlayModeBootstrap
{
    static OverrunPlayModeBootstrap()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) { PrepareAuditFrames(); EditorApplication.delayCall += EnsureSession; }
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) RestoreAuditFrames();
        };
        if (EditorApplication.isPlaying) { PrepareAuditFrames(); EditorApplication.delayCall += EnsureSession; }
    }
    static void PrepareAuditFrames()
    {
        if (!Directory.Exists("Temp") || Directory.GetFiles("Temp", "Overrun*Audit.request").Length == 0) return;
        if (!SessionState.GetBool("Overrun.auditBackgroundActive", false))
        {
            SessionState.SetBool("Overrun.auditBackgroundPrevious", UnityEngine.Application.runInBackground);
            SessionState.SetBool("Overrun.auditBackgroundActive", true);
            SessionState.SetBool("Overrun.auditHadBestRound", UnityEngine.PlayerPrefs.HasKey(OverrunRunRecord.BestRoundKey));
            SessionState.SetInt("Overrun.auditBestRound", OverrunRunRecord.BestRound);
        }
        // Explicit audits must receive frames even while Codex has OS focus.
        // This is temporary and does not change PlayerSettings or normal play.
        UnityEngine.Application.runInBackground = true;
        EditorApplication.QueuePlayerLoopUpdate();
    }
    static void RestoreAuditFrames()
    {
        if (!SessionState.GetBool("Overrun.auditBackgroundActive", false)) return;
        UnityEngine.Application.runInBackground = SessionState.GetBool("Overrun.auditBackgroundPrevious", false);
        if (SessionState.GetBool("Overrun.auditHadBestRound", false)) UnityEngine.PlayerPrefs.SetInt(OverrunRunRecord.BestRoundKey, SessionState.GetInt("Overrun.auditBestRound", 0));
        else UnityEngine.PlayerPrefs.DeleteKey(OverrunRunRecord.BestRoundKey);
        UnityEngine.PlayerPrefs.Save();
        SessionState.EraseBool("Overrun.auditHadBestRound"); SessionState.EraseInt("Overrun.auditBestRound");
        SessionState.EraseBool("Overrun.auditBackgroundActive"); SessionState.EraseBool("Overrun.auditBackgroundPrevious");
    }
    static void EnsureSession()
    {
        if (EditorApplication.isPlaying && !EditorApplication.isCompiling) OverrunRuntimeBootstrap.EnsureSession();
    }
}
