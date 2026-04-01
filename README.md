# HMI Asset Workspace Scaffold

This package is a starter scaffold for a Unity EditorWindow-based HMI Asset Workspace.

## Included
- Package structure
- Editor asmdef
- Core Observable/EventChannel primitives
- Data models and state
- Service interfaces and starter implementations
- Controller skeletons
- EditorWindow bootstrapping
- Minimal UXML + USS

## Open the window
Unity menu: `Window > HMI Asset Workspace`

## Recommended next steps
1. Replace the simple `ListView` with your virtualized card/grid implementation.
2. Extend `AssetEntry` metadata and indexing.
3. Add dependency scanning and package install workflow.
4. Swap placeholder preview logic for `PreviewRenderUtility` strategies.
5. Add tests around `SelectionController` and `ActionController`.
