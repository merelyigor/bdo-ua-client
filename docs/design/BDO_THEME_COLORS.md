# BDO Theme — Quick Reference

## Colors (copy-paste ready)

```csharp
// Backgrounds
Color.FromArgb(18, 18, 18)      // BdoBlack — form background
Color.FromArgb(28, 28, 28)      // BdoDarkGray — panels, GroupBox
Color.FromArgb(45, 45, 45)      // BdoMediumGray — buttons, textbox, progress

// Accent
Color.FromArgb(200, 164, 21)    // BdoGold — install button, progress bar, accents
Color.FromArgb(220, 186, 43)    // BdoLightGold — hover state

// Text
Color.FromArgb(240, 240, 240)   // BdoWhite — primary text
Color.FromArgb(160, 160, 160)   // BdoGrayText — secondary text, labels

// Status
Color.FromArgb(0, 180, 0)       // BdoGreen — success, UpToDate
Color.FromArgb(255, 68, 68)     // BdoRed — error, Corrupted

// Border
Color.FromArgb(61, 61, 61)      // BdoBorder — button borders
```

## Button Template (standard)

```csharp
button.FlatStyle = FlatStyle.Flat;
button.BackColor = Color.FromArgb(45, 45, 45);
button.ForeColor = Color.FromArgb(240, 240, 240);
button.FlatAppearance.BorderColor = Color.FromArgb(61, 61, 61);
button.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 164, 21);
```

## Button Template (gold — Install)

```csharp
installButton.FlatStyle = FlatStyle.Flat;
installButton.BackColor = Color.FromArgb(200, 164, 21);
installButton.ForeColor = Color.FromArgb(18, 18, 18);
installButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
```

## GroupBox Template

```csharp
groupBox.BackColor = Color.FromArgb(28, 28, 28);
groupBox.ForeColor = Color.FromArgb(240, 240, 240);
groupBox.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
```

## Label Template

```csharp
// Primary label
label.ForeColor = Color.FromArgb(240, 240, 240);
label.BackColor = Color.Transparent;

// Secondary label
label.ForeColor = Color.FromArgb(160, 160, 160);
label.BackColor = Color.Transparent;
```

## TextBox Template

```csharp
textBox.BackColor = Color.FromArgb(45, 45, 45);
textBox.ForeColor = Color.FromArgb(160, 160, 160);
textBox.BorderStyle = BorderStyle.FixedSingle;
```

## ProgressBar Template

```csharp
progressBar.ForeColor = Color.FromArgb(200, 164, 21);
progressBar.BackColor = Color.FromArgb(45, 45, 45);
```

## RadioButton Template (dynamic)

```csharp
rb.ForeColor = Color.FromArgb(240, 240, 240);
rb.BackColor = Color.Transparent;
```

## Status Colors

```csharp
// UpToDate
localizationStateLabel.ForeColor = Color.FromArgb(0, 180, 0);

// Corrupted
localizationStateLabel.ForeColor = Color.FromArgb(255, 68, 68);

// Default/other
localizationStateLabel.ForeColor = Color.FromArgb(240, 240, 240);
```

## Files Modified

1. `MainForm.Designer.cs` — colors for all controls
2. `MainForm.cs` — color constants, BuildDynamicModes, status helpers

## Files NOT Modified

- `Program.cs` — no changes
- `Api/` — no changes
- `Services/` — no changes
- `Storage/` — no changes
- `Logging/` — no changes
