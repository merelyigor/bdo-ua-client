# BDO Dark Theme — Implementation Plan

## Overview

Apply BDO dark theme (black + gold) to MainForm. All functionality remains unchanged. Only visual styling.

## Prerequisites

- NuGet package `ReaLTaiizor` version 3.8.0.3 already installed
- `ThemePrototype.cs` exists as visual reference (can be deleted after implementation)

## Color Palette

```
Background (main):      #121212  (RGB 18,18,18)
Background (panels):    #1C1C1C  (RGB 28,28,28)
Background (controls):  #2D2D2D  (RGB 45,45,45)
Gold (accent):          #C8A415  (RGB 200,164,21)
Gold (hover):           #DCBA2B  (RGB 220,186,43)
Text (primary):         #F0F0F0  (RGB 240,240,240)
Text (secondary):       #A0A0A0  (RGB 160,160,160)
Text (disabled):        #666666  (RGB 102,102,102)
Success:                #00B400  (RGB 0,180,0)
Error:                  #FF4444  (RGB 255,68,68)
Border:                 #3D3D3D  (RGB 61,61,61)
```

## Files to Modify

### 1. `MainForm.Designer.cs`

Apply colors to all controls:

**Form:**
- `BackColor = Color.FromArgb(18, 18, 18)`
- `ForeColor = Color.FromArgb(240, 240, 240)`

**GroupBoxes (gameGroupBox, modeGroupBox, statusGroupBox):**
- `BackColor = Color.FromArgb(28, 28, 28)`
- `ForeColor = Color.FromArgb(240, 240, 240)`
- `Font = new Font("Segoe UI", 9F, FontStyle.Bold)`

**Labels (gameStatusLabel, gamePathLabel, localizationStateLabel, installedInfoLabel, detailsLabel, progressLabel, versionLabel):**
- `ForeColor = Color.FromArgb(240, 240, 240)` (or GrayText for secondary)
- `BackColor = Color.Transparent`

**Buttons (detectGameButton, browseGameButton, installButton, restoreOriginalButton, cancelButton, updateButton):**
- `FlatStyle = FlatStyle.Flat`
- `BackColor = Color.FromArgb(45, 45, 45)`
- `ForeColor = Color.FromArgb(240, 240, 240)`
- `FlatAppearance.BorderColor = Color.FromArgb(61, 61, 61)`
- `FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 164, 21)`

**installButton (special — gold accent):**
- `BackColor = Color.FromArgb(200, 164, 21)`
- `ForeColor = Color.FromArgb(18, 18, 18)`
- `Font = new Font("Segoe UI", 10F, FontStyle.Bold)`

**ProgressBar:**
- `ForeColor = Color.FromArgb(200, 164, 21)` (gold)
- `BackColor = Color.FromArgb(45, 45, 45)`

**TextBox (messageTextBox):**
- `BackColor = Color.FromArgb(45, 45, 45)`
- `ForeColor = Color.FromArgb(160, 160, 160)`
- `BorderStyle = BorderStyle.FixedSingle`

**RadioButton (dynamic, created in BuildDynamicModes):**
- `ForeColor = Color.FromArgb(240, 240, 240)`
- `BackColor = Color.Transparent`

**FlowLayoutPanel (modesFlowPanel):**
- `BackColor = Color.Transparent`

**TableLayoutPanel (mainLayout, gameLayout, statusLayout, progressPanel, footerPanel):**
- `BackColor = Color.Transparent`

### 2. `MainForm.cs`

**BuildDynamicModes() — RadioButton styling:**
After creating each RadioButton, add:
```csharp
rb.ForeColor = Color.FromArgb(240, 240, 240);
rb.BackColor = Color.Transparent;
```

**ShowModeLoadingPlaceholder() — Label styling:**
```csharp
label.ForeColor = Color.FromArgb(160, 160, 160);
```

**ShowModeFailurePlaceholder() — Label styling:**
```csharp
label.ForeColor = Color.FromArgb(160, 160, 160);
```

**SetLocalizationStateText() — already sets ForeColor, keep as is.**

**ApplyLocalizationStatePresentation() — update colors:**
```csharp
UpToDate => Color.FromArgb(0, 180, 0),  // green
Corrupted => Color.FromArgb(255, 68, 68),  // red
_ => Color.FromArgb(240, 240, 240)  // default white
```

**SetGameFound() — update green:**
```csharp
gameStatusLabel.ForeColor = Color.FromArgb(0, 180, 0);
```

**SetGameNotFound() — update gray:**
```csharp
gameStatusLabel.ForeColor = Color.FromArgb(160, 160, 160);
```

**SetGameSearching() — update gray:**
```csharp
gameStatusLabel.ForeColor = Color.FromArgb(160, 160, 160);
```

### 3. `MainForm.Designer.cs` — Remove GroupBox borders (optional, cleaner look)

Replace GroupBox with Panel + Label for cleaner BDO style:
- Panel with dark background
- Label as title with gold color
- This is optional — GroupBox with dark colors also works

## Implementation Steps

### Step 1: Add color constants
Add static readonly color fields to MainForm.cs:
```csharp
private static readonly Color BdoBlack = Color.FromArgb(18, 18, 18);
private static readonly Color BdoDarkGray = Color.FromArgb(28, 28, 28);
private static readonly Color BdoMediumGray = Color.FromArgb(45, 45, 45);
private static readonly Color BdoGold = Color.FromArgb(200, 164, 21);
private static readonly Color BdoWhite = Color.FromArgb(240, 240, 240);
private static readonly Color BdoGrayText = Color.FromArgb(160, 160, 160);
private static readonly Color BdoGreen = Color.FromArgb(0, 180, 0);
private static readonly Color BdoRed = Color.FromArgb(255, 68, 68);
private static readonly Color BdoBorder = Color.FromArgb(61, 61, 61);
```

### Step 2: Style MainForm
Set BackColor and ForeColor in InitializeComponent.

### Step 3: Style GroupBoxes
Set BackColor and ForeColor for gameGroupBox, modeGroupBox, statusGroupBox.

### Step 4: Style all Labels
Set ForeColor for all labels. Use BdoWhite for primary, BdoGrayText for secondary.

### Step 5: Style all Buttons
Set FlatStyle, BackColor, ForeColor, FlatAppearance for all buttons.
Make installButton gold (BdoGold background, BdoBlack text).

### Step 6: Style ProgressBar
Set ForeColor (gold) and BackColor (dark gray).

### Step 7: Style TextBox (messageTextBox)
Set BackColor, ForeColor, BorderStyle.

### Step 8: Update BuildDynamicModes()
Add ForeColor and BackColor to dynamically created RadioButtons.

### Step 9: Update helper methods
Update SetGameFound, SetGameNotFound, SetGameSearching, ApplyLocalizationStatePresentation to use new colors.

### Step 10: Test
- Run with `--prototype` flag to verify colors
- Run normal mode to verify functionality
- Test all states: NotInstalled, UpToDate, UpdateAvailable, Corrupted
- Test Install, Restore Original, Cancel operations
- Test mode switching
- Test game detection

## Do NOT Change

- Any business logic
- Any event handlers
- Any API calls
- Any file operations
- Any state management
- Layout structure (only colors/fonts)
- Control sizes or positions
- Enabled/disabled logic

## Testing Checklist

- [ ] App starts with dark background
- [ ] All text is readable (white on dark)
- [ ] GroupBoxes have dark background
- [ ] Buttons have dark background with visible borders
- [ ] Install button is gold
- [ ] ProgressBar is gold
- [ ] RadioButton text is readable
- [ ] Status colors work (green for OK, red for error)
- [ ] Game detection works
- [ ] Mode selection works
- [ ] Install works
- [ ] Restore Original works
- [ ] Cancel works
- [ ] Update notification visible
- [ ] Version label visible
- [ ] Logs button visible
- [ ] All text is in Ukrainian (no English labels)
