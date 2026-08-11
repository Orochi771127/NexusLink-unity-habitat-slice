using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NexusLink.EditorTools
{
    /// <summary>
    /// Runs the approved Moonlake rebuild exactly once in the already-open
    /// Unity editor. The trigger is removed before scheduling the work so a
    /// later domain reload cannot repeat the destructive scene replacement.
    /// </summary>
    [InitializeOnLoad]
    public static class MoonlakeHd25dOneShot
    {
        static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
        static readonly string TriggerPath = Path.Combine(ProjectRoot, "Temp", "MoonlakeHd25dRebuild.trigger");
        static readonly string StatusPath = Path.Combine(ProjectRoot, "output", "habitat", "moonlake-hd25d-v01", "one-shot-status.txt");

        static MoonlakeHd25dOneShot()
        {
            if (!File.Exists(TriggerPath))
                return;

            File.Delete(TriggerPath);
            EditorApplication.delayCall += RunOnce;
        }

        static void RunOnce()
        {
            try
            {
                Debug.Log("[Moonlake HD25D] One-shot trigger accepted; rebuilding the active HeroZone in place.");
                MoonlakeHd25dSceneBuilder.BuildFromCommandLine();
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
                File.WriteAllText(StatusPath, "SUCCESS " + DateTime.Now.ToString("O"));
                EditorApplication.RepaintHierarchyWindow();
                SceneView.RepaintAll();
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
                File.WriteAllText(StatusPath, "ERROR " + DateTime.Now.ToString("O") + Environment.NewLine + exception);
                Debug.LogException(exception);
            }
        }
    }
}
