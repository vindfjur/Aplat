using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Aplat;
using Aplat.UI;

// The command palette must know everything the program can do, in both languages. The main
// window is built (and shown), the palette's list is read, and a list of expected commands is
// checked against it — so a feature added later without a way into the palette is noticed
// here. Searches then check that people find things by the names they would use.
// Add a line to Expected when a new command is added to the program.
class CommandTests
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int fails, checks;
    static void Check(bool ok, string msg) { checks++; Console.WriteLine((ok ? "ok   " : "FAIL ") + msg); if (!ok) fails++; }

    // Russian titles as the palette shows them; a submenu or section item is "<parent>: <item>".
    static readonly string[] Expected =
    {
        // File
        "Создать", "Открыть", "Сохранить", "Сохранить как", "Выход",
        "Экспортировать OBJ", "Экспортировать STL", "Экспортировать FBX", "Экспорт холста PNG", "Экспорт холста JPG",
        "Экспорт кадра PNG", "Экспорт кадра JPG", "Экспорт оборота (серия PNG)",
        // Edit
        "Отменить", "Повторить", "Вырезать", "Копировать", "Вставить", "Дублировать", "Удалить", "Выделить всё",
        "Снять выделение", "Группировать", "Разгруппировать",
        // Object
        "Куб", "Сфера", "Цилиндр", "Конус", "Пирамида", "Тетраэдр", "Тор", "Капсула", "Переименовать",
        "Поставить на пол", "Сбросить поворот", "Скрыть / показать", "Поднять выбранное", "Опустить выбранное",
        // View
        "Каркас", "Вершины", "Сетка", "Обводить полигоны выделенного", "Спереди", "Сбоку", "Сверху", "Перспектива",
        "Фокус на выбранном", "Показать всю сцену", "Холст и сетка", "Освещение и солнце", "Тени", "Показывать FPS",
        "Умные направляющие",
        "Автовращение: Включить", "Автовращение: Камера вокруг объекта", "Автовращение: Сам объект вокруг своей оси",
        "Скорость: Медленно", "Скорость: Обычно", "Скорость: Быстро", "Скорость: Очень быстро",
        "Тема: Светлая", "Тема: Графит (тёмная)", "Тема: Как в системе", "Язык: Русский", "Язык: English",
        "Панели: Файл", "Панели: Режимы", "Панели: Сбросить расположение",
        // Help
        "Поиск команд", "Горячие клавиши", "Проверить обновления", "Проверять обновления при запуске", "О программе",
        // Toolbars: modes and history
        "2D", "3D", "Выбор", "Переместить", "Вращение", "Масштаб", "Форма", "История",
        // Tools and their options
        "Кисть", "Карандаш", "Ручка", "Маркер", "Выделить", "Пипетка", "Заливка", "Ластик", "Рука", "Текст", "Линейка",
        "Линия", "Прямоугольник", "Эллипс", "Треугольник", "Цвет кисти", "Очистить холст", "Очистить все фигуры",
        "Ластик: Точный", "Ластик: Объект", "Выделение: Область", "Выделение: Лассо", "Фигуры: Контур", "Фигуры: Заливка",
        "Текст: Жирный", "Текст: Курсив", "Текст: Подчёркнутый", "Текст: По левому краю", "Текст: По центру",
        "Текст: По правому краю", "Вернуть прошлый инструмент после выбора цвета",
        "Кисть: меньше", "Кисть: больше", "Закрепить фигуру или текст",
    };

    // query (any language) → Russian title that must be among the first results
    static readonly string[][] Searches =
    {
        new[] { "fps", "Показывать FPS" }, new[] { "частота кадров", "Показывать FPS" },
        new[] { "frame rate", "Показывать FPS" }, new[] { "тени", "Тени" }, new[] { "shadow", "Тени" },
        new[] { "about", "О программе" }, new[] { "о программе", "О программе" }, new[] { "version", "О программе" },
        new[] { "update", "Проверить обновления" }, new[] { "обновления", "Проверить обновления" },
        new[] { "hotkeys", "Горячие клавиши" }, new[] { "shortcuts", "Горячие клавиши" }, new[] { "горячие клавиши", "Горячие клавиши" },
        new[] { "язык", "Язык: Русский" }, new[] { "language", "Язык: English" }, new[] { "english", "Язык: English" },
        new[] { "theme", "Тема: Графит (тёмная)" }, new[] { "dark", "Тема: Графит (тёмная)" }, new[] { "тёмная", "Тема: Графит (тёмная)" },
        new[] { "eyedropper", "Пипетка" }, new[] { "пипетка", "Пипетка" }, new[] { "color picker", "Пипетка" },
        new[] { "ruler", "Линейка" }, new[] { "линейка", "Линейка" }, new[] { "lasso", "Выделение: Лассо" }, new[] { "лассо", "Выделение: Лассо" },
        new[] { "object eraser", "Ластик: Объект" }, new[] { "ластик объект", "Ластик: Объект" },
        new[] { "auto-rotate", "Автовращение: Включить" }, new[] { "автовращение", "Автовращение: Включить" },
        new[] { "spin", "Автовращение: Включить" }, new[] { "speed", "Скорость: Быстро" }, new[] { "скорость", "Скорость: Быстро" },
        new[] { "shape mode", "Форма" }, new[] { "форма", "Форма" }, new[] { "obj", "Экспортировать OBJ" }, new[] { "png", "Экспорт холста PNG" },
        new[] { "screenshot", "Экспорт кадра PNG" }, new[] { "lighting", "Освещение и солнце" }, new[] { "солнце", "Освещение и солнце" },
        new[] { "canvas", "Холст и сетка" }, new[] { "group", "Группировать" }, new[] { "ungroup", "Разгруппировать" },
        new[] { "ground", "Поставить на пол" }, new[] { "на пол", "Поставить на пол" }, new[] { "brush", "Кисть" }, new[] { "кисть", "Кисть" },
        new[] { "smart guides", "Умные направляющие" }, new[] { "панели", "Панели: Файл" }, new[] { "bold", "Текст: Жирный" },
        new[] { "undo", "Отменить" }, new[] { "redo", "Повторить" }, new[] { "ctrl+q", "Поиск команд" }, new[] { "wireframe", "Каркас" },
        // typos and the wrong keyboard layout
        new[] { "rbcnm", "Кисть" }, new[] { "кисьт", "Кисть" }, new[] { "shdows", "Тени" }, new[] { "eyedroper", "Пипетка" },
        new[] { "лстик", "Ластик" }, new[] { "ыфму", "Сохранить" },
    };

    // The English counterpart of a Russian title as the palette shows it (part by part).
    static string English(string ru)
    {
        string en;
        if (Loc.EnglishEntries.TryGetValue(ru, out en)) return en.TrimEnd('.', '…');
        return string.Join(": ", ru.Split(new[] { ": " }, StringSplitOptions.None).Select(EnglishPart));
    }

    static string EnglishPart(string part)
    {
        string en;
        if (Loc.EnglishEntries.TryGetValue(part, out en) || Loc.EnglishEntries.TryGetValue(part + "...", out en)
            || Loc.EnglishEntries.TryGetValue(part + "…", out en)) return en.TrimEnd('.', '…');
        // Texts that the table holds with more words around them (a key hint, an explanation).
        var longer = Loc.EnglishEntries.Where(kv => kv.Key.StartsWith(part + " (") || kv.Key.StartsWith(part + " — ")).ToList();
        if (longer.Count == 0) return part;
        en = longer[0].Value;
        int cut = en.IndexOf(" — ", StringComparison.Ordinal);
        if (cut < 0) cut = en.LastIndexOf(" (", StringComparison.Ordinal);
        return cut > 0 ? en.Substring(0, cut) : en;
    }

    static string Norm(string s) { return CommandMatcher.Normalize(s); }

    static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
    }

    static void RunFor(Lang lang)
    {
        Loc.Set(lang);
        string tag = lang == Lang.English ? "[en] " : "[ru] ";
        var form = new MainForm();
        try
        {
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0); form.Size = new Size(1500, 950);
            form.Show(); Pump(700);
            var cmds = (List<PaletteCommand>)form.GetType().GetMethod("BuildCommands", F).Invoke(form, null);
            Console.WriteLine(tag + cmds.Count + " commands");
            var titles = new HashSet<string>(cmds.Select(c => Norm(c.Title)));

            var missing = new List<string>();
            foreach (string ru in Expected)
            {
                string want = lang == Lang.English ? English(ru) : ru;
                if (!titles.Contains(Norm(want))) missing.Add(want);
            }
            Check(missing.Count == 0, tag + "all " + Expected.Length + " expected commands are listed" +
                (missing.Count == 0 ? "" : " — missing: " + string.Join(" | ", missing)));

            // Panels and tabs come from the live window.
            var overlays = (System.Collections.IList)form.GetType().GetField("_overlays", F).GetValue(form);
            string panelPrefix = lang == Lang.English ? "Panel \u201c" : "Панель «";
            int panels = cmds.Count(c => c.Title.StartsWith(panelPrefix, StringComparison.Ordinal));
            Check(overlays.Count > 0 && panels == overlays.Count, tag + "one command per floating panel (" + panels + " of " + overlays.Count + ")");
            string tabPrefix = Norm(lang == Lang.English ? "Tab" : "Вкладка");
            Check(cmds.Count(c => Norm(c.Title).StartsWith(tabPrefix + " ", StringComparison.Ordinal)) >= 3, tag + "the side panel's tabs are listed");

            // Every command is runnable or deliberately dimmed, and has a group.
            Check(cmds.All(c => c.Run != null && !string.IsNullOrEmpty(c.Group)), tag + "every command has an action and a group");
            Check(cmds.Count(c => c.Enabled) > 40, tag + "most commands are available");

            // Toggles show their state: grid is on in a fresh scene, shadows too.
            var grid = cmds.FirstOrDefault(c => Norm(c.Title) == Norm(lang == Lang.English ? English("Сетка") : "Сетка"));
            Check(grid != null && grid.Checked == ((ToolStripButton)form.GetType().GetField("tbGrid", F).GetValue(form)).Checked, tag + "a toggle shows its current state (Grid)");

            // No Russian left in the English list (the languages' own names aside).
            if (lang == Lang.English)
            {
                var bad = cmds.SelectMany(c => new[] { c.Title, c.Group, c.Hint }).Where(t => Loc.HasCyrillic(t) && t != "Русский" && !t.EndsWith(": Русский")).Distinct().ToList();
                Check(bad.Count == 0, tag + "no Russian text in titles, groups and hints" + (bad.Count == 0 ? "" : " — " + string.Join(" | ", bad.Take(6))));
            }

            // Other names: the list is searchable by the name in the other language.
            var items = CommandPalette.MakeItems(cmds);
            foreach (var s in Searches)
            {
                string want = lang == Lang.English ? English(s[1]) : s[1];
                var res = CommandMatcher.Search(s[0], items);
                var top = res.Hits.Concat(res.Suggestions).Take(5).Select(i => Norm(cmds[i].Title)).ToList();
                Check(top.Contains(Norm(want)), tag + "'" + s[0] + "' finds '" + want + "'" +
                    (top.Contains(Norm(want)) ? "" : " — got: " + string.Join(" | ", res.Hits.Concat(res.Suggestions).Take(5).Select(i => cmds[i].Title))));
            }

            // The unavailable are kept but never run: nothing selected → "Group" is dimmed.
            var group = cmds.FirstOrDefault(c => Norm(c.Title) == Norm(lang == Lang.English ? English("Группировать") : "Группировать"));
            Check(group != null && !group.Enabled, tag + "an unavailable command is listed dimmed (Group with nothing selected)");
        }
        finally
        {
            var scene = form.GetType().GetField("_scene", F).GetValue(form);
            scene.GetType().GetProperty("IsModified").SetValue(scene, false, null);
            form.Close(); form.Dispose();
        }
    }

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try { RunFor(Lang.Russian); RunFor(Lang.English); }
        catch (Exception e) { Check(false, "threw " + e); }
        Console.WriteLine(fails == 0 ? "ALL OK (" + checks + " checks)" : fails + " FAILED of " + checks);
        return fails == 0 ? 0 : 1;
    }
}
