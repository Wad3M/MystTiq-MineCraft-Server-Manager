# MystMC v2.1.5 — Compile Fix

- Fixed CS1503 in `PrototypeWindow.xaml.cs` when the Create Server page passes the read-only Download Source `TextBlock` into `AddFormRow`.
- `AddFormRow` now accepts `FrameworkElement` instead of `Control`, allowing both interactive WPF controls and display-only elements without casts or duplicated layout helpers.
- Preserves the v2.1.4 dark-theme control styling and v2.1.3 server installer functionality.
