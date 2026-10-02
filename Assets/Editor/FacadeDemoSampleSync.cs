using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PyxlMedia.InteriorMapping.Development
{
    /// <summary>
    /// Mirrors the editable demo in Assets into the package's Samples~ folder, which Unity cannot open.
    /// </summary>
    /// <remarks>
    /// Lives in the host project only, so it never ships. Run it before tagging a release.
    /// </remarks>
    public static class FacadeDemoSampleSync
    {
        private const string SourceFolder = "Assets/Samples/Facade Demo";
        private const string PackageSampleFolder = "Packages/com.pyxlmedia.interior-mapping/Samples~/Facade Demo";

        [MenuItem("Tools/Interior Mapping/Sync Facade Demo Into Package")]
        public static void Sync()
        {
            // Unsaved edits would otherwise be left out of the copy.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            AssetDatabase.SaveAssets();

            // Mirror rather than overlay, so files deleted from the demo leave the sample too.
            if (Directory.Exists(PackageSampleFolder))
            {
                FileUtil.DeleteFileOrDirectory(PackageSampleFolder);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PackageSampleFolder));
            FileUtil.CopyFileOrDirectory(SourceFolder, PackageSampleFolder);

            Debug.Log($"Synced {SourceFolder} into {PackageSampleFolder}.");
        }
    }
}
