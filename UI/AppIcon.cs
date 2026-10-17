using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Aplat.UI
{
    // The application's icon for every window — dialogs included: a Form with no Icon
    // of its own shows WinForms' stock one in its title bar. Loaded from the .ico
    // embedded in the assembly, which keeps all its sizes (a 16 px title bar gets the
    // 16 px picture, not a shrunk 32); falls back to the exe's own icon resource.
    public static class AppIcon
    {
        private static Icon _icon;
        private static bool _loaded;

        public static Icon Get()
        {
            if (_loaded) return _icon;
            _loaded = true;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (string name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream s = asm.GetManifestResourceStream(name))
                        if (s != null) { _icon = new Icon(s); return _icon; }
                }
            }
            catch { /* fall through */ }
            try { _icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _icon = null; /* design mode / tests: the stock icon stays */ }
            return _icon;
        }

        public static void Apply(Form form)
        {
            var icon = Get();
            if (form != null && icon != null) form.Icon = icon;
        }
    }
}
