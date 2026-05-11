# Weapon Shooting System — Implementation Spec

Status: **planning — implementation not started**.

This document tracks the work package for the construct's weapon
firing system. It covers:

1. A **crosshair** UI that represents the ship's true aim direction.
2. **Weapon firing behaviours** for the two existing weapon shapes
   (Pyramid, Cylinder) — including projectile spawning, direction
   selection, and per-weapon reload.
3. **Projectile objects** (`Bullet`, `Rocket`) and their movement
   logic.
4. **Fire input** wiring and the controller that dispatches LMB
   presses to all weapons of the **currently selected** weapon
   type.
5. **Weapon selection toolbar** in the Fly scene with mouse scroll
   and number-key selection, plus per-type reload progress bars.

The shooting system data model (categories, coupled weapon
materials, face-validity) is already in place from prior PRs. This
spec is purely about *what happens when the player presses fire*.

---

## 0. What this PR ships vs what's deferred

**In scope (v1):**

- Crosshair UI in the Fly scene that drifts on screen as the ship
  rotates faster than the camera.
- `WeaponBehavior` abstract base + `PyramidWeapon` and
  `CylinderWeapon` concrete subclasses on the respective prefabs.
- Bullet and Rocket projectile prefabs with straight-line motion.
- Pyramid aim logic: frontal pyramids → shoot at crosshair; off-axis
  pyramids → shoot along their tip direction.
- Cylinder aim logic: spawn inside hollow, launch along the "barrel"
  (opposite of placement face), then redirect to the crosshair point
  that was active *at fire time*.
- Per-weapon reload cooldowns (per-instance; instances of the same
  type stay synchronised because they fire on the same frame).
- LMB → Fire input action, **hold-to-fire** — every frame LMB is
  held, fire any selected-type weapons whose cooldown has elapsed.
- **Weapon-type selection** via mouse scroll wheel or digit keys
  `1`–`9`. Selected type's instances are the only ones that fire on
  LMB.
- **Weapon toolbar** in the Fly scene: one button per weapon type
  on the construct, with a per-type reload progress bar above each
  button. Active type highlighted. Hidden entirely when the
  construct has no weapons.
- `FlyShootingController` owns input + selection + dispatch.
- `FlyWeaponToolbarController` owns the toolbar UI; reflects
  selection and cooldown state.
- Projectiles despawn after a max range / lifetime.

**Out of scope (v2+):**

- Damage application and HP depletion on placed cubes.
- Hit detection / colliders on projectiles.
- Splash damage radius for the rocket.
- Audio (gunfire, explosions).
- Visual effects (muzzle flash, tracer, explosion).
- Destructible / despawning cubes when HP reaches 0.
- Multiplayer / synchronisation.
- Click-on-toolbar-to-select (UX-only follow-up; selection is via
  scroll wheel / digits in v1).

The v1 ship is a *visible* weapon system: projectiles spawn, fly
the correct paths, and disappear at range. Damage and feedback are
deliberately deferred so the firing surface lands first.

---

## 1. Crosshair

### 1.1 Concept

The third-person camera (`FlyCamera`) smoothly tracks construct
rotation via `Slerp(currentRotation, targetRotation, followSpeed *
Time.deltaTime)`. The camera *lags* behind the construct during fast
turns. A reticle locked to screen center would represent the
camera's forward direction — which is wrong, because the ship's
weapons fire in the *construct's* forward direction.

The crosshair must therefore be a **screen-space projection of the
construct's forward vector**. During steady straight-line flight,
projection equals screen center. During fast yaw / pitch / roll, the
reticle drifts away from center, showing the player exactly where
shots actually go.

### 1.2 Algorithm (per frame, `LateUpdate`)

```text
aimPoint  = construct.position + construct.forward * aimRange
screenPos = flyCamera.WorldToScreenPoint(aimPoint)
if screenPos.z < 0:                # aim is behind camera
    hide crosshair
else:
    crosshair.position = screenPos # for screen-space-overlay canvas
```

- `aimRange = 100f` world units — confirmed.
- Execution order: `[DefaultExecutionOrder(100)]` on the controller
  so it runs after `FlyCamera.LateUpdate`.

### 1.3 Reticle visual

Programmatic UI, no external sprite asset:

- 5 white `Image` rectangles parented to a single
  `_crosshairRoot` `RectTransform`:
  - center dot 4×4 px
  - 4 arms 2×12 px arranged around a 2 px gap (`+` shape)
- Color: white by default — confirmed.

### 1.4 Canvas placement

- Screen Space Overlay canvas at `sortingOrder: 110`. Above the
  Build toolbar (90) and below the Fly weapon toolbar (120) and
  pause overlay (300).

### 1.5 Pause / scene-transition handling

- **Freeze entirely while paused** — confirmed. When
  `PauseMenu.Instance.IsOpen`, skip the per-frame projection and
  leave the reticle at its last computed position. The pause dim
  (sortingOrder 300) covers it visually anyway.

### 1.6 Edge cases

| Case | Behavior |
|---|---|
| Construct facing behind camera | `screenPos.z < 0` → hide entirely. |
| First frame after scene load | Skip if `construct == null` or `flyCamera == null`. |
| Crosshair perfectly centered | Steady flight or aligned construct. |

---

## 2. Weapon firing data model

### 2.1 Class hierarchy

```
WeaponBehavior (abstract)
 ├── PyramidWeapon        (component on PlacedPyramid.prefab)
 └── CylinderWeapon       (component on PlacedCylinder.prefab)
```

Each `WeaponBehavior` instance lives on the spawned weapon
GameObject in flight (instantiated by `FlyController.BuildConstruct`).
The component is invoked by `FlyShootingController` per frame —
never reads input directly.

### 2.2 `WeaponBehavior` (abstract base)

```csharp
public abstract class WeaponBehavior : MonoBehaviour
{
    [SerializeField] protected GameObject projectilePrefab;
    [SerializeField] protected float reloadSeconds = 0.2f;
    [SerializeField] protected float damage = 1f;          // for v2
    [SerializeField] protected float armorPenetration = 0f;// for v2

    // Set by FlyController.BuildConstruct after instantiation.
    public Transform Construct { get; set; }

    // Set by FlyController.BuildConstruct so the toolbar can group
    // weapons by type and the firing controller can address each
    // type independently. The SO carries displayName + swatch color
    // (via its weaponMaterial) for UI rendering.
    public ShapeDefinition Shape { get; set; }

    float _cooldownRemaining;
    public float CooldownRemaining => _cooldownRemaining;
    public float ReloadSeconds => reloadSeconds;
    public bool CanFire => _cooldownRemaining <= 0f;

    void Update()
    {
        if (_cooldownRemaining > 0f) _cooldownRemaining -= Time.deltaTime;
    }

    public void TryFire(Vector3 crosshairWorldTarget)
    {
        if (!CanFire) return;
        _cooldownRemaining = reloadSeconds;
        Fire(crosshairWorldTarget);
    }

    protected abstract void Fire(Vector3 crosshairWorldTarget);
}
```

### 2.3 `PyramidWeapon`

**Aim rule:**

- Frontal pyramid (tip aligned with `construct.forward`) → fire
  toward the crosshair world target.
- Off-axis pyramid → fire straight along its tip direction.

The pyramid's tip in local space is at `(0, +0.5, 0)`. After
placement rotation, world-space tip direction is `transform.up`
and world-space tip position is
`transform.TransformPoint(Vector3.up * 0.5f)`.

```csharp
protected override void Fire(Vector3 crosshairWorldTarget)
{
    Vector3 tipPos = transform.TransformPoint(new Vector3(0, 0.5f, 0));
    Vector3 tipDir = transform.up;

    Vector3 fireDir = (Vector3.Dot(tipDir, Construct.forward) > 0.7f)
        ? (crosshairWorldTarget - tipPos).normalized
        : tipDir;

    SpawnBullet(tipPos, fireDir);
}
```

Threshold 0.7 = cos 45°. With 90°-stepped rotations the dot is
exactly ±1 or 0, so the boundary is unambiguous.

**Pyramid stats (defaults):**

| Field | v1 default |
|---|---|
| `reloadSeconds` | 0.2 |
| `damage` | 2 (placeholder — applied in v2) |
| `armorPenetration` | 1 |
| `projectilePrefab` | Bullet |

### 2.4 `CylinderWeapon`

Placement orientation determines the launch direction only; the
rocket then redirects to the crosshair world target captured at
fire time.

```csharp
[SerializeField] float launchExitDistance = 0.5f; // ~half a cube

protected override void Fire(Vector3 crosshairWorldTarget)
{
    Vector3 spawnPos = transform.position;          // hollow centre
    Vector3 launchDir = transform.up;               // opposite of placement face
    Vector3 exitPos = spawnPos + launchDir * launchExitDistance;

    SpawnRocket(spawnPos, launchDir, exitPos, crosshairWorldTarget);
}
```

**Cylinder stats:**

| Field | v1 default |
|---|---|
| `reloadSeconds` | 3.0 |
| `damage` | 20 (splash, applied in v2) |
| `armorPenetration` | 2 (low — confirmed) |
| `projectilePrefab` | Rocket |
| `launchExitDistance` | 0.5 |

---

## 3. Projectile objects

### 3.1 `Bullet`

Visual: small sphere (Unity built-in `Sphere` primitive mesh),
scaled to ~0.05 units (5% of a cube width). Yellow tracer.

Components: `MeshFilter` (Sphere), `MeshRenderer` (`BulletMat`),
`Bullet` MonoBehaviour. No `Rigidbody`, no `Collider` in v1.

```csharp
public class Bullet : MonoBehaviour
{
    [SerializeField] float speed = 80f;
    [SerializeField] float maxRange = 200f;

    Vector3 _direction;
    float _traveled;

    public void Launch(Vector3 origin, Vector3 direction)
    {
        transform.position = origin;
        transform.rotation = Quaternion.LookRotation(direction);
        _direction = direction.normalized;
        _traveled = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        transform.position += _direction * speed * dt;
        _traveled += speed * dt;
        if (_traveled >= maxRange) Destroy(gameObject);
    }
}
```

### 3.2 `Rocket`

Visual: Unity built-in `Capsule` mesh, scaled to **1/8 of a cube
width (= 0.125 units)** radially. Fits inside the cylinder's
`innerRadius = 0.25`.

```csharp
public class Rocket : MonoBehaviour
{
    [SerializeField] float speed = 20f;
    [SerializeField] float maxRange = 200f;

    enum Phase { Exit, Seek }
    Phase _phase = Phase.Exit;
    Vector3 _launchDir;
    Vector3 _seekDir;
    Vector3 _exitWorld;
    Vector3 _target;
    float _traveled;

    public void Launch(Vector3 spawnPos, Vector3 launchDir,
        Vector3 exitWorld, Vector3 crosshairTarget)
    {
        transform.position = spawnPos;
        transform.rotation = Quaternion.LookRotation(launchDir);
        _launchDir = launchDir.normalized;
        _exitWorld = exitWorld;
        _target = crosshairTarget;
        _traveled = 0f;
        _phase = Phase.Exit;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (_phase == Phase.Exit)
        {
            transform.position += _launchDir * speed * dt;
            if (Vector3.Dot(transform.position - _exitWorld, _launchDir) > 0f)
            {
                _seekDir = (_target - transform.position).normalized;
                transform.rotation = Quaternion.LookRotation(_seekDir);
                _phase = Phase.Seek;
            }
        }
        else
        {
            transform.position += _seekDir * speed * dt;
            _traveled += speed * dt;
            if (_traveled >= maxRange) Destroy(gameObject);
        }
    }
}
```

The crosshair target is captured **at fire time** and stays fixed
for the rocket's lifetime — straight-line flight to the locked
point.

### 3.3 Projectile materials

| File | Notes |
|---|---|
| `Assets/Materials/BulletMat.mat` | URP/Lit, opaque, base color `(1.0, 0.9, 0.35)`, modest emission. |
| `Assets/Materials/RocketMat.mat` | URP/Lit, opaque, base color `(1.0, 0.45, 0.25)`, modest emission. |

---

## 4. Multi-weapon dispatch + selection

### 4.1 `FlyShootingController`

A MonoBehaviour on the same GameObject as `FlyController`. Owns:

- The list of all weapons in the construct, grouped by `Shape`.
- The **selected weapon type** (index into the distinct-Shape list).
- The fire input poll (LMB hold).
- The selection inputs (mouse scroll wheel + digit keys 1–9).
- Public events for the toolbar UI to subscribe to.

```csharp
public class FlyShootingController : MonoBehaviour
{
    [SerializeField] FlyController flyController;
    [SerializeField] float aimRange = 100f;
    [SerializeField] float scrollPerSelectionTick = 1f;

    CubeFlyInputActions _input;
    // Each "group" is one weapon type — same ShapeDefinition.
    readonly List<WeaponTypeGroup> _types = new();
    int _selectedTypeIndex = -1;
    float _scrollAccumulator;

    public event Action TypesChanged;          // group list rebuilt
    public event Action<int> SelectedChanged;  // selection moved

    public IReadOnlyList<WeaponTypeGroup> Types => _types;
    public int SelectedTypeIndex => _selectedTypeIndex;
    public bool HasWeapons => _types.Count > 0;

    public void RegisterWeapons(IEnumerable<WeaponBehavior> weapons)
    {
        _types.Clear();
        Dictionary<ShapeDefinition, WeaponTypeGroup> byShape = new();
        foreach (WeaponBehavior w in weapons)
        {
            if (w == null || w.Shape == null) continue;
            if (!byShape.TryGetValue(w.Shape, out WeaponTypeGroup g))
            {
                g = new WeaponTypeGroup(w.Shape);
                byShape[w.Shape] = g;
                _types.Add(g);
            }
            g.Instances.Add(w);
        }
        _selectedTypeIndex = _types.Count > 0 ? 0 : -1;
        TypesChanged?.Invoke();
        SelectedChanged?.Invoke(_selectedTypeIndex);
    }

    void Update()
    {
        if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (!HasWeapons) return;

        // --- Selection inputs ---
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
                             Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            int max = Mathf.Min(digits.Length, _types.Count);
            for (int i = 0; i < max; i++)
            {
                if (kb[digits[i]].wasPressedThisFrame) { SetSelected(i); break; }
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            _scrollAccumulator += mouse.scroll.ReadValue().y;
            while (_scrollAccumulator >= scrollPerSelectionTick)
            { CycleSelected(+1); _scrollAccumulator -= scrollPerSelectionTick; }
            while (_scrollAccumulator <= -scrollPerSelectionTick)
            { CycleSelected(-1); _scrollAccumulator += scrollPerSelectionTick; }
        }

        // --- Fire input (hold-to-fire) ---
        if (!_input.Fly.Fire.IsPressed()) return;

        Transform construct = flyController.Construct;
        if (construct == null) return;
        Vector3 target = construct.position + construct.forward * aimRange;

        WeaponTypeGroup active = _types[_selectedTypeIndex];
        for (int i = 0; i < active.Instances.Count; i++)
        {
            WeaponBehavior w = active.Instances[i];
            if (w != null) w.TryFire(target);
        }
    }

    public void SetSelected(int i)
    {
        if (i < 0 || i >= _types.Count) return;
        if (i == _selectedTypeIndex) return;
        _selectedTypeIndex = i;
        SelectedChanged?.Invoke(_selectedTypeIndex);
    }

    void CycleSelected(int delta)
    {
        if (_types.Count == 0) return;
        int next = (_selectedTypeIndex + delta + _types.Count) % _types.Count;
        SetSelected(next);
    }
}

public class WeaponTypeGroup
{
    public ShapeDefinition Shape { get; }
    public List<WeaponBehavior> Instances { get; } = new();
    public WeaponTypeGroup(ShapeDefinition shape) { Shape = shape; }
    public float MaxReloadSeconds => Instances.Count > 0 ? Instances[0].ReloadSeconds : 0f;
    public float CooldownRemaining => Instances.Count > 0 ? Instances[0].CooldownRemaining : 0f;
    public float ReadyFraction => MaxReloadSeconds <= 0f ? 1f
        : 1f - Mathf.Clamp01(CooldownRemaining / MaxReloadSeconds);
}
```

**Fire semantics**: every frame LMB is held, each off-cooldown
weapon of the selected type fires. With shared `reloadSeconds`, all
instances of the same type stay synchronised — they all fire on the
same frame, all reset together. The progress bar shows the cooldown
of `Instances[0]` (representative of the whole group).

**Cross-type cooldown isolation**: each weapon's
`_cooldownRemaining` ticks down in its own `Update()` regardless of
selection. Switching to a not-recently-fired type means its weapons
are immediately ready.

### 4.2 Weapon registration

`FlyController.BuildConstruct`, after instantiating each placed
shape:

```csharp
WeaponBehavior weapon = go.GetComponent<WeaponBehavior>();
if (weapon != null)
{
    weapon.Construct = construct;
    weapon.Shape = shape;
    _spawnedWeapons.Add(weapon);
}
```

After the loop:

```csharp
shootingController.RegisterWeapons(_spawnedWeapons);
```

`FlyController` gains a public `Transform Construct => construct;`
getter so the shooting controller and the crosshair can both reach
the construct's transform.

### 4.3 Selection input

| Input | Effect |
|---|---|
| `1`…`9` (no modifier) | `SetSelected(digit − 1)` if that index is valid. Ignored otherwise. |
| Mouse scroll wheel up | `CycleSelected(+1)` per accumulated tick — wraps from last → first. |
| Mouse scroll wheel down | `CycleSelected(−1)` — wraps from first → last. |
| LMB (`Fire`, IsPressed) | Fire all selected-type weapons each frame the button is held. |

Suppressed when `PauseMenu.IsOpen` or
`EventSystem.IsPointerOverGameObject()`.

---

## 5. Weapon toolbar (`FlyWeaponToolbarController`)

### 5.1 Concept

A horizontal bottom-of-screen toolbar built at runtime — one button
per weapon type on the construct. Above each button: a thin reload
progress bar. Active type highlighted with the same blue tint
`BuildToolbarController` uses for selected shapes.

Toolbar is hidden entirely when the construct has no weapons.

### 5.2 Layout

```
        ┌──────────────────┐  ┌──────────────────┐
        │░░░░░░░░░░░░░░░░░░│  │██████████████████│   ← reload bars
        │  ┌────────────┐  │  │  ┌────────────┐  │
        │  │  Pyramid   │  │  │  │  Cylinder  │  │   ← weapon buttons
        │  │     ▣      │  │  │  │     ▣      │  │     (swatch color
        │  └────────────┘  │  │  └────────────┘  │      from weapon
        └──────────────────┘  └──────────────────┘      material)
                  ↑                     ↑
              selected                 idle
```

- **Button** = labeled rectangle (~160×60 px) styled like
  `BuildToolbarController` armour buttons. Label = `Shape.displayName`.
  Background tint when selected (`SelectedTypeColor`); idle
  background otherwise.
- **Corner swatch** (~18×18 px) inside the button, colored to the
  weapon's `weaponMaterial.SwatchColor` so the player can tell
  weapons apart at a glance.
- **Reload bar** (~140×6 px) anchored just above the button. Fills
  left-to-right as cooldown progresses. Color = swatch color so the
  bar matches the weapon visually. When `ReadyFraction == 1f` the
  bar is full and the weapon is ready to fire.

Canvas: Screen Space Overlay, `sortingOrder: 120` (above crosshair
110, below pause overlay 300).

### 5.3 Lifecycle

```csharp
void Start()
{
    if (shootingController == null) shootingController = FindAnyObjectByType<FlyShootingController>();
    if (shootingController == null) return;
    shootingController.TypesChanged    += RebuildButtons;
    shootingController.SelectedChanged += OnSelectedChanged;
    BuildCanvas();
    RebuildButtons();
}

void Update()
{
    if (!shootingController.HasWeapons) return;
    // Tick each reload bar each frame.
    for (int i = 0; i < _bars.Length; i++)
    {
        if (_bars[i] == null) continue;
        _bars[i].fillAmount = shootingController.Types[i].ReadyFraction;
    }
}
```

`RebuildButtons` clears any prior child buttons and creates one per
`Types` entry. Called on scene start (after `FlyController.Start`
runs `RegisterWeapons`) and any time the construct's weapon list
changes (which won't happen in v1 — set once, used until scene
unload).

`OnSelectedChanged(int)` updates the highlight on each button
(only the new selected index gets `SelectedTypeColor`).

### 5.4 Click-to-select (optional)

For v1, clicking a button does NOT select (selection is via scroll
or digits only — keeps the user's spec literal). Clickable buttons
can be added trivially as a follow-up by wiring `btn.onClick →
shootingController.SetSelected(i)`. I'll include the wiring but
keep the click target disabled — flip a `[SerializeField]
bool clickToSelect` to enable.

### 5.5 Empty / no-weapons construct

`RebuildButtons` checks `Types.Count == 0` → hides the entire
canvas (`_canvas.gameObject.SetActive(false)`). Crosshair stays
visible regardless.

---

## 6. Input

`CubeFlyInputActions.cs` — add a `Fire` action to the `Fly` map:

```csharp
InputAction fire = _flyMap.AddAction("Fire", InputActionType.Button, "<Mouse>/leftButton");
Fly = new FlyActions(_flyMap, thrust, pitch, yaw, roll, look, lookHeld, fire);
```

Polled via `Fire.IsPressed()` (**hold-to-fire** — confirmed). The
per-weapon reload throttles the actual rate.

Weapon-selection inputs (`1`–`9`, scroll wheel) are polled
directly from `Keyboard.current` / `Mouse.current` in
`FlyShootingController.Update` — no Input Action wrapper needed.

---

## 7. Scene + asset wiring

### 7.1 Scene additions (`Assets/Scenes/FlyScene.unity`)

- `FlyHUD` GameObject — hosts `FlyCrosshair` and
  `FlyWeaponToolbarController` (each builds its own canvas).
- `FlyShootingController` component added to the existing GameObject
  hosting `FlyController` (sibling).

### 7.2 Prefab additions

- `Assets/Prefabs/Projectiles/Bullet.prefab` (+ `.meta`)
- `Assets/Prefabs/Projectiles/Rocket.prefab` (+ `.meta`)

### 7.3 Prefab modifications

- `Assets/Prefabs/PlacedPyramid.prefab` — add `PyramidWeapon`
  MonoBehaviour with the bullet prefab reference and stats.
- `Assets/Prefabs/PlacedCylinder.prefab` — add `CylinderWeapon`
  MonoBehaviour with the rocket prefab reference and stats.

### 7.4 New materials

- `Assets/Materials/BulletMat.mat` (+ `.meta`)
- `Assets/Materials/RocketMat.mat` (+ `.meta`)

### 7.5 New scripts

| File | Lines (est.) |
|---|---|
| `Assets/Scripts/Fly/FlyCrosshair.cs` | ~130 |
| `Assets/Scripts/Fly/FlyShootingController.cs` | ~170 |
| `Assets/Scripts/Fly/FlyWeaponToolbarController.cs` | ~200 |
| `Assets/Scripts/Fly/WeaponBehavior.cs` (abstract) | ~50 |
| `Assets/Scripts/Fly/PyramidWeapon.cs` | ~50 |
| `Assets/Scripts/Fly/CylinderWeapon.cs` | ~40 |
| `Assets/Scripts/Fly/Bullet.cs` | ~40 |
| `Assets/Scripts/Fly/Rocket.cs` | ~70 |

### 7.6 Script modifications

- `Assets/Input/CubeFlyInputActions.cs` — add `Fire` action and
  property.
- `Assets/Scripts/Fly/FlyController.cs` — expose `Construct`
  getter; collect spawned weapons in `BuildConstruct`; register
  with `FlyShootingController`.

---

## 8. Order of implementation

Each phase independently testable before moving on:

1. **Crosshair** (`FlyCrosshair.cs` + scene `FlyHUD`). Verify it
   drifts correctly during fast turns and freezes on pause.
2. **Input** (`Fire` action wired through `CubeFlyInputActions`).
3. **Projectile prefabs** (Bullet, Rocket with their materials).
   Manually spawn one in a debug method, verify motion and despawn.
4. **`WeaponBehavior` base + concrete weapons** (`PyramidWeapon`,
   `CylinderWeapon`). Add to respective prefabs.
5. **`FlyShootingController`** — wire to `FlyController`, register
   weapons in `BuildConstruct`. Smoke-test LMB hold-to-fire with a
   default-selected single weapon type.
6. **Selection (`SetSelected` + scroll/digit polling)** inside
   `FlyShootingController`. Smoke-test with two weapon types,
   confirm only the selected type fires.
7. **`FlyWeaponToolbarController`** — runtime toolbar UI, reload
   bars, highlight updates.
8. **End-to-end smoke test** — build a construct with mixed
   weapons; fly; observe:
   - Crosshair drift on fast turns.
   - LMB held → Pyramids auto-fire at the crosshair / their tip.
   - `2` or scroll → Cylinder selected; LMB → Rocket exits then
     redirects to locked target.
   - Reload bar fills smoothly; the bar is full exactly when LMB
     fires the next shot.

---

## 9. Verification checklist

### Crosshair
- [ ] Visible at scene start, centered.
- [ ] Quick yaw / pitch → drifts off-center → settles back.
- [ ] Quick roll → reticle stays put (roll doesn't change forward).
- [ ] 180° spin (facing rear of camera) → crosshair hides.

### Weapons and projectiles
- [ ] LMB held + only-Pyramid construct → bullets spawn from tip,
      ~5/sec, fly toward crosshair point.
- [ ] Rear-facing Pyramid → bullets fly backwards regardless.
- [ ] Cylinder placed bottom-down on top of a cube → rocket spawns
      inside, exits straight up ~0.5 units, then reorients to the
      crosshair point that was active *when LMB was pressed*. Even
      if the player rotates the ship after firing, the rocket stays
      on its locked target path.
- [ ] Cylinder rotated 90° → rocket exits sideways out of the now-
      side-facing open end, then steers to target.
- [ ] Multiple weapons of same type → all fire in sync.

### Selection + toolbar
- [ ] No-weapon construct → toolbar hidden, no fire on LMB.
- [ ] Mixed construct → toolbar shows one entry per weapon type
      (Pyramid, Cylinder).
- [ ] `1` → Pyramid selected (highlighted); `2` → Cylinder selected.
- [ ] Mouse scroll up → cycles to next type; scroll down → previous.
      Wraps around at ends.
- [ ] Reload bar fills smoothly while cooldown ticks down; reaches
      full at exactly the moment the weapon becomes ready.
- [ ] Selected weapon's bar visually distinct (color + highlight).
- [ ] Switching mid-cooldown → other type's bar reflects its own
      state (not the just-fired type's).
- [ ] LMB held + only Pyramid selected on a mixed construct →
      Cylinders do NOT fire.

### Pause / UI gating
- [ ] LMB over the corner Hangar button → no fire.
- [ ] LMB while pause overlay open → no fire.
- [ ] Selection inputs ignored while paused.
- [ ] Crosshair freezes in place when paused.

---

## 10. Open questions / decisions to confirm

All previously open items are now answered. Remaining items track
post-v1 work:

- **v2 damage / hit detection** — separate spec.
- **Toolbar button click-to-select** — wiring stubbed (off by
  default), enable when desired.
- **Per-weapon-type crosshair color** (e.g. red when Cylinder
  armed) — possible polish in v2.
