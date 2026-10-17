using System;
using System.Collections.Generic;

namespace Aplat.UI
{
    // Other names for the palette's commands. The command list is built from the live UI in the
    // language the UI is in, so each command is also given
    //   • its name in the other language (taken from the localisation table), and
    //   • the synonyms below, in both languages, so "fps", "частота кадров", "shadow" or "тени"
    //     find the command whichever language the interface is in.
    // A line is  <Russian title as in the UI> | <synonyms separated by commas>.  A submenu item
    // is written "<submenu>: <item>", as the palette shows it. Commands missing here are still
    // found by their own title, group and hint — this only adds names people would search for.
    public static class CommandKeywords
    {
        // Names shared by whole kinds of commands (constants on purpose: search data, not UI text).
        public const string PanelWords = "панель; panel; show panel; hide panel; показать панель; скрыть панель";
        public const string TabWords = "вкладка; tab; side panel; боковая панель";
        public const string RecentFileWords = "недавние; recent file; open recent; последний файл";

        // A constant on purpose: this is search data, not text shown to the user.
        private const string Table = @"
Показывать FPS | fps, частота кадров, кадры в секунду, производительность, нагрузка, процессор, память, счётчик, frame rate, performance, cpu, ram
Тени | тень, shadow, shadows, солнце, sun
О программе | about, версия, version, лицензия, license, авторы
Проверить обновления | обновление, обновить, update, updates, новая версия, new version, check for updates
Проверять обновления при запуске | автообновление, auto update, at startup, при старте, обновления, updates
Горячие клавиши | hotkeys, shortcuts, keyboard, клавиатура, сочетания клавиш, справка
Поиск команд | command palette, search, найти, команды, ctrl+q
Холст и сетка | canvas, grid, размер холста, фон, background, пропорции, canvas size
Освещение и солнце | light, lighting, sun, свет, солнце, лампа, direction, направление света
Настройка панелей | panels, панели, interface, интерфейс, overlay, прозрачность панелей, panel opacity
Автовращение: Включить | auto-rotate, auto rotate, spin, turntable, вращение, вращать, крутить, f9, on, off, выключить
Автовращение: Камера вокруг объекта | orbit camera, camera, камера, облёт
Автовращение: Сам объект вокруг своей оси | spin object, object rotates, объект, вращать объект
Скорость: Медленно | auto-rotate speed, slow, скорость вращения, автовращение
Скорость: Обычно | auto-rotate speed, normal, скорость вращения, автовращение
Скорость: Быстро | auto-rotate speed, fast, скорость вращения, автовращение
Скорость: Очень быстро | auto-rotate speed, very fast, скорость вращения, автовращение
Тема: Светлая | light theme, day, светлая тема, white, theme, оформление
Тема: Графит (тёмная) | dark theme, night, тёмная тема, графит, dark mode, graphite, theme, оформление
Тема: Как в системе | system theme, auto, windows, как в windows, theme, оформление
Язык: Русский | language, russian, язык, locale, русский язык
Язык: English | language, english, английский, язык, locale
Панели инструментов: Файл | toolbar, toolbars, панель инструментов, file toolbar
Панели инструментов: Режимы | toolbar, toolbars, панель инструментов, modes toolbar
Панели инструментов: Сбросить расположение | reset layout, reset toolbars, toolbars, панели по умолчанию
Форма | shape mode, edit shape, edit mode, вершины, vertices, edges, faces, грани, рёбра, v
Экспортировать OBJ | obj, wavefront, 3d model, модель, export
Экспортировать STL | stl, 3d print, печать, export
Экспортировать FBX | fbx, export, unity, unreal
Экспорт холста PNG | png, image, картинка, рисунок, save image, export canvas
Экспорт холста JPG | jpg, jpeg, image, картинка, рисунок, export canvas
Экспорт кадра PNG | screenshot, скриншот, render, снимок, view, export frame
Экспорт кадра JPG | screenshot, скриншот, render, снимок, jpeg, export frame
Экспорт оборота (серия PNG) | turntable, 360, анимация, animation, frames, кадры, video, gif, export
Пипетка | eyedropper, color picker, pick color, выбрать цвет, взять цвет, i
Вернуть прошлый инструмент после выбора цвета | eyedropper return, back to previous tool, return to previous tool, пипетка, option
Выделение: Область | rectangle select, marquee, select area, выделить область, прямоугольное выделение
Выделение: Лассо | lasso, free select, обвести, выделение, selection
Ластик: Точный | precise eraser, erase pixels, стирать пиксели, eraser mode
Ластик: Объект | object eraser, erase object, стереть объект, ластик объектами, eraser mode
Фигуры: Контур | shape style, outline, only outline, контур фигуры, stroke
Фигуры: Заливка | shape style, solid, fill, заливка фигуры
Текст: Жирный | bold, b, жирный шрифт, ctrl+b
Текст: Курсив | italic, i, наклонный, ctrl+i
Текст: Подчёркнутый | underline, u, подчёркивание, ctrl+u
Текст: По левому краю | align left, left, выравнивание, ctrl+l
Текст: По центру | align center, center, выравнивание, ctrl+e
Текст: По правому краю | align right, right, выравнивание, ctrl+r
Линейка | ruler, straight line, ровная линия, r, прямая
Лассо | lasso, free select, обвести
Объёмный текст | 3d text, text3d, надпись, volume text
Поставить на пол | drop to ground, floor, на пол, gravity, snap to floor, опустить
Группировать | group, объединить, ctrl+g
Разгруппировать | ungroup, разделить, ctrl+shift+g
Скрыть / показать | hide, show, visibility, видимость, спрятать
Сбросить поворот | reset rotation, обнулить поворот, unrotate
Переименовать | rename, имя, name
Дублировать | duplicate, copy, клон, clone, копия, ctrl+d
Вырезать | cut, буфер обмена, clipboard, ctrl+x
Копировать | copy, буфер обмена, clipboard, ctrl+c
Вставить | paste, буфер обмена, clipboard, ctrl+v
Удалить | delete, remove, убрать, del
Выделить всё | select all, ctrl+a
Снять выделение | deselect, clear selection, esc
Каркас | wireframe, mesh, edges, рёбра, сетка полигонов, f5
Вершины | vertices, points, узлы, f6
Сетка | grid, floor, пол, f8
Спереди | front view, фронт, вид спереди, 1
Сбоку | side view, вид сбоку, 3
Сверху | top view, вид сверху, 7
Перспектива | perspective, 3d view, 0
Фокус на выбранном | focus, zoom to selection, приблизить, f
Показать всю сцену | fit, zoom to fit, show all, всё, home, вписать
Обводить полигоны выделенного | outline, selection outline, контур выделения, polygons
Умные направляющие | smart guides, snapping, привязка, magnet, примагничивание
Создать | new, new scene, новая сцена, ctrl+n
Открыть | open, load, загрузить, ctrl+o
Сохранить | save, ctrl+s
Сохранить как | save as, ctrl+shift+s
Выход | exit, quit, close, закрыть, выйти
Отменить | undo, назад, ctrl+z
Повторить | redo, вперёд, ctrl+y
История | history, undo list, список правок
Очистить холст | clear canvas, erase all, стереть всё, чистый лист
Очистить все фигуры | clear scene, delete all, удалить всё
Куб | cube, box, коробка
Сфера | sphere, ball, шар
Цилиндр | cylinder, tube, труба
Конус | cone
Пирамида | pyramid
Тетраэдр | tetrahedron
Тор | torus, donut, бублик, кольцо, ring
Капсула | capsule, pill
Кисть | brush, paint, draw, рисовать, b
Карандаш | pencil, draw, рисовать
Ручка | pen, ink, draw
Маркер | marker, highlighter, выделитель
Выделить | select area, marquee, выделение
Заливка | fill, bucket, paint bucket, залить
Ластик | eraser, rubber, стирать, e
Рука | hand, pan, move canvas, двигать холст
Текст | text, type, надпись, печатать
Цвет кисти | brush color, colour, цвет, палитра, palette
Выбор | select tool, pointer, cursor, указатель, f1
Переместить | move, translate, двигать, f2
Вращение | rotate, turn, поворот, f3
Масштаб | scale, resize, размер, f4
2D | flat view, draw mode, плоский вид, холст, canvas
3D | perspective, orbit, объёмный вид
Кисть: меньше | brush size, smaller, decrease, уменьшить кисть, [
Кисть: больше | brush size, larger, increase, bigger, увеличить кисть, ]
Поднять выбранное | raise, up, вверх, page up, pgup
Опустить выбранное | lower, down, вниз, page down, pgdn
Закрепить фигуру или текст | apply, commit, confirm, готово, закончить, enter
";

        private static Dictionary<string, string> _words;                // normalised Russian title → synonyms
        private static Dictionary<string, string> _toEn, _toRu;          // normalised title → counterpart
        private static Lang _builtFor = (Lang)(-1);

        private static void Build()
        {
            if (_builtFor == Loc.Current && _words != null) return;
            _builtFor = Loc.Current;
            _words = new Dictionary<string, string>();
            foreach (string line in Table.Split('\n'))
            {
                int bar = line.IndexOf('|');
                if (bar < 0) continue;
                _words[CommandMatcher.Normalize(line.Substring(0, bar))] = line.Substring(bar + 1).Trim().Replace(',', ';');
            }
            _toEn = new Dictionary<string, string>();
            _toRu = new Dictionary<string, string>();
            // The table holds whole texts ("Bold (Ctrl+B)", "Lasso — circle strokes…"); the palette
            // shows only their first part, so that part is a key as well. Whole texts win.
            foreach (var kv in Loc.EnglishEntries)
            {
                _toEn[CommandMatcher.Normalize(kv.Key)] = Clean(kv.Value);
                _toRu[CommandMatcher.Normalize(kv.Value)] = Clean(kv.Key);
            }
            foreach (var kv in Loc.EnglishEntries)
            {
                string ru = Head(kv.Key), en = Head(kv.Value);
                if (ru.Length == 0 || en.Length == 0 || (ru == kv.Key && en == kv.Value)) continue;
                string nr = CommandMatcher.Normalize(ru), ne = CommandMatcher.Normalize(en);
                if (!_toEn.ContainsKey(nr)) _toEn[nr] = en;
                if (!_toRu.ContainsKey(ne)) _toRu[ne] = ru;
            }
        }

        // "Name — what it does (Ctrl+B)" → "Name".
        private static string Head(string s)
        {
            int dash = s.IndexOf(" — ", StringComparison.Ordinal);
            if (dash > 0) s = s.Substring(0, dash);
            int paren = s.LastIndexOf(" (", StringComparison.Ordinal);
            if (paren > 0 && s.EndsWith(")", StringComparison.Ordinal)) s = s.Substring(0, paren);
            return Clean(s);
        }

        private static string Clean(string s) { return s.TrimEnd('.', '…', ' '); }

        // The other names of a command shown in the current language, ';'-separated.
        public static string AliasesFor(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            Build();
            var ru = new List<string>();
            var other = new List<string>();
            bool english = Loc.IsEnglish;
            foreach (string part in title.Split(new[] { ": " }, StringSplitOptions.None))
            {
                string key = CommandMatcher.Normalize(part), found;
                if (english)
                {
                    other.Add(_toRu.TryGetValue(key, out found) ? found : part);
                    ru.Add(_toRu.TryGetValue(key, out found) ? found : part);
                }
                else
                {
                    ru.Add(part);
                    other.Add(_toEn.TryGetValue(key, out found) ? found : part);
                }
            }
            string otherTitle = string.Join(": ", other);
            string words;
            _words.TryGetValue(CommandMatcher.Normalize(string.Join(": ", ru)), out words);
            // Also the synonyms of the bare item ("Включить" alone is not enough, but a
            // submenu item can be known by the name it has outside its submenu).
            if (words == null && ru.Count > 1) _words.TryGetValue(CommandMatcher.Normalize(ru[ru.Count - 1]), out words);
            string result = otherTitle == title ? "" : otherTitle;
            if (!string.IsNullOrEmpty(words)) result = result.Length == 0 ? words : result + ";" + words;
            return result.Length == 0 ? null : result;
        }
    }
}
