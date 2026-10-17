# Aplat — features

*(Русская версия: [FEATURES.ru.md](FEATURES.ru.md).)*

Aplat is a Windows editor that combines 2D painting on a canvas with a 3D scene of primitives, materials and
light. Projects are stored in `.aplat` files, a ZIP container with readable XML.

## Contents

- [Quick start](#quick-start)
- [Working modes](#working-modes)
- [2D painting](#2d-painting)
- [Ruler](#ruler)
- [3D scene](#3d-scene)
- [Camera and views](#camera-and-views)
- [Lighting and shadows](#lighting-and-shadows)
- [Materials](#materials)
- [Precise positioning](#precise-positioning)
- [Files and export](#files-and-export)
- [Undo history](#undo-history)
- [Interface](#interface)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Limitations](#limitations)

## Quick start

1. Start Aplat: the scene opens with an empty canvas.
2. Pick the brush (**B**) and draw something on the canvas. Colour and size are set in the panels on the right.
3. Add a 3D shape with the "+" menu of the Objects panel. Move, rotate and scale it (**F2**–**F4**).
4. Orbit the scene: the right mouse button rotates the camera, the wheel zooms, and the keys **1**, **3**, **7**,
   **0** switch views.
5. Save the project with Ctrl+S. Later changes are written into the same file automatically, even without Ctrl+S.
   If you opened someone else's or an original file, choose "Save As" right away, before changing anything (see
   [Files and export](#files-and-export)).

## Working modes

The same screen is used for painting and for 3D work; what the mouse does depends on the current tool.

| Mode | How to enable | What the mouse does |
|---|---|---|
| Paint | **B**, tools of the Modes panel | Draws on the canvas and on the surface of 3D shapes |
| Select | **F1**, Esc | Picks objects and live elements |
| Move, rotate, scale | **F2**, **F3**, **F4** | Gizmos on the selected object |
| Shape | **V** | Drags the vertices, edges and faces of the selected object |
| Hand | Hand tool or middle button | Pans the view |

The camera (right button, wheel, digit keys) works in every mode.

## 2D painting

| Tool | Description |
|---|---|
| Brush (B) | A soft stroke with a dense core and a translucent halo |
| Pencil | A grainy graphite stroke |
| Pen | A thin line with a crisp edge |
| Marker | A wide, flat, translucent stroke |
| Eyedropper | Picks a colour from the canvas; an optional switch returns to the previous tool afterwards |
| Fill | Fills a connected area. The fill tolerance (0–100) sets how far the colour of neighbouring pixels may differ from the pixel you clicked and still be filled (0: only the same colour, 100: everything) |
| Eraser (E) | Precise erases pixels down to the base; object removes a whole stroke, shape, text or fill. The kind is chosen in the "2D" panel; E toggles it |
| Select (F1, Esc) | The ordinary cursor: picks objects and live elements and draws nothing |
| Hand | Pans the view (the camera), not the canvas itself |
| Text | Click the canvas and type right on it |
| Area / Lasso | Selects part of the canvas; it can be moved, stretched and rotated |
| Shapes | Line, rectangle, ellipse, triangle |

> **Object limit.** When about 1500 objects accumulate on the canvas, the oldest ones are quietly merged into the
> background and stop being separate objects: see [Limitations](#limitations).

**Brush size.** Use the slider, the `[` and `]` keys, or Alt + wheel over the canvas. Each tool remembers its own
size (defaults: brush 28, pencil 8, pen 4, marker 32, eraser 28, shapes 6). Fill, text and selection have no size.
The cursor ring shows the real width of the stroke.

On the canvas the size is given in sheet pixels and grows with the view zoom: after zooming in, the same stroke
takes more room on the screen. On the surface of a 3D shape the size is given in screen pixels and does not
depend on the zoom.

**Palette.** Thirteen quick colours and a colour picker; a ring marks the current colour.

**Strokes.** Each stroke is drawn as a single smooth curve through the mouse points. The canvas has a resolution of
2048 × 2048.

### Painting on 3D shapes

The brush, pencil, pen, marker, fill, eyedropper and eraser also work on the surface of 3D shapes; shapes and text
there become pixels at once and are not kept editable. The object eraser, area selection and lasso work on the
canvas only. Text on a 3D shape is entered through a dialog.

| Feature | Canvas | 3D shape |
|---|---|---|
| Brush, pencil, pen, marker | yes | yes |
| Fill, eyedropper | yes | yes |
| Precise eraser | yes | yes |
| Shapes | live, editable | pixels at once |
| Text | typed in place, stays editable | dialog, pixels at once |
| Object eraser | yes | no |
| Area, lasso | yes | no |

### Editable elements

Shapes, text and selected areas are "live" elements: they stay editable until you commit them. After that they
become part of the picture (see [Limitations](#limitations)).

- corner handles resize (Shift keeps the proportions; text is always proportional), side handles change only the
  width or height, the circle on top rotates (Shift: 15° steps), dragging the body moves (Shift: along one axis),
  arrow keys nudge by one pixel (Shift: ten);
- colour, thickness and opacity in the "2D" panel change a live element at once. A shape can be an outline, a fill
  or both; clicking another shape tile turns a rectangle into an ellipse or a triangle;
- Enter, a click elsewhere or a change of tool commits the element; Esc or Delete removes it (an area returns to
  its place); Ctrl+Z right after committing makes it editable again;
- Shift (or Ctrl) while drawing a shape gives a square, a circle or a line at exactly 0°, 45° or 90°.

### Text

Clicking with the Text tool puts a caret on the canvas.

- Editing: arrows, Home, End (Ctrl: by words or to the ends), Shift to select, Backspace and Delete,
  Ctrl+A / C / X / V; with the mouse a click places the caret, a drag selects, a double click selects a word.
- Formatting: Ctrl+B, Ctrl+I, Ctrl+U and Ctrl+L / E / R for alignment. The "Text" section of the "2D" panel sets
  the font, size in points, bold, italic, underline and alignment. New text inherits the last formatting; its
  colour and opacity come from the brush.
- Esc or Ctrl+Enter ends typing and leaves the text as an editable element; a double click or Enter opens it for
  editing again.

### Object eraser

Every stroke, shape, text, fill and pasted piece on the canvas is a separate object. The object eraser removes the
topmost visible object under its tip, and what it covered becomes visible again.

- A tap or a sweep; the brush size sets the reach (half of the value).
- It does not react to empty places, such as the middle of an outlined rectangle.
- One sweep is one undo step.

### Lasso

The "Select" tile has an "Area / Lasso" switch. A loop selects the strokes, shapes and text that it encloses or
crosses.

- Moving is done by dragging the frame, scaling by the corner handles, rotating by the round handle (Shift: 15°
  steps).
- A bar above the frame recolours the selection to the brush colour, duplicates it or deletes it.
- Enter or a click outside commits; Esc puts everything back. A whole lasso operation is one undo step.
- The lasso does not pick up fills.

### The back of the canvas

Seen from behind, the sheet shows the back of a stretched canvas: cloth on a wooden frame. You cannot paint there:
the cursor shows "forbidden" and the status bar explains why.

## Ruler

The ruler (**R**, the last button of the floating toolbar) is a wooden scene object with a scale on both sides. The
marks are measured in sheet pixels (the reference sheet is 1024 px): a mark every 25 px, a longer one at 50 and
numbers at every hundred. These are canvas units, not screen pixels: the readings do not change when you zoom
the view.

**Drawing along it.** Start a stroke near an edge of the ruler and the line follows that edge exactly; move the
pen far away and it lets go of the edge. This works for the brush, pencil, pen,
marker and precise eraser at any angle. You cannot draw on the ruler itself: the cursor is crossed out above it.

**Handles** (while a drawing tool is active):

- the round handles at the ends rotate the ruler; the angle is shown next to it; it snaps to 0°, 45° and 90°
  (Shift: 15° steps);
- dragging an end of the body makes it longer or shorter; the other end stays in place, the length is shown in
  pixels and snaps to multiples of 25;
- a double click on an end pins it. A pinned end is marked with a filled dot and its handles are locked, and the
  ruler turns around it. A second double click releases the end; with both ends pinned the ruler cannot be moved;
- Ctrl + dragging the body moves it.

**In the scene.** The ruler is an ordinary object: in 3D mode it can be moved, rotated and scaled. A ruler lifted or
tilted off the sheet stops guiding the pen. Pressing R again removes it, and once more places a new one. The
ruler is saved in `.aplat`, but actions with it are not recorded in the undo history and it is not exported
with models.

## 3D scene

**Primitives:** cube, sphere, cylinder, cone, tetrahedron, pyramid, torus and capsule. New shapes are placed on a
free spot of the floor.

**The canvas** is an object like the others and can be deleted. While it is absent, the "+" menu of the Objects
panel offers "Canvas": it adds a clean sheet below all objects.

**Smoothing.** Surfaces are smoothed automatically while pronounced edges (such as those of a cube) stay sharp.

**Transforms.** The move, rotate and scale gizmos work along the axes.

- Shift + drag: precise, slowed-down mode.
- Ctrl + move: snap to the grid. Ctrl + rotate: 15° steps.
- Alt + grabbing a gizmo: a copy of the object that you drag.
- The rotation rings follow the object's local axes.

**Smart guides.** While an object is dragged it snaps to the centres of other objects on each axis, and a match is
shown by a line. This works without modifiers and can be switched off in the view panel. Snapping to the grid is
done with Ctrl.

**Shape mode** (**V**) drags the vertices, edges and faces of one object. It is not a full mesh editor: no new
faces are created and surfaces cannot be extruded or cut.

- The selected object shows its points and edges; a click picks the nearest point, otherwise the nearest edge,
  otherwise the face. Hidden points and edges cannot be picked.
- A drag moves the picked elements in the screen plane, or along an axis by the X / Y / Z handle or while the X,
  Y or Z key is held. Shift: precise. Ctrl: snap to the grid.
- Vertices at the same place (the poles of a sphere, cube corners) move together.
- Every drag is one undo step.
- Groups, the canvas and locked objects cannot be edited.

**Objects.** Group (Ctrl+G) and ungroup (Ctrl+Shift+G), lock and hide objects with the icons in the Objects list,
rename with a double click. Context menu: copy, rename, hide / show, "To floor", "Reset rotation", grouping and
deletion.

## Camera and views

- The axis widget in the top-right corner shows the camera orientation. A click on an axis gives a side, top or
  front view; a click on the centre gives perspective; dragging rotates the camera.
- Orthographic views: front (**1**), side (**3**), top (**7**). Perspective: **0**. The digit row and the numeric
  keypad both work.
- In perspective, orbiting (right mouse button) and flying with W, A, S, D, Q, E while a mouse button is held are
  available; the wheel changes the flying speed.
- **F** focuses the selection, **Home** shows the whole scene. Pan with the middle mouse button or the Hand tool;
  zoom with Ctrl + wheel.
- The view panel offers the zoom in percent, a zoom slider, "show all", the grid, smart guides, the view switch
  and the flying speed. The camera state is saved in the scene file.

### Auto-rotate (F9)

View → Auto-rotate → Enable (F9) rotates the scene without your input.

- A card on the canvas (bottom right by default) shows what rotates and how fast, and offers "Pause / Resume",
  "Stop" (return to the starting position), "Turn off", the mode switch and a speed slider.
- Mode: **the camera around the object** (around the selection, or around the centre of the scene without a
  selection) or **the objects about their own axis** (the selection, or the whole scene as one body when nothing
  is selected). In both modes the canvas sheet stays in place.
- The speed can also be chosen in the menu: 10, 24, 60 or 120 °/s.
- Rotation pauses while you drag with the mouse. The camera mode works in perspective, so enabling it in a 2D view
  switches to 3D.
- The state is saved in `.aplat`.

## Lighting and shadows

View → Lighting and sun… (**F7**) controls a directional light.

- **Position.** Azimuth and elevation, or the "time of day" slider. This is an artistic control: the slider moves
  the sun along an arc and tints the light warm near the horizon, but does not calculate the real position of the
  sun from a date and latitude.
- **Surface shading.** The brightness of a surface depends on the angle at which the sun's light hits it.
- **Highlights** depend on the camera position.
- **Ambient light** has its own colour and strength, so shadows do not turn black.
- **Light marker** (off by default): a sun symbol standing where the light comes from and an arrow along its
  direction. It does not appear in exported images.
- **Shadows** (the Shadows switch in the dialog and in View → Shadows): objects shade the floor, each other and
  themselves. A density slider sets how dark they are. The switch is saved in `.aplat`, and the last choice
  applies to new scenes.
- The dialog updates the scene at once. "Cancel" restores the previous settings; OK adds an entry to the history.

### Local light

In an object's properties, **Local light → Light nearby objects** turns the object into a point light at its
centre with adjustable colour, power, radius and optional flicker. For a group, the setting applies to the objects
that are switched on as sources.

### Lighting limitations

- Shadows need OpenGL 2.0 with framebuffer objects. Where these are missing, the switch is unavailable with an
  explanation and the scene is drawn without shadows.
- With the sun below about 5° of elevation there is no shadow.
- Local lights cast no shadows; their falloff is approximate.
- The canvas sheet is drawn unlit and receives no shadow, but casts one. Shapes with opacity below 50 % and shapes
  with a cut-out texture cast no shadows; glass receives a shadow in proportion to its opacity.

## Materials

The "Material" section of the properties panel:

| Parameter | Effect |
|---|---|
| Metalness | 0: a dielectric with a white highlight; 1: a metal whose highlight takes the material colour and which reflects the surroundings |
| Roughness | Blurs the highlight: 0 is a mirror, 100 is a matte surface |
| Glow | A visual glow of the material with its own colour; it does not light neighbouring objects (a local light does that) |
| Opacity | From 0 to 100 % |
| Ambient occlusion map | A shading image laid over the base texture |

This is a simplified material model in the spirit of PBR (metalness and roughness), not a physically exact render.

- **Presets:** plastic, glossy varnish, metal, matte metal, chrome, gold, wood, rubber, ceramic, neon, glass and
  PNG cut-outs. When the values match no preset, the field says "custom". For the canvas sheet, which is paper
  drawn unlit, the presets and the metal, gloss and glow controls are disabled.
- **Textures.** A colour or an image from a file, with a "do not tile" option that fits the image once. An image
  can also be dropped on the window. Images are embedded in `.aplat`.
- **Transparency.** *PNG alpha* keeps semi-transparent pixels, *Opaque texture* ignores the alpha channel,
  *PNG cut-outs* make holes at an adjustable threshold, and *Crisp texture pixels* switch to nearest-neighbour
  filtering. *Pass light through transparency* lets a transparent material dim and tint local light; opaque
  geometry blocks it.
- **Metal** reflects an environment (sky, ground and the sun), so the reflection moves over the surface when the
  scene rotates.
- A change applies to the object or to every object of a group. All parameters are saved and copied with the
  object.

## Precise positioning

- Arrow keys move the selection along the screen axes (Shift: 0.1; Ctrl: snap to the grid). PgUp and PgDn raise
  and lower it (Shift: finer).
- The "Object" panel shows the position in canvas pixels, the rotation in degrees and the scale in percent, one
  X · Y · Z row per section.
  - Drag a field sideways to change the value (Shift: finer; Ctrl: steps of 8 px, 15° or 10 %).
  - Click a field to type a number; arithmetic is accepted, for example `512+32` or `-(10*3)`. Enter applies,
    Esc cancels.
  - The wheel and the up and down keys change the value while the field has focus.
  - Each gesture is one undo step.

## Files and export

**Project files (`.aplat`).** A project holds the objects, paint, camera and the undo history. It is a ZIP
container with readable XML and PNG images; the file is not encrypted. A file from a newer version of Aplat is rejected with a message.

> **Before sending a project to someone.** The file contains the edit history, that is, earlier states of the
> picture. If the history holds anything confidential, do not send the `.aplat`: use an image or model export.
> "Save As" does not clear the history.

**Saving.**

- The new file is written next to the old one, verified and only then replaces it. This lowers the risk of
  damaging the previous version if a write fails.
- **Autosave** works only for a project that already has a file: every 10 seconds and when the window closes,
  changes are written straight into that file (the time of the last write is shown in the status bar). There is
  no separate recovery copy; the earlier state of the file is available only through the undo history saved in
  the project.
- A project that has never been saved is not written automatically: when closing, Aplat asks whether to save it.
- **"Don't save" does not restore the file.** For a project with a file, changes have already been written by
  autosave, so answering "Don't save" when closing or opening another project does not bring back the earlier
  state of the file. If a file must stay untouched, choose "Save As" before making changes.
- **File → Recent** lists the eight latest projects.

**Opening.** Drop an `.aplat` on the window to open it; a dropped picture becomes the object's texture. Double
clicking an `.aplat` in Explorer (the installer registers the file type for the current user) or running
`Aplat.exe <file>` opens the project. If Aplat is already running, the project opens in it, and with unsaved
changes it asks: save, do not save or cancel.

**Model export**

| Format | Geometry | Textures and materials | Use |
|---|---|---|---|
| OBJ | yes, with smooth normals | a ZIP with `.mtl` and baked textures: the drawing is kept in the textures, which viewers with OBJ/MTL support can show | Exchange with programs that read OBJ |
| STL | yes, triangulated | no | 3D printing; you are asked whether to include the canvas sheet |
| FBX | yes, with normals and UVs | a material with a colour per object; textures and anything painted on shapes are not carried over | Programs that support FBX 7.4 (binary) |

The ruler is not exported. Every export is written to a temporary file first and then replaces the target.

**Image export.** The canvas as PNG or JPG with its real aspect ratio, and a screenshot of the frame as PNG or
JPG.

**Turntable export.** The camera circles the scene through 360° and writes 4 to 360 PNG frames into a folder.
Aplat does not create a GIF or a video: assembling an animation from the frames is done in another program.

## Undo history

- Undo (Ctrl+Z), redo (Ctrl+Y, Ctrl+Shift+Z). Each step is named after the action it undoes (Brush, Move, Fill,
  Group…).
- **While you work** the number of steps is not limited, but memory is: while the history takes up to 160 MB,
  everything is kept; beyond that the oldest steps are dropped, but the five latest always stay.
- **In the project file** the history is saved and survives a restart, but only the newest steps within about
  48 MB are written (at least five).
- **The "History" list** jumps to any of the 30 newest states; older steps remain reachable through ordinary undo.

## Interface

- **Themes:** Light and Graphite (dark), plus "Like Windows", which follows the system setting (View → Theme). The
  choice is remembered.
- **Languages:** English and Russian (View → Language). The first start follows the Windows display language; a
  change takes effect after a restart.
- **Icons:** one icon set ([Lucide](https://lucide.dev)).
- **Properties panel** on the right with the "Object", "Material" and "2D" sections.
- **Floating panels:** the toolbar, the Objects list, the view panel, the recent-tools panel and the auto-rotate
  card.
  - Each panel is docked to one of eight anchors of the viewport (corners and edge midpoints) or free. While you
    drag it near an edge, an outline shows where it will land (hold Alt to move without snapping). Right-click a
    panel: "Dock", "Free", "Hide".
  - **View → Panels** shows or hides the toolbars and every panel; "Position and transparency…" sets where each
    one sits and how transparent the panels are; "Reset the layout" restores the defaults. The layout is
    remembered.
  - The "File" and "Modes" toolbars can also be docked to any window edge or undocked into a floating window.
- **Command search (Ctrl+Q, Help → Search commands).** One list of everything the program can do: menu items,
  panel buttons, tools, keys and recent files. Commands are found by name in either language; the search
  tolerates typos and a wrong keyboard layout. Recently run commands come first.
- **Menus.** File, Edit, Object, View, Help. Items that cannot be used are greyed out, and each item shows its
  shortcut.
- **Status bar hints** describe what the mouse and modifiers do for the current tool.
- **View → Outline the polygons of the selection.** When switched off, selected objects are marked with a
  bounding box.
- **View → Show FPS** shows a plate with the frames per second, processor load and memory. It can be dragged
  inside the viewport; a double click puts it back.
- **Help → About** shows the version, developer, licence and links, and can check for updates. The automatic check
  at start-up can be switched off in **Help → Check for updates at start-up**.
- The window remembers its size, position and maximised state. Esc clears the selection and then returns to the
  Select tool; Ctrl+A selects everything; Ctrl+D duplicates.

## Keyboard shortcuts

The same list is available in the program: Help → Keyboard shortcuts.

| Keys | Action |
|---|---|
| Ctrl+Q | Command search |
| Ctrl+N / Ctrl+O / Ctrl+S | New / open / save |
| Ctrl+Shift+S | Save as |
| Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z | Undo / redo |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / cut / paste objects |
| Ctrl+D | Duplicate |
| Ctrl+A | Select all |
| Delete | Delete the selection |
| Ctrl+G / Ctrl+Shift+G | Group / ungroup |
| Esc | Clear the selection, then the Select tool |
| F1–F4 | Select / move / rotate / scale |
| V | Shape mode |
| B / E / R | Brush / eraser / ruler |
| `[` / `]`, Alt + wheel | Brush size |
| F5 / F6 / F8 | Wireframe / vertices / grid |
| F7 | Lighting and sun |
| F9 | Auto-rotate |
| F / Home | Focus the selection / the whole scene |
| 1 / 3 / 7 / 0 (and the numeric keypad) | Front / side / top / perspective |
| Arrows (+Shift, +Ctrl) | Nudge the selection (precise, to the grid) |
| PgUp / PgDn (+Shift) | Raise / lower the selection |
| Shift + gizmo drag | Precise transform |
| Ctrl + move / rotate | Snap to the grid / 15° steps |
| Alt + gizmo drag | Drag a copy |
| Right / middle mouse button, wheel | Orbit / pan / zoom |
| W A S D Q E | Fly the camera (while a mouse button is held) |

## Limitations

- **Committed elements.** Shapes, text and selections are editable only until committed. Ctrl+Z right after
  committing brings editing back; later the element can only be erased or drawn again.
- **Object limit on the canvas.** When about 1500 objects (strokes, shapes, text, fills) accumulate on the canvas,
  the oldest ones are quietly merged into the background: the object eraser and the lasso no longer see them, and
  they can be removed only with the precise eraser. There is no warning and it cannot be switched off. The picture
  of a project from an old format is a background from the start.
- **Painting on 3D shapes.** The object eraser, area selection and lasso work on the canvas only; shapes and text
  on a 3D shape become pixels at once (see the [table](#painting-on-3d-shapes)).
- **Ruler.** Actions with it are not recorded in the undo history.
- **Shape mode.** Only moving vertices, edges and faces; no extrusion, no new faces and no cuts.
- **Lighting.** Local lights cast no shadows; shadows need OpenGL 2.0 (see [Lighting limitations](#lighting-limitations)).
- **Export.** STL stores no colour or textures, and FBX does not carry textures or anything painted on shapes (OBJ keeps them). Aplat does not create animations (GIF, video), only turntable frames.
- **The project file** is not encrypted and contains the edit history (see [Files and export](#files-and-export)).
