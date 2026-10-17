# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-18

First public release.

### Added

- 2D painting on a canvas sheet: brush, pencil, pen, marker, fill, eyedropper, precise and object eraser, shapes,
  text, area selection and lasso. Shapes, text and selections stay editable until they are committed.
- Ruler: a wooden scene object that guides the pen along its edges and can be moved, rotated and scaled in 3D.
- 3D scene: primitives, move / rotate / scale gizmos, grouping, locking, a Shape mode for dragging vertices, edges
  and faces, painting on shapes, materials with metalness and roughness, textures, transparency and local lights.
- Sun lighting with highlights, metal reflections and soft shadows (OpenGL 2.0 with framebuffer objects).
- Auto-rotate of the camera or of the objects.
- Undo history bounded by memory rather than by step count, saved in the project.
- Project format `.aplat`: a ZIP container with readable XML and PNG layers; autosave for projects that have a file.
- Export: OBJ with MTL and textures, binary STL, binary FBX 7.4, PNG / JPG images and turntable frames.
- Floating, dockable panels; command search (Ctrl+Q); Light and Graphite themes; English and Russian interface.
- Per-user Windows installer and a portable ZIP.

### Security

- Hardened loading of project files: size limits on archive entries, XML entity processing disabled, and no
  access to network paths or URLs from texture references. See [SECURITY.md](SECURITY.md).

### Known limitations

See the Limitations section of [FEATURES.md](FEATURES.md#limitations).

[Unreleased]: https://github.com/vindfjur/Aplat/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/vindfjur/Aplat/releases/tag/v1.0.0
