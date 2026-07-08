# Milestone B3b — Slot Picker Full-Match Restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline — live-Unity-Editor work, single instance, sequential compile order). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Restyle the Slot Picker to `hangar-slot-picker.png` by reusing B3a's primitives — warm gradient background, ink-bordered + toon-shadowed dark cards, ochre slot titles, sand stats.

**Architecture:** All edits in `Assets/Scripts/HangarSelect/HangarSelectController.cs`. Reuse `UIStyle.BuildBrandBackground`, `CscTheme.AddToonOutline`, `CscTheme.AddToonShadow`; recolor via `CscPalette`/`CscTheme`. No new primitives. Cards are axis-aligned so the B3a plate-AA concern doesn't apply.

**Tech Stack:** Unity 6.3 LTS, C# (`Assembly-CSharp`), legacy `UnityEngine.UI`, UnityMCP for compile verification.

**Verification model:** No automated tests. `read_console` (clean compile) after edits; final visual match + working navigation/delete-confirm is the maintainer's Play-check.

**Source of truth:** spec `docs/superpowers/specs/2026-07-07-ui-rebrand-b3b-design.md`; mockup `unity_handoff/reference/screens/hangar-slot-picker.png`. `CscTheme`/`CscPalette`/`UIStyle` are already imported (the file uses `UIStyle`).

**Note on edits:** each step gives the exact line to add and the anchor it follows. Because prior sub-phases shifted line numbers, **Read the method first, then Edit** against the exact current text (the anchors below are verbatim from the current file).

---

### Task 0: Pre-flight

- [ ] Confirm branch `explore/ui-rebrand`; `mcpforunity://instances` shows one live instance; `read_console` (error) clean (MCP-infra only).

---

### Task 1: `BuildUI` — background, title color, cancel shadow

**Files:** Modify `Assets/Scripts/HangarSelect/HangarSelectController.cs`

- [ ] **Step 1: Background.** After the canvas is created (anchor:
  `Canvas canvas = UIStyle.BuildScreenSpaceCanvas("HangarSelectCanvas", sortingOrder: 200);`
  and its `RectTransform root = (RectTransform)canvas.transform;`), insert on the next line:
```csharp
            UIStyle.BuildBrandBackground(root);
```

- [ ] **Step 2: Title color.** After the title build
  (`Text title = UIStyle.BuildLabel(root, "Choose a Slot", fontSize: 72, style: FontStyle.Bold, font: CscTheme.DisplayOr);`)
  add:
```csharp
            title.color = CscPalette.Sand100;
```

- [ ] **Step 3: Cancel shadow.** After `cancelButton.onClick.AddListener(OnCancel);` add:
```csharp
            CscTheme.AddToonShadow(cancelButton.gameObject, 6f);
```

- [ ] **Step 4: Compile.** `refresh_unity` (force/scripts/request) → poll state → `read_console`. No C# errors.

- [ ] **Step 5: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/HangarSelect/HangarSelectController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): brand background on the slot picker (B3b)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `BuildCard` — card fill/outline/shadow, title/body colors, button shadows

**Files:** Modify `Assets/Scripts/HangarSelect/HangarSelectController.cs`

- [ ] **Step 1: Card fill + outline + shadow.** Replace the card-bg color line
  `bg.color = new Color(0.10f, 0.10f, 0.14f, 0.92f);` with the `CscTheme` fill, and add
  the outline + shadow on the card root right after `card.Root = rt;`:
```csharp
            bg.color = CscTheme.CardFill;
```
  then after `card.Root = rt;`:
```csharp
            CscTheme.AddToonOutline(rootGO, 3f);
            CscTheme.AddToonShadow(rootGO, 6f);
```

- [ ] **Step 2: Slot title color.** After the card title build
  (`Text title = UIStyle.BuildLabel(rt, $"Slot {slot + 1}", fontSize: 32, style: FontStyle.Bold, font: CscTheme.DisplayOr);`)
  add:
```csharp
            title.color = CscPalette.Ochre300;
```

- [ ] **Step 3: Body color.** After the body build
  (`Text body = UIStyle.BuildLabel(rt, string.Empty, fontSize: 22);`) add:
```csharp
            body.color = CscPalette.Sand100;
```

- [ ] **Step 4: Button shadows.** Add a toon shadow to each of the three card buttons —
  after `card.PrimaryButton = primary;`, after `card.DeleteButton = del;`, and after
  `card.DeleteCancelButton = delCancel;` respectively:
```csharp
            CscTheme.AddToonShadow(primary.gameObject, 6f);
```
```csharp
            CscTheme.AddToonShadow(del.gameObject, 6f);
```
```csharp
            CscTheme.AddToonShadow(delCancel.gameObject, 6f);
```

- [ ] **Step 5: Compile.** `refresh_unity` → poll → `read_console`. No C# errors.

- [ ] **Step 6: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/HangarSelect/HangarSelectController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): restyle slot cards — fill, ink border, shadow, ochre titles (B3b)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: `RefreshAllCards` — muted "last edited" line

**Files:** Modify `Assets/Scripts/HangarSelect/HangarSelectController.cs`

- [ ] **Step 1: Read `RefreshAllCards`** (and any helper it calls to build the filled-slot
  body string, e.g. a `$"…\nLast edited …"` assembly). Identify where the `Last edited …`
  line is concatenated into the body text.

- [ ] **Step 2: Mute that line** by wrapping just it in a rich-text color tag (Steel300 =
  `#7E776C`). For a body assembled like `$"{cls}\n{cubes} · Mass {mass}\nHP {hp}\nLast edited {when}"`,
  change the last segment to `\n<color=#7E776C>Last edited {when}</color>`. uGUI
  `Text.supportRichText` is on by default, so no other change is needed.
  **Fallback:** if the string is assembled in a way that makes wrapping one line awkward,
  accept the single `Sand100` body color for this first take and note it at the gate.

- [ ] **Step 3: Compile.** `refresh_unity` → poll → `read_console`. No C# errors.

- [ ] **Step 4: Commit.**
```bash
git -C "/Users/anon/My project" add "Assets/Scripts/HangarSelect/HangarSelectController.cs"
git -C "/Users/anon/My project" commit -m "feat(ui): muted last-edited line on slot cards (B3b)" \
  -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Verify + present for the gate

- [ ] **Step 1:** `read_console` (error) → clean.
- [ ] **Step 2:** Present for the maintainer's Play-mode gate: Slot Picker matches
  `hangar-slot-picker.png` (warm bg, dark ink-bordered + shadowed cards, ochre `SLOT n`,
  sand stats, muted last-edited); Continue/Start loads the slot, Delete shows the two-click
  confirm, Cancel returns to menu. **Hold the push**; on confirm, push onto
  `explore/ui-rebrand` + dispatch the B3b internal review. Merge to `main` = hard gate.

---

## Self-review

- **Spec coverage:** background (T1) ✓; title→Sand100 (T1) ✓; card fill=CardFill + outline + shadow (T2) ✓; slot title→Ochre300 (T2) ✓; body→Sand100 (T2) ✓; button shadows incl. bottom Cancel (T1/T2) ✓; muted last-edited via rich text w/ fallback (T3) ✓; ochre primary fill correctly **absent** (B4) ✓; verify + gate (T4) ✓. All spec scope mapped.
- **Placeholders:** none — every added line is concrete (exact `CscPalette`/`CscTheme` members + values). T3 defers only the exact string-edit site to a read (the wrap value `#7E776C` and method are specified), which is a read-then-edit instruction, not a vague placeholder.
- **Type/name consistency:** `UIStyle.BuildBrandBackground`, `CscTheme.AddToonOutline/AddToonShadow/CardFill`, `CscPalette.Sand100/Ochre300` — all real members from B1/B3a. Local names (`root`, `title`, `body`, `bg`, `rootGO`, `rt`, `primary`, `del`, `delCancel`, `cancelButton`) match the current file.
