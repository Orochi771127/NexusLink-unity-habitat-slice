using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NexusLink.EditorTools
{
    /// <summary>
    /// Refreshes previews only after the interactive editor has completed its
    /// assembly reload and URP has had time to initialize. It never changes
    /// scene composition.
    /// </summary>
    [InitializeOnLoad]
    public static class MoonlakeHd25dPreviewOneShot
    {
        static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;
        static readonly string TriggerPath = Path.Combine(ProjectRoot, "Temp", "MoonlakeHd25dPreview.trigger");
        static readonly string StatusPath = Path.Combine(ProjectRoot, "output", "habitat", "moonlake-hd25d-v01", "preview-refresh-status.txt");
        static double readyAt;

        static MoonlakeHd25dPreviewOneShot()
        {
            if (!File.Exists(TriggerPath))
                return;

            File.Delete(TriggerPath);
            readyAt = EditorApplication.timeSinceStartup + 6.0d;
            EditorApplication.update += TryRender;
        }

        static void TryRender()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < readyAt)
                return;

            EditorApplication.update -= TryRender;
            try
            {
                MoonlakeHd25dSceneBuilder.RenderCurrentPreviews();
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
                File.WriteAllText(StatusPath, "SUCCESS " + DateTime.Now.ToString("O"));
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
