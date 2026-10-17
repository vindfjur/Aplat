using System;
using System.IO;
using Aplat.Models;
using Aplat.Rendering;

namespace Aplat
{
    // View → Shadows: the sun's shadows on and off. The choice is part of the scene (Light.Shadows,
    // saved in the .aplat, undoable like the Lighting dialog) and is also remembered in
    // %AppData%\Aplat\shadows.txt for new scenes. Where the OpenGL context cannot do shadows
    // (ShadowSupport) the item is greyed out.
    public partial class MainForm
    {
        private static string ShadowsPath { get { return Path.Combine(SettingsDir, "shadows.txt"); } }

        // The remembered switch; on when nothing was saved.
        private static bool LoadShadowsPreference()
        {
            try
            {
                if (File.Exists(ShadowsPath)) return File.ReadAllText(ShadowsPath).Trim() != "off";
            }
            catch { }
            return true;
        }

        private static void SaveShadowsPreference(bool on)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(ShadowsPath, on ? "on" : "off");
            }
            catch { }
        }

        // A fresh scene starts with the remembered switch.
        private static void ApplyShadowsPreference(Scene scene)
        {
            if (scene != null && scene.Light != null) scene.Light.Shadows = LoadShadowsPreference();
        }

        private void SyncShadowsMenu()
        {
            bool possible = !ShadowSupport.Known || ShadowSupport.Available;
            menuShadows.Enabled = possible;
            menuShadows.Checked = possible && _scene.Light != null && _scene.Light.Shadows;
            menuShadows.ToolTipText = possible ? ""
                : string.Format(Loc.T("Тени: несовместимо с вашей версией OpenGL ({0})"), ShadowSupport.Version);
        }

        private void ToggleShadows()
        {
            if (ShadowSupport.Known && !ShadowSupport.Available) return;
            if (_scene.Light == null) _scene.Light = new LightSettings();
            PushUndo(Loc.T("Тени"));
            _scene.Light.Shadows = !_scene.Light.Shadows;
            SaveShadowsPreference(_scene.Light.Shadows);
            _scene.IsModified = true;
            sceneView.Refresh3D();
            UpdateTitle();
        }
    }
}
