using System.Drawing;
using System.Windows.Forms;
using Aplat.UI;

namespace Aplat.Dialogs
{
    // "Help → Hotkeys": the full reference as a table (HotkeyTable).
    // Keep it in step with MainForm.TryHandleEditorShortcut and SceneView's keys.
    public sealed class HotkeysDialog : Form
    {
        private static HotkeyTable.Row R(string keys, string action) { return new HotkeyTable.Row(keys, action); }

        public HotkeysDialog()
        {
            Text            = Loc.T("Горячие клавиши");
            StartPosition   = FormStartPosition.CenterParent;
            ClientSize      = new Size(700, 620);
            MinimumSize     = new Size(520, 380);
            MinimizeBox     = false;
            ShowInTaskbar   = false;
            Padding         = new Padding(20, 16, 12, 16);

            var host = new ScrollHost { Dock = DockStyle.Fill };
            host.Content = new HotkeyTable(Sections());
            var close = new Button
            {
                Text = Loc.T("Закрыть"), Dock = DockStyle.Bottom, Height = 36,
                DialogResult = DialogResult.OK, FlatStyle = FlatStyle.System,
            };
            Controls.Add(host);
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;
            ThemeManager.StyleDialog(this);
        }

        private static HotkeyTable.Row[] Append(HotkeyTable.Row[] rows, HotkeyTable.Row row)
        {
            var all = new HotkeyTable.Row[rows.Length + 1];
            rows.CopyTo(all, 0);
            all[rows.Length] = row;
            return all;
        }

        private static HotkeyTable.Section[] Sections()
        {
            var list = new[]
            {
                new HotkeyTable.Section(Loc.T("Файл"),
                    R("Ctrl+Q", Loc.T("Поиск команд — все действия программы, понимает опечатки")),
                    R("Ctrl+N", Loc.T("Новая сцена")),
                    R("Ctrl+O", Loc.T("Открыть")),
                    R("Ctrl+S", Loc.T("Сохранить")),
                    R("Ctrl+Shift+S", Loc.T("Сохранить как")),
                    R(Loc.T("Перетащить .aplat"), Loc.T("Открыть сцену, бросив файл в окно")),
                    R(Loc.T("Перетащить картинку"), Loc.T("Текстура выбранного объекта или холста"))),

                new HotkeyTable.Section(Loc.T("Правка"),
                    R("Ctrl+Z", Loc.T("Отменить")),
                    R("Ctrl+Y / Ctrl+Shift+Z", Loc.T("Повторить")),
                    R("Ctrl+C / Ctrl+X / Ctrl+V", Loc.T("Копировать, вырезать, вставить объекты")),
                    R("Ctrl+D", Loc.T("Дубликат рядом с оригиналом")),
                    R("Ctrl+A", Loc.T("Выделить всё")),
                    R("Delete", Loc.T("Удалить выбранное")),
                    R("Ctrl+G", Loc.T("Сгруппировать")),
                    R("Ctrl+Shift+G", Loc.T("Разгруппировать")),
                    R(Loc.T("Alt+тянуть гизмо"), Loc.T("Копия объекта"))),

                new HotkeyTable.Section(Loc.T("Рисование на холсте"),
                    R("B", Loc.T("Кисть")),
                    R("E", Loc.T("Ластик; ещё раз — Точный ↔ Объект")),
                    R("R", Loc.T("Линейка: рисуйте по её краю — линия ровная")),
                    R(Loc.T("Тянуть конец линейки"), Loc.T("Линейка: сделать длиннее или короче")),
                    R(Loc.T("Двойной щелчок по концу"), Loc.T("Линейка: закрепить этот конец / отпустить")),
                    R(Loc.T("Лассо (панель «Выделение»)"), Loc.T("Обвести штрихи, фигуры, текст — двигать, масштабировать, вращать")),
                    R(Loc.T("[ / ] / Alt+колесо"), Loc.T("Размер кисти меньше / больше")),
                    R(Loc.T("Shift+тянуть"), Loc.T("Фигура: квадрат, круг, линия под 45°")),
                    R("Enter", Loc.T("Закрепить фигуру, текст или вырезку")),
                    R("Esc", Loc.T("Убрать фигуру; у текста — закончить набор")),
                    R("Delete", Loc.T("Убрать фигуру, текст, вырезку или выделенное лассо")),
                    R("Ctrl+Z", Loc.T("Сразу после закрепления — править снова")),
                    R(Loc.T("Стрелки"), Loc.T("Сдвиг на пиксель (Shift — на 10)")),
                    R(Loc.T("Двойной клик"), Loc.T("Править текст"))),

                new HotkeyTable.Section(Loc.T("Ввод текста на холсте"),
                    R("Enter", Loc.T("Новая строка")),
                    R("Ctrl+Enter", Loc.T("Закончить набор")),
                    R("Tab", Loc.T("Отступ")),
                    R(Loc.T("Shift+стрелки"), Loc.T("Выделить")),
                    R("Ctrl+← / Ctrl+→", Loc.T("Прыжок по словам")),
                    R("Home / End", Loc.T("Начало и конец строки")),
                    R(Loc.T("Двойной клик"), Loc.T("Выделить слово")),
                    R("Ctrl+C / Ctrl+X / Ctrl+V", Loc.T("Копировать, вырезать, вставить текст")),
                    R("Ctrl+Z / Ctrl+Y", Loc.T("Отменить, повторить — по словам")),
                    R("Ctrl+B / Ctrl+I / Ctrl+U", Loc.T("Жирный, курсив, подчёркнутый")),
                    R("Ctrl+L / Ctrl+E / Ctrl+R", Loc.T("По левому краю, центру, правому краю"))),

                new HotkeyTable.Section(Loc.T("Режимы"),
                    R("Esc", Loc.T("Снять выделение, затем режим выбора")),
                    R("F1", Loc.T("Выбор (курсор): выделять объекты и живые элементы")),
                    R("F2", Loc.T("Переместить")),
                    R("F3", Loc.T("Вращение")),
                    R("F4", Loc.T("Масштаб")),
                    R("V", Loc.T("Форма: тянуть вершины, рёбра и грани")),
                    R(Loc.T("Shift+тянуть"), Loc.T("Точное, медленное перемещение / вращение / масштаб")),
                    R(Loc.T("Ctrl+переместить"), Loc.T("Привязка к сетке и соседним объектам")),
                    R(Loc.T("Ctrl+вращать"), Loc.T("Шаг 15°"))),

                new HotkeyTable.Section(Loc.T("Вид"),
                    R("F5", Loc.T("Каркас")),
                    R("F6", Loc.T("Вершины")),
                    R("F7", Loc.T("Освещение и солнце")),
                    R("F8", Loc.T("Сетка")),
                    R("F9", Loc.T("Автовращение камеры или выбранного объекта (карточка: пауза, стоп, выкл.)")),
                    R(Loc.T("Alt + тянуть панель"), Loc.T("Переместить плавающую панель без примагничивания")),
                    R("F", Loc.T("Фокус на выбранном объекте")),
                    R("Home", Loc.T("Показать всю сцену")),
                    R("1 / 3 / 7", Loc.T("Спереди (2D) / сбоку / сверху")),
                    R("0", Loc.T("Перспектива (3D)"))),

                new HotkeyTable.Section(Loc.T("Камера и сцена"),
                    R(Loc.T("ПКМ"), Loc.T("Орбита камеры")),
                    R(Loc.T("Тянуть гизмо осей"), Loc.T("Орбита; клик по оси — вид с неё")),
                    R(Loc.T("СКМ"), Loc.T("Панорама (так же работает «Рука»)")),
                    R(Loc.T("Ctrl+колесо"), Loc.T("Масштаб")),
                    R(Loc.T("Колесо"), Loc.T("Скорость полёта в 3D")),
                    R("WASD / QE", Loc.T("Полёт камеры, пока зажата кнопка мыши")),
                    R(Loc.T("Стрелки"), Loc.T("Сдвиг выбранного (Shift — точнее, Ctrl — по сетке)")),
                    R("PgUp / PgDn", Loc.T("Поднять / опустить выбранное (Shift — точнее)"))),
            };
            if (Features.Text3D)
                list[list.Length - 1].Rows = Append(list[list.Length - 1].Rows, R(Loc.T("ПКМ по 3D-тексту"), Loc.T("Изменить текст…")));
            return list;
        }
    }
}
